using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Database;

// The VIP goods table is a JSON blob in the metadata db (table.Table), written with WhenWritingDefault. A row ingested
// before the sale price and the purchase limit has neither, so it must read as "limit unknown" (null) and not as 0
// (no limit); an ingest that has them stores a 0 limit too.
public class PremiumClubTableJsonTests {
    private static readonly JsonSerializerOptions Options = (JsonSerializerOptions) typeof(PropertyBuilderExtensions)
        .GetField("Options", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    // The NA row of a metadata db ingested before the fields (goods 3001 is one per account).
    private const string OldRow = """
        {"!": "vip", "Buffs": {"100000043": {"Id": 100000043, "Level": 1}}, "Items": {}, "Packages": {"3001": {"Price": 490, "Period": 720, "BonusItems": [{"Id": 20300607, "Amount": 20, "Rarity": 1}]}}}
        """;

    [Test]
    public void OldRow_LimitIsUnknown() {
        var table = (PremiumClubTable) JsonSerializer.Deserialize<Table>(OldRow, Options)!;
        PremiumClubTable.Package package = table.Packages[3001];

        Assert.Multiple(() => {
            Assert.That(package.BuyLimit, Is.Null);
            Assert.That(package.SalePrice, Is.EqualTo(0));
            Assert.That(package.Price, Is.EqualTo(490));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public void NewRow_KeepsTheLimit(int buyLimit) {
        Table written = new PremiumClubTable(new Dictionary<int, PremiumClubTable.Buff>(), new Dictionary<int, PremiumClubTable.Item>(),
            new Dictionary<int, PremiumClubTable.Package> {
                [1001] = new(false, 0, 0, 720, 490, 490, false, buyLimit, ResetType.Default, []),
            });

        string json = JsonSerializer.Serialize(written, Options);
        var read = (PremiumClubTable) JsonSerializer.Deserialize<Table>(json, Options)!;

        Assert.That(read.Packages[1001].BuyLimit, Is.EqualTo(buyLimit));
    }
}
