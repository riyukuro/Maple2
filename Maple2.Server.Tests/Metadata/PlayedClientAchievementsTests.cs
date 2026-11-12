using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

// MS2_PLAYED_CLIENT_XML: trophies the played client holds locked are marked and do not count.
public class PlayedClientAchievementsTests {
    private static XmlDocument File(string rows) {
        var document = new XmlDocument();
        document.LoadXml($"<ms2>{rows}</ms2>");
        return document;
    }

    private static bool Available(XmlElement node) => node.GetAttribute("feature") != "Off";

    [Test]
    public void LockedIds_AreTheTrophiesWithOnlyLockedRows() {
        XmlDocument first = File("""<achieves id="100" locking="1" />""");
        XmlDocument second = File("""<achieves id="101" />""");
        XmlDocument third = File("""<achieves id="102" locking="" />""");
        XmlDocument fourth = File("""<achieves id="103" locking="1" /><achieves id="103" feature="Later" />""");
        XmlDocument fifth = File("""<achieves id="104" locking="1" /><achieves id="104" feature="Off" />""");
        XmlDocument sixth = File("""<achieves id="105" locking="1" feature="Off" />""");

        Assert.That(PlayedClientAchievements.LockedIds([first, second, third, fourth, fifth, sixth], Available).OrderBy(id => id),
            Is.EqualTo(new[] { 100, 104 }));
    }

    // The played client's flag stands alone for a trophy that client has: a data folder lock does not hold a trophy
    // the played client shows open, and the data folder's flag is used only where the played client has no row.
    [Test]
    public void Locked_IsThePlayedClientsFlagWhenItHasTheTrophy() {
        XmlDocument file = File("""<achieves id="100" locking="1" /><achieves id="101" /><achieves id="105" locking="1" feature="Off" />""");
        Dictionary<int, bool> states = PlayedClientAchievements.LockStates([file], Available);

        Assert.Multiple(() => {
            Assert.That(states, Is.EquivalentTo(new Dictionary<int, bool> { [100] = true, [101] = false }));
            Assert.That(PlayedClientAchievements.Locked(100, false, states), Is.True);
            Assert.That(PlayedClientAchievements.Locked(101, true, states), Is.False);
            Assert.That(PlayedClientAchievements.Locked(105, true, states), Is.True);
            Assert.That(PlayedClientAchievements.Locked(105, false, states), Is.False);
            Assert.That(PlayedClientAchievements.Locked(100, true, new Dictionary<int, bool>()), Is.True);
            Assert.That(PlayedClientAchievements.Locked(100, false, new Dictionary<int, bool>()), Is.False);
        });
    }

    [Test]
    public void IsLocked_IsANonZeroNumber() {
        Assert.Multiple(() => {
            Assert.That(PlayedClientAchievements.IsLocked("1"), Is.True);
            Assert.That(PlayedClientAchievements.IsLocked("0"), Is.False);
            Assert.That(PlayedClientAchievements.IsLocked(""), Is.False);
        });
    }

    [Test]
    public void IsAchieveFile_IsTheAchieveFolder() {
        Assert.Multiple(() => {
            Assert.That(PlayedClientAchievements.IsAchieveFile("achieve/22200455.xml"), Is.True);
            Assert.That(PlayedClientAchievements.IsAchieveFile("Achieve/22200455.xml"), Is.True);
            Assert.That(PlayedClientAchievements.IsAchieveFile("table/achieve.xml"), Is.False);
        });
    }
}
