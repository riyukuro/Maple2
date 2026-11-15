using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maple2.File.Ingest;
using Maple2.File.Ingest.Helpers;
using Maple2.File.IO;

namespace Maple2.Server.Tests.Ingest;

// Loose files read over the played client's pack (MS2_PLAYED_CLIENT_OVERLAY). Synthetic files.
[NonParallelizable]
public class PlayedClientOverlayTests {
    private string root = null!;

    [SetUp]
    public void SetUp() {
        root = Directory.CreateTempSubdirectory("ms2-overlay-test-").FullName;
    }

    [TearDown]
    public void TearDown() {
        Directory.Delete(root, true);
    }

    private string Dir(string name, params (string Path, string Text)[] files) {
        string dir = Path.Combine(root, name);
        foreach ((string path, string text) in files) {
            string file = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            System.IO.File.WriteAllText(file, text);
        }
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public void Dirs_Unset_None() {
        Assert.That(PlayedClientOverlay.Dirs(null), Is.Empty);
        Assert.That(PlayedClientOverlay.Dirs(" "), Is.Empty);
    }

    [Test]
    public void Dirs_Missing_Throws() {
        string a = Dir("a");
        Assert.Throws<DirectoryNotFoundException>(() => PlayedClientOverlay.Dirs($"{a};{Path.Combine(root, "none")}"));
    }

    [Test]
    public void Files_LaterDirWins_NamesWithoutCase() {
        string a = Dir("a", ("itemData/203.xml", "a"), ("string/en/x.xml", "a"));
        string b = Dir("b", ("itemdata/203.xml", "b"));
        IReadOnlyDictionary<string, string> files = PlayedClientOverlay.Files(PlayedClientOverlay.Dirs($"{a};{b}"));

        Assert.That(files, Has.Count.EqualTo(2));
        Assert.That(System.IO.File.ReadAllText(files["ITEMDATA/203.xml"]), Is.EqualTo("b"));
        Assert.That(files.ContainsKey("string/en/x.xml"));
    }

    [Test]
    public void Merge_ReplacesKeepsAndAdds() {
        string packDir = Dir("pack", ("itemdata/200.xml", "<ms2 a=\"pack\" />"), ("itemdata/203.xml", "<ms2 b=\"pack\" />"),
            ("table/x.xml", "<ms2 c=\"pack\" />"));
        string pack = Path.Combine(root, "Xml.m2d");
        PlayedClientOverlay.Create(PlayedClientOverlay.Files([packDir]), pack);

        string overlay = Dir("overlay", ("itemData/203.xml", "<ms2 b=\"overlay\" />"), ("itemdata/209.xml", "<ms2 d=\"overlay\" />"));
        string merged = Path.Combine(root, "Merged.m2d");
        PlayedClientOverlay.Counts counts = PlayedClientOverlay.Merge(pack, PlayedClientOverlay.Files([overlay]), merged);

        Assert.That((counts.Kept, counts.Replaced), Is.EqualTo((2, 1)));
        Assert.That(counts.Added, Is.EqualTo(new[] { "itemdata/209.xml" }));
        using var reader = new M2dReader(merged);
        Assert.That(reader.Files.Select(file => file.Name),
            Is.EqualTo(new[] { "itemdata/200.xml", "itemdata/203.xml", "table/x.xml", "itemdata/209.xml" }));
        Assert.That(reader.Files.Select(file => file.Index), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        string Attribute(string name, string attribute) =>
            reader.GetXmlDocument(reader.Files.Single(file => file.Name == name)).DocumentElement!.GetAttribute(attribute);
        Assert.That(Attribute("itemdata/200.xml", "a"), Is.EqualTo("pack"));
        Assert.That(Attribute("itemdata/203.xml", "b"), Is.EqualTo("overlay"));
        Assert.That(Attribute("table/x.xml", "c"), Is.EqualTo("pack"));
        Assert.That(Attribute("itemdata/209.xml", "d"), Is.EqualTo("overlay"));
    }

    // An added file is named in lower case, as pack names are (the restored client's Table/na/... is table/na/...).
    // The played pack's region is told from its own names: the overlay's table/na/ over a KR pack is not a second
    // region folder.
    [Test]
    public void Merge_AddedNamesLowerCase_NotARegion() {
        string packDir = Dir("pack", ("table/kr/a.xml", "<ms2 />"), ("string/kr/itemname.xml", "<ms2 />"));
        string pack = Path.Combine(root, "Xml.m2d");
        PlayedClientOverlay.Create(PlayedClientOverlay.Files([packDir]), pack);

        string overlay = Dir("overlay", ("Table/na/mapleSurvivalSkinInfo.xml", "<ms2 />"));
        string merged = Path.Combine(root, "Merged.m2d");
        PlayedClientOverlay.Counts counts = PlayedClientOverlay.Merge(pack, PlayedClientOverlay.Files([overlay]), merged);

        Assert.That(counts.Added, Is.EqualTo(new[] { "table/na/maplesurvivalskininfo.xml" }));
        using var reader = new M2dReader(merged);
        Assert.That(reader.Files.Select(file => file.Name), Does.Contain("table/na/maplesurvivalskininfo.xml"));
        IReadOnlySet<string> old = PackTables.PlayedOverlayNames;
        try {
            Assert.That(PackTables.RegionFolders(reader.Files.Select(file => file.Name)), Is.EquivalentTo(new[] { "KR", "NA" }));
            PackTables.PlayedOverlayNames = counts.Added.ToHashSet();
            Assert.That(PackTables.RegionFolders(PackTables.PlayedNames(reader)), Is.EqualTo(new[] { "KR" }));
            Assert.That(PackTables.PlayedStringFolder(PackTables.PlayedNames(reader), "en", "itemname.xml"), Is.EqualTo("kr"));
        } finally {
            PackTables.PlayedOverlayNames = old;
        }
    }
}
