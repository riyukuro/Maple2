using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Ingest.Utils;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// table/enchantlimittable.xml: the rows of the features and locale the server has; of rows with one slot the one the
// client's SelectFeatureElement picks (KR rows of the KMS2 table).
public class EnchantLimitIngestTests {
    private const string Xml = """
        <ms2>
          <limit slot="30" minLv="20" maxLv="99" maxGrade="15" locale="" />
          <limit slot="54" minLv="20" maxLv="99" maxGrade="15" feature="RuneBlader" locale="" />
          <limit slot="13" minLv="20" maxLv="99" maxGrade="15" feature="EnchantExpansion01" locale="" />
          <limit slot="13" minLv="60" maxLv="99" maxGrade="15" feature="EnchantExpansion01" locale="KR" />
          <limit slot="13" minLv="50" maxLv="99" maxGrade="15" feature="EnchantExpansion01" locale="NA" />
        </ms2>
        """;

    private static EnchantLimitTable Map(string locale, params string[] features) {
        var document = new XmlDocument();
        document.LoadXml(Xml);
        var levels = new Dictionary<string, int>();
        foreach (string feature in features) {
            levels[feature] = 1;
        }
        return ServerTableMapper.MapEnchantLimit(document,
            row => (row.GetAttribute("feature") is "" || levels.ContainsKey(row.GetAttribute("feature")))
                   && row.GetAttribute("locale") is var rowLocale && (rowLocale == "" || rowLocale == locale),
            (a, b) => PlayedNpcStats.SelectFeatureElement(a, b, feature => levels.GetValueOrDefault(feature), locale));
    }

    [Test]
    public void Kr_TakesTheLocaleRow() {
        EnchantLimitTable table = Map("KR", "RuneBlader", "EnchantExpansion01");

        Assert.That(table.Slots.Keys, Is.EquivalentTo(new[] { 30, 54, 13 }));
        Assert.That(table.Slots[30], Is.EqualTo(new EnchantLimitTable.Limit(20, 99, 15)));
        Assert.That(table.Slots[13], Is.EqualTo(new EnchantLimitTable.Limit(60, 99, 15)));
    }

    [Test]
    public void Na_TakesItsOwnRow() {
        Assert.That(Map("NA", "EnchantExpansion01").Slots[13], Is.EqualTo(new EnchantLimitTable.Limit(50, 99, 15)));
    }

    // A slot whose feature is off has no row, so the client enchants no item of it.
    [Test]
    public void FeatureOff_NoRow() {
        EnchantLimitTable table = Map("KR");

        Assert.That(table.Slots.Keys, Is.EquivalentTo(new[] { 30 }));
    }
}
