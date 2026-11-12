using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: OnLegionBattle modes 0 and 1 (client/ms2/scenestategame.cpp) set a spawn time by key, npc id, time; an
// existing key only gets its time replaced. UIGlobalPortalDialog (client/ms2/uiglobalportaldialog.cpp) reads two
// ints, the text key, the sound and three captions. Made-up ids.
public class TimeEventPacketTests {
    private static WorldBossMetadata Boss(int id, int npcId) => new(id, 100, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1),
        TimeSpan.FromHours(1), TimeSpan.Zero, TimeSpan.FromMinutes(5), [2000001], [100], [npcId], id, true, false, 1, false, false);

    [Test]
    public void LegionBattleUpdate_UsesTheKeyOfLoad() {
        ByteWriter load = LegionBattlePacket.Load(new Dictionary<int, WorldBossMetadata> { [1000] = Boss(1000, 23000001) });
        var reader = new ByteReader(load.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.LegionBattle));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(1));
        int loadKey = reader.ReadInt();
        Assert.That(reader.ReadInt(), Is.EqualTo(23000001));

        ByteWriter update = LegionBattlePacket.Update(Boss(1000, 23000001).Id, 23000001, 1234567890L);
        reader = new ByteReader(update.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.LegionBattle));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(loadKey));
        Assert.That(reader.ReadInt(), Is.EqualTo(23000001));
        Assert.That(reader.ReadLong(), Is.EqualTo(1234567890L));
        Assert.That(update.Length, Is.EqualTo(2 + 1 + 4 + 4 + 8));
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4)]
    public void GlobalPortalAnnounce_HasThreeCaptions(int entries) {
        var fields = Enumerable.Range(0, entries).Select(i => new GlobalPortalMetadata.Field("name" + i, 61000001 + i, 1)).ToArray();
        var portal = new GlobalPortalMetadata(80001, 100, DateTime.Now, DateTime.Now, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, "text", "sound", fields);
        var reader = new ByteReader(GlobalPortalPacket.Announce(portal, 7).ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.GlobalPortal));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(80001));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("text"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("sound"));
        for (int i = 0; i < 3; i++) {
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo(i < entries ? "name" + i : ""));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void GlobalPortalDead_IsTheModeAlone() {
        ByteWriter writer = GlobalPortalPacket.CannotEnterDead();
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.GlobalPortal));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(writer.Length, Is.EqualTo(3));
    }
}
