using FrontBack;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

static class SyncChecks
{
    // 仅替代需要运行游戏引擎的通道；负载走游戏真实序列化路径。
    sealed class Channel(IPlayerCollection players) : IDamageChoiceChannel
    {
        public Channel[] Remotes = [];
        public readonly Dictionary<ulong, uint> ChoiceIds = new();
        readonly Dictionary<(ulong, uint), TaskCompletionSource<PlayerChoiceResult>> received = new();
        public uint Reserve(Player player)
        {
            uint id = ChoiceIds.GetValueOrDefault(player.NetId);
            ChoiceIds[player.NetId] = id + 1;
            return id;
        }
        public void Send(Player player, uint id, PlayerChoiceResult result)
        {
            var writer = new PacketWriter(); result.ToNetData().Serialize(writer);
            foreach (var remote in Remotes)
            {
                var reader = new PacketReader(); reader.Reset(writer.Buffer);
                var net = new NetPlayerChoiceResult(); net.Deserialize(reader);
                remote.Pending(player.NetId, id).SetResult(PlayerChoiceResult.FromNetData(player, players, net));
            }
        }
        TaskCompletionSource<PlayerChoiceResult> Pending(ulong player, uint id)
        {
            if (!received.TryGetValue((player, id), out var completion))
                received[(player, id)] = completion = new();
            return completion;
        }
        public Task<PlayerChoiceResult> Receive(Player player, uint id) => Pending(player.NetId, id).Task;
    }

    public static void Run(Player first, Player second, IRunState players, Action<bool, string> check)
    {
        foreach (bool hostStartsFirst in new[] { true, false })
        {
            var host = new Channel(players); var client = new Channel(players);
            host.Remotes = [client]; client.Remotes = [host];
            var preferred = new DamageRules { FrontPercent = 250.5m, BackPercent = 0m };
            var party = new PartyRanks(players.Players.Select(p => p.NetId));
            party.Apply(second.NetId, RankOperation.Preset, Lineup.Split, Rank.Front);
            Task<BattleSetup> BeginHost() => DamageRuleSync.ExchangeAsync(host, players.Players,
                second.NetId, NetGameType.Host, preferred, party);
            Task<BattleSetup> BeginClient() => DamageRuleSync.ExchangeAsync(client, players.Players,
                first.NetId, NetGameType.Client, new DamageRules { FrontPercent = 999m, BackPercent = 999m },
                new PartyRanks(party.Players));
            var firstTask = hostStartsFirst ? BeginHost() : BeginClient();
            check(!firstTask.IsCompleted, $"Pre-combat barrier waits for remote ({hostStartsFirst})");
            var secondTask = hostStartsFirst ? BeginClient() : BeginHost();
            var result = Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            check(result.All(r => r.Rules == preferred && r.Ranks.SequenceEqual(party.Snapshot())),
                $"Host rules and ranks roundtrip through native choice payloads on both peers ({hostStartsFirst})");
            check(host.ChoiceIds.OrderBy(p => p.Key).SequenceEqual(client.ChoiceIds.OrderBy(p => p.Key)), "Choice IDs remain deterministic");
        }
        foreach (int count in new[] { 3, 4 })
        {
            var fullParty = Enumerable.Range(0, count).Select(index =>
            {
                var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
                typeof(Player).GetField("<NetId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(player, (ulong)(100 + index));
                return player;
            }).ToArray();
            var channels = fullParty.Select(_ => new Channel(players)).ToArray();
            foreach (var channel in channels) channel.Remotes = channels.Where(other => other != channel).ToArray();
            var preferred = new DamageRules { FrontPercent = 125m, BackPercent = 75m };
            var ranks = new PartyRanks(fullParty.Select(p => p.NetId));
            ranks.Apply(fullParty[^1].NetId, RankOperation.Preset, count == 4 ? Lineup.Spread : Lineup.Split, Rank.Back);
            var results = Task.WhenAll(fullParty.Select((player, index) =>
                DamageRuleSync.ExchangeAsync(channels[index], fullParty, player.NetId,
                    index == count - 1 ? NetGameType.Host : NetGameType.Client,
                    index == count - 1 ? preferred : new DamageRules(), index == count - 1 ? ranks : new PartyRanks(ranks.Players))))
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            check(results.All(setup => setup.Rules == preferred && setup.Ranks.SequenceEqual(ranks.Snapshot())),
                $"All {count} peers restore host ranks and multipliers from native payloads");
        }
        var path = Path.Combine(Path.GetTempPath(), "FrontBack.Checks", Guid.NewGuid() + ".json");
        var store = new DamageSettingsStore(path);
        var rules = new DamageRules { FrontPercent = 0m, BackPercent = 10000m };
        store.Save(rules);
        var loaded = new DamageSettingsStore(path); loaded.Load();
        check(loaded.Current == rules, "Saved settings survive reload, including supported limits");
        var active = new Formation(new PartyRanks([1, 2])); active.SetRules(loaded.Current);
        loaded.Save(new DamageRules { FrontPercent = 120m, BackPercent = 80m });
        check(active.Rules == rules, "Saving preferences does not mutate active combat snapshot");
        try { loaded.Save(new DamageRules { FrontPercent = -1m }); check(false, "Invalid percentage rejected"); }
        catch (InvalidDataException) { check(loaded.Current.FrontPercent == 120m, "Invalid percentage rejected without altering saved rules"); }
        File.Delete(path);
    }
}
