using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// A box group marked isApplySmartGenderDrop gives only the rows for the opener's gender. KR box 320000003 (a look
// package) has such a group 1 of two rows, 11250085 (male) and 11250086 (female), both of weight 10000.
public class SmartGenderDropTests {
    private const int BoxId = 320000003;
    private const int MaleItem = 11250085;
    private const int FemaleItem = 11250086;

    private static IndividualDropItemTable.Item Row(int itemId) =>
        new([itemId], false, 10000, 0, 10000, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], false);

    private static GameSession Start(bool smartGender, Gender gender) {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(MaleItem, gender: Gender.Male), TestGameSession.Metadata(FemaleItem, gender: Gender.Female));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [BoxId] = new Dictionary<int, IndividualDropItemTable.Entry> {
                [1] = new(1, 0, [new IndividualDropItemTable.Entry.DropCount(1, 100)], 0, false, smartGender, [Row(MaleItem), Row(FemaleItem)]),
            },
        }));
        GameSession session = test.Start();
        session.Player.Value.Character.Gender = gender;
        return session;
    }

    [TestCase(Gender.Male, MaleItem)]
    [TestCase(Gender.Female, FemaleItem)]
    public void SmartGroup_GivesTheOpenersGender(Gender gender, int expected) {
        GameSession session = Start(smartGender: true, gender);

        var got = new HashSet<int>();
        for (int i = 0; i < 40; i++) {
            got.UnionWith(session.Field!.ItemDrop.GetIndividualDropItems(session, 1, BoxId).Select(item => item.Id));
        }

        Assert.That(got, Is.EquivalentTo(new[] { expected }));
    }

    // Without the mark either row may come.
    [Test]
    public void PlainGroup_GivesEitherGender() {
        GameSession session = Start(smartGender: false, Gender.Male);

        var got = new HashSet<int>();
        for (int i = 0; i < 200 && got.Count < 2; i++) {
            got.UnionWith(session.Field!.ItemDrop.GetIndividualDropItems(session, 1, BoxId).Select(item => item.Id));
        }

        Assert.That(got, Is.EquivalentTo(new[] { MaleItem, FemaleItem }));
    }

    // A box id the table does not hold gives nothing.
    [Test]
    public void UnknownBox_GivesNothing() {
        GameSession session = Start(smartGender: true, Gender.Male);

        Assert.That(session.Field!.ItemDrop.GetIndividualDropItems(session, 1, BoxId + 1), Is.Empty);
        Assert.That(session.Field!.ItemDrop.GetIndividualDropItems(BoxId + 1), Is.Empty);
    }
}
