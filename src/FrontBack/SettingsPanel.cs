using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.addons.mega_text;

namespace FrontBack;

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen._Ready))]
public static class SettingsPanel
{
    public static void Postfix(NSettingsScreen __instance)
    {
        if (!ModEntry.Ready) return;
        var settings = __instance.GetNode<NSettingsPanel>("%GeneralSettings");
        var content = settings.Content;
        if (content.HasNode("FrontBackSettingsRow")) return;
        // 复用原版整行布局和 NSettingsButton，保留贴图、字体、音效与交互动画。
        // 不复制信号连接，避免按钮同时触发原版“模组设置”。
        var template = __instance.GetNode<Control>("%Modding");
        var templateButton = __instance.GetNode<NSettingsButton>("%ModdingButton");
        var row = CopyNative(template);
        row.Name = "FrontBackSettingsRow";
        row.Visible = true;
        row.GetNode<MegaRichTextLabel>("Label").Text = "前后排伤害设置";
        var button = row.GetNode<NSettingsButton>(template.GetPathTo(templateButton));
        button.Name = "FrontBackSettingsButton";
        var divider = CopyNative(__instance.GetNode<Control>("%ModdingDivider"));
        divider.Name = "FrontBackSettingsDivider";
        divider.Visible = true;
        content.AddChild(divider);
        content.AddChild(row);
        button.Enable();
        button.GetNode<MegaLabel>("Label").SetTextAutoSize("打开设置");
        // 原版面板在自己的 _Ready 中计算滚动范围；新增条目后需重新计算。
        content.MinimumSizeChanged += () => settings.CallDeferred("RefreshSize");
        settings.CallDeferred("RefreshSize");
        settings.CallDeferred("UpdateNavigation");
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Open(__instance)));
    }

    private static T CopyNative<T>(T source) where T : Node
    {
        var copy = (T)source.Duplicate((int)Node.DuplicateFlags.Scripts);
        void ClearUniqueNames(Node node)
        {
            node.UniqueNameInOwner = false;
            foreach (var child in node.GetChildren()) ClearUniqueNames(child);
        }
        ClearUniqueNames(copy);
        return copy;
    }

    private static void Open(NSettingsScreen screen)
    {
        if (screen.HasNode("FrontBackSettingsOverlay")) return;
        var overlay = new Control { Name = "FrontBackSettingsOverlay", MouseFilter = Control.MouseFilterEnum.Stop };
        screen.AddChild(overlay);
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.72f) };
        overlay.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        overlay.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        var frame = new StyleBoxFlat
        {
            BgColor = new Color("17232a"), BorderColor = new Color("877451"),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 16
        };
        panel.AddThemeStyleboxOverride("panel", frame);
        center.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 32);
        panel.AddChild(margin);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 18);
        margin.AddChild(rows);
        var nativeButton = screen.GetNode<NSettingsButton>("%ModdingButton");
        var nativeLabel = nativeButton.GetNode<MegaLabel>("Label");
        void Typography(Control control, int size)
        {
            control.AddThemeFontOverride("font", nativeLabel.GetThemeFont("font"));
            control.AddThemeFontSizeOverride("font_size", size);
            control.AddThemeColorOverride("font_color", new Color("f4e8cd"));
            control.AddThemeColorOverride("font_outline_color", new Color("10181e"));
            control.AddThemeConstantOverride("outline_size", 2);
        }
        Label Text(string value, int size = 24)
        {
            var label = new Label { Text = value };
            Typography(label, size);
            rows.AddChild(label);
            return label;
        }
        Text("前后排伤害设置", 32);
        Text("100% = 原版伤害；0% = 不受到此类攻击伤害。\n范围 0–10000%，支持一位小数。", 22);
        SpinBox Percentage(string title, decimal value)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            rows.AddChild(row);
            var label = new Label { Text = title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Typography(label, 26);
            row.AddChild(label);
            var input = new SpinBox
            {
                MinValue = 0, MaxValue = 10000, Step = 0.1, Value = (double)value,
                CustomMinimumSize = new Vector2(220, 52)
            };
            Typography(input.GetLineEdit(), 26);
            var normal = new StyleBoxFlat
            {
                BgColor = new Color("0f1a21"), BorderColor = new Color("586365"),
                BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                ContentMarginLeft = 14, ContentMarginRight = 14,
                ContentMarginTop = 8, ContentMarginBottom = 8,
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
            };
            input.GetLineEdit().AddThemeStyleboxOverride("normal", normal);
            var focus = (StyleBoxFlat)normal.Duplicate();
            focus.BgColor = Colors.Transparent;
            focus.BorderColor = new Color("d5bb7c");
            input.GetLineEdit().AddThemeStyleboxOverride("focus", focus);
            row.AddChild(input);
            // 单位独立于可编辑文本：聚焦、全选、清空输入时都不会消失或被改动。
            var unit = new Label
            {
                Text = "%", CustomMinimumSize = new Vector2(34, 52),
                VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore
            };
            Typography(unit, 26);
            row.AddChild(unit);
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
        NSettingsButton Action(string title, Action callback)
        {
            var button = CopyNative(nativeButton);
            button.Name = title;
            button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
            button.CustomMinimumSize = new Vector2(208, 60);
            button.Size = button.CustomMinimumSize;
            button.Scale = Vector2.One;
            buttons.AddChild(button);
            button.Enable();
            button.GetNode<MegaLabel>("Label").SetTextAutoSize(title);
            button.Resized += () => button.PivotOffset = button.Size * 0.5f;
            button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => callback()));
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
