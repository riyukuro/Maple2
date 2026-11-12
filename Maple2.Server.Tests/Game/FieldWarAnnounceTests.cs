using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game;

// The field war announce (FieldWar mode 0: the war id and its uid): on a field enter while a war is open, and once a
// war, to a player of the required level and trophy in a field the global portal announce goes to.
public class FieldWarAnnounceTests {
    private const int WarId = 10000001;
    private const long Uid = 1234567890123L;

    private TestGameSession test = null!;

    private GameSession Enter(MapType type, short level, bool open = true, int requireAchievementId = 0) {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            FieldWarRequireLevel = 70,
            FieldWarRequireAchieveID = requireAchievementId,
        });
        GameSession session = test.Start();
        session.Player.Value.Character.Level = level;
        MapMetadata map = TestGameSession.Map(2000001);
        test.EnterMap(map with { Property = map.Property with { Type = type } });
        test.World.OpenFieldWar = open ? new FieldWarInfo { EventId = Uid, FieldWarId = WarId, MapId = 2020061 } : null;
        test.TakeSent();
        return session;
    }

    [Test]
    public void OpenWar_IsAnnouncedOnce() {
        GameSession session = Enter(MapType.None, 70);

        session.AnnounceOpenFieldWar();

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.FieldWar }));
        var reader = new ByteReader(sent[0], 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(WarId));
        Assert.That(reader.ReadLong(), Is.EqualTo(Uid));
        Assert.That(reader.Available, Is.EqualTo(0));
        Assert.That(session.FieldWarUid, Is.EqualTo(Uid));

        session.AnnounceOpenFieldWar();
        session.AnnounceFieldWar(WarId, Uid);
        Assert.That(test.TakeSent(), Is.Empty);

        // The next war is announced again.
        session.AnnounceFieldWar(WarId, Uid + 1);
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.FieldWar }));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void NoOpenWar_NothingIsSent() {
        GameSession session = Enter(MapType.None, 70, open: false);
        session.AnnounceOpenFieldWar();
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.FieldWarUid, Is.EqualTo(0));
    }

    [Test]
    public void BelowTheLevel_OrWithoutTheTrophy_NothingIsSent() {
        GameSession session = Enter(MapType.None, 69);
        session.AnnounceOpenFieldWar();
        Assert.That(test.TakeSent(), Is.Empty);

        session = Enter(MapType.None, 70, requireAchievementId: 22200490);
        session.AnnounceOpenFieldWar();
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.FieldWarUid, Is.EqualTo(0));
    }

    [TestCase(MapType.Dungeon)]
    [TestCase(MapType.FieldWar)]
    [TestCase(MapType.Pvp)]
    public void OtherFieldTypes_NothingIsSent(MapType type) {
        GameSession session = Enter(type, 70);
        session.AnnounceOpenFieldWar();
        session.AnnounceFieldWar(WarId, Uid);
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
