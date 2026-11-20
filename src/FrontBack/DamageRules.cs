using System.Text.Json;

namespace FrontBack;

public sealed record DamageRules
{
    public decimal FrontPercent { get; init; } = 150m;
    public decimal BackPercent { get; init; } = 50m;

    public void Validate()
    {
        foreach (var percent in new[] { FrontPercent, BackPercent })
            if (percent is < 0m or > 10000m || decimal.Round(percent, 1) != percent)
                throw new InvalidDataException("伤害百分比范围为 0–10000%，最多一位小数。");
    }

    public int[] ToWire(bool isHost)
    {
        Validate();
        return [(int)(FrontPercent * 10m), (int)(BackPercent * 10m), isHost ? 1 : 0];
    }

    public static DamageRules FromWire(IReadOnlyList<int> values)
    {
        if (values.Count != 3 || values[2] is not (0 or 1))
            throw new InvalidDataException("前后排倍率同步数据无效。");
        var rules = new DamageRules { FrontPercent = values[0] / 10m, BackPercent = values[1] / 10m };
        rules.Validate();
        return rules;
    }
}

public sealed class DamageSettingsStore(string filePath)
{
    public DamageRules Current { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(filePath)) return;
        var rules = JsonSerializer.Deserialize<DamageRules>(File.ReadAllText(filePath))
            ?? throw new InvalidDataException("前后排倍率配置为空。");
        rules.Validate();
        Current = rules;
    }

    public void Save(DamageRules rules)
    {
        rules.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var temporary = filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, filePath, overwrite: true);
        Current = rules;
    }
}
