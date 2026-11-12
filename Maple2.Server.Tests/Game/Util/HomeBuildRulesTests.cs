using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Game.Util;

// The home build budget and build helpers: RequestCube 56, 57 and 58.
public class HomeBuildRulesTests {
    private const UgcMapError Ok = UgcMapError.s_ugcmap_ok;

    // A meso cube the server pack lists with price 0 takes the played client's price, and is then refused at
    // budget 0 instead of being placed free.
    [Test]
    public void ClientPriceTable_GivesThePriceTheBudgetIsChargedWith() {
        var server = new Dictionary<int, Maple2.Model.Metadata.FurnishingShopTable.Entry> {
            [50200299] = new(50200299, true, Maple2.Model.Enum.FurnishingCurrencyType.Meso, 0),
            [50100000] = new(50100000, true, Maple2.Model.Enum.FurnishingCurrencyType.Meret, 30),
        };
        Maple2.Model.Metadata.FurnishingShopTable.Entry?[] rows = [
            Maple2.Model.Metadata.FurnishingShopTable.ClientRow("50200299", "1", "50", ""),
            Maple2.Model.Metadata.FurnishingShopTable.ClientRow("50200300", "true", "70", "3"),
            Maple2.Model.Metadata.FurnishingShopTable.ClientRow("50200299", "1", "50", "1"),
            Maple2.Model.Metadata.FurnishingShopTable.ClientRow("x", "1", "50", "1"),
        ];
        Assert.That(rows[3], Is.Null);
        Dictionary<int, Maple2.Model.Metadata.FurnishingShopTable.Entry> merged = Maple2.Model.Metadata.FurnishingShopTable.WithClientRows(server, [rows[0]!, rows[1]!, rows[2]!]);

        Assert.That(merged[50200299], Is.EqualTo(new Maple2.Model.Metadata.FurnishingShopTable.Entry(50200299, true, Maple2.Model.Enum.FurnishingCurrencyType.Meso, 50)));
        Assert.That(merged[50200300], Is.EqualTo(new Maple2.Model.Metadata.FurnishingShopTable.Entry(50200300, true, Maple2.Model.Enum.FurnishingCurrencyType.Meret, 70)));
        Assert.That(merged[50100000].Price, Is.EqualTo(30));
        Assert.That(server[50200299].Price, Is.EqualTo(0));

        var home = new Home { BuildMeso = 0 };
        Assert.That(HomeBuildRules.Charge(home, merat: false, server[50200299].Price), Is.EqualTo(Ok));
        Assert.That(HomeBuildRules.Charge(home, merat: false, merged[50200299].Price), Is.EqualTo(UgcMapError.s_err_ugcmap_not_enough_meso_balance));
        home.BuildMeso = 50;
        Assert.That(HomeBuildRules.Charge(home, merat: false, merged[50200299].Price), Is.EqualTo(Ok));
        Assert.That(home.BuildMeso, Is.EqualTo(0));
    }

    // CPriceTable::LoadHousing (commonfiles/pricetable.cpp:111) skips no row: a row without the ugcHousing
    // attributes is stored with buy false, price 0 and money type 1 over what the id had, and the client then
    // refuses to buy that item. So such a row replaces a priced server row here too.
    [Test]
    public void ClientPriceTable_RowWithoutHousingAttributesReplacesAsNotBuyable() {
        var server = new Dictionary<int, Maple2.Model.Metadata.FurnishingShopTable.Entry> {
            [50100000] = new(50100000, true, Maple2.Model.Enum.FurnishingCurrencyType.Meret, 30),
        };
        Maple2.Model.Metadata.FurnishingShopTable.Entry? row = Maple2.Model.Metadata.FurnishingShopTable.ClientRow("50100000", "", "", "");
        Assert.That(row, Is.EqualTo(new Maple2.Model.Metadata.FurnishingShopTable.Entry(50100000, false, (Maple2.Model.Enum.FurnishingCurrencyType) 1, 0)));

        Dictionary<int, Maple2.Model.Metadata.FurnishingShopTable.Entry> merged = Maple2.Model.Metadata.FurnishingShopTable.WithClientRows(server, [row!]);
        Assert.That(merged[50100000].Buyable, Is.False);
        Assert.That(merged[50100000].Price, Is.EqualTo(0));
    }

    [Test]
    public void CheckAdd_RefusesNoOneSelfAndRepeats() {
        var builders = new List<long> { 5 };
        Assert.That(HomeBuildRules.CheckAdd(1, 0, builders), Is.EqualTo(HomeBuildRules.Refused));
        Assert.That(HomeBuildRules.CheckAdd(1, 1, builders), Is.EqualTo(HomeBuildRules.Refused));
        Assert.That(HomeBuildRules.CheckAdd(1, 5, builders), Is.EqualTo(HomeBuildRules.Refused));
        Assert.That(HomeBuildRules.CheckAdd(1, 6, builders), Is.EqualTo(Ok));
    }

