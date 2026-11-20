using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace FrontBack;

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen._Ready))]
public static class SettingsPanel
{
    public static void Postfix(NSettingsScreen __instance)
    {
        if (!ModEntry.Ready) return;
        var settings = __instance.GetNode<NSettingsPanel>("%GeneralSettings");
        var content = settings.Content;
        if (content.HasNode("FrontBackSettingsButton")) return;
        var button = new Button
        {
            Name = "FrontBackSettingsButton", Text = "前后排伤害设置 / FrontBack",
            CustomMinimumSize = new Vector2(0, 64)
        };
        button.AddThemeFontSizeOverride("font_size", 26);
        content.AddChild(button);
        // 原版面板在自己的 _Ready 中计算滚动范围；新增条目后需重新计算。
        content.MinimumSizeChanged += () => settings.CallDeferred("RefreshSize");
        settings.CallDeferred("RefreshSize");
        button.Pressed += () => Open(__instance);
    }

    private static void Open(NSettingsScreen screen)
    {
        if (screen.HasNode("FrontBackSettingsOverlay")) return;
        var overlay = new Control { Name = "FrontBackSettingsOverlay", MouseFilter = Control.MouseFilterEnum.Stop };
        screen.AddChild(overlay);
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.85f) };
        overlay.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        overlay.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(680, 0) };
        center.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 28);
        panel.AddChild(margin);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 18);
        margin.AddChild(rows);
        Label Text(string value, int size = 24)
        {
            var label = new Label { Text = value };
            label.AddThemeFontSizeOverride("font_size", size);
            rows.AddChild(label);
            return label;
        }
        Text("前后排伤害设置", 32);
        Text("100% = 原版伤害；0% = 不受到此类攻击伤害。\n范围 0–10000%，支持一位小数。", 22);
        SpinBox Percentage(string title, decimal value)
        {
            var row = new HBoxContainer();
            rows.AddChild(row);
            var label = new Label { Text = title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            label.AddThemeFontSizeOverride("font_size", 26);
            row.AddChild(label);
            var input = new SpinBox
            {
                MinValue = 0, MaxValue = 10000, Step = 0.1, Value = (double)value,
                Suffix = "%", CustomMinimumSize = new Vector2(240, 52)
            };
            input.GetLineEdit().AddThemeFontSizeOverride("font_size", 26);
            row.AddChild(input);
            return input;
        }
        var current = ModEntry.DamageSettings.Current;
        var front = Percentage("前排承受伤害", current.FrontPercent);
        var back = Percentage("后排承受伤害", current.BackPercent);
        Text("联机使用房主的数值。保存后从下一场战斗生效。\n客机可保存自己的偏好，供以后自己当房主时使用。", 22);
        var status = Text("");
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.CustomMinimumSize = new Vector2(600, 0);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 16);
        rows.AddChild(buttons);
        Button Action(string title, Action callback)
        {
            var button = new Button { Text = title, CustomMinimumSize = new Vector2(170, 52) };
            button.AddThemeFontSizeOverride("font_size", 24);
            button.Pressed += callback;
            buttons.AddChild(button);
            return button;
        }
        Action("恢复默认", () => { front.Value = 150; back.Value = 50; status.Text = "点击保存应用默认值。"; });
        Action("取消", () => { overlay.QueueFree(); });
        Action("保存", () =>
        {
            try
            {
                front.Apply();
                back.Apply();
                ModEntry.DamageSettings.Save(new DamageRules
                {
                    FrontPercent = decimal.Round((decimal)front.Value, 1),
                    BackPercent = decimal.Round((decimal)back.Value, 1)
                });
                status.Text = "已保存，下一场战斗生效。";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                status.Text = "保存失败：" + error.Message;
            }
        });
        front.GetLineEdit().GrabFocus();
    }
}
