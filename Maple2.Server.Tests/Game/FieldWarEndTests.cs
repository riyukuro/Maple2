using System.Collections.Generic;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// FieldWarEnd on a war map's field: a clear gives the players the war's reward content once per room; a failed war
// gives nothing. Made-up reward ids and amounts.
public class FieldWarEndTests {
    private const int WarMap = 2020061;
    private const int Reward = 99931001;

    private static (TestGameSession Test, FieldManager Field) Start() {
        var test = new TestGameSession();
        Setup(test);
        return (test, test.Session.Field!);
    }

    private static void Setup(TestGameSession test) {
        test.SetServerTable(new TimeEventTable(new Dictionary<int, GlobalPortalMetadata>(), new Dictionary<int, WorldBossMetadata>(),
            FieldWar: new Dictionary<int, FieldWarMetadata> { [10000001] = new(10000001, 1, 1, WarMap, Reward, 2020003) }));
        test.SetTable(new RewardContentTable(
            new Dictionary<int, RewardContentTable.Base> { [Reward] = new(Reward, MesoTableId: 100001, ExpTableId: 0, MesoFactor: 0f, ExpFactor: 0f, ItemTableId: 0, PrestigeExpTableId: 0) },
            new Dictionary<int, RewardContentTable.Item>(), new Dictionary<int, long> { [100001] = 5000 },
            new Dictionary<int, Dictionary<int, long>>(), new Dictionary<int, long>()));
        test.Start();
        test.EnterMap(TestGameSession.Map(WarMap));
    }

    [Test]
    public void Clear_GivesTheWarsRewardOnce() {
        (TestGameSession test, FieldManager field) = Start();
        GameSession session = test.Session;
        long meso = session.Currency.Meso;

        int first = field.EndFieldWar(true);
        int second = field.EndFieldWar(true);

        Assert.Multiple(() => {
            Assert.That(first, Is.EqualTo(Reward));
            Assert.That(second, Is.EqualTo(0));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso + 5000));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Every player in the room at the end is paid once; one who left the room before the end is not.
    [Test]
    public void Clear_PaysEachPlayerInTheRoomOnce() {
        var first = new TestGameSession();
        var second = new TestGameSession();
        var gone = new TestGameSession();
        foreach (TestGameSession test in new[] { first, second, gone }) {
            Setup(test);
        }
        FieldManager field = first.Session.Field!;
        field.Players.Clear();
        field.Players[1001] = first.Session.Player;
        field.Players[1002] = second.Session.Player;
        second.Session.Field = field;
        long[] meso = [first.Session.Currency.Meso, second.Session.Currency.Meso, gone.Session.Currency.Meso];

        field.EndFieldWar(true);
        field.EndFieldWar(true);

        Assert.Multiple(() => {
            Assert.That(first.Session.Currency.Meso, Is.EqualTo(meso[0] + 5000));
            Assert.That(second.Session.Currency.Meso, Is.EqualTo(meso[1] + 5000));
            Assert.That(gone.Session.Currency.Meso, Is.EqualTo(meso[2]));
            Assert.That(first.Errors, Is.Empty);
            Assert.That(second.Errors, Is.Empty);
        });
    }

    [Test]
    public void Fail_GivesNothingAndEndsTheWar() {
        (TestGameSession test, FieldManager field) = Start();
        long meso = test.Session.Currency.Meso;

        Assert.Multiple(() => {
            Assert.That(field.EndFieldWar(false), Is.EqualTo(0));
            Assert.That(field.EndFieldWar(true), Is.EqualTo(0));
            Assert.That(test.Session.Currency.Meso, Is.EqualTo(meso));
        });
    }
}
