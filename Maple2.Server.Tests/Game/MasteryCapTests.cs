using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// KR table/mastery.xml ends types 3 to 10 at grade 15, value 81440: a gain past it stops there. Breeding (5),
// Engraving (8) and Alchemist (9) keep their own mastery (Ranching, Handicrafts, Alchemy).
public class MasteryCapTests {
    [TestCase(MasteryType.Breeding)]
    [TestCase(MasteryType.Engraving)]
    [TestCase(MasteryType.Alchemist)]
    public void Gain_StopsAtTheLastGrade(MasteryType type) {
        var test = new TestGameSession();
        test.SetTable(new MasteryRewardTable(new System.Collections.Generic.Dictionary<MasteryType,
            System.Collections.Generic.IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        GameSession session = test.Start();
        session.Mastery.Add(type, 81000, 0);
        test.TakeSent();

        session.Mastery.Add(type, 1000, 0);

        Assert.Multiple(() => {
            Assert.That(session.Mastery[type], Is.EqualTo(81440));
            Assert.That(session.Mastery[type], Is.EqualTo(Constant.RanchingMasteryMax));
            Assert.That(System.Convert.ToHexString(test.TakeSent()[0]),
                Is.EqualTo(System.Convert.ToHexString(MasteryPacket.UpdateMastery(type, 81440, 0).ToArray())));
            Assert.That(test.Errors, Is.Empty);
        });
        int others = (type == MasteryType.Breeding ? 0 : session.Player.Value.Character.Mastery.Ranching)
            + (type == MasteryType.Engraving ? 0 : session.Player.Value.Character.Mastery.Handicrafts)
            + (type == MasteryType.Alchemist ? 0 : session.Player.Value.Character.Mastery.Alchemy);
        Assert.That(others, Is.EqualTo(0));
    }
}
