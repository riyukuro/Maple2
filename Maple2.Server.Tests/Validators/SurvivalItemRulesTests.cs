using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class SurvivalItemRulesTests {
    private static readonly MapMetadataSurvival Match = new(UserDrop: 1, UserAdditionalDrop: 311000039, PlusInven: 1,
        PlusInvenBasic: 311000042, PlusInvenZero: 1);

    [Test]
    public void MatchOnlyNeedsTheFlag() {
        Assert.Multiple(() => {
            Assert.That(SurvivalItemRules.IsMatchOnly(20000897, true), Is.True);
            Assert.That(SurvivalItemRules.IsMatchOnly(40400083, true), Is.True);
            Assert.That(SurvivalItemRules.IsMatchOnly(20000897, false), Is.False);
        });
    }

    [Test]
    public void OutsideSourcesStayNormalItems() {
        foreach (int id in new[] { 30001099, 30001102, 30001103, 30001104, 40400067, 50600212 }) {
            Assert.That(SurvivalItemRules.IsMatchOnly(id, true), Is.False, $"{id}");
        }
        Assert.That(SurvivalItemRules.OutsideSourceIds, Has.Count.EqualTo(6));
    }

    [Test]
    public void ClearReasonByField() {
        Assert.Multiple(() => {
            Assert.That(SurvivalItemRules.ClearReason(null), Is.EqualTo(SurvivalItemRules.LeftMatch));
            Assert.That(SurvivalItemRules.ClearReason(Match), Is.EqualTo(SurvivalItemRules.MatchStart));
            Assert.That(SurvivalItemRules.ClearReason(Match with { PlusInvenZero = 0 }), Is.Null);
        });
    }

    [Test]
    public void KitAndDeathDropBoxes() {
        Assert.Multiple(() => {
            Assert.That(SurvivalItemRules.KitBox(Match), Is.EqualTo(311000042));
            Assert.That(SurvivalItemRules.KitBox(Match with { PlusInven = 0 }), Is.Zero);
            Assert.That(SurvivalItemRules.KitBox(null), Is.Zero);
            Assert.That(SurvivalItemRules.DropsOwnItems(Match), Is.True);
            Assert.That(SurvivalItemRules.DropsOwnItems(Match with { UserDrop = 0 }), Is.False);
            Assert.That(SurvivalItemRules.DropsOwnItems(null), Is.False);
            Assert.That(SurvivalItemRules.DeathBox(Match), Is.EqualTo(311000039));
            Assert.That(SurvivalItemRules.DeathBox(null), Is.Zero);
        });
    }

    [Test]
    public void ReentryOfARunningGameKeepsItems() {
        Assert.Multiple(() => {
            Assert.That(SurvivalItemRules.ClearReason(Match, inRunningGame: true), Is.Null);
            Assert.That(SurvivalItemRules.ClearReason(null, inRunningGame: true), Is.EqualTo(SurvivalItemRules.LeftMatch));
        });
    }

    // The tab scan of InventoryManager.ClearSurvivalItems and TakeSurvivalItems.
    [Test]
    public void MatchOnlyItemsPicksTheDeleteSet() {
        var items = new List<Item> {
            new(Metadata(20000733, true)), new(Metadata(30001102, true)), new(Metadata(20000001, false)), new(Metadata(20000897, true)),
        };
        Assert.That(SurvivalItemRules.MatchOnlyItems(items).Select(item => item.Id), Is.EqualTo(new[] { 20000733, 20000897 }));
    }

    // The parser gives every map a survival object; one with every field 0 is no survival element.
    [Test]
    public void HasSurvivalIgnoresTheParserDefault() {
        Assert.Multiple(() => {
            Assert.That(MapMapper.HasSurvival(null), Is.False);
            Assert.That(MapMapper.HasSurvival(new Maple2.File.Parser.Xml.Map.Survival()), Is.False);
            Assert.That(MapMapper.HasSurvival(new Maple2.File.Parser.Xml.Map.Survival { plusInven_Zero = 1 }), Is.True);
            Assert.That(MapMapper.HasSurvival(new Maple2.File.Parser.Xml.Map.Survival { userAdditionalDrop = 311000039 }), Is.True);
        });
    }

    private static ItemMetadata Metadata(int id, bool survival) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0) {
            Survival = survival,
        };
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }
}
