using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// SetDebugMode mode 4 bodies as the client's NPC control window writes them (after the mode byte).
public class GmDebugRequestsTests {
    private static ByteReader Reader(string hex) {
        return new ByteReader(Convert.FromHexString(hex.Replace(" ", "")), 0);
    }

    [Test]
    public void NpcControl_SelectAndStopSkillHaveNoFields() {
        foreach (string command in new[] { "00", "09" }) {
            ByteReader reader = Reader("2A 00 00 00 " + command);

            GmNpcControlRequest request = GmNpcControlRequest.Read(reader);

            Assert.That(request.ObjectId, Is.EqualTo(42));
            Assert.That(reader.Available, Is.EqualTo(0));
        }
    }

    [Test]
    public void NpcControl_HealthPercentIsOneLong() {
        ByteReader reader = Reader("2A 00 00 00 01 32 00 00 00 00 00 00 00");

        GmNpcControlRequest request = GmNpcControlRequest.Read(reader);

        Assert.That(request.Command, Is.EqualTo(GmNpcControlRequest.HealthPercent));
        Assert.That(request.Value, Is.EqualTo(50));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void NpcControl_UseSkillList() {
        ByteReader reader = Reader(
            "2A 00 00 00 08 " +
            "01 00 02 00 " + // loop, 2 skills
            "01 00 00 00 01 00 00 00 " +
            "02 00 00 00 03 00 00 00");

        GmNpcControlRequest request = GmNpcControlRequest.Read(reader);

        Assert.That(request.SkillCount, Is.EqualTo(2));
        Assert.That((request.SkillId, request.SkillLevel), Is.EqualTo((1, 1)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void NpcControl_AddAndRemoveEffect() {
        ByteReader add = Reader("2A 00 00 00 0E 65 66 A0 00 03 00 00 00 00 00 00 00");
        ByteReader remove = Reader("2A 00 00 00 0F 65 66 A0 00 00 00 00 00");

        GmNpcControlRequest added = GmNpcControlRequest.Read(add);
        GmNpcControlRequest removed = GmNpcControlRequest.Read(remove);

        Assert.That((added.EffectId, added.EffectLevel), Is.EqualTo((10511973, 3)));
        Assert.That(removed.EffectId, Is.EqualTo(10511973));
        Assert.That(add.Available, Is.EqualTo(0));
        Assert.That(remove.Available, Is.EqualTo(0));
    }

    [Test]
    public void HealthTarget_StaysBetweenOneAndTheTotal() {
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthPercent, 50, 1000), Is.EqualTo(500));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthPercent, 1, 10), Is.EqualTo(1));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthPercent, 0, 1000), Is.EqualTo(-1));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthPercent, 101, 1000), Is.EqualTo(-1));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthValue, 5000, 1000), Is.EqualTo(1000));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthValue, 0, 1000), Is.EqualTo(-1));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.StopSkill, 5, 1000), Is.EqualTo(-1));
    }
}
