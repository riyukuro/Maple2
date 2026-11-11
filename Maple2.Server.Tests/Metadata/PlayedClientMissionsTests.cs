using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

// MS2_PLAYED_CLIENT_XML: which dungeon missions the played client's own mission table can build.
public class PlayedClientMissionsTests {
    private static XmlDocument Table(string rows) {
        var document = new XmlDocument();
        document.LoadXml($"<ms2>{rows}</ms2>");
        return document;
    }

    // The filter the ingest passes: here a row with locale KR or feature Off is not available.
    private static bool Available(XmlElement row) => row.GetAttribute("locale") is "" or "NA" && row.GetAttribute("feature") != "Off";

    [Test]
    public void Buildable_IsTheAvailableRowsWithARegisteredType() {
        XmlDocument table = Table("""
            <DungeonMission id="1" type="PlayTime" />
            <DungeonMission id="2" type="LastHitNpc" locale="NA" />
            <DungeonMission id="3" type="PlayTime" locale="KR" />
            <DungeonMission id="4" type="PlayTime" feature="Off" />
            <DungeonMission id="5" type="" />
            <DungeonMission id="0" type="PlayTime" />
            <DungeonMission id="x" type="PlayTime" />
            <DungeonMission id="6" type="NoSuchClass" />
            <DungeonMission id="7" type="UseSp" />
            <Other id="8" type="PlayTime" />
            """);

        Assert.That(PlayedClientMissions.Buildable(table, Available).OrderBy(id => id), Is.EqualTo(new[] { 1, 2, 7 }));
    }

    // The client's table keeps the first available row of an id (std::map insert); a later row does not replace it.
    [Test]
    public void Buildable_TheFirstAvailableRowOfAnIdCounts() {
        XmlDocument table = Table("""
            <DungeonMission id="1" type="PlayTime" locale="KR" />
            <DungeonMission id="1" type="NoSuchClass" />
            <DungeonMission id="1" type="PlayTime" />
            <DungeonMission id="2" type="PlayTime" locale="KR" />
            <DungeonMission id="2" type="DeathCount" />
            """);

        Assert.That(PlayedClientMissions.Buildable(table, Available).OrderBy(id => id), Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void Buildable_AnEmptyTableBuildsNothing() {
        Assert.That(PlayedClientMissions.Buildable(Table(""), Available), Is.Empty);
    }

    // Every mission type the server ingests has a client class, so a mission of the server's own data is
    // never dropped for its type.
    [Test]
    public void EveryServerMissionTypeHasAClientClass() {
        foreach (string type in System.Enum.GetNames<Maple2.Model.Enum.DungeonMissionType>()) {
            Assert.That(PlayedClientMissions.ClientClasses, Does.Contain(type));
        }
    }

    [Test]
    public void Keep_DropsTheIdsTheClientCannotBuild() {
        var missions = new Dictionary<int, string> { [1] = "a", [2] = "b", [3] = "c" };
        Dictionary<int, string> kept = PlayedClientMissions.Keep(missions, new HashSet<int> { 1, 3, 9 });
        Assert.That(kept.Keys.OrderBy(id => id), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(kept[3], Is.EqualTo("c"));
    }
}
