using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

// Client: UIMessengerBrowser FUN_141a22ee0 (0x90) reads unicode name, int npc id; FUN_141a22fc0 (0x92) reads int npc
// id (client/ms2/uimessenger.cpp). Made-up ids and names.
public class BossKillNoticeTests {
    [Test]
    public void KillBossIsNameThenNpcId() {
        ByteWriter writer = AppendMessagePacket.KillBoss("Tester", 29900001);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.AppendMessageKillBoss));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Tester"));
            Assert.That(reader.ReadInt(), Is.EqualTo(29900001));
            Assert.That(writer.Length, Is.EqualTo(2 + 2 + 12 + 4));
        });
    }

    [Test]
    public void AssistBonusIsNpcId() {
        ByteWriter writer = AppendMessagePacket.AssistBonus(29900002);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.AppendMessageAssistBonus));
            Assert.That(reader.ReadInt(), Is.EqualTo(29900002));
            Assert.That(writer.Length, Is.EqualTo(2 + 4));
        });
    }

    [Test]
    public void BonusExpUpCarriesTypeAndNpcObject() {
        var reader = new ByteReader(ExperienceUpPacket.Add(300, 5000, 0, ExpMessageCode.s_msg_take_assist_bonus_exp, 7001).Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ExpUp));
            Assert.That(reader.ReadByte(), Is.Zero);
            Assert.That(reader.ReadLong(), Is.EqualTo(300));
            Assert.That(reader.Read<ExpMessageCode>(), Is.EqualTo(ExpMessageCode.s_msg_take_assist_bonus_exp));
            Assert.That(reader.ReadLong(), Is.EqualTo(5000));
            Assert.That(reader.ReadLong(), Is.Zero);
            Assert.That(reader.ReadInt(), Is.EqualTo(7001));
            Assert.That(reader.ReadBool(), Is.False);
        });
    }

    [TestCase(1, false, false)]
    [TestCase(2, false, false)]
    [TestCase(3, true, false)]
    [TestCase(4, true, true)]
    [TestCase(5, false, false)]
    [TestCase(6, true, false)]
    [TestCase(7, true, false)]
    public void NoticesByClass(int npcClass, bool killNotice, bool fieldBoss) {
        Assert.Multiple(() => {
            Assert.That(BossKillRules.KillNotice(npcClass), Is.EqualTo(killNotice));
            Assert.That(BossKillRules.FieldBoss(npcClass), Is.EqualTo(fieldBoss));
        });
    }

    [TestCase(1000L, 300L)]
    [TestCase(999L, 299L)]
    [TestCase(1L, 0L)]
    [TestCase(0L, 0L)]
    [TestCase(-5L, 0L)]
    public void FieldBossBonusIsThirtyPercentRoundedDown(long killExp, long bonus) {
        Assert.That(BossKillRules.FieldBossBonus(killExp), Is.EqualTo(bonus));
    }

    [Test]
    public void DeadDealerGetsNoKillExp() {
        Assert.Multiple(() => {
            Assert.That(BossKillRules.DealerGetsExp(dead: false), Is.True);
            Assert.That(BossKillRules.DealerGetsExp(dead: true), Is.False);
        });
    }
}
