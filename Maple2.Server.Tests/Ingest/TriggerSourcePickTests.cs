using System.Linq;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// KR file names. The played client's trigger files win over the data pack's: 02000410_bf/clearcheck.xml is in both
// (the played copy sends dungeon mission 24090013, the data pack's 24090003), 02000374_bf/startportal.xml is only in
// the played client, and a file only the data pack has is kept.
public class TriggerSourcePickTests {
    private static readonly string[] Data = [
        "trigger/02000410_bf/clearcheck.xml",
        "trigger/02000374_bf/main.xml",
        "trigger/99999999_dev/onlydata.xml",
        "table/feature.xml",
    ];
    private static readonly string[] Played = [
        "trigger/02000410_bf/ClearCheck.xml",
        "trigger/02000374_bf/main.xml",
        "trigger/02000374_bf/startportal.xml",
        "npcdata/240.xml",
    ];

    [Test]
    public void PlayedCopy_WinsOverTheDataPack() {
        var picked = TriggerMapper.Pick(Data, Played);
        Assert.That(picked.Where(pick => pick.Name.ToLowerInvariant() == "trigger/02000410_bf/clearcheck.xml"),
            Is.EqualTo(new[] { ("trigger/02000410_bf/ClearCheck.xml", true) }));
        Assert.That(picked.Count(pick => pick.Name == "trigger/02000374_bf/main.xml"), Is.EqualTo(1));
        Assert.That(picked, Does.Contain(("trigger/02000374_bf/main.xml", true)));
    }

    [Test]
    public void PlayedOnly_AndDataOnly_AreBothKept() {
        var picked = TriggerMapper.Pick(Data, Played);
        Assert.That(picked, Does.Contain(("trigger/02000374_bf/startportal.xml", true)));
        Assert.That(picked, Does.Contain(("trigger/99999999_dev/onlydata.xml", false)));
        Assert.That(picked.Select(pick => pick.Name), Has.None.StartsWith("table/").And.None.StartsWith("npcdata/"));
        Assert.That(picked, Has.Count.EqualTo(4));
    }

    [Test]
    public void NoPlayedClient_TakesTheDataPack() {
        var picked = TriggerMapper.Pick(Data, null);
        Assert.That(picked, Is.EqualTo(new[] {
            ("trigger/02000410_bf/clearcheck.xml", false),
            ("trigger/02000374_bf/main.xml", false),
            ("trigger/99999999_dev/onlydata.xml", false),
        }));
    }
}
