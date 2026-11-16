using System;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Layouts against the client's readers (commonfiles and client sources):
// NpcNotice: CSceneStateGame::OnNpcNotice (scenestategame.cpp:9145), a mode byte then per mode: 0 the unicode text and
// the int duration; 1 and 2 the int object id and a unicode string; 3 the kind byte, the int duration (the popup's
// npc id slot, "duration" in its xml), four ansi strings (name, icon, sound, script) and the unicode text.
// UiText: CSceneStateGame::FUN_141731190, int actor, int stat, int value.
// SuperWorldChat: CNetworkService::FUN_14153f8c0, byte kind, int actor, and for kind 0 the int item id.
// QuizEvent: UIQuizDialog::OnQuizPacket (uiquizdialog.cpp:128).
// SmartPush: CSceneStateGame::OnSmartPush (scenestategame.cpp:7335), a non-zero first byte then two ints.
// TransferEnchant: UIItemEnchantTransformDialog::OnTransformResult reads nothing after the opcode.
// WebOpen: CSceneStateGame::OnWebOpen, a kind byte and the unicode url.
public class SmallNoticePacketLayoutTests {
    private static string Op(SendOp op) => Convert.ToHexString([(byte) (ushort) op, (byte) ((ushort) op >> 8)]);
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string Int(int value) => Convert.ToHexString(BitConverter.GetBytes(value));
    private static string Uni(string value) => Convert.ToHexString(BitConverter.GetBytes((short) value.Length)) + Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes(value));
    private static string Ansi(string value) => Convert.ToHexString(BitConverter.GetBytes((short) value.Length)) + Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(value));

    [Test]
    public void NpcNotice_FollowsTheClientsModes() {
        Assert.Multiple(() => {
            Assert.That(Hex(NpcNoticePacket.Announce("A", 5)), Is.EqualTo(Op(SendOp.NpcNotice) + "00" + Uni("A") + Int(5)));
            Assert.That(Hex(NpcNoticePacket.TargetEffect(7, "E")), Is.EqualTo(Op(SendOp.NpcNotice) + "01" + Int(7) + Uni("E")));
            Assert.That(Hex(NpcNoticePacket.Animation(7, "S")), Is.EqualTo(Op(SendOp.NpcNotice) + "02" + Int(7) + Uni("S")));
            Assert.That(Hex(NpcNoticePacket.SidePopup(NodePopupType.CutIn, 9, "ill", "voi", "scr", "snd")),
                Is.EqualTo(Op(SendOp.NpcNotice) + "03" + "01" + Int(9) + Ansi("") + Ansi("ill") + Ansi("voi") + Ansi("snd") + Uni("scr")));
        });
    }

    [Test]
    public void UiText_IsActorStatValue() {
        Assert.That(Hex(UiTextPacket.Recover(5, BasicAttribute.Health, 300)), Is.EqualTo(Op(SendOp.UiText) + Int(5) + Int(4) + Int(300)));
    }

    [Test]
    public void SuperChat_IsKindActorAndTheItemOnSelect() {
        Assert.Multiple(() => {
            Assert.That(Hex(SuperChatPacket.Select(5, 20)), Is.EqualTo(Op(SendOp.SuperWorldChat) + "00" + Int(5) + Int(20)));
            Assert.That(Hex(SuperChatPacket.Deselect(5)), Is.EqualTo(Op(SendOp.SuperWorldChat) + "01" + Int(5)));
        });
    }

    [Test]
    public void QuizEvent_FollowsTheDialogsModes() {
        Assert.Multiple(() => {
            Assert.That(Hex(QuizEventPacket.Question("C", "Q", "A", 30)),
                Is.EqualTo(Op(SendOp.QuizEvent) + "00" + Uni("C") + Uni("Q") + Uni("A") + Int(30)));
            Assert.That(Hex(QuizEventPacket.Answer(true, "A", 5)), Is.EqualTo(Op(SendOp.QuizEvent) + "01" + "01" + Uni("A") + Int(5)));
        });
    }

    [Test]
    public void SmartPush_IsAFlagAndTwoInts() {
        Assert.Multiple(() => {
            Assert.That(Hex(SmartPushPacket.ActivateEffect(3, 4)), Is.EqualTo(Op(SendOp.SmartPush) + "01" + Int(3) + Int(4)));
            Assert.That(Hex(SmartPushPacket.ActivateGather(3, 4)), Is.EqualTo(Op(SendOp.SmartPush) + "01" + Int(3) + Int(4)));
        });
    }

    [Test]
    public void TransferEnchantAndWebOpen() {
        Assert.Multiple(() => {
            Assert.That(Hex(TransferEnchantPacket.Result()), Is.EqualTo(Op(SendOp.TransferEnchant)));
            Assert.That(Hex(WebOpenPacket.Open(1, "u")), Is.EqualTo(Op(SendOp.WebOpen) + "01" + Uni("u")));
        });
    }
}
