using System.Linq;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

// The world map's crowd ranking: online players outside dungeons count per field and channel; a field counts its
// busiest channel; the three busiest fields of the world rank 1 to 3. The field ids are made up.
public class WorldMapPopulationRulesTests {
    [Test]
    public void Count_SkipsOfflinePlayersAndDungeons() {
        (int, short, int)[] online = [(2000001, 1, 0), (2000001, 1, 0), (2000001, 2, 0), (2000002, -1, 0), (2000003, 1, 23000001), (0, 1, 0)];
        Assert.That(WorldMapPopulationRules.Count(online).OrderBy(entry => entry.Channel), Is.EqualTo(new[] {
            (2000001, (short) 1, 2),
            (2000001, (short) 2, 1),
        }));
    }

    [Test]
    public void Rank_TakesTheThreeBusiestFieldsOfTheWorld() {
        (int, short, int)[] counts = [
            (2000001, 1, 2), (2000001, 3, 5), (2000002, 1, 7), (2000003, 2, 5), (2000004, 1, 1), (2000005, 1, 9),
        ];
        // 2000005 is on another world; 2000001 and 2000003 tie at 5 (the lower field first), 2000001 named by channel 3.
        Assert.That(WorldMapPopulationRules.Rank(counts, mapId => mapId != 2000005), Is.EqualTo(new[] {
            (2000002, 1, (short) 1),
            (2000001, 2, (short) 3),
            (2000003, 3, (short) 2),
        }));
    }

    [Test]
    public void Rank_ATieOfChannelsNamesTheLowerOne() {
        (int, short, int)[] counts = [(2000001, 4, 3), (2000001, 2, 3)];
        Assert.That(WorldMapPopulationRules.Rank(counts, _ => true), Is.EqualTo(new[] { (2000001, 1, (short) 2) }));
    }

    [Test]
    public void Rank_OfNobody_IsEmpty() {
        Assert.That(WorldMapPopulationRules.Rank([(2000001, 1, 0)], _ => true), Is.Empty);
    }
}
