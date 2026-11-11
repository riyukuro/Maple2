using Maple2.File.Ingest;

namespace Maple2.Server.Tests.Ingest;

// The ingest warns when LOCALE is not the played client's region (the one table/<region>/ folder of its pack).
public class PlayedLocaleWarningTests {
    [Test]
    public void RegionFolders_AreTheTableRegionFolders() {
        string[] names = ["table/kr/itemwebfinder.xml", "table/kr/other.xml", "table/itemdata.xml", "string/en/a.xml", "table/zz/a.xml"];
        Assert.That(PackTables.RegionFolders(names), Is.EqualTo(new[] { "KR" }));
    }

    [Test]
    public void Warning_NamesBothWhenTheyDiffer() {
        string? warning = PackTables.PlayedLocaleWarning(["KR"], "NA");
        Assert.That(warning, Does.Contain("LOCALE is NA").And.Contain("pack (MS2_PLAYED_CLIENT_XML) is KR").And.Contain("LOCALE=KR"));
    }

    [Test]
    public void Warning_NoneWhenTheyMatchOrTheRegionIsUnknown() {
        Assert.Multiple(() => {
            Assert.That(PackTables.PlayedLocaleWarning(["KR"], "KR"), Is.Null);
            Assert.That(PackTables.PlayedLocaleWarning(["NA"], "na"), Is.Null);
            Assert.That(PackTables.PlayedLocaleWarning([], "NA"), Is.Null);
            Assert.That(PackTables.PlayedLocaleWarning(["KR", "NA"], "NA"), Is.Null);
        });
    }
}
