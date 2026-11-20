using Godot;

namespace FrontBack;

internal static class RankStyle
{
    public static string Title(Rank rank) => rank switch { Rank.Front => "前排", Rank.Back => "后排", _ => "中排" };
    public static Color Color(Rank rank) => new(rank switch
    {
        Rank.Front => "efbd67", Rank.Back => "81c8ec", _ => "b9c7ba"
    });

    public static StyleBoxFlat Frame(Color background, Color edge, int x = 12, int y = 8) => new()
    {
        BgColor = background, BorderColor = edge,
        BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        ContentMarginLeft = x, ContentMarginRight = x, ContentMarginTop = y, ContentMarginBottom = y
    };

    public static Button Button(string title, Color? tint = null)
    {
        var color = tint ?? new Color("c4a36d");
        var button = new Button { Text = title };
        button.AddThemeStyleboxOverride("normal", Frame(new Color("1e2e35"), color.Darkened(0.36f)));
        button.AddThemeStyleboxOverride("hover", Frame(new Color("334a50"), color));
        button.AddThemeStyleboxOverride("pressed", Frame(new Color("142129"), color));
        button.AddThemeStyleboxOverride("disabled", Frame(new Color("182229"), new Color("535953")));
        button.AddThemeStyleboxOverride("focus", Frame(Colors.Transparent, color.Lightened(0.25f)));
        button.AddThemeColorOverride("font_color", new Color("f4e8cf"));
        button.AddThemeFontSizeOverride("font_size", 20);
        return button;
    }
}
