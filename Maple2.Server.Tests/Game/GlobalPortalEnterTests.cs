using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game;

// The global portal announce of a field enter (GameSession.AnnounceOpenGlobalPortal, called by EnterField): KMS2
// sends the announce again to a player who enters a field while the portal is open.
public class GlobalPortalEnterTests {
    private const int PortalId = 80001;
    private const int EventId = 7;

    private TestGameSession test = null!;

    private GameSession Enter(MapType type, bool open, int error = 0, int metadataId = PortalId) {
        test = new TestGameSession();
        test.SetServerTable(new TimeEventTable(new Dictionary<int, GlobalPortalMetadata> {
            [PortalId] = new(PortalId, 100, DateTime.MinValue, DateTime.MaxValue, TimeSpan.FromHours(1), TimeSpan.Zero, TimeSpan.FromMinutes(1),
                "popup", "sound", [new GlobalPortalMetadata.Field("first", 65000001, 1)]),
        }, new Dictionary<int, WorldBossMetadata>()));
        GameSession session = test.Start();
        MapMetadata map = TestGameSession.Map(2000001);
        test.EnterMap(map with { Property = map.Property with { Type = type } });
        test.World.OpenPortal = open ? new GlobalPortalInfo { MetadataId = metadataId, EventId = EventId } : null;
        test.World.TimeEventError = error;
        test.TakeSent();
        return session;
    }

    [TestCase(MapType.None)]
    [TestCase(MapType.Home)]
    [TestCase(MapType.Telescope)]
    [TestCase(MapType.Shelter)]
    public void OpenPortal_IsAnnouncedAtTheEnter(MapType type) {
        GameSession session = Enter(type, open: true);

        session.AnnounceOpenGlobalPortal();

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.GlobalPortal }));
        var reader = new ByteReader(sent[0], 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(PortalId));
        Assert.That(reader.ReadInt(), Is.EqualTo(EventId));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("popup"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("sound"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("first"));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void NoOpenPortal_NothingIsSent() {
        GameSession session = Enter(MapType.None, open: false);

        session.AnnounceOpenGlobalPortal();

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [TestCase(MapType.Dungeon)]
    [TestCase(MapType.PocketRealm)]
    [TestCase(MapType.Event)]
    [TestCase(MapType.Pvp)]
    [TestCase(MapType.Arcade)]
    [TestCase(MapType.SurvivalSolo)]
    public void OpenPortal_IsNotAnnouncedInOtherFieldTypes(MapType type) {
        GameSession session = Enter(type, open: true);

        session.AnnounceOpenGlobalPortal();

        Assert.That(test.TakeSent(), Is.Empty);
    }

    // A World answer with an error, or a portal row the table lacks, sends nothing.
    [Test]
    public void WorldErrorOrUnknownRow_NothingIsSent() {
        GameSession session = Enter(MapType.None, open: true, error: 1);
        session.AnnounceOpenGlobalPortal();
        Assert.That(test.TakeSent(), Is.Empty);

        session = Enter(MapType.None, open: true, metadataId: PortalId + 1);
        session.AnnounceOpenGlobalPortal();
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }
}
