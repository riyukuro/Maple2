using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The quest condition of a home's commendation, from a real KR row, through RequestCube 0x19 and the database.
public class HomeCommendConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const byte VoteHome = 25;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
    }

    private void Commend() {
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(VoteHome)));
    }

    // 23500016: <condition type="commend_home" value="1" />: a commendation of another account's home counts; one in
    // the own home is refused (cant_commend_myself) and a second one the same day too (cant_commend_duplicate).
    [Test]
    public void CommendHome_23500016_AnotherAccountsHomeOnceADay() {
        Quest quest = test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [new ConditionMetadata(ConditionType.commend_home, 1, null, null)],
        });
        test.EnterHome();
        Commend();
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));

        long ownerId;
        using (GameStorage.Request db = test.Storage.Context()) {
            ownerId = db.CreateAccount(new Account { Username = "owner" }, "pw")!.Id;
        }
        var other = (Home) RuntimeHelpers.GetUninitializedObject(typeof(Home));
        typeof(Home).GetField("<AccountId>k__BackingField", Any)!.SetValue(other, ownerId);
        typeof(HomeFieldManager).GetField("<Home>k__BackingField", Any)!.SetValue(session.Field, other);
        Commend();
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
        Commend();

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetLastHomeCommend(test.AccountId), Is.Not.Zero);
        }
    }
}
