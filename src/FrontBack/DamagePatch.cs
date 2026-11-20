using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.ValueProps;

namespace FrontBack;

[HarmonyPatch(typeof(Hook), "ModifyDamageInternal")]
public static class DamagePatch
{
    // 在原版乘算之后、伤害上限之前插入。保留无实体等上限效果与原版格挡/取整。
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        if (__originalMethod.GetMethodBody()?.LocalVariables.FirstOrDefault()?.LocalType != typeof(decimal))
            throw new InvalidOperationException("伤害局部变量布局与 v0.113.1 不同。");
        var markers = new List<int>();
        for (int i = 2; i + 2 < code.Count; i++)
            if (code[i].opcode == OpCodes.Ldc_I4_8 &&
                code[i - 1].opcode == OpCodes.Box && Equals(code[i - 1].operand, typeof(ModifyDamageHookType)) &&
                code[i + 1].opcode == OpCodes.Box && Equals(code[i + 1].operand, typeof(ModifyDamageHookType)) &&
                code[i + 2].operand is MethodInfo method && method.Name == nameof(Enum.HasFlag) &&
                code[i - 2].opcode == OpCodes.Ldarg_S && Convert.ToInt32(code[i - 2].operand) == 8)
                markers.Add(i - 2);
        if (markers.Count != 1) throw new InvalidOperationException("未能唯一定位伤害上限阶段，停止加载。");
        int index = markers[0];
        var insert = new List<CodeInstruction>
        {
            new(OpCodes.Ldloc_0), new(OpCodes.Ldarg_1), new(OpCodes.Ldarg_2), new(OpCodes.Ldarg_3),
            new(OpCodes.Ldarg_S, (byte)5), new(OpCodes.Ldarg_S, (byte)8),
            new(OpCodes.Call, AccessTools.Method(typeof(DamagePatch), nameof(ApplyMultiplier))),
            new(OpCodes.Stloc_0)
        };
        insert[0].labels.AddRange(code[index].labels);
        code[index].labels.Clear();
        insert[0].blocks.AddRange(code[index].blocks);
        code[index].blocks.Clear();
        code.InsertRange(index, insert);
        return code;
    }

    public static decimal ApplyMultiplier(decimal damage, ICombatState? combat, Creature? target,
        Creature? dealer, ValueProp props, ModifyDamageHookType hooks)
    {
        if (!ModEntry.Ready || !hooks.HasFlag(ModifyDamageHookType.Multiplicative) ||
            dealer?.IsMonster != true || target?.IsPlayer != true ||
            !props.HasFlag(ValueProp.Move) || props.HasFlag(ValueProp.Unblockable)) return damage;
        return damage * (FormationStore.Get(combat)?.Multiplier(target.Player!.NetId) ?? 1m);
    }
}