    [Test]
    public void CheckAdd_StopsAtTheLimit() {
        var builders = new List<long>();
        for (long id = 10; builders.Count < HomeBuildRules.MaxBuilders - 1; id++) {
            builders.Add(id);
        }
        Assert.That(HomeBuildRules.CheckAdd(1, 2, builders), Is.EqualTo(Ok));
        builders.Add(2);
        Assert.That(HomeBuildRules.CheckAdd(1, 3, builders), Is.EqualTo(HomeBuildRules.Refused));
    }

    [Test]
    public void CheckRemove_NeedsAListedAccount() {
        var builders = new List<long> { 5 };
        Assert.That(HomeBuildRules.CheckRemove(5, builders), Is.EqualTo(Ok));
        Assert.That(HomeBuildRules.CheckRemove(6, builders), Is.EqualTo(HomeBuildRules.Refused));
        Assert.That(HomeBuildRules.CheckRemove(0, builders), Is.EqualTo(HomeBuildRules.Refused));
    }

    [Test]
    public void TheRefusalIsTheCodeTheClientShowsNothingFor() {
        Assert.That((byte) HomeBuildRules.Refused, Is.EqualTo(1));
    }

    [Test]
    public void CheckBudget_ChargesOnlyTheDifference() {
        // budget 100 -> 150 needs 50 from the wallet
        Assert.That(HomeBuildRules.CheckBudget(150, 0, 100, 0, 50, 0), Is.EqualTo(Ok));
        Assert.That(HomeBuildRules.CheckBudget(150, 0, 100, 0, 49, 0), Is.EqualTo(UgcMapError.s_err_ugcmap_not_enough_meso_balance));
        Assert.That(HomeBuildRules.CheckBudget(0, 150, 0, 100, 0, 49), Is.EqualTo(UgcMapError.s_err_ugcmap_not_enough_merat_balance));
        // lowering it never needs the wallet
        Assert.That(HomeBuildRules.CheckBudget(10, 10, 100, 100, 0, 0), Is.EqualTo(Ok));
        Assert.That(HomeBuildRules.CheckBudget(-1, 0, 100, 0, 500, 0), Is.EqualTo(HomeBuildRules.Refused));
        // A lowered budget must fit into the wallet below its cap; nothing may be cut off.
        Assert.That(HomeBuildRules.CheckBudget(40, 0, 100, 0, 940, 0, 1000, 1000), Is.EqualTo(Ok));
        Assert.That(HomeBuildRules.CheckBudget(40, 0, 100, 0, 941, 0, 1000, 1000), Is.EqualTo(HomeBuildRules.Refused));
        Assert.That(HomeBuildRules.CheckBudget(0, 40, 0, 100, 0, 940, 1000, 1000), Is.EqualTo(Ok));
        Assert.That(HomeBuildRules.CheckBudget(0, 40, 0, 100, 0, 941, 1000, 1000), Is.EqualTo(HomeBuildRules.Refused));
        Assert.That(HomeBuildRules.CheckBudget(0, 0, 100, 0, long.MaxValue - 99, 0), Is.EqualTo(HomeBuildRules.Refused));
        Assert.That(HomeBuildRules.CheckBudget(0, 0, 100, 0, long.MaxValue - 100, 0), Is.EqualTo(Ok));
        // Raising the budget is not bound by the cap.
        Assert.That(HomeBuildRules.CheckBudget(150, 0, 100, 0, 1000, 0, 1000, 1000), Is.EqualTo(Ok));
        Assert.That(HomeBuildRules.CheckBudget(0, -1, 0, 100, 0, 500), Is.EqualTo(HomeBuildRules.Refused));
    }

    [Test]
    public void TheShortBudgetCodesAreTheClients() {
        Assert.That((byte) UgcMapError.s_err_ugcmap_not_enough_meso_balance, Is.EqualTo(96));
        Assert.That((byte) UgcMapError.s_err_ugcmap_not_enough_merat_balance, Is.EqualTo(97));
    }

    [Test]
    public void Charge_TakesThePriceFromTheRightBudget() {
        var home = new Home { BuildMeso = 100, BuildMerat = 30 };
        Assert.That(HomeBuildRules.Charge(home, false, 60), Is.EqualTo(Ok));
        Assert.That(home.BuildMeso, Is.EqualTo(40));
        Assert.That(home.BuildMerat, Is.EqualTo(30));
        Assert.That(HomeBuildRules.Charge(home, true, 30), Is.EqualTo(Ok));
        Assert.That(home.BuildMerat, Is.EqualTo(0));
    }

