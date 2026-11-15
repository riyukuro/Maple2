using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools;

namespace Maple2.Server.Tests.Game;

// A field mission counts its goals in the fields the client's quest tracker counts them in (CQuestCommon::FUN_140744b90,
// commonfiles/questdata.cpp:294): the first progress map, or a field whose outdoor parent (CFieldMetaTable::GetParentField,
// built from fieldmetadata.xml's indoor portals) is that map. The server also counted in an indoor field only when the
// mission's map was among the last 3 return maps, which a relog or a 4th hop lost while the client still tracked it.
public class FieldMissionIndoorTests {
    private const int Outdoor = 2000062;
    private const int Indoor = 2000069; // KR fieldmetadata.xml: reached from 2000062 by an indoor portal, its own return field

    // The client's closure: indoor portals walked on through the fields they reach, seeded only by outdoor fields; a
    // field reached from two outdoor fields keeps the later one of the map's walk.
    [Test]
    public void BuildIndoorParents_WalksIndoorPortalsFromOutdoorFields() {
        var outdoor = new Dictionary<int, bool> { [1] = true, [2] = false, [3] = false, [18] = true, [5] = false, [6] = true };
        var indoor = new Dictionary<int, IReadOnlyList<int>> {
            [1] = [2], // outdoor 1 -> indoor 2 -> indoor 3
            [2] = [3],
            [3] = [2], // a loop back adds nothing
            [18] = [3], // a second outdoor field reaching 3, walked before 1 (both in bin 1, the later set first)
            [5] = [7], // an indoor field is not a seed: 7 has no parent
            [6] = [], // no indoor portal: nothing
        };

        Dictionary<int, int> parents = FieldMetaTable.BuildIndoorParents([1, 2, 3, 18, 5, 6], outdoor, indoor);

        Assert.That(parents, Is.EquivalentTo(new Dictionary<int, int> { [2] = 1, [3] = 1 }));
        // Set in the other order, 18 is walked last and keeps 3.
        Assert.That(FieldMetaTable.BuildIndoorParents([18, 1, 2, 3, 5, 6], outdoor, indoor)[3], Is.EqualTo(18));
    }

    // ATL::CAtlMap's walk: bins in order, the last key set first in a bin; the 39th key rehashes 17 bins to 53, which
    // turns each old bin over.
    [Test]
    public void AtlMapOrder_WalksBinsLatestFirstAndRehashes() {
        Assert.That(FieldMetaTable.AtlMapOrder([1, 18, 2, 1]), Is.EqualTo(new[] { 18, 1, 2 }));
        int[] keys = [0, 17, 34, .. Enumerable.Range(1, 16), .. Enumerable.Range(18, 16), 35, 36, 37, 106];
        Assert.That(keys, Has.Length.EqualTo(39));
        // 106 % 53 == 0: set last, before the rehash it heads bin 4 (106 % 17); after it, bin 0 holds 0 and 106 in
        // the old walk's order turned over, 106 last moved so first.
        List<int> order = FieldMetaTable.AtlMapOrder(keys);
        Assert.That(order.Take(3), Is.EqualTo(new[] { 106, 0, 1 }));
    }

    [TestCase(Outdoor, true)]
    [TestCase(Indoor, true)]
    [TestCase(2000001, false)]
    public void FieldMissionTracked_FirstProgressMapOrItsIndoorChild(int mapId, bool expected) {
        var parents = new Dictionary<int, int> { [Indoor] = Outdoor };
        Assert.That(QuestRules.FieldMissionTracked([Outdoor, 2000001], mapId, parents), Is.EqualTo(expected));
    }

    // Only the first progress map is the client's; no parents (an older ingest) or no map counts nothing more.
    [Test]
    public void FieldMissionTracked_OnlyTheFirstMap() {
        var parents = new Dictionary<int, int> { [Indoor] = Outdoor };
        Assert.Multiple(() => {
            Assert.That(QuestRules.FieldMissionTracked([2000001, Outdoor], Indoor, parents), Is.False);
            Assert.That(QuestRules.FieldMissionTracked([Outdoor], Indoor, null), Is.False);
            Assert.That(QuestRules.FieldMissionTracked([], Outdoor, parents), Is.False);
        });
    }

    // In the indoor field after a relog (no return map left): the goal counts with the parents, as before without them.
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void IndoorChild_CountsWithoutTheReturnMap(bool withParents, int expected) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        try {
            test.SetTable(new FieldMetaTable(new Dictionary<int, IReadOnlyList<int>>(), new Dictionary<int, IReadOnlyList<int>>(),
                withParents ? new Dictionary<int, int> { [Indoor] = Outdoor } : null));
            MapMetadata map = TestGameSession.Map(Indoor);
            test.EnterMap(map with { Property = map.Property with { ExploreType = 2 } });
            Character character = session.Player.Value.Character;
            character.MapId = Indoor;
            character.ReturnMaps = new LimitedStack<int>(3);
            QuestMetadata metadata = TestGameSession.QuestData(990244, QuestType.FieldMission) with {
                Conditions = [new ConditionMetadata(ConditionType.trigger, 2, new ConditionMetadata.Parameters(Strings: ["r244"]), null)],
            };
            Quest quest = test.StartQuest(metadata with { Basic = metadata.Basic with { ProgressMaps = [Outdoor] } });

            session.Quest.Update(ConditionType.trigger, codeString: "r244");

            Assert.That(quest.Conditions[0].Counter, Is.EqualTo(expected));
        } finally {
            session.Dispose();
        }
    }

    // An exploreType-2 field entered from the mission's map that is not its indoor child: the client does not track the
    // mission there, so nothing counts; only an ingest without the parent table still counts it by the return map.
    [TestCase(true, 0)]
    [TestCase(false, 1)]
    public void OtherIndoorField_CountsOnlyWithoutParents(bool withParents, int expected) {
        const int otherIndoor = 2000070;
        var test = new TestGameSession();
        GameSession session = test.Start();
        try {
            test.SetTable(new FieldMetaTable(new Dictionary<int, IReadOnlyList<int>>(), new Dictionary<int, IReadOnlyList<int>>(),
                withParents ? new Dictionary<int, int> { [Indoor] = Outdoor } : null));
            MapMetadata map = TestGameSession.Map(otherIndoor);
            test.EnterMap(map with { Property = map.Property with { ExploreType = 2 } });
            Character character = session.Player.Value.Character;
            character.MapId = otherIndoor;
            character.ReturnMaps = new LimitedStack<int>(3, Outdoor);
            QuestMetadata metadata = TestGameSession.QuestData(990251, QuestType.FieldMission) with {
                Conditions = [new ConditionMetadata(ConditionType.trigger, 2, new ConditionMetadata.Parameters(Strings: ["r251"]), null)],
            };
            Quest quest = test.StartQuest(metadata with { Basic = metadata.Basic with { ProgressMaps = [Outdoor] } });

            session.Quest.Update(ConditionType.trigger, codeString: "r251");

            Assert.That(quest.Conditions[0].Counter, Is.EqualTo(expected));
        } finally {
            session.Dispose();
        }
    }
}
