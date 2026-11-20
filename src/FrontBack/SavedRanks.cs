using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace FrontBack;

internal static class SavedRanks
{
    private static readonly AccessTools.FieldRef<RunManager, long> StartTime =
        AccessTools.FieldRefAccess<RunManager, long>("_startTime");

    private static string PathFor(IRunState run)
    {
        var key = $"{StartTime(RunManager.Instance)}:{run.Rng.Seed}:{string.Join(",", run.Players.Select(p => p.NetId).Order())}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(OS.GetUserDataDir(), "mod_data", ModEntry.Id, "ranks", hash + ".json");
    }

    public static void TryRestore(IRunState run, PartyRanks party)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host || party.Revision != 0) return;
        try
        {
            var path = PathFor(run);
            if (File.Exists(path)) party.Restore(JsonSerializer.Deserialize<Rank[]>(File.ReadAllText(path)) ?? []);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            GD.PushWarning($"[FrontBack] 读取队伍站位失败：{error.Message}");
        }
    }

    public static void TrySave(IRunState run, PartyRanks party)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;
        try
        {
            var path = PathFor(run);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(party.Snapshot()));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"[FrontBack] 保存队伍站位失败：{error.Message}");
        }
    }
}