    [Test]
    public void Charge_RefusesAShortBudgetAndTakesNothing() {
        var home = new Home { BuildMeso = 40, BuildMerat = 5 };
        Assert.That(HomeBuildRules.Charge(home, false, 41), Is.EqualTo(UgcMapError.s_err_ugcmap_not_enough_meso_balance));
        Assert.That(HomeBuildRules.Charge(home, true, 6), Is.EqualTo(UgcMapError.s_err_ugcmap_not_enough_merat_balance));
        Assert.That(home.BuildMeso, Is.EqualTo(40));
        Assert.That(home.BuildMerat, Is.EqualTo(5));
        // a free cube needs no budget
        Assert.That(HomeBuildRules.Charge(new Home(), false, 0), Is.EqualTo(Ok));
    }

    [Test]
    public void MayGrant_OnlyAListedHelperOutsideThePlanner() {
        Assert.That(HomeBuildRules.MayGrant(false, true), Is.True);
        Assert.That(HomeBuildRules.MayGrant(true, true), Is.False);
        Assert.That(HomeBuildRules.MayGrant(false, false), Is.False);
        Assert.That(HomeBuildRules.MayGrant(true, false), Is.False);
    }

    [Test]
    public void CanCharge_AnswersAsChargeAndTakesNothing() {
        var home = new Home { BuildMeso = 40, BuildMerat = 5 };
        Assert.That(HomeBuildRules.CanCharge(home, false, 40), Is.EqualTo(Ok));
        Assert.That(HomeBuildRules.CanCharge(home, true, 5), Is.EqualTo(Ok));
        Assert.That(HomeBuildRules.CanCharge(home, false, 41), Is.EqualTo(UgcMapError.s_err_ugcmap_not_enough_meso_balance));
        Assert.That(HomeBuildRules.CanCharge(home, true, 6), Is.EqualTo(UgcMapError.s_err_ugcmap_not_enough_merat_balance));
        Assert.That(HomeBuildRules.CanCharge(new Home(), false, 0), Is.EqualTo(Ok));
        Assert.That(home.BuildMeso, Is.EqualTo(40));
        Assert.That(home.BuildMerat, Is.EqualTo(5));
    }

    [Test]
    public void Refund_GivesTheChargeBack() {
        var home = new Home { BuildMeso = 100, BuildMerat = 30 };
        HomeBuildRules.Charge(home, false, 60);
        HomeBuildRules.Refund(home, false, 60);
        HomeBuildRules.Charge(home, true, 10);
        HomeBuildRules.Refund(home, true, 10);
        Assert.That(home.BuildMeso, Is.EqualTo(100));
        Assert.That(home.BuildMerat, Is.EqualTo(30));
    }

    [Test]
    public void MayBuild_IsTheOwnerOrAListedHelper() {
        var builders = new List<long> { 5 };
        Assert.That(HomeEditRules.MayBuild(1, 1, builders), Is.True);
        Assert.That(HomeEditRules.MayBuild(1, 1, null), Is.True);
        Assert.That(HomeEditRules.MayBuild(1, 5, builders), Is.True);
        Assert.That(HomeEditRules.MayBuild(1, 6, builders), Is.False);
        Assert.That(HomeEditRules.MayBuild(1, 5, null), Is.False);
        Assert.That(HomeEditRules.MayBuild(1, 5, new List<long>()), Is.False);
        // an account id of 0 or a plot without an owner never passes
        Assert.That(HomeEditRules.MayBuild(1, 0, new List<long> { 0 }), Is.False);
        Assert.That(HomeEditRules.MayBuild(0, 5, builders), Is.False);
        Assert.That(HomeEditRules.MayBuild(0, 0, builders), Is.False);
    }

    [Test]
    public void OwnsPlot_StaysOwnerOnly() {
        Assert.That(HomeEditRules.OwnsPlot(1, 5), Is.False);
        Assert.That(HomeEditRules.OwnsPlot(1, 1), Is.True);
    }

    [TestCase(FurnishingCurrencyType.Meso, 1500, 1500)]
    [TestCase(FurnishingCurrencyType.Meret, 30, 0)]
    [TestCase(FurnishingCurrencyType.Meso, 0, 0)]
    public void BuyCubeCountsTheMesosSpent(FurnishingCurrencyType currency, long price, long expected) {
        Assert.That(HomeBuildRules.BuyCubeMesos(currency, price), Is.EqualTo(expected));
    }
}
