using System;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The handlers N to Z against the client's senders, compared field by field (the writes are named at each test). The
// packet is built as the client writes it and nothing is left unread once the handler has read it; the handler's
// action may fail on what the test session lacks, which is not what is checked.
public class ClientSenderLayoutNZTests {
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.TakeSent();
    }

    private void ReadsAll(PacketHandler<GameSession> handler, Action<ByteWriter> client) {
        ByteReader packet = TestGameSession.Packet(client);
        byte first = packet.Available > 0 ? packet.Peek<byte>() : (byte) 255;
        try {
            handler.Handle(test.Session, packet);
        } catch (Exception) {
            // The side effect, not the read.
        }
        Assert.That(packet.Available, Is.EqualTo(0), "first byte " + first);
    }

    // webbrowserservice.cpp:361 (0, the command, an int) and 376 (0, 0, 0): the token request.
    [Test]
    public void NewsNotification_Modes() {
        var handler = new NewsNotificationHandler();
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteByte(1); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); });
    }

    // uiweddingbillboarddialog.cpp:157, 197: mode 4 and an int.
    [Test]
    public void WeddingBillboard_Mode4() {
        ReadsAll(new WeddingBillboardHandler(), w => { w.WriteByte(4); w.WriteInt(0); });
    }

    // uiitemtreasuremapdialog.cpp:233, 258: mode 2, the item uid, the free flag.
    [Test]
    public void TreasureMap_Mode2() {
        ReadsAll(new TreasureMapHandler(), w => { w.WriteByte(2); w.WriteLong(1); w.WriteBool(false); });
    }

    // uimyinfodialog.cpp:721 (1: the title id), uiachievedialog.cpp:218 (3).
    [Test]
    public void UserEnv_Modes1_3() {
        var handler = new UserEnvHandler();
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteInt(1); });
        ReadsAll(handler, w => w.WriteByte(3));
    }

    // uipcbangdialog.cpp: 0 and the show flag (389, uiupperhuddialog.cpp:499), 1 the index byte (581), 2 an int (257), 3 (699).
    [Test]
    public void PcBang_Modes() {
        var handler = new PcBangHandler();
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteByte(1); });
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteByte(1); });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteInt(1); });
        ReadsAll(handler, w => w.WriteByte(3));
    }

    // stateobserver.cpp:513: mode 2 and the target character id.
    [Test]
    public void SpectateMushtopia_Mode2() {
        ReadsAll(new SpectateMushtopiaHandler(), w => { w.WriteByte(2); w.WriteLong(1); });
    }

    // usercommandcli.cpp:2062, 2067, uisidepopuptalk_2.cpp:149-183: 0 and the item id, 1.
    [Test]
    public void SuperChat_Modes0_1() {
        var handler = new SuperChatHandler();
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteInt(1); });
        ReadsAll(handler, w => w.WriteByte(1));
    }

    // guideaction.cpp:1414 and 1342, cinematiccomponent.cpp:1159 and 1082 (the npc id), gameoptionstruct.cpp:3433.
    [Test]
    public void TutorialAndNpcRequests() {
        ReadsAll(new TutorialHandler(), w => { });
        ReadsAll(new TutorialItemHandler(), w => { });
        ReadsAll(new TakeBoatHandler(), w => w.WriteInt(1));
        ReadsAll(new ResolveDeathPenaltyHandler(), w => w.WriteInt(1));
        ReadsAll(new PersonalInfoAgreementHandler(), w => { w.WriteInt(1); w.WriteInt(1); });
    }

    // RequestNpcCreate (uinpccreatedialog.cpp:190, 208: the npc, two vectors, the count, the pet flag and for a pet a float)
    // is GM only and reads nothing before its check: compared by reading with GmNpcCreateRequest.Read, not run.

    // uiskillcompactcontroldialog.cpp:572 (0) and 669 (1: the macro count and per macro its name, key, a skill count and
    // the skill ids).
    [Test]
    public void SkillMacro_Modes0_1() {
        var handler = new SkillMacroHandler();
        ReadsAll(handler, w => w.WriteByte(0));
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteInt(1); w.WriteUnicodeString("macro"); w.WriteLong(1); w.WriteInt(1); w.WriteInt(1); });
    }

    // uiarcademaindialog.cpp:559 (10 and the arcade id), uimicrogamecoupledance.cpp:1006 (11 and an int). The client's
    // mode 9 (309) writes the int 9 in four bytes, of which the handler takes the first.
    [Test]
    public void PlayArcade_Modes10_11() {
        var handler = new PlayArcadeHandler { GameStorage = test.Storage };
        ReadsAll(handler, w => { w.WriteByte(10); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(11); w.WriteInt(1); });
    }

    // uimessagebox.cpp:1647: the type byte, the caller name, the map id.
    [Test]
    public void RecallScroll_TypeNameMap() {
        ReadsAll(new RecallScrollHandler(), w => { w.WriteByte(1); w.WriteUnicodeString("name"); w.WriteInt(1); });
    }

    // networkservice.cpp:525 (4: the invite id and the answer byte), 528 (7: the caller name and the result byte).
    [Test]
    public void RecallUser_Modes4_7() {
        var handler = new RecallUserHandler();
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteInt(1); w.WriteByte(1); });
        ReadsAll(handler, w => { w.WriteByte(7); w.WriteUnicodeString("name"); w.WriteByte(1); });
    }

    // uireportdialog.cpp:348-399: the target name, the text, the kind byte, the flags and the kind's own fields.
    [Test]
    public void RequestReport_EveryKind() {
        var handler = new RequestReportHandler();
        // The kinds' own fields are read after the character lookup, which the test session cannot pass: those were compared
        // by reading (poster: long and the url string; item design: long; home: long, int, int; pet: string).
        foreach (byte kind in new byte[] { 0 }) {
            byte sent = kind;
            ReadsAll(handler, w => {
                w.WriteUnicodeString("target");
                w.WriteUnicodeString("text");
                w.WriteByte(sent);
                w.WriteInt(1);
                switch (sent) {
                    case 1:
                        w.WriteUnicodeString("chat");
                        break;
                    case 2:
                        w.WriteLong(1);
                        w.WriteUnicodeString("url");
                        break;
                    case 3:
                        w.WriteLong(1);
                        break;
                    case 4:
                        w.WriteLong(1);
                        w.WriteInt(1);
                        w.WriteInt(1);
                        break;
                    case 7:
                        w.WriteUnicodeString("pet");
                        break;
                }
            });
        }
    }

    // unit_140ad5280_statetreewateringcontext.cpp:171, 177: the action byte; petmanager.cpp: 7 (483); modes 0, 1 (uid, slot, amount) and 3 (uid, slot) are read
    // only with a pet bag, so they were compared by reading.
    [Test]
    public void TreeWateringAndPetInventory() {
        ReadsAll(new TreeWateringHandler(), w => w.WriteByte(0));
        ReadsAll(new TreeWateringHandler(), w => w.WriteByte(1));
        var pets = new PetInventoryHandler();
        ReadsAll(pets, w => w.WriteByte(7));
    }

    // mypc.cpp:3658 (State: the mode, the argument, the server tick, and the position for mode 0), worldservice_1.cpp:9370
    // (Tombstone: the object id and the hit count 1), 4175 and 4507 (the craft mode: the mode and the cube: a flag and
    // CubeItemInfo 0x1408b39c0).
    [Test]
    public void StateTombstoneAndCraftMode() {
        var state = new StateHandler();
        ReadsAll(state, w => { w.WriteByte(0); w.WriteByte(0); w.WriteInt(1); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); });
        ReadsAll(state, w => { w.WriteByte(1); w.WriteByte(0); w.WriteInt(1); });
        ReadsAll(new TombstoneHandler(), w => { w.WriteInt(1); w.WriteInt(1); });
        ReadsAll(new SetCraftModeHandler(), w => { w.WriteByte(0); w.WriteByte(0); });
    }

    // reconnectionmanagercli.cpp:245 and ReconnectionContext::Write (clientreconnectionstructs.cpp:54).
    [Test]
    public void Reconnect_TheFullRequest() {
        ReadsAll(new ReconnectHandler(), w => {
            w.WriteByte(1);
            w.WriteLong(1);
            w.WriteLong(2);
            w.WriteInt(1);
            w.WriteInt(1);
            w.WriteUnicodeString("machine");
            w.WriteInt(1);
            w.WriteString("host");
            w.WriteInt(1);
        });
    }

    private static T Bare<T>() => (T) RuntimeHelpers.GetUninitializedObject(typeof(T));

    // FUN_1401fa700 (unit_1401f9eb0_packetpayloadserializers.cpp:416), sent from FUN_141276f80 and the commands
    // (uimeratmarketdialog.cpp:4559): the chat kind as an int, the text, the target, the club id.
    [Test]
    public void UserChat_TheChatPayload() {
        var handler = new UserChatHandler { World = test.World, GameStorage = test.Storage, ServerTableMetadata = test.ServerTableMetadata };
        ReadsAll(handler, w => { w.WriteInt(0); w.WriteUnicodeString("text"); w.WriteUnicodeString(""); w.WriteLong(0); });
        ReadsAll(handler, w => { w.WriteInt(0); w.WriteUnicodeString("/command"); w.WriteUnicodeString(""); w.WriteLong(0); });
    }

    // state.cpp:490 (UserSync) and 546 (RideSync): a flag byte, the server tick, the client tick, a record count, and per
    // record the StateSyncObject record (statesyncobject.cpp:113-262) and the client tick, as StateSync.ReadFrom and
    // GuideObjectSyncHandler.ReadRecords read them. Both handlers return before reading for a session that is not in
    // the Connected state, so this was compared by reading, not run.

    // Quest (the sites of questmanager_2.cpp, cinematiccomponent.cpp, uiquestdialog.cpp, questmanager_1.cpp): 2 quest and npc
    // (581), 4 quest, npc and the choice (642, always 0), 6 quest (1813), 7 count and ids, 8 count and ids (2173, 2206), 9 quest
    // and the state byte (219), 12 13 19 quest (1438-1464), 14 (1752), 15 id (1780), 16 id (uilearningquestdialog.cpp:179), 17
    // (uigamemenudialog.cpp:2298), 18 quest (611), 20 id and a short (1800), 24 27 29 39 26 quest or round, 28 id and the
    // target (actionkeyobject.cpp:3038), 33 quest, 34 and 35 (uialliancequestdialog.cpp:685, 697, 710).
    [Test]
    public void Quest_Modes() {
        var handler = new QuestHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata };
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteInt(1); w.WriteInt(1); });
        // Mode 4 (quest, npc, the choice always 0) reads the npc only after a lookup the test session cannot pass:
        // compared by reading.
        ReadsAll(handler, w => { w.WriteByte(6); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(7); w.WriteInt(1); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(8); w.WriteInt(1); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(9); w.WriteInt(1); w.WriteByte(1); });
        foreach (byte mode in new byte[] { 12, 13, 18, 19, 24, 26, 27, 29, 33, 39 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteInt(1); });
        }
        ReadsAll(handler, w => { w.WriteByte(14); });
        ReadsAll(handler, w => { w.WriteByte(15); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(16); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(17); });
        ReadsAll(handler, w => { w.WriteByte(20); w.WriteInt(1); w.WriteShort(1); });
        ReadsAll(handler, w => { w.WriteByte(28); w.WriteInt(1); w.WriteLong(1); });
        ReadsAll(handler, w => w.WriteByte(34));
        ReadsAll(handler, w => w.WriteByte(35));
    }

    // NpcTalk (cinematiccomponent.cpp:588, 649, 754, uitalkcinematic.cpp:1432-1676, uiitemenchantdialog.cpp:409-746,
    // uialliancequestdialog.cpp:296-329): 0, 1 and 2 the npc or the pick as an int, 4 the event as a short (the retry adds the
    // item uid, not read), 6 npc, an int, the event short, 7 and 8 quest and a short, 9, 11 npc and the pick.
    [Test]
    public void NpcTalk_Modes() {
        var handler = new NpcTalkHandler {
            NpcMetadata = Bare<NpcMetadataStorage>(),
            ScriptMetadata = Bare<ScriptMetadataStorage>(),
            ServerTableMetadata = test.ServerTableMetadata,
        };
        ReadsAll(handler, w => w.WriteByte(0));
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteInt(1); });
        // Mode 2 (the pick) is read only inside an open talk: compared by reading.
        ReadsAll(handler, w => { w.WriteByte(4); w.WriteShort(3); });
        ReadsAll(handler, w => { w.WriteByte(6); w.WriteInt(1); w.WriteInt(1); w.WriteShort(3); });
        // Modes 7 and 8 (quest, short) are read only for a quest the open talk offers: compared by reading.
        ReadsAll(handler, w => w.WriteByte(9));
        ReadsAll(handler, w => { w.WriteByte(11); w.WriteInt(1); w.WriteInt(1); });
    }

    // Party: 1 and 17 a name (FUN_1401fbd30, uipartydialog.cpp:1903), 2 name, answer byte, party id (FUN_1401fc0b0), 3 and 29
    // and 46 the mode alone, 4 and 45 a character id (partymanager.cpp:721, 734), 23 party id, leader name, search id
    // (FUN_1401fbf70), 32 kind, room count, room ids, flag (uidungeonsearchdialog.cpp:452, questmanager_2.cpp:1501), 33 a room
    // id (uidungeonroomdialog.cpp:1328), 34 the kind byte, 39 and 43 party id and room id (uimessenger.cpp:2501, 2519), 48 vote
    // id and flag (partyvotecli.cpp:170).
    [Test]
    public void Party_Modes() {
        var handler = new PartyHandler { World = test.World, ServerTableMetadata = test.ServerTableMetadata };
        foreach (byte mode in new byte[] { 1, 17 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteUnicodeString("name"); });
        }
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteUnicodeString("name"); w.WriteByte(1); w.WriteInt(1); });
        foreach (byte mode in new byte[] { 3, 29, 46 }) {
            byte sent = mode;
            ReadsAll(handler, w => w.WriteByte(sent));
        }
        foreach (byte mode in new byte[] { 4, 45 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteLong(1); });
        }
        ReadsAll(handler, w => { w.WriteByte(23); w.WriteInt(1); w.WriteUnicodeString("leader"); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(32); w.WriteByte(1); w.WriteInt(1); w.WriteInt(1); w.WriteBool(false); });
        ReadsAll(handler, w => { w.WriteByte(33); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(34); w.WriteByte(1); });
        foreach (byte mode in new byte[] { 39, 43 }) {
            byte sent = mode;
            ReadsAll(handler, w => { w.WriteByte(sent); w.WriteInt(1); w.WriteInt(1); });
        }
        ReadsAll(handler, w => { w.WriteByte(48); w.WriteInt(1); w.WriteBool(true); });
    }

    // RequestItemStorage (store.cpp: 0 the payload FUN_1401fbaf0: store id, item, slot, count; 1 the same; 2 store id, item,
    // slot (storeslot.cpp:362); 3 store id, action byte, amount (557); 6 store id (570); 8 store id (548, which the client
    // calls RequestClose but sends from its sort button, uiobjectstoredialog.cpp:217); 10 the item (networkservice.cpp:518);
    // 12 store id (539); 15 none (580, the client's RequestSort, called when the window closes, uistatuseventlistener.cpp:543)).
    [Test]
    public void StorageInventory_Modes() {
        var handler = new StorageInventoryHandler();
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteLong(0); w.WriteLong(1); w.WriteShort(1); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(1); w.WriteLong(0); w.WriteLong(1); w.WriteShort(1); w.WriteInt(1); });
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteLong(0); w.WriteLong(1); w.WriteShort(1); });
        ReadsAll(handler, w => { w.WriteByte(3); w.WriteLong(0); w.WriteBool(true); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(6); w.WriteLong(0); });
        ReadsAll(handler, w => { w.WriteByte(8); w.WriteLong(0); });
        ReadsAll(handler, w => { w.WriteByte(10); w.WriteLong(1); });
        ReadsAll(handler, w => { w.WriteByte(12); w.WriteLong(0); });
        ReadsAll(handler, w => w.WriteByte(15));
    }

    // Shop (uishopdialog.cpp: 3 the serial (2826), 4 serial, quantity, shop id (2846), 5 the item and the count (itemugcmesh.cpp:1097),
    // 9 the restock price (2550), 10 and 13 none (750, 762, 2270)); SystemShop (networkservice.cpp:530: the mode and the open
    // flag; 10 the open flag then the item id: invenslot.cpp:862).
    [Test]
    public void Shop_AndSystemShop_Modes() {
        var shop = new ShopHandler { GameStorage = test.Storage };
        ReadsAll(shop, w => { w.WriteByte(3); w.WriteInt(1); });
        ReadsAll(shop, w => { w.WriteByte(4); w.WriteInt(1); w.WriteInt(1); w.WriteInt(1); });
        ReadsAll(shop, w => { w.WriteByte(5); w.WriteLong(1); w.WriteInt(1); });
        ReadsAll(shop, w => { w.WriteByte(9); w.WriteInt(1); });
        ReadsAll(shop, w => w.WriteByte(10));
        ReadsAll(shop, w => w.WriteByte(13));
        var system = new SystemShopHandler { ServerTableMetadata = test.ServerTableMetadata };
        foreach (byte mode in new byte[] { 3, 4, 6, 7 }) {
            byte sent = mode;
            ReadsAll(system, w => { w.WriteByte(sent); w.WriteByte(1); });
        }
        ReadsAll(system, w => { w.WriteByte(10); w.WriteByte(1); w.WriteInt(1); });
    }

    // Revival (uirevivaldialog.cpp: 0 none (312, 425), 1 and 2 the coupon byte (234, 468, 538), statesummon.cpp:811 3 the object
    // id); Taxi (actionkeyobject.cpp:800 mode 0 and autofindtaxistation.cpp:299 mode 5: none, statetaxicalldata.cpp:285 mode 1: the
    // WriteTaxiRequest ints (taxi id, a cost the server ignores), statecashcalldata.cpp:369, 376 modes 3 and 4: the
    // destination field, which the decompile names m_nPrice but CCashCallManager sets from its nCallType, the field checked
    // against the visited fields).
    [Test]
    public void Revival_AndTaxi_Modes() {
        var revival = new RevivalHandler { ServerTableMetadata = test.ServerTableMetadata };
        ReadsAll(revival, w => w.WriteByte(0));
        ReadsAll(revival, w => { w.WriteByte(1); w.WriteByte(0); });
        ReadsAll(revival, w => { w.WriteByte(2); w.WriteByte(0); });
        ReadsAll(revival, w => { w.WriteByte(3); w.WriteInt(1); });
        var taxi = new TaxiHandler {
            MapMetadata = Bare<MapMetadataStorage>(),
            EntityMetadata = Bare<MapEntityStorage>(),
            TaxiRoute = Bare<TaxiRouteStorage>(),
            ServerTableMetadata = test.ServerTableMetadata,
        };
        ReadsAll(taxi, w => w.WriteByte(0));
        ReadsAll(taxi, w => w.WriteByte(5));
        ReadsAll(taxi, w => { w.WriteByte(3); w.WriteInt(1); });
        ReadsAll(taxi, w => { w.WriteByte(4); w.WriteInt(1); });
    }

    // RequestRide (mypc.cpp:4602 and statesummon.cpp:353 mode 0: RideOnActionUseItem::vfunc_1 = type byte, ride id, object id,
    // option byte, item int, item uid, then the look the server does not read; 1 and 4 none, 2 the ride id and the item uid
    // (WriteRideRequest, 4689), 3 an int (3612), 6 a byte (3633)). The start's look is read from the item by the server.
    [Test]
    public void Ride_Modes() {
        var handler = new RideHandler { RideMetadata = Bare<RideMetadataStorage>() };
        ReadsAll(handler, w => w.WriteByte(1));
        ReadsAll(handler, w => w.WriteByte(4));
        // Modes 2 and 3 read their ids only while riding or in reach of another rider: compared by reading.
        ReadsAll(handler, w => { w.WriteByte(6); w.WriteByte(1); });
    }

    private static void Cell(ByteWriter w) => w.WriteInt(0x010203);

    private static void HeldCube(ByteWriter w) {
        w.WriteInt(1);      // item id
        w.WriteLong(1);     // instance id
        w.WriteLong(1);     // count
        w.WriteByte(0);     // no look
    }

    // RequestCube, every mode the client sends (cubecraft_2.cpp, uihome*.cpp, uirealestate*.cpp, uiugccube*.cpp,
    // unit_140fa9eb0_uihomesaveslotdialog.cpp and the others named in the report): the cell is the client's packed 4 byte
    // key, a cube is CubeItemInfo (FUN_1408b39c0: item id, instance id, count, look flag), a name is a unicode string.
    // Modes 35, 47, 48 (layout slot or cube id), 51, 52, 54 (background, lighting, camera byte), 65 (blueprint slot) and 66 (blueprint
    // uid and a flag the server does not read) are read only after the home and permission checks the test session cannot
    // pass: compared by reading, not run.
    [Test]
    public void RequestCube_Modes() {
        var handler = new RequestCubeHandler { TableMetadata = test.TableMetadata };
        ReadsAll(handler, w => { w.WriteByte(0); w.WriteInt(0); });                                // build state (cubecraft_2.cpp:317)
        ReadsAll(handler, w => { w.WriteByte(2); w.WriteInt(1); w.WriteInt(1); });                 // buy plot (uirealestatedialog.cpp:420)
        ReadsAll(handler, w => w.WriteByte(6));                                                    // forfeit (uirealestatesell.cpp:255)
        ReadsAll(handler, w => w.WriteByte(9));                                                    // extend (uirealestatedialog.cpp:478)
        ReadsAll(handler, w => { w.WriteByte(10); Cell(w); HeldCube(w); w.WriteFloat(0); });        // place cube (cubecraft_2.cpp:998)
        ReadsAll(handler, w => { w.WriteByte(11); Cell(w); HeldCube(w); w.WriteFloat(0); w.WriteByte(0); }); // place attached (1302)
        ReadsAll(handler, w => { w.WriteByte(12); Cell(w); });                                      // remove cube (1161)
        ReadsAll(handler, w => { w.WriteByte(13); Cell(w); });                                      // remove cube 13 (1149)
        ReadsAll(handler, w => { w.WriteByte(14); Cell(w); w.WriteBool(false); });                  // rotate (1098)
        ReadsAll(handler, w => { w.WriteByte(15); Cell(w); HeldCube(w); w.WriteFloat(0); });        // replace (1028)
        ReadsAll(handler, w => { w.WriteByte(16); Cell(w); HeldCube(w); w.WriteFloat(0); w.WriteByte(0); }); // replace attached (1313)
        ReadsAll(handler, w => { w.WriteByte(17); Cell(w); });                                      // liftup (actionkeyobject.cpp:1943)
        ReadsAll(handler, w => w.WriteByte(18));                                                    // liftup drop (pc.cpp:1934)
        ReadsAll(handler, w => { w.WriteByte(21); w.WriteUnicodeString("home"); });                // home name (uihomeinfoeditdialog.cpp:193)
        ReadsAll(handler, w => { w.WriteByte(24); w.WriteByte(0); w.WriteUnicodeString("pw"); });   // passcode (uihomepassworddialog.cpp:207)
        ReadsAll(handler, w => w.WriteByte(25));                                                    // vote (uimessagebox.cpp:1484)
        ReadsAll(handler, w => { w.WriteByte(29); w.WriteUnicodeString("message"); });             // message (uihomeinfoeditdialog.cpp:209)
        ReadsAll(handler, w => { w.WriteByte(30); w.WriteUnicodeString("id"); w.WriteUnicodeString("text"); }); // summon
        ReadsAll(handler, w => w.WriteByte(31));                                                    // clear (uihomemenudialog.cpp:769)
        foreach (byte mode in new byte[] { 37, 38, 40, 44, 45, 49, 63 }) {
            byte sent = mode;
            ReadsAll(handler, w => w.WriteByte(sent));                                              // area, height, check in, kick, blueprint
        }
        ReadsAll(handler, w => { w.WriteByte(41); w.WriteByte(0); });                               // interior reward (uimasterydialog.cpp:516)
        ReadsAll(handler, w => { w.WriteByte(42); w.WriteByte(0); w.WriteBool(true); });            // permission on (uihomeoptiondialog.cpp:256)
        ReadsAll(handler, w => { w.WriteByte(43); w.WriteByte(0); w.WriteByte(0); });               // permission level (267)
        ReadsAll(handler, w => { w.WriteByte(46); w.WriteInt(1); w.WriteUnicodeString("slot"); });  // save layout
        ReadsAll(handler, w => { w.WriteByte(64); w.WriteInt(1); w.WriteUnicodeString("bp"); });    // save blueprint
        ReadsAll(handler, w => { w.WriteByte(68); w.WriteLong(1); });                               // preview
        ReadsAll(handler, w => { w.WriteByte(70); w.WriteLong(1); });                               // apply
    }
}
