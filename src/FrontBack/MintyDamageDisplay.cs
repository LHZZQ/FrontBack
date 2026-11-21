using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Modding;

namespace FrontBack;

internal static class MintyDamageDisplay
{
    private const string RendererName = "MintySpire2.MintySpire2Code.combat.SummedIncomingDamageRender";
    private static Action? refreshBars;
    private static bool warnedAboutRefresh;

    [ThreadStatic]
    internal static bool CalculatingPersonalDamage;

    internal static void Initialize()
    {
        ModManager.OnModDetected += OnModDetected;
        foreach (var mod in ModManager.GetLoadedMods()) OnModDetected(mod);
    }

    private static void OnModDetected(Mod mod)
    {
        if (mod.state != ModLoadState.Loaded || mod.manifest?.id != "MintySpire2") return;
        ModManager.OnModDetected -= OnModDetected;
        try
        {
            foreach (var assembly in mod.assemblies)
                if (Connect(assembly)) return;
            GD.PushWarning("[FrontBack] Minty Spire 2 incoming damage renderer was not found.");
        }
        catch (Exception error)
        {
            GD.PushWarning($"[FrontBack] Minty Spire 2 damage preview integration is unavailable: {error.Message}");
        }
    }

    private static bool Connect(Assembly assembly)
    {
        var renderer = assembly.GetType(RendererName);
        if (renderer is null) return false;

        // Minty 的怪物死亡回调只刷新已注册的血条，可以在阵型变化后复用。
        var refresh = AccessTools.DeclaredMethod(renderer, "CatchMonsterDeath", Type.EmptyTypes).CreateDelegate<Action>();
        var calculate = AccessTools.DeclaredMethod(renderer, "CalculateIncomingDamage",
            [typeof(Creature), typeof(Dictionary<string, int>)]);
        if (calculate is null) return false;

        var type = typeof(MintyDamageDisplay);
        new Harmony(ModEntry.Id).Patch(calculate,
            prefix: new HarmonyMethod(type, nameof(BeginPersonalDamage)),
            finalizer: new HarmonyMethod(type, nameof(EndPersonalDamage)));
        refreshBars = refresh;
        return true;
    }

    private static void BeginPersonalDamage(out bool __state)
    {
        __state = CalculatingPersonalDamage;
        CalculatingPersonalDamage = true;
    }

    private static void EndPersonalDamage(bool __state) => CalculatingPersonalDamage = __state;

    internal static void Refresh(ICombatState combat)
    {
        if (refreshBars is null || combat.Enemies.Any(enemy => enemy.IsAlive && enemy.Monster?.NextMove is null)) return;
        try
        {
            refreshBars();
        }
        catch (Exception error)
        {
            if (warnedAboutRefresh) return;
            warnedAboutRefresh = true;
            GD.PushWarning($"[FrontBack] Could not refresh Minty Spire 2 damage totals: {error.Message}");
        }
    }
}
