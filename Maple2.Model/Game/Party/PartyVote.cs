using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game.Party;

public class PartyVote : IByteSerializable {
    public readonly PartyVoteType Type;
    public readonly ICollection<long> Voters;
    public readonly ICollection<long> Approvals;
    public readonly ICollection<long> Disapprovals;
    public readonly long InitiatorId;
    // The vote's number in its party: the client keeps it and answers with it (PartyVoteCli::SendParty).
    public int Id { get; init; }
    public PartyMember? TargetMember;
    // When the vote ends, unix seconds: the client counts its timer down to it (PartyVoteCli::vfunc_1).
    public long EndTime { get; init; }
    public readonly byte VotesNeeded;

    public PartyVote(PartyVoteType type, ICollection<long> voters, long requestorId) {
        Type = type;
        Voters = voters;
        InitiatorId = requestorId;
        Approvals = new List<long> { requestorId };
        Disapprovals = new List<long>();
        if (type == PartyVoteType.Kick) {
            VotesNeeded = (byte) Math.Ceiling(Voters.Count / 2.0);
        }
    }

    public void WriteTo(IByteWriter writer) {
        writer.Write<PartyVoteType>(Type);
        writer.WriteInt(Id);
        writer.WriteLong(EndTime);

        writer.WriteInt(Voters.Count);
        foreach (long characterId in Voters) {
            writer.WriteLong(characterId);
        }

        writer.WriteInt(Approvals.Count);
        foreach (long characterId in Approvals) {
            writer.WriteLong(characterId);
        }

        writer.WriteInt(Disapprovals.Count);
        foreach (long characterId in Disapprovals) {
            writer.WriteLong(characterId);
        }

        if (Type == PartyVoteType.Kick) {
            writer.WriteLong(InitiatorId);
            writer.WriteLong(TargetMember!.CharacterId);
            writer.WriteUnicodeString(TargetMember.Name);
            writer.WriteByte(VotesNeeded); // The client's reason byte; see FORK.md.
        }
    }
}
