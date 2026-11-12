using System.Collections.Generic;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Ingest;

// guildquestrewardfactor.xml rows of the client's shape (made-up factors) and the bonus the reward line adds.
[NonParallelizable]
public class GuildQuestRewardFactorTests {
    private Dictionary<string, int> oldFeatures = null!;
    private string oldLocale = null!;

    [SetUp]
    public void SetFilter() {
        oldFeatures = FeatureLocaleFilter.Features;
        oldLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = "KR";
        FeatureLocaleFilter.Features = new Dictionary<string, int> { ["TestGuild01"] = 1 };
    }

    [TearDown]
    public void RestoreFilter() {
        FeatureLocaleFilter.Features = oldFeatures;
        FeatureLocaleFilter.Locale = oldLocale;
    }

    [Test]
    public void ReadsTheRowsOfEnabledFeatures() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <guildNpc level="1" guildExpFactor="1" userExpFactor="1" guildFundFactor="1" userMesoFactor="1" GuildCoinFactor="1" feature="TestGuild01" />
              <guildNpc level="2" guildExpFactor="1.1" userExpFactor="1.3" guildFundFactor="1.7" userMesoFactor="1.9" GuildCoinFactor="2.1" feature="TestGuild01" />
              <guildNpc level="3" guildExpFactor="9" userExpFactor="9" guildFundFactor="9" userMesoFactor="9" GuildCoinFactor="9" feature="TestGuildOff" />
              <guildNpc level="300" guildExpFactor="9" userExpFactor="9" guildFundFactor="9" userMesoFactor="9" GuildCoinFactor="9" />
            </ms2>
            """);

        Dictionary<short, GuildTable.QuestRewardFactor> rows = TableMapper.GuildQuestRewardFactors(document);

        Assert.That(rows.Keys, Is.EquivalentTo(new short[] { 1, 2 }));
        Assert.That(rows[2], Is.EqualTo(new GuildTable.QuestRewardFactor(GuildExp: 1.1f, UserExp: 1.3f, GuildFund: 1.7f, UserMeso: 1.9f, GuildCoin: 2.1f)));
    }

    [Test]
    public void BonusOnlyAtOneOrMore() {
        Assert.That(GuildRules.QuestRewardBonus(1000, 1.5f), Is.EqualTo(1500));
        Assert.That(GuildRules.QuestRewardBonus(1000, 1f), Is.EqualTo(1000));
        Assert.That(GuildRules.QuestRewardBonus(1000, 0f), Is.EqualTo(1000)); // a level the table lacks
        Assert.That(GuildRules.QuestRewardBonus(1000, 0.5f), Is.EqualTo(1000));
        Assert.That(GuildRules.QuestRewardBonus(77, 1.2f), Is.EqualTo((long) (77f * (1f + (1.2f - 1f)))));
        Assert.That(GuildRules.QuestRewardBonus(0, 2.5f), Is.EqualTo(0));
    }
}
