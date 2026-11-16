using Maple2.Model.Game;
using Maple2.Tools.Extensions;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The fixed Party modes read the way CPartyManager::OnParty (partymanager.cpp:447) and each mode's reader reads
// them, field by field: leave (long id, bool disband), kick and offline and leader change (long id), invite (unicode
// party name, int), gear score (long id, int), tombstone (long id, byte), match dungeon change (bool, int),
// dungeon match register (byte kind, bool registered, bool announce, one more byte), dungeon record (long), find
// dungeon cooldown (int), invite sent (unicode name), vote reply (long id, bool), survival squad register (two
// bools), dungeon room create (int). Disband, match confirm and vote end read nothing.
public class PartyModeLayoutTests {
    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Party));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    [Test]
    public void LongIdModes() {
        foreach ((ByteWriter writer, byte mode) in new[] {
                     (PartyPacket.Kicked(0x0102030405), (byte) 4),
                     (PartyPacket.NotifyLogout(0x0102030405), (byte) 6),
                     (PartyPacket.NotifyUpdateLeader(0x0102030405), (byte) 8),
                 }) {
            ByteReader reader = Open(writer, mode);
            Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405));
            Assert.That(reader.Available, Is.Zero);
        }
    }

    [Test]
    public void LeaveDisbandAndInvite() {
        ByteReader leave = Open(PartyPacket.Leave(9, true), 3);
        Assert.That(leave.ReadLong(), Is.EqualTo(9));
        Assert.That(leave.ReadBool(), Is.True);
        Assert.That(leave.Available, Is.Zero);

        Assert.That(Open(PartyPacket.Disband(), 7).Available, Is.Zero);

        ByteReader invite = Open(PartyPacket.Invite(33, "Lead"), 11);
        Assert.That(invite.ReadUnicodeString(), Is.EqualTo("Lead"));
        Assert.That(invite.ReadInt(), Is.EqualTo(33));
        Assert.That(invite.Available, Is.Zero);
    }

    [Test]
    public void GearScoreTombstoneAndDungeonModes() {
        ByteReader gear = Open(PartyPacket.Unknown1(), 15);
        Assert.That(gear.ReadLong(), Is.Zero);
        Assert.That(gear.ReadInt(), Is.Zero);
        Assert.That(gear.Available, Is.Zero);

        ByteReader tomb = Open(PartyPacket.Tombstone(9, true), 18);
        Assert.That(tomb.ReadLong(), Is.EqualTo(9));
        Assert.That(tomb.ReadByte(), Is.EqualTo(1));
        Assert.That(tomb.Available, Is.Zero);

        ByteReader reset = Open(PartyPacket.DungeonReset(true, 23000001), 25);
        Assert.That(reset.ReadBool(), Is.True);
        Assert.That(reset.ReadInt(), Is.EqualTo(23000001));
        Assert.That(reset.Available, Is.Zero);

        ByteReader search = Open(PartyPacket.PartySearch(1, true), 30);
        Assert.That(search.ReadByte(), Is.EqualTo(1));
        Assert.That(search.ReadBool(), Is.True);
        Assert.That(search.ReadBool(), Is.True);
        search.ReadByte();
        Assert.That(search.Available, Is.Zero);

        Assert.That(Open(PartyPacket.PartySearchDungeon(), 31).Available, Is.Zero);

        ByteReader record = Open(PartyPacket.DungeonRecord(), 35);
        Assert.That(record.ReadLong(), Is.Zero);
        Assert.That(record.Available, Is.Zero);

        ByteReader cooldown = Open(PartyPacket.DungeonHelperCooldown(5000), 40);
        Assert.That(cooldown.ReadInt(), Is.EqualTo(5000));
        Assert.That(cooldown.Available, Is.Zero);

        ByteReader room = Open(PartyPacket.DungeonRoomCreate(23000001), 56);
        Assert.That(room.ReadInt(), Is.EqualTo(23000001));
        Assert.That(room.Available, Is.Zero);
    }

    [Test]
    public void RequestVoteAndSurvivalModes() {
        ByteReader join = Open(PartyPacket.JoinRequest("Ask"), 44);
        Assert.That(join.ReadUnicodeString(), Is.EqualTo("Ask"));
        Assert.That(join.Available, Is.Zero);

        ByteReader ready = Open(PartyPacket.ReadyCheck(9, true), 48);
        Assert.That(ready.ReadLong(), Is.EqualTo(9));
        Assert.That(ready.ReadBool(), Is.True);
        Assert.That(ready.Available, Is.Zero);

        Assert.That(Open(PartyPacket.EndVote(), 49).Available, Is.Zero);

        ByteReader survival = Open(PartyPacket.SurvivalPartySearch(true, false), 55);
        Assert.That(survival.ReadBool(), Is.True);
        Assert.That(survival.ReadBool(), Is.False);
        Assert.That(survival.Available, Is.Zero);
    }

    // Modes 0x15 (OnPartyNoticeWithSound: a string id text, then the unicode sound key) and 0x25 (FUN_14162ee70: long
    // id, unicode name, a string id text).
    [Test]
    public void NoticeModesCarryTheInterfaceTextOnce() {
        var text = new InterfaceText("T");
        ByteWriter textOnly = new();
        textOnly.WriteClass<InterfaceText>(text);
        int textLength = textOnly.Length;

        ByteReader sound = Open(PartyPacket.NoticeWithSound(text, "Snd"), 21);
        sound.Skip(textLength);
        Assert.That(sound.ReadUnicodeString(), Is.EqualTo("Snd"));
        Assert.That(sound.Available, Is.Zero);

        ByteReader member = Open(PartyPacket.MemberNotice(9, "Mem", text), 37);
        Assert.That(member.ReadLong(), Is.EqualTo(9));
        Assert.That(member.ReadUnicodeString(), Is.EqualTo("Mem"));
        member.Skip(textLength);
        Assert.That(member.Available, Is.Zero);
    }
}
