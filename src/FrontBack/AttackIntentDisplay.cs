using Godot;
using HarmonyLib;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace FrontBack;

internal static class AttackIntentDisplay
{
    public static void Refresh(ICombatState combat)
    {
        var room = NCombatRoom.Instance;
        if (room is null) return;

        var targets = combat.Players.Select(player => player.Creature).ToArray();
        foreach (var node in room.CreatureNodes)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) continue;
            var enemy = node.Entity;
            if (!enemy.IsEnemy || !enemy.IsAlive || !ReferenceEquals(enemy.CombatState, combat) ||
                enemy.Monster?.NextMove is not { } move) continue;

            // 只刷新已有的攻击图标，避免重新揭示意图或重播整组淡入。
            var icons = node.IntentContainer.GetChildren().OfType<NIntent>();
            foreach (var (icon, intent) in icons.Zip(move.Intents))
                if (intent is AttackIntent) icon.UpdateIntent(intent, targets, enemy);
        }
    }
}

[HarmonyPatch(typeof(AttackIntent), nameof(AttackIntent.GetSingleDamage))]
internal static class InterceptIntentPatch
{
    // 此同步计算只预览原本打向本机玩家的一击，实际结算仍使用原版拦截倍率。
    [ThreadStatic]
    internal static InterceptPower? SingleHitPreview;

    private static bool Prefix(AttackIntent __instance, Creature owner, ref int __result)
    {
        var combat = owner.CombatState;
        if (!ModEntry.Ready || !owner.IsMonster || FormationStore.Get(combat) is null ||
            LocalContext.GetMe(combat)?.Creature is not { } covered || __instance.DamageCalc is not { } calculate)
            return true;

        var interceptors = covered.GetPowerInstances<CoveredPower>()
            .Select(power => power.Applier)
            .OfType<Creature>()
            .Where(creature => creature.IsAlive && creature.IsPlayer)
            .Distinct()
            .Select(creature => creature.GetPower<InterceptPower>())
            .OfType<InterceptPower>()
            .ToArray();
        if (interceptors.Length == 0) return true;

        decimal damage = calculate();
        int preview = 0;
        var previous = SingleHitPreview;
        try
        {
            foreach (var interceptor in interceptors)
            {
                var recipient = interceptor.Owner;
                SingleHitPreview = interceptor;
                preview += (int)Hook.ModifyDamage(recipient.Player!.RunState, combat, recipient, owner,
                    damage, ValueProp.Move, null, null, ModifyDamageHookType.All, CardPreviewMode.None, out _);
            }
        }
        finally
        {
            SingleHitPreview = previous;
        }
        __result = preview;
        return false;
    }
}

[HarmonyPatch(typeof(InterceptPower), nameof(InterceptPower.ModifyDamageMultiplicative))]
internal static class InterceptMultiplierPreviewPatch
{
    private static void Postfix(InterceptPower __instance, Creature? target, ref decimal __result)
    {
        if (ReferenceEquals(__instance, InterceptIntentPatch.SingleHitPreview) && target == __instance.Owner)
            __result = 1m;
    }
}

[HarmonyPatch]
internal static class InterceptIntentRefreshPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
    [
        AccessTools.Method(typeof(CoveredPower), nameof(CoveredPower.AfterApplied)),
        AccessTools.Method(typeof(CoveredPower), nameof(CoveredPower.AfterRemoved))
    ];

    private static void Postfix(CoveredPower __instance, ref Task __result)
    {
        if (ModEntry.Ready && __instance.Owner.CombatState is { } combat)
            __result = RefreshAfter(__result, combat);
    }

    private static async Task RefreshAfter(Task change, ICombatState combat)
    {
        await change;
        AttackIntentDisplay.Refresh(combat);
    }
}
