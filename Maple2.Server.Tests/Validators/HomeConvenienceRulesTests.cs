using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class HomeConvenienceRulesTests {
    // RequestHomeBank 0x004D: one byte, the kind of the button (1 home dialog, 2 inventory).
    [TestCase((byte) 1, false, true, true)]
    [TestCase((byte) 1, true, false, false)] // the home dialog's kind from outside the own home
    [TestCase((byte) 1, false, false, false)]
    [TestCase((byte) 2, true, false, true)]
    [TestCase((byte) 2, false, true, false)]
    [TestCase((byte) 0, true, true, false)]
    [TestCase((byte) 3, true, true, false)]
    public void BankKindByte(byte wire, bool premium, bool inOwnHome, bool expected) {
        var packet = new ByteReader(new byte[] { 0x4D, 0x00, wire }, 2);
        byte kind = packet.ReadByte();
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(HomeConvenienceRules.IsBankKindAllowed(kind, premium, inOwnHome), Is.EqualTo(expected));
    }

    [TestCase(1001, 1000, true)]
    [TestCase(1000, 1000, false)] // the end second itself is no longer premium
    [TestCase(0, 1000, false)]
    public void PremiumEndsAtItsSecond(long end, long now, bool expected) {
        Assert.That(HomeConvenienceRules.IsPremium(end, now), Is.EqualTo(expected));
    }

    // The cooltime constant is milliseconds, the call times are seconds.
    [TestCase(0, 1800000, 1000, true)] // never called
    [TestCase(1000, 1800000, 1001, false)]
    [TestCase(1000, 1800000, 2799, false)]
    [TestCase(1000, 1800000, 2800, true)]
    [TestCase(1000, 1800000, 500000, true)]
    [TestCase(1000, 0, 1000, true)]
    public void CooltimeIsMilliseconds(long last, int cooltimeMs, long now, bool expected) {
        Assert.That(HomeConvenienceRules.IsReady(last, cooltimeMs, now), Is.EqualTo(expected));
    }

    // RequestHomeDoctor 0x004E has no body: the place decides.
    [TestCase(true, 5, 5, true)]
    [TestCase(true, 6, 5, false)]
    [TestCase(false, 0, 5, false)]
    public void DoctorOnlyInOwnHome(bool inHome, long owner, long account, bool expected) {
        Assert.That(HomeConvenienceRules.IsDoctorAllowed(inHome, owner, account), Is.EqualTo(expected));
    }

    // "Use the safe inside your home": the home dialog's call, not the inventory's premium button.
    [TestCase(HomeConvenienceRules.BankFromHome, true)]
    [TestCase(HomeConvenienceRules.BankFromInventory, false)]
    public void HomeBankConditionCountsTheHomeCallOnly(byte kind, bool expected) {
        Assert.That(HomeConvenienceRules.CountsAsHomeBank(kind), Is.EqualTo(expected));
    }
}
