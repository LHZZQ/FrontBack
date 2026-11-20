using System.Text.Json;

namespace FrontBack;

// 配置只影响本地画面；玩法倍率写在 Formation 中，避免双方配置导致伤害不同步。
public sealed class VisualConfig
{
    public float RowOffsetY { get; set; } = 35;
    public double SwapAnimationSeconds { get; set; } = 0.2;

    public static VisualConfig Load(string directory)
    {
        var path = Path.Combine(directory, "FrontBack.config.json");
        if (!File.Exists(path)) return new();
        var config = JsonSerializer.Deserialize<VisualConfig>(File.ReadAllText(path)) ?? new();
        if (!float.IsFinite(config.RowOffsetY) || Math.Abs(config.RowOffsetY) > 100 ||
            !double.IsFinite(config.SwapAnimationSeconds) || config.SwapAnimationSeconds is < 0 or > 1)
            throw new InvalidDataException("FrontBack.config.json 中的画面参数无效。");
        return config;
    }
}
