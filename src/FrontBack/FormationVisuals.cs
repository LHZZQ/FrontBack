using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FrontBack;

public static class FormationVisuals
{
    private sealed class Layout
    {
        public required List<NCreature> Nodes;
        public required Dictionary<NCreature, Vector2> OriginalPositions;
        public readonly Dictionary<NCreature, Tween> Tweens = new();
    }

    private static readonly ConditionalWeakTable<ICombatState, Layout> Layouts = new();

    public static void Capture(List<NCreature> nodes)
    {
        var players = nodes.Where(n => n.Entity.IsPlayer).ToList();
        if (players.Count != 2 || players[0].Entity.CombatState is not { } combat ||
            FormationStore.Get(combat) is null) return;
        if (Layouts.TryGetValue(combat, out var old))
            foreach (var tween in old.Tweens.Values)
                if (GodotObject.IsInstanceValid(tween)) tween.Kill();
        Layouts.Remove(combat);
        Layouts.Add(combat, new Layout
        {
            Nodes = nodes.ToList(),
            OriginalPositions = nodes.ToDictionary(n => n, n => n.Position)
        });
        Apply(combat, animate: false);
    }

    public static void Apply(ICombatState combat, bool animate)
    {
        if (!ModEntry.Ready || !Layouts.TryGetValue(combat, out var layout) ||
            FormationStore.Get(combat) is not { } formation) return;
        var players = layout.Nodes.Where(n => GodotObject.IsInstanceValid(n) && n.Entity.IsPlayer).ToList();
        if (players.Count != 2) return;
        // 使用原版算出的两个落点，右侧离怪物较近；保留相机缩放与遭遇布局。
        var anchors = players.Select(n => layout.OriginalPositions[n]).OrderBy(p => p.X).ToArray();
        var front = anchors[1] + new Vector2(0, ModEntry.Config.RowOffsetY);
        var back = anchors[0] - new Vector2(0, ModEntry.Config.RowOffsetY);
        foreach (var node in layout.Nodes.Where(GodotObject.IsInstanceValid))
        {
            var owner = node.Entity.Player ?? node.Entity.PetOwner;
            if (owner is null) continue;
            var playerNode = players.FirstOrDefault(n => n.Entity.Player == owner);
            if (playerNode is null) continue;
            bool isFront = owner.NetId == formation.FrontId;
            var delta = (isFront ? front : back) - layout.OriginalPositions[playerNode];
            var target = layout.OriginalPositions[node] + delta;
            if (layout.Tweens.Remove(node, out var previous) && GodotObject.IsInstanceValid(previous)) previous.Kill();
            node.ZIndex = isFront ? 1 : 0;
            if (animate && ModEntry.Config.SwapAnimationSeconds > 0)
            {
                var tween = node.CreateTween();
                tween.TweenProperty(node, "position", target, ModEntry.Config.SwapAnimationSeconds)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                layout.Tweens[node] = tween;
            }
            else node.Position = target;
            if (!node.Entity.IsPlayer) continue;
            var label = node.GetNodeOrNull<Label>("FrontBackRole");
            if (label is null)
            {
                label = new Label { Name = "FrontBackRole", Position = new Vector2(-90, -340),
                    MouseFilter = Control.MouseFilterEnum.Ignore };
                node.AddChild(label);
            }
            label.Text = isFront ? "前排 ×1.5" : "后排 ×0.5";
        }
    }
}

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets))]
public static class LayoutPatch
{
    public static void Postfix(List<NCreature> creatureNodes) => FormationVisuals.Capture(creatureNodes);
}
