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
    // A played-only row takes its name from the played client's own string folder (string/kr for a KR pack: an NA
    // string/en row of the same id can name another item), else from the ingest's language.
    [Test]
    public void PlayedStringFolder_IsThePacksRegionWhenItHasTheFile() {
        string[] kr = ["table/kr/a.xml", "string/kr/itemname.xml", "string/en/itemname.xml"];
        Assert.Multiple(() => {
            Assert.That(PackTables.PlayedStringFolder(kr, "en", "itemname.xml"), Is.EqualTo("kr"));
            Assert.That(PackTables.PlayedStringFolder(kr, "en", "npcname.xml"), Is.EqualTo("en"));
            Assert.That(PackTables.PlayedStringFolder(["table/kr/a.xml", "table/na/a.xml", "string/kr/itemname.xml"], "en", "itemname.xml"), Is.EqualTo("en"));
            Assert.That(PackTables.PlayedStringFolder(["string/kr/itemname.xml"], "en", "itemname.xml"), Is.EqualTo("en"));
        });
    }
}
