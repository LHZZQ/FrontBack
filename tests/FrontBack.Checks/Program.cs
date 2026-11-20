using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using FrontBack;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
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
        var formation = new Formation(22, 11);
        Equal(11UL, formation.FrontId, "Initial order independent of local perspective");
        formation.Swap(); formation.Swap();
        Equal(11UL, formation.FrontId, "Two ordered swap requests return to original");
        Equal(2, formation.Revision, "Each swap increments revision");
        Equal(1m, formation.Multiplier(999), "Unknown player unaffected");

        var packet = new NetSwapAction { TurnNumber = 70000, RoundNumber = 123 };
        var writer = new PacketWriter(); packet.Serialize(writer);
        var reader = new PacketReader(); reader.Reset(writer.Buffer);
        var decoded = new NetSwapAction(); decoded.Deserialize(reader);
        Equal(packet.TurnNumber, decoded.TurnNumber, "Native packet turn roundtrip (32 bits)");
        Equal(packet.RoundNumber, decoded.RoundNumber, "Native packet round roundtrip");

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
        var combat = Stub.Create<ICombatState>((method, _) => method.Name switch
        {
            "get_RunState" => run,
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
        Equal(true, harmony.GetPatchedMethods().Count() == 5, "All five game integration patches installed");
        BeforeCombatRulesPatch.Original(run, combat).GetAwaiter().GetResult();
        Equal(true, true, "Reverse patch invokes original pre-combat hooks without recursion");
        var hook = AccessTools.Method(typeof(Hook), "ModifyDamageInternal");
        decimal Damage(decimal amount, Creature? dealer = null, ValueProp props = ValueProp.Move,
            ModifyDamageHookType hooks = ModifyDamageHookType.All) =>
            (decimal)hook.Invoke(null, new object?[] { run, combat, target, dealer ?? monster,
                amount, props, null, null, hooks, null })!;
        Equal(30m, Damage(20), "Actual game hook: front 20 -> 30 before block");
        FormationStore.Get(combat)!.Swap();
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
        FormationStore.Get(combat)!.Swap();
        Equal(50.1m, Damage(20), "Custom front percentage reaches actual damage hook");
        var intangible = (MegaCrit.Sts2.Core.Models.Powers.IntangiblePower)RuntimeHelpers.GetUninitializedObject(
            typeof(MegaCrit.Sts2.Core.Models.Powers.IntangiblePower));
        typeof(AbstractModel).GetField("<IsMutable>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(intangible, true);
        typeof(PowerModel).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(intangible, target);
        listeners.Add(intangible);
        Equal(1m, Damage(20), "Actual Intangible cap remains 1 for front row");
        SyncChecks.Run(first, second, run, (condition, name) => Equal(true, condition, name));
        harmony.UnpatchAll("FrontBack.Checks");
        Console.WriteLine($"{count} checks passed. In-game animation and two-peer networking remain manual tests.");
    }
}
