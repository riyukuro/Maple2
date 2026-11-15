using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// A condition code or target that lists ranges next to ids: the client reads each comma item alone, a dashed one as
// a range (CConditionParam::Parse), and a value in any range or equal to any id matches (CConditionParam::Matches).
public class ConditionRangeListTests {
    // KR 40002470: npc, target "02010001-02019999,02000335,02000336,02000337,02000338" (the kill's map).
    private const string KarkarMaps = "02010001-02019999,02000335,02000336,02000337,02000338";
    // KR 89000535 and achievement 22210011: interact_object_rep with ids and ranges in one code.
    private const string Objects = "11000001,12000200-12000203,12000221-12000225,12000231-12000235,12000241-12000245,12000250-12000253,12000261-12000265,14000001-14999999";

    private static GameSession Session() {
        var test = new TestGameSession();
        return test.Start();
    }

    [Test]
    public void ConvertCodes_ListWithRanges_KeepsEachRange() {
        ConditionMetadata.Parameters parameters = KarkarMaps.Split(',').ConvertCodes()!;

        Assert.Multiple(() => {
            Assert.That(parameters.Ranges, Is.EqualTo(new[] { new ConditionMetadata.Range<int>(2010001, 2019999) }));
            Assert.That(parameters.Integers, Is.EqualTo(new[] { 2000335, 2000336, 2000337, 2000338 }));
            Assert.That(parameters.Strings, Is.Null);
        });
    }

    // A dashed item that is not two numbers stays a string, as before.
    [Test]
    public void ConvertCodes_DashedName_StaysAString() {
        ConditionMetadata.Parameters parameters = new[] { "boss-a", "boss-b" }.ConvertCodes()!;

        Assert.Multiple(() => {
            Assert.That(parameters.Strings, Is.EqualTo(new[] { "boss-a", "boss-b" }));
            Assert.That(parameters.Ranges, Is.Null);
        });
    }

    [TestCase(2010500, true)]
    [TestCase(2019999, true)]
    [TestCase(2000336, true)]
    [TestCase(2000339, false)]
    [TestCase(2020000, false)]
    public void Npc_40002470_KillOnAMapOfTheList(int mapId, bool counts) {
        var condition = new ConditionMetadata(ConditionType.npc, 40, null, KarkarMaps.Split(',').ConvertCodes());

        Assert.That(condition.Check(Session(), targetLong: mapId, codeLong: 21000001), Is.EqualTo(counts));
    }

    [TestCase(11000001, true)]
    [TestCase(12000222, true)]
    [TestCase(14123456, true)]
    [TestCase(12000204, false)]
    [TestCase(13000000, false)]
    public void InteractObjectRep_89000535_ObjectOfTheList(int objectId, bool counts) {
        var condition = new ConditionMetadata(ConditionType.interact_object_rep, 10, Objects.Split(',').ConvertCodes(), null);

        Assert.That(condition.Check(Session(), codeLong: objectId), Is.EqualTo(counts));
    }

    // Two ranges and no id (the CN quest 89010015 lists quest_clear "73000001-73000040,73000050-73000059").
    [TestCase(73000020, true)]
    [TestCase(73000055, true)]
    [TestCase(73000045, false)]
    public void QuestClear_TwoRanges_EitherRange(int questId, bool counts) {
        var condition = new ConditionMetadata(ConditionType.quest_clear, 1, "73000001-73000040,73000050-73000059".Split(',').ConvertCodes(), null);

        Assert.That(condition.Check(Session(), codeLong: questId), Is.EqualTo(counts));
    }

    // A lone dashed code that is not two numbers stays a name and does not stop the ingest; a lone range is a range.
    [Test]
    public void ConvertCodes_LoneDashedName_StaysAString() {
        Assert.Multiple(() => {
            Assert.That(new[] { "boss-a" }.ConvertCodes()!.Strings, Is.EqualTo(new[] { "boss-a" }));
            Assert.That(new[] { "1-3" }.ConvertCodes()!.Range, Is.EqualTo(new ConditionMetadata.Range<int>(1, 3)));
            // "-5" has one part around its dash: the client drops it, so nothing matches, alone or in a list.
            Assert.That(new[] { "-5" }.ConvertCodes(), Is.EqualTo(new ConditionMetadata.Parameters()));
            Assert.That(new[] { "-5", "7" }.ConvertCodes()!.Integers, Is.EqualTo(new[] { 7 }));
        });
    }

    // A target that tested only ids takes the ranges of its list too (interact_object's interact type, fame_grade's
    // grade, the lapenshard tier and the others of CheckTarget): the client's Matches holds for any range.
    [TestCase(ConditionType.interact_object, 2, true)]
    [TestCase(ConditionType.interact_object, 5, true)]
    [TestCase(ConditionType.interact_object, 4, false)]
    [TestCase(ConditionType.fame_grade, 2, true)]
    [TestCase(ConditionType.lapenshard_upgrade_result, 4, false)]
    [TestCase(ConditionType.item_move, 3, true)]
    [TestCase(ConditionType.item_move, 4, false)]
    public void IdOnlyTarget_RangeOfTheList(ConditionType type, int value, bool counts) {
        var condition = new ConditionMetadata(type, 1, null, new[] { "1-3", "5" }.ConvertCodes());

        Assert.That(condition.Check(Session(), targetLong: value), Is.EqualTo(counts));
    }
}
