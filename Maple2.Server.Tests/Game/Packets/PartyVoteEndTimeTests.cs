using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

public class PartyVoteEndTimeTests {
    // Client: PartyVoteCli::vfunc_1 (partyvotecli.cpp:113) reads the kind, the int vote id, then the int64 end time
    // its timer counts down to.
    [Test]
    public void ReadyCheckCarriesItsEndTime() {
        var vote = new PartyVote(PartyVoteType.ReadyCheck, new long[] { 1, 2 }, 1) { EndTime = 1_700_000_030 };
        var writer = new ByteWriter();
        writer.WriteClass<PartyVote>(vote);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadByte(), Is.EqualTo((byte) PartyVoteType.ReadyCheck));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(1_700_000_030));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
    }

    // The kick variant (partyvotecli.cpp:200) also reads the owner, the target, the name and a byte.
    [Test]
    public void KickCarriesItsEndTime() {
        var target = new PartyMember {
            Info = new PlayerInfo(new CharacterInfo(5, 6, "t", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []),
        };
        var vote = new PartyVote(PartyVoteType.Kick, new long[] { 1, 2, 3, 6 }, 1) {
            TargetMember = target,
            EndTime = 1_700_000_060,
        };
        var writer = new ByteWriter();
        writer.WriteClass<PartyVote>(vote);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadByte(), Is.EqualTo((byte) PartyVoteType.Kick));
        reader.ReadInt();
        Assert.That(reader.ReadLong(), Is.EqualTo(1_700_000_060));
        Assert.That(writer.Length, Is.EqualTo(1 + 4 + 8 + (4 + 4 * 8) + (4 + 8) + 4 + 8 + 8 + (2 + 2) + 1));
    }
}
