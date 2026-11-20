using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;

namespace FrontBack;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Id = "FrontBack";
    public static bool Ready { get; private set; }
    public static VisualConfig Config { get; private set; } = new();

    public static void Initialize()
    {
        var harmony = new Harmony(Id);
        try
        {
            Config = VisualConfig.Load(Path.GetDirectoryName(typeof(ModEntry).Assembly.Location)!);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Ready = true;
            GD.Print("[FrontBack] v0.1.0 initialized; target game v0.113.1; damage before caps.");
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

    public static Formation? Get(ICombatState? combat)
    {
        // 用整局玩家数量判断，死亡不会使双人模式中途失效。
        if (combat?.RunState.Players.Count != 2) return null;
        return States.GetValue(combat, c =>
            new Formation(c.RunState.Players[0].NetId, c.RunState.Players[1].NetId));
    }

    public static bool CanSwap(ICombatState combat, Player player) =>
        ModEntry.Ready && Get(combat) is not null &&
        combat.Players.Count == 2 && combat.Players.All(p => p.Creature.IsAlive) &&
        player.PlayerCombatState?.Phase == PlayerTurnPhase.Play;
}
