using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;

namespace FrontBack;

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom._Ready))]
public static class CombatOverlay
{
    public static void Postfix(NCombatRoom __instance)
    {
        if (!ModEntry.Ready || !FormationStore.Supports(RunManager.Instance.DebugOnlyGetState()?.Players.Count ?? 0)) return;
        FormationVisuals.WatchAllyVisibility(__instance);

        if (__instance.Mode != CombatRoomMode.ActiveCombat) return;
        var board = new FormationBoard(__instance);
        board.Attach();
    }
}

internal sealed class FormationBoard(NCombatRoom room)
{
    private readonly Button launcher = RankStyle.Button("⇄  队伍站位");
    private readonly PanelContainer board = new() { Visible = false, ZIndex = 21, MouseFilter = Control.MouseFilterEnum.Stop };
    private readonly Dictionary<Rank, HBoxContainer> slots = new();
    private readonly Dictionary<Rank, Label> headings = new();
    private readonly Dictionary<Rank, Button> shortcuts = new();
    private readonly Dictionary<ulong, PanelContainer> cards = new();
    private readonly Dictionary<Lineup, Button> presets = new();
    private readonly Rank[] orderedRanks = [Rank.Front, Rank.Middle, Rank.Back];
    private ICombatState? state;
    private Player? local;
    private int displayedRevision = -1;
    private DamageRules? displayedRules;

