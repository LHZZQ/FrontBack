using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace FrontBack;

public interface IDamageChoiceChannel
{
    uint Reserve(Player player);
    void Send(Player player, uint id, PlayerChoiceResult result);
    Task<PlayerChoiceResult> Receive(Player player, uint id);
}

public sealed class NativeDamageChoiceChannel(PlayerChoiceSynchronizer synchronizer) : IDamageChoiceChannel
{
    public uint Reserve(Player player) => synchronizer.ReserveChoiceId(player);
    public void Send(Player player, uint id, PlayerChoiceResult result) => synchronizer.SyncLocalChoice(player, id, result);
    public Task<PlayerChoiceResult> Receive(Player player, uint id) => synchronizer.WaitForRemoteChoice(player, id);
}

public sealed record BattleSetup(DamageRules Rules, Rank[] Ranks);

public static class DamageRuleSync
{
    // 使用原生玩家选择通道：先按同一顺序保留 ID，再发送本机数据，最后等待远端。
    // 不会因房主不是较小 ID、某一端先收到数据或双方本地配置不同而改变结果。
    public static async Task<BattleSetup> ExchangeAsync(IDamageChoiceChannel channel,
        IReadOnlyList<Player> players, ulong localId, NetGameType type, DamageRules localRules,
        PartyRanks party, CancellationToken cancellationToken = default)
    {
        localRules.Validate();
        if (type == NetGameType.Singleplayer) return new BattleSetup(localRules, party.Snapshot());
        var ordered = players.OrderBy(p => p.NetId).ToArray();
        var ids = ordered.Select(channel.Reserve).ToArray();
        var tasks = new Task<PlayerChoiceResult>[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            if (ordered[i].NetId == localId && type != NetGameType.Replay)
            {
                var payload = localRules.ToWire(type == NetGameType.Host).ToList();
                if (type == NetGameType.Host) payload.AddRange(party.Snapshot().Select(rank => (int)rank));
                var result = PlayerChoiceResult.FromIndexes(payload);
                channel.Send(ordered[i], ids[i], result);
                tasks[i] = Task.FromResult(result);
            }
            else tasks[i] = channel.Receive(ordered[i], ids[i]);
        }
        var results = await Task.WhenAll(tasks).WaitAsync(cancellationToken);
        var payloads = results.Select(r => r.AsIndexes()).ToArray();
        var validated = payloads.Select(p => DamageRules.FromWire(p.Take(3).ToArray())).ToArray();
        var hosts = Enumerable.Range(0, payloads.Length).Where(i => payloads[i][2] == 1).ToArray();
        if (hosts.Length != 1) throw new InvalidDataException("前后排同步未找到唯一房主。");
        var snapshot = payloads[hosts[0]].Skip(3).ToArray();
        if (snapshot.Length != ordered.Length || snapshot.Any(rank => rank is < 0 or > 2))
            throw new InvalidDataException("房主站位同步数据无效。");
        return new BattleSetup(validated[hosts[0]], snapshot.Select(rank => (Rank)rank).ToArray());
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
public static class BeforeCombatRulesPatch
{
    // 复制原方法入口；同步完成后才执行原版开战效果，避免开场伤害用错倍率。
    [HarmonyReversePatch]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task Original(IRunState runState, ICombatState? combatState) =>
        throw new NotSupportedException("Harmony reverse patch not installed.");

    public static bool Prefix(IRunState runState, ICombatState? combatState, ref Task __result)
    {
        if (!ModEntry.Ready || combatState is null || !FormationStore.Supports(runState.Players.Count)) return true;
        __result = SynchronizeAndStart(runState, combatState);
        return false;
    }

    private static async Task SynchronizeAndStart(IRunState runState, ICombatState combat)
    {
        var manager = RunManager.Instance;
        using var disconnected = new CancellationTokenSource();
        void OnDisconnected(NetErrorInfo _) => disconnected.Cancel();
        manager.NetService.Disconnected += OnDisconnected;
        try
        {
            var formation = FormationStore.Get(combat)!;
            SavedRanks.TryRestore(runState, formation.Party);
            var setup = await DamageRuleSync.ExchangeAsync(new NativeDamageChoiceChannel(manager.PlayerChoiceSynchronizer), runState.Players,
                manager.NetService.NetId, manager.NetService.Type, ModEntry.DamageSettings.Current, formation.Party, disconnected.Token);
            formation.Party.Restore(setup.Ranks);
            formation.SetRules(setup.Rules);
            FormationVisuals.Apply(combat, animate: false);
            GD.Print($"[FrontBack] 队伍站位已同步，前排 {setup.Rules.FrontPercent}%，中排 100%，后排 {setup.Rules.BackPercent}%");
        }
        finally { manager.NetService.Disconnected -= OnDisconnected; }
        await Original(runState, combat);
    }
}
