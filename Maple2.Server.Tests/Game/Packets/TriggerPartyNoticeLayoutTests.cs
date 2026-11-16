using System;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Layouts against the client's readers:
// Trigger: CTriggerService::FUN_141861840 (triggerservice.cpp:366) reads a kind byte. Kind 12 (OnTextPopup) an ansi
// string (the script text); kind 22 (FUN_141862930) an int result (0 shows the rollback message); kind 15
// (FUN_141862110) a dialog kind byte, and for dialog kinds 3 and 4 an int, an int and a byte (the start tick, the
// duration and whether it removes itself).
// Party mode 20: CPartyManager::OnPartyNotice (partymanager.cpp:1269) reads three unicode strings: the text key, the
// system sound and the key's argument.
public class TriggerPartyNoticeLayoutTests {
    private static string Op(SendOp op) => Convert.ToHexString([(byte) (ushort) op, (byte) ((ushort) op >> 8)]);
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string Int(int value) => Convert.ToHexString(BitConverter.GetBytes(value));

    [Test]
    public void EditScriptAndResetScript_FollowTheClientsReaders() {
        Assert.Multiple(() => {
            Assert.That(Hex(TriggerPacket.EditScript("ab")), Is.EqualTo(Op(SendOp.Trigger) + "0C" + "0200" + "6162"));
            Assert.That(Hex(TriggerPacket.ResetScript()), Is.EqualTo(Op(SendOp.Trigger) + "16" + Int(0)));
            Assert.That(Hex(TriggerPacket.ResetScript(true)), Is.EqualTo(Op(SendOp.Trigger) + "16" + Int(1)));
        });
    }

    [TestCase(3)]
    [TestCase(4)]
    public void PvpDialogs_AreATickADurationAndARemoveFlag(int kind) {
        string hex = Hex(kind == 3 ? TriggerPacket.PvpRankingDialog(30, false) : TriggerPacket.PvpTeamScoreDialog(30));
        Assert.That(hex, Does.StartWith(Op(SendOp.Trigger) + "0F" + "0" + kind));
        string body = hex[(Op(SendOp.Trigger).Length + 4)..];
        Assert.That(body, Has.Length.EqualTo(8 + 8 + 2));
        Assert.That(body[8..], Is.EqualTo(Int(30) + (kind == 3 ? "00" : "01")));
    }

    [Test]
    public void PartyNotice_IsKeySoundAndArgument() {
        Assert.That(Hex(PartyPacket.PartyNotice(PartyMessage.s_party_vote_expired, "S")),
            Is.EqualTo(Op(SendOp.Party) + "14" + "1400" + Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes("s_party_vote_expired"))
                + "0100" + "5300" + "0000"));
    }
}
