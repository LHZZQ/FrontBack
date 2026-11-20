namespace FrontBack;

// 与游戏无关的规则：排序后的稳定玩家 ID 决定每场战斗的初始站位。
public sealed class Formation
{
    public ulong FirstId { get; }
    public ulong SecondId { get; }
    public ulong FrontId { get; private set; }
    public int Revision { get; private set; }
    public DamageRules Rules { get; private set; } = new();

    public Formation(ulong first, ulong second)
    {
        if (first == second) throw new ArgumentException("需要两位不同玩家。");
        FirstId = Math.Min(first, second);
        SecondId = Math.Max(first, second);
        FrontId = FirstId;
    }

    public bool Contains(ulong id) => id == FirstId || id == SecondId;
    public decimal Multiplier(ulong id) => !Contains(id) ? 1m :
        (id == FrontId ? Rules.FrontPercent : Rules.BackPercent) / 100m;

    public void SetRules(DamageRules rules)
    {
        rules.Validate();
        Rules = rules;
    }

    public void Swap()
    {
        FrontId = FrontId == FirstId ? SecondId : FirstId;
        Revision++;
    }
}
