using System.Collections.Generic;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// An import picks the file at its full path (any case) before a file of the same name in another folder.
// Made-up folders.
public class TriggerImportMatchTests {
    private static readonly string[] Files = ["trigger/A_Map/common.xml", "trigger/b_map/Common.xml"];

    [Test]
    public void FullPath_WinsOverTheSameNameInAnotherFolder() {
        var log = new List<string>();
        Assert.That(TriggerMapper.MatchImport(Files, "trigger/B_MAP/common.xml", log.Add), Is.EqualTo("trigger/b_map/Common.xml"));
        Assert.That(TriggerMapper.MatchImport(Files, "trigger/a_map/COMMON.xml", log.Add), Is.EqualTo("trigger/A_Map/common.xml"));
        Assert.That(log, Is.Empty);
    }

    [Test]
    public void NameFallback_LogsWhenSeveralFilesShareTheName() {
        var log = new List<string>();
        Assert.That(TriggerMapper.MatchImport(Files, "trigger/c_map/common.xml", log.Add), Is.EqualTo("trigger/A_Map/common.xml"));
        Assert.That(log, Has.Count.EqualTo(1));

        log.Clear();
        Assert.That(TriggerMapper.MatchImport(Files, "trigger/c_map/other.xml", log.Add), Is.Null);
        Assert.That(TriggerMapper.MatchImport(["trigger/a_map/one.xml"], "trigger/c_map/one.xml", log.Add), Is.EqualTo("trigger/a_map/one.xml"));
        Assert.That(log, Is.Empty);
    }
}
