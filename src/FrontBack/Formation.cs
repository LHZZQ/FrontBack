namespace FrontBack;

public enum Rank : byte { Middle, Front, Back }

public enum RankOperation : byte { Preset, Swap }

public enum Lineup : byte { Together, Spread, Split }

public sealed class PartyRanks
{
    private readonly Dictionary<ulong, Rank> ranks;

    public int Count => ranks.Count;
    public int Revision { get; private set; }
    public IEnumerable<ulong> Players => ranks.Keys;

    public PartyRanks(IEnumerable<ulong> players)
    {
        ranks = players.Order().ToDictionary(id => id, _ => Rank.Middle);
    }

    public Rank Get(ulong id) => ranks.GetValueOrDefault(id);

    public Rank[] Snapshot() => ranks.Keys.Order().Select(Get).ToArray();

    public void Restore(IReadOnlyList<Rank> snapshot)
    {
        if (snapshot.Count != Count || !SupportedLineups().Any(lineup =>
                Slots(lineup).Order().SequenceEqual(snapshot.Order())))
            throw new InvalidDataException("队伍站位数据不合法。");
        if (Snapshot().SequenceEqual(snapshot)) return;
        var ids = ranks.Keys.Order().ToArray();
        for (int i = 0; i < ids.Length; i++) ranks[ids[i]] = snapshot[i];
        Revision++;
    }

    public IEnumerable<Lineup> SupportedLineups() => Count == 4
        ? [Lineup.Together, Lineup.Spread, Lineup.Split] : [Lineup.Together, Lineup.Split];

    public Lineup Current => SupportedLineups().First(lineup => Slots(lineup).Order().SequenceEqual(ranks.Values.Order()));

    private Rank[] Slots(Lineup lineup) => (Count, lineup) switch
    {
        (_, Lineup.Together) => Enumerable.Repeat(Rank.Middle, Count).ToArray(),
        (4, Lineup.Spread) => [Rank.Front, Rank.Middle, Rank.Middle, Rank.Back],
        (4, Lineup.Split) => [Rank.Front, Rank.Front, Rank.Back, Rank.Back],
        (3, Lineup.Split) => [Rank.Front, Rank.Back, Rank.Back],
        (2, Lineup.Split) => [Rank.Front, Rank.Back],
        _ => []
    };

    public bool Apply(ulong owner, RankOperation operation, Lineup lineup = Lineup.Together,
        Rank preferred = Rank.Middle, ulong teammate = 0)
    {
        if (!ranks.ContainsKey(owner)) return false;
        switch (operation)
        {
            case RankOperation.Preset when SupportedLineups().Contains(lineup):
                return ApplyLineup(owner, lineup, preferred);
            case RankOperation.Swap when ranks.TryGetValue(teammate, out var other) && ranks[owner] != other:
                ranks[teammate] = ranks[owner];
                ranks[owner] = other;
                Revision++;
                return true;
            default:
                return false;
        }
    }

    private bool ApplyLineup(ulong owner, Lineup lineup, Rank preferred)
    {
        var available = Slots(lineup).ToList();
        var next = new Dictionary<ulong, Rank>();
        if (available.Remove(preferred)) next[owner] = preferred;
        var ordered = ranks.Keys.Order().ToArray();
        // 优先保留原来的位置，再按稳定玩家 ID 补满剩下的名额。
        foreach (var id in ordered)
            if (!next.ContainsKey(id) && available.Remove(ranks[id])) next[id] = ranks[id];
        foreach (var id in ordered)
        {
            if (next.ContainsKey(id)) continue;
            next[id] = available[0];
            available.RemoveAt(0);
        }
        int before = Revision;
        Restore(ordered.Select(id => next[id]).ToArray());
        return Revision != before;
    }
}

public sealed class Formation(PartyRanks party)
{
    public PartyRanks Party { get; } = party;
    public DamageRules Rules { get; private set; } = new();

    public decimal Multiplier(ulong id) => Party.Get(id) switch
    {
        Rank.Front => Rules.FrontPercent / 100m,
        Rank.Back => Rules.BackPercent / 100m,
        _ => 1m
    };

    public void SetRules(DamageRules rules)
    {
        rules.Validate();
        Rules = rules;
    }
}
