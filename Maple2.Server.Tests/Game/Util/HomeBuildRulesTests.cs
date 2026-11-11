using System;
using System.Collections.Generic;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Game.Util;

// The home build budget and build helpers: RequestCube 56, 57 and 58.
public class HomeBuildRulesTests {
    private const UgcMapError Ok = UgcMapError.s_ugcmap_ok;

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
}
