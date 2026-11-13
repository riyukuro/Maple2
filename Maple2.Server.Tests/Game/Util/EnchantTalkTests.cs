using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// NpcTalk 4, the enchant dialog's events while the player talks to a field npc.
public class EnchantTalkTests {
    private static ByteReader Reader(ByteWriter writer) => new(writer.Buffer, 0);

    [Test]
    public void ItemChoiceCarriesTheUid() {
        // UIItemEnchantDialog::OnSelectItem: Encode2(1), then the item uid.
        var writer = new ByteWriter();
        writer.WriteShort(1);
        writer.WriteLong(0x0102030405060708);
        ByteReader reader = Reader(writer);
        EnchantTalk.Event talkEvent = EnchantTalk.Read(reader);
        Assert.Multiple(() => {
            Assert.That(talkEvent.Type, Is.EqualTo(ScriptEventType.EnchantSelect));
            Assert.That(talkEvent.ItemUid, Is.EqualTo(0x0102030405060708));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    [TestCase((short) 2, ScriptEventType.EnchantFail)]
    [TestCase((short) 3, ScriptEventType.EnchantComplete)]
    [TestCase((short) 4, ScriptEventType.EnchantRetry)]
    public void OtherEventsAreTheShortOnly(short sent, ScriptEventType type) {
        // vfunc_85(2), OnComplete's vfunc_85(3) and OnRetry's vfunc_85(4): Encode2(type) alone.
        var writer = new ByteWriter();
        writer.WriteShort(sent);
        ByteReader reader = Reader(writer);
        EnchantTalk.Event talkEvent = EnchantTalk.Read(reader);
        Assert.Multiple(() => {
            Assert.That(talkEvent.Type, Is.EqualTo(type));
            Assert.That(talkEvent.ItemUid, Is.EqualTo(0));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }
}
