using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// Layouts against the client's readers:
// NpcTalk: UITalkManager::OnTalkPacket (uitalkcinematic.cpp:1018). Mode 3 carries a sub mode byte: 3 (move the player)
// reads an int, 7 (meso reward) an int64, 10 (cutscene) an int and a unicode string; mode 9 (alliance talk) reads the
// flag byte then three ints (the dialogue's id, index and button).
// SystemShop: CSceneStateGame::OnSystemShop (scenestategame.cpp:7928), a kind byte and a bool.
// MassiveEvent: UIMassiveEvent::FUN_1419f5e90 (uimassiveevent.cpp:183), a kind byte then per kind: 0 four ints; 1 a
// unicode string and three ints; 2 a type byte, a unicode string and an int; 3, 5 and 6 a string id text and an int;
// 4 a string id text and three ints; 7 an int, a bool and an int; 8 an int.
public class ScriptEventPacketLayoutTests {
    private static string Op(SendOp op) => Convert.ToHexString([(byte) (ushort) op, (byte) ((ushort) op >> 8)]);
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string Int(int value) => Convert.ToHexString(BitConverter.GetBytes(value));
    private static string Long(long value) => Convert.ToHexString(BitConverter.GetBytes(value));

    private static string Text(InterfaceText text) {
        ByteWriter writer = Packet.Of(SendOp.MassiveEvent);
        writer.WriteClass<InterfaceText>(text);
        return Hex(writer)[4..];
    }

    [Test]
    public void NpcTalk_ActionsAndAllianceTalkFollowTheClientsReader() {
        var dialogue = new NpcDialogue(7, 8, (NpcTalkButton) 9);
        Assert.Multiple(() => {
            Assert.That(Hex(NpcTalkPacket.MovePlayer(5)), Is.EqualTo(Op(SendOp.NpcTalk) + "03" + "03" + Int(5)));
            Assert.That(Hex(NpcTalkPacket.RewardMeso(300)), Is.EqualTo(Op(SendOp.NpcTalk) + "03" + "07" + Long(300)));
            Assert.That(Hex(NpcTalkPacket.Cutscene("M", 2000001)),
                Is.EqualTo(Op(SendOp.NpcTalk) + "03" + "0A" + Int(2000001) + "0100" + "4D00"));
            Assert.That(Hex(NpcTalkPacket.AllianceTalk((NpcTalkType) 2, dialogue)),
                Is.EqualTo(Op(SendOp.NpcTalk) + "09" + "02" + Int(7) + Int(8) + Int(9)));
        });
    }

    [Test]
    public void SystemShop_IsAKindAndABool() {
        Assert.Multiple(() => {
            Assert.That(Hex(SystemShopPacket.Arena()), Is.EqualTo(Op(SendOp.SystemShop) + "03" + "01"));
            Assert.That(Hex(SystemShopPacket.Mentee()), Is.EqualTo(Op(SendOp.SystemShop) + "06" + "01"));
            Assert.That(Hex(SystemShopPacket.Mentor()), Is.EqualTo(Op(SendOp.SystemShop) + "07" + "01"));
        });
    }

    [Test]
    public void MassiveEvent_FollowsTheEventDialogsKinds() {
        var text = new InterfaceText("T");
        string uni = "0100" + "5400";
        Assert.Multiple(() => {
            Assert.That(Hex(MassiveEventPacket.Round(1, 2, 3, 4)), Is.EqualTo(Op(SendOp.MassiveEvent) + "00" + Int(1) + Int(2) + Int(3) + Int(4)));
            Assert.That(Hex(MassiveEventPacket.Countdown("T", 1, 2)), Is.EqualTo(Op(SendOp.MassiveEvent) + "01" + uni + Int(1) + Int(2) + Int(1)));
            Assert.That(Hex(MassiveEventPacket.Text(text, 5)), Is.EqualTo(Op(SendOp.MassiveEvent) + "03" + Text(text) + Int(5)));
            Assert.That(Hex(MassiveEventPacket.RoundCountdown(text, 1, 2)), Is.EqualTo(Op(SendOp.MassiveEvent) + "04" + Text(text) + Int(1) + Int(2) + Int(1)));
            Assert.That(Hex(MassiveEventPacket.Winner(text, 5)), Is.EqualTo(Op(SendOp.MassiveEvent) + "05" + Text(text) + Int(5)));
            Assert.That(Hex(MassiveEventPacket.GameOver(text, 5)), Is.EqualTo(Op(SendOp.MassiveEvent) + "06" + Text(text) + Int(5)));
            Assert.That(Hex(MassiveEventPacket.StartRound(2, 6, true)), Is.EqualTo(Op(SendOp.MassiveEvent) + "07" + Int(2) + "01" + Int(6)));
            Assert.That(Hex(MassiveEventPacket.PvpCountdown(9)), Is.EqualTo(Op(SendOp.MassiveEvent) + "08" + Int(9)));
        });
    }

    // Mail: CSceneStateGame::OnMail (scenestategame.cpp:11496): mode 3 (returned) reads an int64 mail id, mode 12 (ad
    // bill) an int64 mail id and an int64 time. ResponsePet: CPetManager::OnPetUnsummoned reads the int owner object
    // id and the int64 pet uid; OnPetCountList the int count then an int pet id and an int16 rarity each.
    // ResponseRide mode 5: the int owner object id. SyncNumber: CNetworkService::OnSyncNumber, one byte.
    // RecallScroll mode 1: the type byte, a unicode string, int, int map id, int, int64 end time.
    [Test]
    public void MailPetRideAndRecallModes_FollowTheClientsReaders() {
        Assert.Multiple(() => {
            Assert.That(Hex(MailPacket.Returned()), Is.EqualTo(Op(SendOp.Mail) + "03" + Long(0)));
            Assert.That(Hex(PetPacket.UnSummon(9, 0x0102030405)), Is.EqualTo(Op(SendOp.ResponsePet) + "01" + Int(9) + Long(0x0102030405)));
            Assert.That(Hex(PetPacket.LoadCollection(new System.Collections.Generic.Dictionary<int, short> { [60000001] = 3 })),
                Is.EqualTo(Op(SendOp.ResponsePet) + "07" + Int(1) + Int(60000001) + "0300"));
            Assert.That(Hex(RidePacket.ChangeShared(7)), Is.EqualTo(Op(SendOp.ResponseRide) + "05" + Int(7)));
            Assert.That(Hex(StateSyncPacket.SyncNumber(4)), Is.EqualTo(Op(SendOp.SyncNumber) + "04"));
        });
    }
}
