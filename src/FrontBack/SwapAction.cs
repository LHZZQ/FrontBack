using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace FrontBack;

// 房主为所有操作排序。各端用同一队伍状态验证剩余名额和目标。
public sealed class SwapAction(Player player, NetSwapAction request) : GameAction
{
    private readonly MegaCrit.Sts2.Core.Combat.ICombatState? requestedCombat = player.Creature.CombatState;
    public override ulong OwnerId => player.NetId;
    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

    protected override Task ExecuteAction()
    {
        var combat = player.Creature.CombatState;
        if (combat is null || !ReferenceEquals(combat, requestedCombat) || combat.RoundNumber != request.RoundNumber ||
            player.PlayerCombatState?.TurnNumber != request.TurnNumber ||
            !FormationStore.CanSwap(combat, player)) return Task.CompletedTask;

        if (request.Operation == RankOperation.Swap &&
            !combat.Players.Any(p => p.NetId == request.Teammate && p.Creature.IsAlive)) return Task.CompletedTask;

        var formation = FormationStore.Get(combat)!;
        if (formation.Party.Apply(player.NetId, request.Operation, request.Lineup, request.Rank, request.Teammate))
        {
            AttackIntentDisplay.Refresh(combat);
            SavedRanks.TrySave(combat.RunState, formation.Party);
            FormationVisuals.Apply(combat, animate: true);
        }
        return Task.CompletedTask;
    }

    public override INetAction ToNetAction() => request;
}

public struct NetSwapAction : INetAction
{
    public int TurnNumber;
    public int RoundNumber;
    public RankOperation Operation;
    public Rank Rank;
    public Lineup Lineup;
    public ulong Teammate;

    public GameAction ToGameAction(Player player) => new SwapAction(player, this);
    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(TurnNumber);
        writer.WriteInt(RoundNumber);
        writer.WriteByte((byte)Operation);
        writer.WriteByte((byte)Rank);
        writer.WriteByte((byte)Lineup);
        writer.WriteULong(Teammate);
    }
    public void Deserialize(PacketReader reader)
    {
        TurnNumber = reader.ReadInt();
        RoundNumber = reader.ReadInt();
        Operation = (RankOperation)reader.ReadByte();
        Rank = (Rank)reader.ReadByte();
        Lineup = (Lineup)reader.ReadByte();
        Teammate = reader.ReadULong();
    }
}
