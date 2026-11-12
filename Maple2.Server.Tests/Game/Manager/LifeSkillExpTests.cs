using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The exp of a pet capture, a gather and a craft: the type's common exp curve at the player's level, the ExpUp
// naming the reason the client prints, and the adventure (prestige) exp of the server pack's life-skill rows.
public class LifeSkillExpTests {
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> {
            [ExpType.petTaming] = new(1, 1f),
            [ExpType.gathering] = new(1, 1f),
            [ExpType.manufacturing] = new(1, 1f),
        }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, level => level * 10L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        // The KR server pack's rows (AdventureExpTable).
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long> {
            [ExpType.gathering] = 500,
            [ExpType.manufacturing] = 250,
            [ExpType.petTaming] = 15000,
        }));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000173));
        session.Player.Value.Character.Level = 50;
        test.TakeSent();
        return session;
    }

    // ExpUp: op, byte, i64 gain, u16 reason, ...
    private static (long Gain, int Reason) ExpUp(byte[] packet) =>
        (BitConverter.ToInt64(packet, 3), BitConverter.ToUInt16(packet, 11));

    // Prestige 1 (add exp): op, byte, i64 current, i64 gained.
    private static long PrestigeGain(byte[] packet) => BitConverter.ToInt64(packet, 11);

    [TestCase(ExpType.petTaming, null, 3200, 15000)]
    [TestCase(ExpType.gathering, ExpMessageCode.masteryFarming, 3106, 500)]
    [TestCase(ExpType.gathering, ExpMessageCode.gathering, 3103, 500)]
    [TestCase(ExpType.manufacturing, ExpMessageCode.masteryCooking, 3110, 250)]
    public void Gain_NamesItsReasonAndAddsPrestige(ExpType type, ExpMessageCode? message, int reason, long prestige) {
        GameSession session = Start();

        Assert.That(session.Exp.AddExp(type, message: message), Is.EqualTo(500));

        List<byte[]> sent = test.TakeSent();
        byte[] expUp = sent.Single(packet => TestGameSession.Op(packet) == SendOp.ExpUp);
        Assert.That(ExpUp(expUp), Is.EqualTo((500L, reason)));
        byte[] gain = sent.Single(packet => TestGameSession.Op(packet) == SendOp.Prestige && packet[2] == 1);
        Assert.That(PrestigeGain(gain), Is.EqualTo(prestige));
        Assert.That(session.Exp.PrestigeCurrentExp, Is.EqualTo(prestige));
    }

    // Below the adventure level's start level a capture still gives its exp but no prestige.
    [Test]
    public void Capture_BelowTheStartLevelAddsNoPrestige() {
        GameSession session = Start();
        test.SetServerTable(new ConstantsTable(AdventureLevelStartLevel: 60));

        Assert.That(session.Exp.AddExp(ExpType.petTaming), Is.EqualTo(500));

        Assert.That(test.TakeSent().Exists(packet => TestGameSession.Op(packet) == SendOp.Prestige), Is.False);
    }
}
