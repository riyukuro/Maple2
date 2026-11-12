using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The client's guild product checks (client/ms2/uishopdialog.cpp:3562-3711) and the fund spend of currency 7.
public class GuildShopRulesTests {
    private const long Now = 1_800_000_000;

    private static ShopItemMetadata.Requirement Requirement(int trophy = 0, GuildNpcType npc = GuildNpcType.Unknown, short npcLevel = 0) {
        return new ShopItemMetadata.Requirement(trophy, new ShopItemMetadata.Achievement(0, 0),
            new ShopItemMetadata.Championship(0, 0), new ShopItemMetadata.GuildNpc(npc, npcLevel),
            new ShopItemMetadata.QuestAlliance(AllianceType.AnyFaction, 0));
    }

    [Test]
    public void TrophyProduct_NeedsSevenDaysInTheGuild() {
        bool ok = ShopRules.CheckGuildProduct(Requirement(trophy: 100), ShopCurrencyType.Meso, Now - 604799, Now, 500, 0, false, out ShopError? error);
        Assert.That(ok, Is.False);
        Assert.That(error, Is.EqualTo(ShopError.s_err_lack_guild_require_date));

        Assert.That(ShopRules.CheckGuildProduct(Requirement(trophy: 100), ShopCurrencyType.Meso, Now - 604800, Now, 500, 0, false, out error), Is.True);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void TrophyProduct_NeedsTheTrophies() {
        Assert.That(ShopRules.CheckGuildProduct(Requirement(trophy: 100), ShopCurrencyType.Meso, 0, Now, 99, 0, false, out ShopError? error), Is.False);
        Assert.That(error, Is.EqualTo(ShopError.s_err_lack_guild_trophy));
        Assert.That(ShopRules.CheckGuildProduct(Requirement(trophy: 100), ShopCurrencyType.Meso, null, Now, 500, 0, false, out error), Is.False);
        Assert.That(error, Is.Null);
    }

    [TestCase(2, 1, false)]
    [TestCase(2, 2, true)]
    [TestCase(2, 5, true)]
    public void NpcProduct_NeedsTheNpcLevel(short level, int guildLevel, bool expected) {
        Assert.That(ShopRules.CheckGuildProduct(Requirement(npc: GuildNpcType.Goods, npcLevel: level), ShopCurrencyType.Meso, 0, Now, 0, guildLevel, false, out ShopError? error),
            Is.EqualTo(expected));
        Assert.That(error, Is.Null);
    }

    [Test]
    public void FundProduct_MasterOnly() {
        Assert.That(ShopRules.CheckGuildProduct(Requirement(), ShopCurrencyType.GuildFund, 0, Now, 0, 0, false, out ShopError? error), Is.False);
        Assert.That(error, Is.EqualTo(ShopError.s_guild_err_buy_no_master));
        Assert.That(ShopRules.CheckGuildProduct(Requirement(), ShopCurrencyType.GuildFund, 0, Now, 0, 0, true, out error), Is.True);
        Assert.That(ShopRules.CheckGuildProduct(Requirement(), ShopCurrencyType.Meso, null, Now, 0, 0, false, out error), Is.True);
    }

    [TestCase(false, 1000, 10, GuildError.s_guild_err_no_master)]
    [TestCase(true, 1000, 1001, GuildError.s_guild_err_not_enough_guild_fund)]
    [TestCase(true, 1000, -1, GuildError.s_guild_err_not_enough_guild_fund)]
    [TestCase(true, 1000, 1000, GuildError.none)]
    public void SpendFund(bool isMaster, int funds, int amount, GuildError expected) {
        Assert.That(GuildRules.CheckSpendFund(isMaster, funds, amount), Is.EqualTo(expected));
    }
}