    public void Attach()
    {
        launcher.Name = "FrontBackFormationButton";
        launcher.ZIndex = 20;
        launcher.Hide();
        room.AddChild(launcher);
        launcher.Pressed += () =>
        {
            board.Visible = !board.Visible;
            if (board.Visible) Refresh();
        };
        board.Name = "FrontBackFormationBoard";
        board.AddThemeStyleboxOverride("panel", RankStyle.Frame(new Color("142028"), new Color("927749"), 16, 13));
        room.AddChild(board);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        board.AddChild(rows);
        var titleBar = new HBoxContainer();
        titleBar.AddChild(new Label { Text = "队伍站位", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = RankStyle.Button("关闭");
        close.Pressed += board.Hide;
        titleBar.AddChild(close);
        rows.AddChild(titleBar);
        rows.AddChild(new Label { Text = "拖动自己的头像换排，或拖到队友头像上互换。" });
        foreach (var rank in orderedRanks)
        {
            var lane = new PanelContainer();
            var color = RankStyle.Color(rank);
            lane.AddThemeStyleboxOverride("panel", RankStyle.Frame(color.Darkened(0.85f), color.Darkened(0.45f), 8, 5));
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 52), MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 6);
            var heading = new Label { CustomMinimumSize = new Vector2(114, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            heading.AddThemeColorOverride("font_color", color);
            row.AddChild(heading);
            headings.Add(rank, heading);
            var portraits = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            portraits.AddThemeConstantOverride("separation", 6);
            row.AddChild(portraits);
            slots.Add(rank, portraits);
            lane.AddChild(row);
            lane.SetDragForwarding(Callable.From<Vector2, Variant>(_ => default),
                Callable.From<Vector2, Variant, bool>((_, data) => IsOwnDrag(data) && CanMove(rank)),
                Callable.From<Vector2, Variant>((_, _) => MoveSelf(rank)));
            rows.AddChild(lane);
        }

        var actions = new HBoxContainer();
        foreach (var rank in orderedRanks)
        {
            var button = RankStyle.Button("我去" + RankStyle.Title(rank), RankStyle.Color(rank));
            button.Pressed += () => MoveSelf(rank);
            actions.AddChild(button);
            shortcuts.Add(rank, button);
        }
        rows.AddChild(actions);
        var lineupButtons = new HBoxContainer();
        int count = RunManager.Instance.DebugOnlyGetState()!.Players.Count;
        void AddPreset(Lineup lineup, string title)
        {
            var button = RankStyle.Button(title);
            button.Pressed += () =>
            {
                if (local is not null && FormationStore.Get(state) is { } formation)
                    Send(RankOperation.Preset, lineup, formation.Party.Get(local.NetId));
            };
            lineupButtons.AddChild(button);
            presets.Add(lineup, button);
        }
        AddPreset(Lineup.Together, "全员回中排");
        if (count == 4) AddPreset(Lineup.Spread, "1 前 · 2 中 · 1 后");
        AddPreset(Lineup.Split, count switch { 4 => "2 前 · 2 后", 3 => "1 前 · 2 后", _ => "1 前 · 1 后" });
        rows.AddChild(lineupButtons);
        var timer = new Godot.Timer { WaitTime = 0.12, Autostart = true };
        room.AddChild(timer);
        timer.Timeout += Refresh;
    }

    private void Refresh()
    {
        var manager = RunManager.Instance;
        local = manager.DebugOnlyGetState()?.Players.FirstOrDefault(p => p.NetId == manager.NetService.NetId);
        state = local?.Creature.CombatState;
        var formation = FormationStore.Get(state);
        if (local is null || formation is null || !CombatManager.Instance.IsInProgress ||
            !ActiveScreenContext.Instance.IsCurrent(room) || !PlaceLauncher())
        {
            launcher.Hide();
            board.Hide();
            return;
        }

        launcher.Show();
        var ownRank = formation.Party.Get(local.NetId);
        launcher.TooltipText = $"当前站位 · {RankStyle.Title(ownRank)} {formation.Multiplier(local.NetId) * 100m:0.#}%";
        if (!board.Visible) return;
        if (cards.Count == 0)
        {
            foreach (var player in state!.RunState.Players.OrderBy(p => p.NetId)) AddPortrait(player);
        }
        if (displayedRevision != formation.Party.Revision || displayedRules != formation.Rules)
        {
            foreach (var (id, card) in cards)
            {
                var parent = slots[formation.Party.Get(id)];
                if (card.GetParent() != parent) card.Reparent(parent, keepGlobalTransform: false);
            }
            headings[Rank.Front].Text = $"前排 {formation.Rules.FrontPercent:0.#}%";
            headings[Rank.Middle].Text = "中排 100%";
            headings[Rank.Back].Text = $"后排 {formation.Rules.BackPercent:0.#}%";
            displayedRevision = formation.Party.Revision;
            displayedRules = formation.Rules;
        }
        foreach (var (rank, button) in shortcuts) button.Disabled = !CanMove(rank);
        foreach (var player in state!.RunState.Players)
            cards[player.NetId].Modulate = player.Creature.IsAlive ? Colors.White : new Color("83908f");
        foreach (var (lineup, button) in presets) button.Disabled = !CanAct() || lineup == formation.Party.Current;
        board.ResetSize();
        var viewport = room.GetViewportRect();
        var buttonRect = launcher.GetGlobalRect();
        float safeBottom = viewport.End.Y - viewport.Size.Y * 0.24f;
        var screenPosition = new Vector2(
            Mathf.Clamp(buttonRect.Position.X, 8, Math.Max(8, viewport.Size.X - board.Size.X - 8)),
            Mathf.Clamp(buttonRect.End.Y + 8, 8, Math.Max(8, safeBottom - board.Size.Y)));
        board.GlobalPosition = screenPosition;
    }

    private bool PlaceLauncher()
    {
        var team = NRun.Instance?.GlobalUi.MultiplayerPlayerContainer;
        if (team is null || !team.IsVisibleInTree()) return false;
        var teammates = team.GetChildren().OfType<NMultiplayerPlayerState>().Where(node => node.IsVisibleInTree()).ToArray();
        if (teammates.Length == 0) return false;
        var occupied = teammates.Select(node => node.GetGlobalRect()).Where(rect => rect.HasArea()).ToList();
        if (occupied.Count == 0) return false;
        var teamBounds = occupied.Aggregate((first, other) => first.Merge(other));
        occupied.AddRange(room.CreatureNodes.Where(node => node.IsVisibleInTree()).Select(node => node.Hitbox.GetGlobalRect()));
        occupied.Add(room.Ui.EndTurnButton.GetGlobalRect());
        occupied.Add(room.Ui.EnergyCounterContainer.GetGlobalRect());
        var view = room.GetViewportRect().Grow(-8);
        var size = launcher.GetCombinedMinimumSize();
        launcher.Size = size;
        float x = Math.Max(view.Position.X, teamBounds.Position.X);
        var candidates = new[]
        {
            new Vector2(x, teamBounds.End.Y + 10),
            new Vector2(teamBounds.End.X + 10, teamBounds.Position.Y),
            new Vector2(teamBounds.End.X + 10, teamBounds.End.Y - size.Y)
        };
        foreach (var candidate in candidates)
        {
            var rect = new Rect2(candidate, size);
            if (!view.Encloses(rect) || rect.End.Y > view.End.Y - view.Size.Y * 0.22f ||
                occupied.Any(other => other.Grow(4).Intersects(rect))) continue;
            launcher.GlobalPosition = candidate;
            return true;
        }
        return false;
    }

    private void AddPortrait(Player player)
    {
        var id = player.NetId;
        var label = PlatformUtil.GetPlayerNameRaw(RunManager.Instance.NetService.Platform, id);
        var card = new PanelContainer { TooltipText = $"{label}\nID {id}", MouseFilter = Control.MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", RankStyle.Frame(new Color("26363a"), new Color("68716a"), 5, 2));
        var content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        card.AddChild(content);
        var portrait = new TextureRect
        {
            Texture = player.Character.IconTexture, CustomMinimumSize = new Vector2(72, 32),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        content.AddChild(portrait);
        var name = new Label
        {
            Text = id == local?.NetId ? "你 · " + label : label, CustomMinimumSize = new Vector2(84, 0),
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        name.AddThemeFontSizeOverride("font_size", 15);
        content.AddChild(name);
        card.SetDragForwarding(Callable.From<Vector2, Variant>(_ =>
            {
                if (id != local?.NetId || !CanAct()) return default;
                card.SetDragPreview(new TextureRect { Texture = portrait.Texture, Size = new Vector2(48, 48),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore });
                return Variant.From($"FrontBack:{id}");
            }),
            Callable.From<Vector2, Variant, bool>((_, data) => IsOwnDrag(data) && CanAct() && player.Creature.IsAlive &&
                FormationStore.Get(state) is { } formation && formation.Party.Get(id) != formation.Party.Get(local!.NetId)),
            Callable.From<Vector2, Variant>((_, _) => Send(RankOperation.Swap, teammate: id)));
        slots[Rank.Middle].AddChild(card);
        cards.Add(id, card);
    }

    private bool IsOwnDrag(Variant data) => local is not null && data.VariantType == Variant.Type.String &&
        data.AsString() == $"FrontBack:{local.NetId}";

    private bool CanAct() => state is not null && local is not null && FormationStore.CanSwap(state, local);
    private bool CanMove(Rank rank) => CanAct() && FormationStore.Get(state)!.Party.Get(local!.NetId) != rank;

    private void MoveSelf(Rank rank)
    {
        if (!CanMove(rank)) return;
        var party = FormationStore.Get(state)!.Party;
        // 同一阵型有目标排时优先换人，没有该排时切到包含它的阵型。
        var target = state!.Players.Where(player => player.Creature.IsAlive && party.Get(player.NetId) == rank)
            .OrderBy(player => player.NetId).FirstOrDefault();
        if (target is not null)
        {
            Send(RankOperation.Swap, teammate: target.NetId);
            return;
        }
        var lineup = rank == Rank.Middle
            ? party.Count == 4 ? Lineup.Spread : Lineup.Together
            : party.Count == 4 && party.Current != Lineup.Split ? Lineup.Spread : Lineup.Split;
        Send(RankOperation.Preset, lineup, rank);
    }

    private void Send(RankOperation operation, Lineup lineup = Lineup.Together, Rank rank = Rank.Middle, ulong teammate = 0)
    {
        if (!CanAct()) return;
        var player = local!;
        var request = new NetSwapAction
        {
            TurnNumber = player.PlayerCombatState!.TurnNumber, RoundNumber = state!.RoundNumber,
            Operation = operation, Rank = rank, Lineup = lineup, Teammate = teammate
        };
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new SwapAction(player, request));
    }
}
