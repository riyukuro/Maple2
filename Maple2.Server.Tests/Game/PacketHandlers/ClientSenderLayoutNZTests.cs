using System;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.PacketHandlers;
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
}
