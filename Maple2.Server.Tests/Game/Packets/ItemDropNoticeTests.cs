using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class ItemDropNoticeTests {
    // Client: CSceneStateGame::OnItemDropNotice (client/ms2/scenestategame.cpp:8022) hands its int, int, long to
    // FUN_140d2e2b0 (unnamed/unit_140d2a7e0_chatlinkhelpers.cpp:716) as item id, grade, uid.
    [Test]
    public void NoticeWritesItemIdGradeUid() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(11300001, "11300001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        var item = new Item(metadata, 4, 7) { Uid = 91 };

        ByteWriter writer = ItemDropNoticePacket.Notice("ab", item);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemDropNotice));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
        Assert.That(reader.ReadInt(), Is.EqualTo(11300001)); // item id, not the amount
        Assert.That(reader.ReadInt(), Is.EqualTo(4)); // grade
        Assert.That(reader.ReadLong(), Is.EqualTo(91));
        Assert.That(writer.Length, Is.EqualTo(2 + (2 + 4) + 4 + 4 + 8));
    }
}
