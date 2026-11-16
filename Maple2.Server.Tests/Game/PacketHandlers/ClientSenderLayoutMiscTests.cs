using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// More handlers against the client's senders, compared field by field (the writes named at each test). The packet is
// built as the client writes it and nothing is left unread once the handler has read it; the handler's action may
// fail on what the test session lacks, which is not what is checked.
public class ClientSenderLayoutMiscTests {
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.TakeSent();
    }

    private void ReadsAll(PacketHandler<GameSession> handler, Action<ByteWriter> client) {
        ByteReader packet = TestGameSession.Packet(client);
        try {
            handler.Handle(test.Session, packet);
        } catch (Exception) {
            // The side effect, not the read.
        }
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // The emote window: learn (inventory.cpp:1622 mode 1, item uid), use (stateskill.cpp:1906 mode 2, skill id and its
    // param string), favorite (uidynamicactiondialog.cpp:369, uicoupleactiondialog.cpp:333 mode 4: id, level, flag).
    [Test]
    public void Emote_Modes1_2_4() {
        ReadsAll(new EmoteHandler(), w => { w.WriteByte(1); w.WriteLong(1); });
        ReadsAll(new EmoteHandler(), w => { w.WriteByte(2); w.WriteInt(1); w.WriteUnicodeString("Idle"); });
        ReadsAll(new EmoteHandler(), w => { w.WriteByte(4); w.WriteInt(1); w.WriteInt(1); w.WriteBool(true); });
    }

    // fishingmanager.cpp: 0 rod uid (826), 1 (839), 10 (716). Modes 8 (result byte, 736 and 970) and 9 (the cell key as
    // 4 bytes, x lowest, 859) are read only with a fishing guide object, which the test session has none of; they were
    // compared by reading (Vector3B is 4 bytes, packed as the client's cell key).
    [Test]
    public void Fishing_Modes0_1_10() {
        ReadsAll(new FishingHandler(), w => { w.WriteByte(0); w.WriteLong(1); });
        ReadsAll(new FishingHandler(), w => w.WriteByte(1));
        ReadsAll(new FishingHandler(), w => w.WriteByte(10));
    }

    // homeblockmanager.cpp: 2 character id and memo (86, 94), 3 id (110), 5 id and memo (223).
    [Test]
    public void HomeBlock_Modes2_3_5() {
        var handler = new HomeBlockHandler { BanWordStorage = TestGameSession.BanWords() };
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteLong(1); w.WriteUnicodeString("memo"); });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(5); w.WriteLong(1); w.WriteUnicodeString("memo"); });
    }

    // UIGlobalPortalDialog (uiglobalportaldialog.cpp:237): mode 2, the event id and the button.
    [Test]
    public void GlobalPortal_Mode2() {
        ReadsAll(new GlobalPortal { World = test.World }, w => { w.WriteByte(2); w.WriteInt(1); w.WriteInt(0); });
    }

    // The channel change (worldservice_1.cpp:4972, 10639; payload 0x1401fcc00): the channel as 2 bytes.
    [Test]
    public void Channel_TheChannel() {
        ReadsAll(new ChannelHandler { World = test.World }, w => w.WriteShort(1));
    }

    // The mentoring pages (mentor.cpp:39-125, :240): the mode byte, then nothing (0, 4, 8, 9), a count and its entries
    // (2, sent empty), the id (6, 7) or the id and the name (5).
    [Test]
    public void Mentor_Modes() {
        var handler = new MentorHandler();
        foreach (byte mode in new byte[] { 0, 4, 8, 9 }) {
            ReadsAll(handler, w => w.WriteByte(mode));
        }
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteInt(0); });
        ReadsAll(handler, w => { w.WriteByte(6); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(7); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(5); w.WriteLong(0); w.WriteUnicodeString("name"); });
    }

    // maidmanager.cpp:204-259 and uimaiddialog.cpp:391 (MaidProfile::Write, maid.cpp:71): 5 to 9 the maid uid, 10 the
    // uid, a flag byte and a long, 4 the uid, the maid id and the thirteen profile strings in order.
    [Test]
    public void Maid_Modes4_5_to_10() {
        var handler = new MaidHandler {
            TableMetadata = test.TableMetadata,
            ServerTableMetadata = test.ServerTableMetadata,
            BanWordStorage = TestGameSession.BanWords(),
        };
        for (byte mode = 5; mode <= 9; mode++) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteLong(1); });
        }
        ReadsAll(handler, w => { w.WriteByte(10); w.WriteLong(1); w.WriteByte(0); w.WriteLong(60); });
        ReadsAll(handler, w => {
            w.WriteByte(4);
            w.WriteLong(1);
            w.WriteInt(1);
            for (int i = 0; i < 13; i++) {
                w.WriteUnicodeString("s");
            }
        });
    }

    // keytable.cpp:385 (1), 407 (2): a count and per key int key, int type, long code, int slot, byte priority;
    // accountsessionstate.cpp:405 (3), 468 (4): short bar, int skill, int item, long uid, int; 493 (5): short, int,
    // long (read only for a hot bar the player has, not tested here); 507 (6) and keytable.cpp:2031 (7): the mode alone; 516 (8): the short bar.
    [Test]
    public void KeyTable_Modes1_to_8() {
        var handler = new KeyTableHandler();
        foreach (byte mode in new byte[] { 1, 2 }) {
            byte sent = mode;
            ReadsAll(handler, w => {
                w.WriteByte(sent);
                w.WriteInt(1);
                w.WriteInt(1);
                w.WriteInt(1);
                w.WriteLong(1);
                w.WriteInt(1);
                w.WriteByte(0);
            });
        }
        foreach (byte mode in new byte[] { 3, 4 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteShort(0); w.WriteInt(1); w.WriteInt(1); w.WriteLong(1); w.WriteInt(-1); });
        }
        ReadsAll(handler, w => w.WriteByte(6));
        ReadsAll(handler, w => w.WriteByte(7));
        ReadsAll(handler, w => { w.WriteByte(8); w.WriteShort(0); });
    }

    // uiitemmergeoptiondialog.cpp:974-1020 and uiitemmergerevertdialog.cpp:155: 0 (info), 1 uid, 2 and 3 and 7 two uids,
    // 4 the choice byte, 6 two uids (the revert item first).
    [Test]
    public void ItemMerge_Modes0_to_7() {
        var handler = new ItemMergeHandler();
        ReadsAll(handler, w => w.WriteByte(0));
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteLong(1); });
        foreach (byte mode in new byte[] { 2, 3, 6, 7 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteLong(1); w.WriteLong(2); });
        }
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteByte(1); });
    }

    // uiitemrepackingscrolldialog.cpp:287: mode 1, the scroll uid, the target uid.
    [Test]
    public void ItemRepack_Mode1() {
        ReadsAll(new ItemRepackHandler(), w => { w.WriteByte(1); w.WriteLong(1); w.WriteLong(2); });
    }

    // dropitem.cpp:940: the object id, the picker flag, the picker id sent only with the flag.
    [Test]
    public void ItemPickup_ObjectAndPicker() {
        ReadsAll(new ItemPickupHandler(), w => { w.WriteInt(1); w.WriteBool(false); });
        ReadsAll(new ItemPickupHandler(), w => { w.WriteInt(1); w.WriteBool(true); w.WriteInt(2); });
    }

    // uiitemextractionscrolldialog.cpp:282: the scroll uid, the target uid.
    [Test]
    public void ItemExtraction_ScrollAndTarget() {
        ReadsAll(new ItemExtractionHandler { TableMetadata = test.TableMetadata }, w => { w.WriteLong(1); w.WriteLong(2); });
    }

    // itemlockmanager.cpp:36 (0), 209 (1: mode byte, uid), 221 and 261 (2: uid), 171 (3: mode byte).
    [Test]
    public void ItemLock_Modes0_to_3() {
        var handler = new ItemLockHandler { ServerTableMetadata = test.ServerTableMetadata };
        ReadsAll(handler, w => w.WriteByte(0));
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteByte(0); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteByte(0); });
    }

    // invenslot.cpp:1145 (3: uid, short slot), networkservice.cpp:514 (4: uid, int amount) and 516 (5: uid),
    // uiinventorydialog.cpp:1600 (8: tab byte, stack flag), inventory.cpp:1303 (9: tab byte).
    [Test]
    public void ItemInventory_Modes3_4_5_8_9() {
        var handler = new ItemInventoryHandler();
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteLong(1); w.WriteShort(1); });
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteLong(1); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(5); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(8); w.WriteByte(0); w.WriteBool(false); });
        ReadsAll(handler, w => { w.WriteByte(9); w.WriteByte(0); });
    }

    // uiitemsocketscrolldialog.cpp:524 (payload 0x14062a040): mode 1, the target uid, then the scroll uid.
    [Test]
    public void ItemSocketScroll_Mode1() {
        ReadsAll(new ItemSocketScrollHandler { TableMetadata = test.TableMetadata }, w => { w.WriteByte(1); w.WriteLong(1); w.WriteLong(2); });
    }

    // itemexchangeslot.cpp:95 (0: scroll uid, item uid), uiitemexchangescrolldialog.cpp:594 (1: scroll, target, count;
    // payload 0x140202470), 894 and 911 (3: scroll, target, count; payload 0x140202580, GM only).
    [Test]
    public void ItemExchangeScroll_Modes0_1() {
        var handler = new ItemExchangeScroll { ItemMetadata = test.ItemMetadata, TableMetadata = test.TableMetadata };
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteLong(1); w.WriteLong(2); });
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteLong(1); w.WriteLong(2); w.WriteInt(1); });
    }

    // enchantscrollslot.cpp:105 (1) and uienchantscrolldialog.cpp:167 (2): the scroll uid, the item uid;
    // uimasterydialog.cpp:594 (4): the recipe id. The notice packets of unit_1407a4f20 name this opcode but nothing
    // constructs them.
    [Test]
    public void EnchantScroll_Modes1_2_4() {
        var handler = new EnchantScrollHandler();
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteLong(1); w.WriteLong(2); });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteLong(1); w.WriteLong(2); });
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteInt(1); });
    }

    // uiitemenchantdialog.cpp: 0 (468), 1 type byte and uid (735, 1162), 2 uid and flag byte (849, 1397), 3 int (813,
    // 822), 4 uid and the other uid (1749), 6 uid (713), uicommon.cpp:1998 9 uid. Modes 4 and 6 are read only with the Ophelia or Peachy dialog staged and
    // 13 to 19 are GM only: those were compared by reading, not run.
    [Test]
    public void ItemEnchant_Modes0_1_2_3_4_6_9() {
        var handler = new ItemEnchantHandler();
        ReadsAll(handler, w => w.WriteByte(0));
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteByte(1); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteLong(1); w.WriteByte(1); });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(9); w.WriteLong(1); });
    }
}
