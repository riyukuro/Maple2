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

// GlobalPortalMinLevel (KR 0, the default row 10): the client's dialog does not show the announce below it and has
// the notice of its mode 4 for an enter below it. The server skips the announce and answers such an enter with mode 4.
public class GlobalPortalLevelTests {
    private const int PortalId = 80001;
    private const int EventId = 7;

    private TestGameSession test = null!;

    private GameSession Start(int minLevel, short level) {
        test = new TestGameSession();
        test.SetServerTable(new TimeEventTable(new Dictionary<int, GlobalPortalMetadata> {
            [PortalId] = new(PortalId, 100, DateTime.MinValue, DateTime.MaxValue, TimeSpan.FromHours(1), TimeSpan.Zero, TimeSpan.FromMinutes(1),
                "popup", "sound", [new GlobalPortalMetadata.Field("first", 65000001, 1)]),
        }, new Dictionary<int, WorldBossMetadata>()));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { GlobalPortalMinLevel = minLevel });
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Player.Value.Character.Level = level;
        test.World.OpenPortal = new GlobalPortalInfo { MetadataId = PortalId, EventId = EventId, Channel = 1, MapId = 65000001, RoomId = 3, PortalId = 1 };
        test.TakeSent();
        return session;
    }

    private void Enter(GameSession session) {
        new Maple2.Server.Game.PacketHandlers.GlobalPortal { World = test.World }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(EventId);
            writer.WriteInt(0);
        }));
    }

    private static byte Mode(byte[] packet) {
        var reader = new ByteReader(packet, 0);
        reader.ReadShort();
        return reader.ReadByte();
    }

    [Test]
    public void BelowTheMinLevel_NoAnnounce_EnterGetsMode4() {
        GameSession session = Start(minLevel: 10, level: 9);

        session.AnnounceOpenGlobalPortal();
        Assert.That(test.TakeSent(), Is.Empty);

        Enter(session);
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.GlobalPortal }));
        Assert.That(Mode(sent[0]), Is.EqualTo(4));
        Assert.That(sent[0].Length, Is.EqualTo(3));
        Assert.That(test.World.Moves, Is.Empty);
    }

    [TestCase(10, (short) 10)]
    [TestCase(0, (short) 1)]
    public void AtTheMinLevelOrWithNone_AnnouncedAndMoved(int minLevel, short level) {
        GameSession session = Start(minLevel, level);

        session.AnnounceOpenGlobalPortal();
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.GlobalPortal }));
        Assert.That(Mode(sent[0]), Is.EqualTo(0));

        Enter(session);
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.GlobalPortal), Is.Empty);
        Assert.That(test.World.Moves, Is.Not.Empty);
    }
}
