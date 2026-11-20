using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

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
