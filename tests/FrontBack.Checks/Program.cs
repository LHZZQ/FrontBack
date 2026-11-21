using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using FrontBack;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(args.Length > 0 ? args[0] : @"D:\steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64", name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
Checks.Run();

public class Stub : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler = (_, _) => throw new NotSupportedException();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => Handler(method!, arguments);
    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var result = Create<T, Stub>();
        ((Stub)(object)result).Handler = handler;
        return result;
    }
}

static class Checks
{
    static int count;
    static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"FAIL {name}: expected={expected}, actual={actual}");
        Console.WriteLine($"PASS {name}"); count++;
    }
    static void Field(object obj, string name, object? value) =>
        obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run()
    {
        ulong[] ids = [11, 22, 33, 44];
        for (int size = 2; size <= 4; size++)
        {
            var ranks = new PartyRanks(ids.Take(size).Reverse());
            Equal(true, ranks.Snapshot().All(r => r == Rank.Middle), $"{size} players start in middle");
            Equal(size == 4, ranks.Apply(11, RankOperation.Preset, Lineup.Spread, Rank.Front), "Only four-player parties can use 1/2/1");
            if (size == 4)
                Equal(true, ranks.Snapshot().SequenceEqual(new[] { Rank.Front, Rank.Middle, Rank.Middle, Rank.Back }), "Four-player spread is 1/2/1");
            Equal(true, ranks.Apply(11, RankOperation.Preset, Lineup.Split, Rank.Front), "Split preset applies");
            Equal(size == 4 ? 2 : 1, ranks.Snapshot().Count(r => r == Rank.Front), "Split uses correct front count");
            Equal(size == 2 ? 1 : 2, ranks.Snapshot().Count(r => r == Rank.Back), "Split uses correct back count");
            Equal(true, ranks.Apply(11, RankOperation.Swap, teammate: ranks.Players.First(id => ranks.Get(id) == Rank.Back)), "Swap with an occupied row");
            var restored = new PartyRanks(ids.Take(size));
            restored.Restore(ranks.Snapshot());
            Equal(true, restored.Snapshot().SequenceEqual(ranks.Snapshot()), "Snapshot restores by stable player ID");
            Equal(true, ranks.Apply(11, RankOperation.Preset, Lineup.Together), "Reset moves every player together");
            Equal(true, ranks.Snapshot().All(r => r == Rank.Middle), "Everyone returns to middle");
        }
        var formation = new Formation(new PartyRanks(ids));
        Equal(1m, formation.Multiplier(999), "Unknown player unaffected");

        var packet = new NetSwapAction { TurnNumber = 70000, RoundNumber = 123,
            Operation = RankOperation.Swap, Rank = Rank.Back, Lineup = Lineup.Spread, Teammate = 76561198000000001UL };
        var writer = new PacketWriter(); packet.Serialize(writer);
        var reader = new PacketReader(); reader.Reset(writer.Buffer);
        var decoded = new NetSwapAction(); decoded.Deserialize(reader);
        Equal(packet.TurnNumber, decoded.TurnNumber, "Native packet turn roundtrip (32 bits)");
        Equal(packet.RoundNumber, decoded.RoundNumber, "Native packet round roundtrip");
        Equal(packet.Operation, decoded.Operation, "Operation serializes");
        Equal(packet.Rank, decoded.Rank, "Rank serializes");
        Equal(packet.Lineup, decoded.Lineup, "Lineup serializes");
        Equal(packet.Teammate, decoded.Teammate, "Full Steam ID serializes");

        var first = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var second = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        Field(first, "<NetId>k__BackingField", 11UL);
        Field(second, "<NetId>k__BackingField", 22UL);
        var listeners = new List<AbstractModel>();
        var run = Stub.Create<IRunState>((method, arguments) => method.Name switch
        {
            "get_Players" => new List<Player> { first, second },
            "IterateHookListeners" => listeners,
            "GetPlayerSlotIndex" => ReferenceEquals(arguments![0], first) ? 0 : 1,
            "GetPlayer" => (ulong)arguments![0]! == first.NetId ? first : second,
            _ => throw new NotSupportedException(method.Name)
        });
        var combat = Stub.Create<ICombatState>((method, arguments) => method.Name switch
        {
            "get_RunState" => run,
            "GetPlayer" => (ulong)arguments![0]! == first.NetId ? first : second,
            _ => throw new NotSupportedException(method.Name)
        });
        var target = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
        Field(target, "<Player>k__BackingField", first);
        var monster = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
        Field(monster, "<Monster>k__BackingField", RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.Monsters.Axebot)));
        typeof(ModEntry).GetField("<Ready>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);

        // 对本机真实 Hook 方法安装补丁并调用，验证插入位置与有效 IL，而非只测倍率函数。
        var harmony = new Harmony("FrontBack.Checks");
        harmony.PatchAll(typeof(ModEntry).Assembly);
        Equal(10, harmony.GetPatchedMethods().Count(), "All game integration patches installed");
        BeforeCombatRulesPatch.Original(run, combat).GetAwaiter().GetResult();
        Equal(true, true, "Reverse patch invokes original pre-combat hooks without recursion");
        var hook = AccessTools.Method(typeof(Hook), "ModifyDamageInternal");
        decimal Damage(decimal amount, Creature? dealer = null, ValueProp props = ValueProp.Move,
            ModifyDamageHookType hooks = ModifyDamageHookType.All) =>
            (decimal)hook.Invoke(null, new object?[] { run, combat, target, dealer ?? monster,
                amount, props, null, null, hooks, null })!;
        var party = FormationStore.Get(combat)!.Party;
        Equal(20m, Damage(20), "Actual game hook: new party takes 100% damage");
        party.Apply(11, RankOperation.Preset, Lineup.Split, Rank.Front);
        Equal(30m, Damage(20), "Actual game hook: front 20 -> 30 before block");
        var nextCombat = Stub.Create<ICombatState>((method, _) => method.Name == "get_RunState" ? run : throw new NotSupportedException());
        Equal(1.5m, FormationStore.Get(nextCombat)!.Multiplier(11), "Selected rank carries over into next battle");
        party.Apply(11, RankOperation.Swap, teammate: 22);
        Equal(10m, Damage(20), "Actual game hook after swap: back 20 -> 10");
        Equal(2.5m, Damage(5), "Hook retains fraction for vanilla rounding");
        Equal(20m, Damage(20, target), "Player self damage unaffected");
        Equal(20m, Damage(20, props: ValueProp.Unblockable), "HP loss unaffected");
        Equal(20m, Damage(20, hooks: ModifyDamageHookType.Cap), "Already calculated damage not multiplied twice");
        Field(target, "_currentHp", 100);
        Field(target, "_block", 8);
        var incoming = Damage(20); // 此时为后排，20 -> 10，再扣 8 点格挡。
        var blocked = target.DamageBlockInternal(incoming, ValueProp.Move);
        target.LoseHpInternal(incoming - blocked, ValueProp.Move);
        Equal(98, target.CurrentHp, "Vanilla block and HP methods: back 20 with 8 block loses 2 HP");
        Field(target, "_currentHp", 100);
        Field(target, "_block", 0);
        target.LoseHpInternal(Damage(5), ValueProp.Move);
        Equal(98, target.CurrentHp, "Vanilla rounding: back 5 loses 2 HP");
        Equal(true, FormationStore.Get(null) is null, "No combat leaves damage unchanged");
        FormationStore.Get(combat)!.SetRules(new DamageRules { FrontPercent = 250.5m, BackPercent = 25.5m });
        Equal(5.1m, Damage(20), "Custom back percentage reaches actual damage hook");
        party.Apply(11, RankOperation.Swap, teammate: 22);
        Equal(50.1m, Damage(20), "Custom front percentage reaches actual damage hook");
        var intangible = (MegaCrit.Sts2.Core.Models.Powers.IntangiblePower)RuntimeHelpers.GetUninitializedObject(
            typeof(MegaCrit.Sts2.Core.Models.Powers.IntangiblePower));
        typeof(AbstractModel).GetField("<IsMutable>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(intangible, true);
        typeof(PowerModel).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(intangible, target);
        listeners.Add(intangible);
        Equal(1m, Damage(20), "Actual Intangible cap remains 1 for front row");
        listeners.Clear();
        CheckIntercept(run, combat, first, second, target, monster, listeners, hook);
        SyncChecks.Run(first, second, run, (condition, name) => Equal(true, condition, name));
        harmony.UnpatchAll("FrontBack.Checks");
        Console.WriteLine($"{count} checks passed. In-game animation and two-peer networking remain manual tests.");
    }

    private static void CheckIntercept(IRunState run, ICombatState combat, Player local, Player teammate,
        Creature covered, Creature monster, List<AbstractModel> listeners, MethodInfo damageHook)
    {
        var recipient = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
        Field(recipient, "<Player>k__BackingField", teammate);
        Field(recipient, "_currentHp", 70);
        Field(local, "<Creature>k__BackingField", covered);
        Field(teammate, "<Creature>k__BackingField", recipient);
        Field(local, "_runState", run);
        Field(teammate, "_runState", run);
        Field(local, "_relics", new List<RelicModel>());
        Field(teammate, "_relics", new List<RelicModel>());
        covered.CombatState = combat;
        recipient.CombatState = combat;
        monster.CombatState = combat;

        T Power<T>(Creature owner, Creature? applier = null) where T : PowerModel
        {
            var power = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(AbstractModel).GetField("<IsMutable>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(power, true);
            typeof(PowerModel).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(power, owner);
            power.Applier = applier;
            listeners.Add(power);
            return power;
        }

        var cover = Power<CoveredPower>(covered, recipient);
        var intercept = Power<InterceptPower>(recipient);
        var data = AccessTools.Method(typeof(InterceptPower), "InitInternalData").Invoke(intercept, null)!;
        ((List<Creature>)AccessTools.Field(data.GetType(), "coveredCreatures").GetValue(data)!).Add(covered);
        AccessTools.Field(typeof(PowerModel), "_internalData").SetValue(intercept, data);
        Field(covered, "_powers", new List<PowerModel> { cover });
        Field(recipient, "_powers", new List<PowerModel> { intercept });
        var weak = Power<WeakPower>(monster);
        Field(monster, "_powers", new List<PowerModel> { weak });

        var formation = FormationStore.Get(combat)!;
        formation.SetRules(new DamageRules());
        LocalContext.NetId = local.NetId;
        var intent = new SingleAttackIntent(24);
        Creature[] targets = [covered, recipient];
        decimal Actual(Creature creature) => (decimal)damageHook.Invoke(null,
            new object?[] { run, combat, creature, monster, 24m, ValueProp.Move, null, null, ModifyDamageHookType.All, null })!;

        Equal(9, intent.GetSingleDamage(targets, monster), "Weak attack: protected front player previews back teammate taking 9 for them");
        Equal(0m, Actual(covered), "Intercept still prevents direct damage to the protected player");
        Equal(18m, Actual(recipient), "Back interceptor still takes both shares in the original damage calculation");
        Field(recipient, "_block", 9);
        var incoming = Actual(recipient);
        var blocked = recipient.DamageBlockInternal(incoming, ValueProp.Move);
        Equal(9m, incoming - blocked, "Back interceptor with 9 block loses 9 HP from both shares");
        formation.Party.Apply(local.NetId, RankOperation.Swap, teammate: teammate.NetId);
        Equal(27, intent.GetSingleDamage(targets, monster), "Weak attack: protected back player previews front teammate taking 27 for them");
        Equal(54, new MultiAttackIntent(24, 2).GetTotalDamage(targets, monster), "Multi-hit intent retains its original repeat count");
        Equal(54m, Actual(recipient), "Front interceptor still takes both shares in the original damage calculation");
        Power<IntangiblePower>(recipient);
        Equal(1, intent.GetSingleDamage(targets, monster), "Intercept preview applies the receiving teammate's damage cap");
        listeners.Remove(cover);
        ((List<PowerModel>)covered.Powers).Remove(cover);
        Equal(9, intent.GetSingleDamage(targets, monster), "Expired cover restores the local player's normal preview");
        LocalContext.NetId = null;
        listeners.Clear();
    }
}
