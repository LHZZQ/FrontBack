using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace FrontBack;

public static class FormationVisuals
{
    private sealed class Placement(NCreature node)
    {
        public readonly NCreature Node = node;
        public readonly Vector2 Original = node.Position;
        public readonly int OriginalZ = node.ZIndex;
        public Vector2 Offset;
        public Tween? Tween;
    }

    private static readonly ConditionalWeakTable<ICombatState, List<Placement>> Layouts = new();

    public static void Capture(List<NCreature> nodes)
    {
        var combat = nodes.FirstOrDefault(n => n.Entity.IsPlayer)?.Entity.CombatState;
        if (combat is null || FormationStore.Get(combat) is null) return;
        if (Layouts.TryGetValue(combat, out var old))
            foreach (var entry in old)
                if (GodotObject.IsInstanceValid(entry.Tween)) entry.Tween!.Kill();
        Layouts.Remove(combat);
        Layouts.Add(combat, nodes.Select(node => new Placement(node)).ToList());
        Apply(combat, animate: false);
    }

    public static void Apply(ICombatState combat, bool animate)
    {
        if (!ModEntry.Ready || !Layouts.TryGetValue(combat, out var layout) ||
            FormationStore.Get(combat) is not { } formation) return;
        var present = layout.Where(entry => GodotObject.IsInstanceValid(entry.Node)).ToList();
        var players = present.Where(entry => entry.Node.Entity.IsPlayer)
            .OrderBy(entry => entry.Node.Entity.Player!.NetId).ToList();
        if (players.Count == 0) return;
        var ranks = formation.Party;
        bool allMiddle = ranks.Players.All(id => ranks.Get(id) == Rank.Middle);
        var center = new Vector2((players.Min(p => p.Original.X) + players.Max(p => p.Original.X)) / 2f,
            players.Average(p => p.Original.Y));
        float span = Math.Max(120, (players.Max(p => p.Original.X) - players.Min(p => p.Original.X)) / 2);
        var offsets = new Dictionary<ulong, Vector2>();
        foreach (var group in players.GroupBy(entry => ranks.Get(entry.Node.Entity.Player!.NetId)))
        {
            var row = group.ToArray();
            var direction = group.Key == Rank.Front ? 1 : group.Key == Rank.Back ? -1 : 0;
            for (int i = 0; i < row.Length; i++)
            {
                var entry = row[i];
                float stagger = i - (row.Length - 1) / 2f;
                var target = center + new Vector2(direction * span + 34 * stagger,
                    direction * ModEntry.Config.RowOffsetY + 100 * stagger);
                var id = entry.Node.Entity.Player!.NetId;
                offsets[id] = allMiddle ? Vector2.Zero : target - entry.Original;
                UpdateBadge(entry.Node, group.Key, formation.Multiplier(id) * 100m);
            }
        }

        foreach (var entry in present)
        {
            var owner = entry.Node.Entity.Player ?? entry.Node.Entity.PetOwner;
            if (owner is null || !offsets.TryGetValue(owner.NetId, out var goal)) continue;
            entry.Node.ZIndex = entry.OriginalZ + (ranks.Get(owner.NetId) == Rank.Front ? 1 : 0);
            if (GodotObject.IsInstanceValid(entry.Tween)) entry.Tween!.Kill();
            // 每帧只加本 Mod 的位移差，保留怪物、动画或其他游戏效果移动的距离。
            void Move(Vector2 offset)
            {
                entry.Node.Position += offset - entry.Offset;
                entry.Offset = offset;
            }
            if (entry.Offset == goal) continue;
            if (!animate || ModEntry.Config.SwapAnimationSeconds <= 0)
                Move(goal);
            else
            {
                entry.Tween = entry.Node.CreateTween();
                entry.Tween.TweenMethod(Callable.From<Vector2>(Move), entry.Offset, goal, ModEntry.Config.SwapAnimationSeconds)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            }
        }
    }

    private static void UpdateBadge(NCreature node, Rank rank, decimal percent)
    {
        var badge = node.GetNodeOrNull<PanelContainer>("FrontBackRole");
        if (badge is null)
        {
            badge = new PanelContainer { Name = "FrontBackRole", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore,
                ZAsRelative = false, ZIndex = 8 };
            badge.AddChild(new Label { MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center });
            node.AddChild(badge);
            var badgeNode = badge;
            void Reposition()
            {
                if (!GodotObject.IsInstanceValid(badgeNode) || !node.IsInsideTree()) return;
                var top = node.GetGlobalTransform().AffineInverse() * node.GetTopOfHitbox();
                badgeNode.Position = top - new Vector2(badgeNode.Size.X / 2, badgeNode.Size.Y + 10);
            }
            badge.Resized += Reposition;
            node.Hitbox.ItemRectChanged += Reposition;
            Reposition();

            Node? ancestor = node.GetParent();
            while (ancestor is not null && ancestor is not NCombatRoom) ancestor = ancestor.GetParent();
            if (ancestor is NCombatRoom room)
                WatchCombatForeground(room, badgeNode, foreground => badgeNode.Visible = foreground);
        }
        var color = RankStyle.Color(rank);
        badge.AddThemeStyleboxOverride("panel", RankStyle.Frame(color.Darkened(0.78f), color, 7, 3));
        var text = badge.GetChild<Label>(0);
        text.Text = $"{RankStyle.Title(rank)} {percent:0.#}%";
        text.AddThemeColorOverride("font_color", color.Lightened(0.35f));
        text.AddThemeFontSizeOverride("font_size", 19);
        badge.ResetSize();
    }

    internal static void WatchCombatForeground(NCombatRoom room, CanvasItem item, Action<bool> update)
    {
        var screens = ActiveScreenContext.Instance;
        var map = NMapScreen.Instance;
        void Refresh()
        {
            // 地图关闭动画结束后才会隐藏，战斗界面等地图收起后再恢复。
            bool mapVisible = map is not null && GodotObject.IsInstanceValid(map) && map.IsVisibleInTree();
            update(!mapVisible && screens.IsCurrent(room));
        }

        screens.Updated += Refresh;
        if (map is not null) map.VisibilityChanged += Refresh;
        item.TreeExiting += () =>
        {
            screens.Updated -= Refresh;
            if (map is not null && GodotObject.IsInstanceValid(map)) map.VisibilityChanged -= Refresh;
        };
        Refresh();
    }
}

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets))]
public static class LayoutPatch
{
    public static void Postfix(List<NCreature> creatureNodes) => FormationVisuals.Capture(creatureNodes);
}

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature))]
internal static class OstyHealthBarPatch
{
    private static void Postfix(NCombatRoom __instance, Creature creature)
    {
        if (!ModEntry.Ready || creature.Monster is not Osty || creature.PetOwner is not { } owner ||
            !LocalContext.IsMe(owner) || FormationStore.Get(creature.CombatState) is null ||
            __instance.GetCreatureNode(creature)?.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar") is not { } healthBar)
            return;

        int originalZ = healthBar.ZIndex;
        // 战斗中血条画在前排角色的 +1 层之上，其他界面恢复原版层级。
        FormationVisuals.WatchCombatForeground(__instance, healthBar,
            foreground => healthBar.ZIndex = foreground ? Math.Max(originalZ, 2) : originalZ);
    }
}
