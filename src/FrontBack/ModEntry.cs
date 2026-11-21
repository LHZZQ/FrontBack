using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;

namespace FrontBack;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Id = "FrontBack";
    public static bool Ready { get; private set; }
    public static VisualConfig Config { get; private set; } = new();
    public static DamageSettingsStore DamageSettings { get; private set; } = null!;

    public static void Initialize()
    {
        var harmony = new Harmony(Id);
        try
        {
            Config = VisualConfig.Load(Path.GetDirectoryName(typeof(ModEntry).Assembly.Location)!);
            DamageSettings = new DamageSettingsStore(Path.Combine(OS.GetUserDataDir(), "mod_data", Id, "damage-settings.json"));
            try { DamageSettings.Load(); }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or UnauthorizedAccessException)
            {
                GD.PushWarning($"[FrontBack] Cannot read damage settings; using 150% / 50%: {error.Message}");
            }
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Ready = true;
            GD.Print("[FrontBack] v0.3.5 initialized; formation board for 2–4 players; target game v0.113.1.");
        }
        catch (Exception error)
        {
            // 接口发生变化时整组撤销，避免只加载按钮却没有伤害规则。
            harmony.UnpatchAll(Id);
            GD.PushError($"[FrontBack] Disabled: {error}");
        }
    }
}

public static class FormationStore
{
    private static readonly ConditionalWeakTable<ICombatState, Formation> States = new();
    private static readonly ConditionalWeakTable<IRunState, PartyRanks> Parties = new();

    public static bool Supports(int playerCount) => playerCount is >= 2 and <= 4;

    public static Formation? Get(ICombatState? combat)
    {
        if (combat is null || !Supports(combat.RunState.Players.Count)) return null;
        return States.GetValue(combat, c =>
            new Formation(Parties.GetValue(c.RunState, run => new PartyRanks(run.Players.Select(p => p.NetId)))));
    }

    public static bool CanSwap(ICombatState combat, Player player) =>
        ModEntry.Ready && Get(combat) is not null && player.Creature.IsAlive &&
        player.PlayerCombatState?.Phase == PlayerTurnPhase.Play &&
        !CombatManager.Instance.IsPlayerReadyToEndTurn(player);
}
