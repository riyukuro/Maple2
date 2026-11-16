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

    // CCItemRemakeOptionPacket (itemremakescrolldata.cpp:469-621): 0 the item uid, the ingredient uid, a lock count and
    // per lock a flag byte and a short (tabcontentspetremakeoption.cpp:1113); 2 the item uid (493); 3 and 6 the kind
    // byte (500, 791); 4 the item uid (unit_14129cfa0_itemremakeoptiondialog.cpp:619). Mode 7 (the same body and a kind
    // and a value) and 8 (uid, count) are GM only: compared by reading, not run.
    [Test]
    public void ChangeAttributes_Modes0_2_3_4_6() {
        var handler = new ChangeAttributesHandler { ItemStatsCalc = test.ItemStats };
        ReadsAll(handler, w => {
            w.WriteByte(0);
            w.WriteLong(1);
            w.WriteLong(0);
            w.WriteByte(1);
            w.WriteBool(false);
            w.WriteShort(1);
        });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteByte(2); });
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(6); w.WriteByte(2); });
    }

    // The remake scroll (uiitemremakescrolldialog.cpp:516, 702; RemakeOption itemremakescrolldata.cpp:641): 1 the scroll
    // uid, the item uid, the ingredient uid, the lock flag, a lock count and per lock a byte and a short; 3 the item uid.
    [Test]
    public void ChangeAttributesScroll_Modes1_3() {
        var handler = new ChangeAttributesScrollHandler { TableMetadata = test.TableMetadata, ItemStatsCalc = test.ItemStats };
        ReadsAll(handler, w => {
            w.WriteByte(1);
            w.WriteLong(1);
            w.WriteLong(2);
            w.WriteLong(0);
            w.WriteBool(true);
            w.WriteByte(1);
            w.WriteBool(false);
            w.WriteShort(1);
        });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteLong(1); });
    }

    // RequestHome: 0 the inviter and the answer byte (scenestategame.cpp:6997), 1 the name (uipartydialog.cpp:3389,
    // usercommandcli.cpp:2029), 3 the template id (mypc.cpp:3165; read only for a home without a name).
    [Test]
    public void Home_Modes0_1_3() {
        var handler = new HomeHandler { MapMetadataStorage = test.MapMetadata, World = test.World };
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteUnicodeString("name"); w.WriteByte(1); });
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteUnicodeString("name"); });
    }

    // HomeAction (uipartydialog.cpp:3450-3474 modes 1, 2, 3: the name; uisurveydialog.cpp:125 mode 5: sub 5, character
    // id, survey id, answer; uiugcportaldialog.cpp:239 mode 6; ugcballobject.cpp:123, 258 mode 7: 3 and 2, object id and
    // the vectors; uiugccubenoticedialog.cpp:111 mode 12; cubecraft_2.cpp:1277 mode 13: the cell key; 
    // uiugcpvpcontroldialog.cpp:109 mode 14: sub 1 and six bytes). Survey, portal, notice, ball and the cell key are read
    // only inside a home field or for a plot owner, which the test session has none of: compared by reading, not run.
    [Test]
    public void HomeAction_VisitorAndPvpModes() {
        var handler = new HomeActionHandler();
        foreach (byte mode in new byte[] { 1, 2, 3 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteUnicodeString("name"); });
        }
        ReadsAll(handler, w => { w.WriteByte(14); w.WriteByte(1); for (int i = 0; i < 6; i++) { w.WriteByte(0); } });
    }

    // clubcli.cpp: 1 the name (925), 3 the club id and the int code (940), 6 id and name (455), 8 the invite (id and three
    // names, PacketInvite 0x14020c670) and the accept byte (971), 10 id (469), 12 id and name (488), 13 id, int, int
    // (514), 14 id and the new name (545).
    [Test]
    public void Club_Modes() {
        var handler = new ClubHandler {
            World = test.World,
            BanWordStorage = TestGameSession.BanWords(),
            ServerTableMetadata = test.ServerTableMetadata,
        };
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteUnicodeString("club"); });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteLong(1); w.WriteInt(0); });
        ReadsAll(handler, w => { w.WriteByte(6); w.WriteLong(1); w.WriteUnicodeString("name"); });
        ReadsAll(handler, w => {
            w.WriteByte(8);
            w.WriteLong(1);
            w.WriteUnicodeString("club");
            w.WriteUnicodeString("inviter");
            w.WriteUnicodeString("invitee");
            w.WriteByte(1);
        });
        ReadsAll(handler, w => { w.WriteByte(10); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(12); w.WriteLong(1); w.WriteUnicodeString("name"); });
        ReadsAll(handler, w => { w.WriteByte(13); w.WriteLong(1); w.WriteInt(1); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(14); w.WriteLong(1); w.WriteUnicodeString("club"); });
    }

    // WriteBuddyRequest (0x1401fc790): 2 name and memo, 3 4 6 7 17 the entry id, 5 and 10 id, name, memo; buddymanager.cpp:243
    // mode 20: the auto reject int.
    [Test]
    public void Buddy_Modes() {
        var handler = new BuddyHandler { BanWordStorage = TestGameSession.BanWords() };
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteUnicodeString("name"); w.WriteUnicodeString("memo"); });
        foreach (byte mode in new byte[] { 3, 4, 6, 7, 17 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteLong(1); });
        }
        foreach (byte mode in new byte[] { 5, 10 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteLong(1); w.WriteUnicodeString("name"); w.WriteUnicodeString("memo"); });
        }
        ReadsAll(handler, w => { w.WriteByte(20); w.WriteInt(1); });
    }

    // coupleeffectsystem.cpp:305 (0 and 1: the character id), 148 (2: character id and the newcomer), 287 (3: the partner).
    [Test]
    public void BuddyBadge_Modes0_to_3() {
        var handler = new BuddyBadgeHandler();
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteLong(1); w.WriteLong(2); });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteLong(1); });
    }

    // stateskill.cpp:1983 (0: emote id, partner), networkservice.cpp:1121 (1: the sender), statecoupleemotion.cpp:1349
    // to 1399 (2: character id and the reason byte), 1408 (3: emote id, sender, two points, the yaw), uimessagebox.cpp:2283
    // (4: emote id, sender) and statecoupleemotion.cpp:924 (6: emote id, partner).
    [Test]
    public void BuddyEmote_Modes() {
        var handler = new BuddyEmoteHandler();
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteInt(1); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteLong(1); w.WriteByte(5); });
        ReadsAll(handler, w => {
            w.WriteByte(3);
            w.WriteInt(1);
            w.WriteLong(1);
            for (int i = 0; i < 6; i++) {
                w.WriteFloat(0);
            }
            w.WriteFloat(0);
        });
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteInt(1); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(6); w.WriteInt(1); w.WriteLong(1); });
    }

    // uimaillistdialog.cpp and FUN_1401fc2a0: 0 (808), 1 the receiver, the title and the content (1619), 2 and 11 and 12
    // the mail id (562, 578, 1346), 13 a count and the ids (1025, 1150, 1173), 18 and 19 the same (1104, 1122). The count
    // modes are run with a count of 0: a count over the mailbox's size is refused before its ids are read.
    [Test]
    public void Mail_Modes() {
        var handler = new MailHandler { BanWordStorage = TestGameSession.BanWords() };
        ReadsAll(handler, w => w.WriteByte(0));
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteUnicodeString("to"); w.WriteUnicodeString("title"); w.WriteUnicodeString("content"); });
        foreach (byte mode in new byte[] { 2, 11, 12 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteLong(1); });
        }
        foreach (byte mode in new byte[] { 13, 18, 19 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteInt(0); });
        }
    }

    // instrumentmanager.cpp: 0 the item uid (631), 1 the note (645, 662), 2 (580), 3 and 5 the instrument and the score
    // (705, 758), 4 and 6 (781, 770), 8 the score (FUN_1401ffdc0: uid, int, instrument as int, title, mml; 1066), 10 the
    // score uid (1077); uimusicconcertdialog.cpp: 11, 13, 14 (354, 290, 318) and 15 the emotion (343). The client sends
    // no 12 (end perform). Most modes are read only for a player holding the instrument and the feature on: compared by
    // reading, run here are the ones that read before any check.
    [Test]
    public void Instrument_ModesWithoutAChecksBeforeTheRead() {
        var handler = new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = TestGameSession.BanWords() };
        ReadsAll(handler, w => w.WriteByte(11));
        ReadsAll(handler, w => w.WriteByte(13));
        ReadsAll(handler, w => w.WriteByte(14));
    }

    // Unk_LapenEquip, Unk_LapenUnequip, Unk_LapenItem, Unk_LapenAddFodder, Unk_LapenUpgrade (itemlapensharddata.cpp:461-530):
    // 1 slot int and uid; 2 slot int; 3 uid, item id, slot int; 4 the same and the amount; 5 the same, a count and per
    // fodder uid and amount, then a trailing int the handler does not read; 6 item id and a second int, also unread.
    [Test]
    public void Lapenshard_Modes() {
        var handler = new LapenshardHandler { TableMetadata = test.TableMetadata, ItemMetadata = test.ItemMetadata };
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteInt(1); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteLong(1); w.WriteInt(1); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteLong(1); w.WriteInt(1); w.WriteInt(1); w.WriteInt(1); });
    }

    // Attendance: 0 (usercommandcli.cpp:1029), 1 (worldservice_1.cpp:5778), 2 and 3 (uiattendgiftdialog.cpp:419, 377), 4 the
    // event id and the coupon uid (uiattendgiftdialog.cpp:477), 8 the event id (385).
    [Test]
    public void Attendance_Modes() {
        var handler = new AttendanceHandler { ItemMetadata = test.ItemMetadata };
        ReadsAll(handler, w => w.WriteByte(0));
        ReadsAll(handler, w => w.WriteByte(1));
        ReadsAll(handler, w => w.WriteByte(2));
        ReadsAll(handler, w => w.WriteByte(3));
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteInt(1); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(8); w.WriteInt(1); });
    }

    // BonusGame: 0 the game id (uitalkcinematic.cpp:1137, payload 0x1401fcd50), 2 (cinematiccomponent.cpp:1215), 3
    // (uirewardnotify.cpp:973).
    [Test]
    public void BonusGame_Modes0_2_3() {
        var handler = new BonusGameHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata };
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteInt(1); });
        ReadsAll(handler, w => w.WriteByte(2));
        ReadsAll(handler, w => w.WriteByte(3));
    }

    // RequestMoveField (mypc.cpp:3155 mode 0: the field id, portal id, two ints, a string, the password string; 974 mode 1;
    // 3086 mode 2: three ints, the account id, the passcode; 3136, 3107, 3125, 4970 modes 3, 4, 5, 6: the mode alone).
    [Test]
    public void MoveField_Modes() {
        var handler = new MoveFieldHandler();
        ReadsAll(handler, w => {
            w.WriteByte(0);
            w.WriteInt(test.Session.Field!.MapId);
            w.WriteInt(1);
            w.WriteInt(0);
            w.WriteInt(0);
            w.WriteUnicodeString("");
            w.WriteUnicodeString("");
        });
        ReadsAll(handler, w => w.WriteByte(1));
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteInt(1); w.WriteInt(0); w.WriteInt(0); w.WriteLong(1); w.WriteUnicodeString(""); });
        foreach (byte mode in new byte[] { 3, 4, 5, 6 }) {
            byte sent = mode;
            ReadsAll(handler, w => w.WriteByte(sent));
        }
    }

    // Job: 2 the npc id (cinematiccomponent.cpp:993), 7 job, serial, skill, level (FUN_1401fdb50), 8 (jobsystemcli.cpp:157), 9
    // and 11 a count and per skill id, level short, learned flag (uiskillbookautodistributedialog.cpp:1202), 10 the
    // rank (975).
    [Test]
    public void Job_Modes() {
        var handler = new JobHandler();
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(7); w.WriteInt(1); w.WriteLong(1); w.WriteInt(1); w.WriteShort(1); });
        ReadsAll(handler, w => w.WriteByte(8));
        foreach (byte mode in new byte[] { 9, 11 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteInt(1); w.WriteInt(1); w.WriteShort(1); w.WriteBool(true); });
        }
        ReadsAll(handler, w => { w.WriteByte(10); w.WriteInt(0); });
    }

    // groupchatmanager.cpp: 1 the name (497), 2 name and chat id (532), 4 chat id and the refuse flag (543), 10 the text and
    // the chat id (569).
    [Test]
    public void GroupChat_Modes() {
        var handler = new GroupChatHandler { World = test.World };
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteUnicodeString("name"); });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteUnicodeString("name"); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteInt(1); w.WriteByte(0); });
        ReadsAll(handler, w => { w.WriteByte(10); w.WriteUnicodeString("text"); w.WriteInt(1); });
    }

    // ChatSticker: 1 a count and the group ids (chatemoticoncli.cpp:428), 3 sticker, window name, chat type (uimessenger.cpp:1429),
    // 4 sticker and group name (uigroupmessenger.cpp:918), 5 and 6 the sticker (uimessenger.cpp:1491, uigroupmessenger.cpp:957).
    // The client sends no mode 0.
    [Test]
    public void ChatSticker_Modes() {
        var handler = new ChatStickerHandler { TableMetadata = test.TableMetadata };
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteInt(1); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteInt(1); w.WriteUnicodeString("window"); w.WriteByte(0); });
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteInt(1); w.WriteUnicodeString("group"); });
        ReadsAll(handler, w => { w.WriteByte(5); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(6); w.WriteInt(1); });
    }

    // uimasterydialog.cpp: 1 the reward box (507, 522), 2 the recipe (589).
    [Test]
    public void Mastery_Modes1_2() {
        var handler = new MasteryHandler { ItemMetadata = test.ItemMetadata, TableMetadata = test.TableMetadata };
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteInt(1); });
    }

    // RoomDungeon: 1 (uidungeonroomdialog.cpp:1588), 2 the room, party flag, portal, ticket flag (1709), 3 an int
    // (partymanager.cpp:800), 4 12 13 14 the mode alone (uidungeonsearchdialog.cpp, uidungeoncategory.cpp:687), 8 an int
    // (1501), 9 (actionkeyobject.cpp:1093), 15 16 17 a room id (1558-1578), 21 (uidungeonhelper.cpp:294), 24 the room and the
    // favorite flag (1488).
    [Test]
    public void DungeonRoom_Modes() {
        var handler = new DungeonRoomHandler { TableMetadata = test.TableMetadata, World = test.World };
        ReadsAll(handler, w => w.WriteByte(1));
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteInt(1); w.WriteBool(false); w.WriteInt(0); w.WriteBool(false); });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteInt(0); });
        foreach (byte mode in new byte[] { 4, 9, 12, 13, 14, 21 }) {
            byte sent = mode;
            ReadsAll(handler, w => w.WriteByte(sent));
        }
        foreach (byte mode in new byte[] { 8, 15, 16, 17 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteInt(1); });
        }
        ReadsAll(handler, w => { w.WriteByte(24); w.WriteInt(1); w.WriteBool(true); });
    }

    // InteractObject: 11 and 12 a count and the names as ASCII strings (stateinteractdata.cpp:434, 458,
    // interactobject.cpp:600, 618); 9 the debug create (uiobjectcreatedialog.cpp:156) is GM only.
    [Test]
    public void InteractObject_Modes11_12() {
        var handler = new InteractObjectHandler();
        ReadsAll(handler, w => { w.WriteByte(11); w.WriteInt(1); w.WriteString("name"); });
        ReadsAll(handler, w => { w.WriteByte(12); w.WriteInt(1); w.WriteString("name"); });
    }

    // The small ones: the field key (cubemapinformationservice_3.cpp:5703, scenestategame.cpp:2284), the dps window flag
    // (uidpsdialog.cpp:86, 98), the guide states a count and id pairs (guideentity_1b.cpp:217), the global factor request
    // (scenestategame.cpp:2144, no fields), furnishing storage 4 uid slot count and 6 uid (networkservice.cpp:497,
    // uiugccubepalettedialog.cpp:1294), achievements 3 the id, 4 id and bookmark (uiachievedialog.cpp:255, 350) and liftable
    // 1 the entity name (actionkeyobject.cpp:1311).
    [Test]
    public void SmallHandlers() {
        ReadsAll(new FieldEnterHandler(), w => w.WriteInt(1));
        ReadsAll(new DpsModeHandler(), w => w.WriteByte(1));
        ReadsAll(new GuideRecordHandler(), w => { w.WriteInt(1); w.WriteInt(1); w.WriteInt(1); });
        ReadsAll(new GlobalFactorHandler(), w => { });
        ReadsAll(new FurnishingStorageHandler(), w => { w.WriteByte(4); w.WriteLong(1); w.WriteShort(1); w.WriteInt(1); });
        ReadsAll(new FurnishingStorageHandler(), w => { w.WriteByte(6); w.WriteLong(1); });
        ReadsAll(new AchievementHandler(), w => { w.WriteByte(3); w.WriteInt(1); });
        ReadsAll(new AchievementHandler(), w => { w.WriteByte(4); w.WriteInt(1); w.WriteBool(true); });
        ReadsAll(new LiftableHandler(), w => { w.WriteByte(1); w.WriteString("entity"); });
    }
}
