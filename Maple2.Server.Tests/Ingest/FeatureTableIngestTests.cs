using System.Collections.Generic;
using System.Text.Json;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// The ingest writes the features the rows were filtered by as the server-table row feature.xml, so a server rule
// that depends on a feature reads the operator's feature.xml instead of a choice fixed in code. Before, the levels
// were dropped after the ingest. The names are made up.
public class FeatureTableIngestTests {
    [Test]
    public void TheFilteredFeatures_AreTheTable() {
        FeatureTable table = ServerTableMapper.MapFeatures(new Dictionary<string, int> { ["FeatureB"] = 20, ["FeatureA"] = 3 });
        Assert.Multiple(() => {
            Assert.That(table.Enabled, Is.EqualTo(new Dictionary<string, int> { ["FeatureA"] = 3, ["FeatureB"] = 20 }));
            Assert.That(table.IsOn("FeatureA"), Is.True);
            // A name feature.xml lacks, or one above the setting (not in the filter), is off; names keep their case.
            Assert.That(table.IsOn("FeatureC"), Is.False);
            Assert.That(table.IsOn("featurea"), Is.False);
        });
    }

    // The same features give the same row (the ingest's checksum), whatever order the filter held them in.
    [Test]
    public void TheRow_IsInNameOrder() {
        string a = JsonSerializer.Serialize<ServerTable>(ServerTableMapper.MapFeatures(new Dictionary<string, int> { ["B"] = 2, ["A"] = 1 }));
        string b = JsonSerializer.Serialize<ServerTable>(ServerTableMapper.MapFeatures(new Dictionary<string, int> { ["A"] = 1, ["B"] = 2 }));
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void TheRow_ReadsBack() {
        string json = JsonSerializer.Serialize<ServerTable>(ServerTableMapper.MapFeatures(new Dictionary<string, int> { ["FeatureA"] = 3 }));
        var back = (FeatureTable) JsonSerializer.Deserialize<ServerTable>(json)!;
        Assert.Multiple(() => {
            Assert.That(back.IsOn("FeatureA"), Is.True);
            Assert.That(back.IsOn("FeatureB"), Is.False);
        });
    }
}
