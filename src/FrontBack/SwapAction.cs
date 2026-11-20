using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace FrontBack;

// 只提交操作，不在点击时改本地状态。房主排序后，各端执行同一个动作。
public sealed class SwapAction(Player player, int turnNumber, int roundNumber) : GameAction
{
    public override ulong OwnerId => player.NetId;
    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

    protected override Task ExecuteAction()
    {
        var combat = player.Creature.CombatState;
        if (combat is not null && combat.RoundNumber == roundNumber &&
            player.PlayerCombatState?.TurnNumber == turnNumber &&
            FormationStore.CanSwap(combat, player))
        {
            var formation = FormationStore.Get(combat)!;
            formation.Swap();
            FormationVisuals.Apply(combat, animate: true);
            Godot.GD.Print($"[FrontBack] swap revision={formation.Revision} front={formation.FrontId}");
        }
        return Task.CompletedTask;
    }

    public override INetAction ToNetAction() => new NetSwapAction
    {
        TurnNumber = turnNumber,
        RoundNumber = roundNumber
    };
}

// v0.113.1 的 ActionTypes.Initialize 会自动发现 Mod 中的 INetAction 类型。
public struct NetSwapAction : INetAction
{
    public int TurnNumber;
    public int RoundNumber;
    public GameAction ToGameAction(Player player) => new SwapAction(player, TurnNumber, RoundNumber);
    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(TurnNumber);
        writer.WriteInt(RoundNumber);
    }
    public void Deserialize(PacketReader reader)
    {
        TurnNumber = reader.ReadInt();
        RoundNumber = reader.ReadInt();
    }
}
