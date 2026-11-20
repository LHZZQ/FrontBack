using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;

namespace FrontBack;

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom._Ready))]
public static class CombatOverlay
{
    public static void Postfix(NCombatRoom __instance)
    {
        if (!ModEntry.Ready || __instance.Mode != CombatRoomMode.ActiveCombat) return;
        var panel = new VBoxContainer
        {
            Name = "FrontBackPanel",
            Position = new Vector2(ModEntry.Config.ButtonX, ModEntry.Config.ButtonY)
        };
        var status = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        var button = new Button { Text = "交换前后排", CustomMinimumSize = new Vector2(230, 48) };
        panel.AddChild(status);
        panel.AddChild(button);
        __instance.AddChild(panel);
        var timer = new Godot.Timer { WaitTime = 0.1, Autostart = true };
        panel.AddChild(timer);
        Player? LocalPlayer() => RunManager.Instance.DebugOnlyGetState()?.Players
            .FirstOrDefault(p => p.NetId == RunManager.Instance.NetService.NetId);
        void Refresh()
        {
            var player = LocalPlayer();
            var combat = player?.Creature.CombatState;
            var formation = FormationStore.Get(combat);
            panel.Visible = formation is not null;
            if (formation is null || player is null || combat is null) return;
            button.Disabled = !FormationStore.CanSwap(combat, player);
            status.Text = $"你在{(formation.FrontId == player.NetId ? "前排 ×1.5" : "后排 ×0.5")}\n双方可换位 · 次数不限";
        }
        button.Pressed += () =>
        {
            var player = LocalPlayer();
            if (player?.Creature.CombatState is not { } combat || !FormationStore.CanSwap(combat, player)) return;
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new SwapAction(
                player, player.PlayerCombatState!.TurnNumber, combat.RoundNumber));
        };
        timer.Timeout += Refresh;
        Refresh();
    }
}
