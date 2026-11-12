using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class FittingDollRulesTests {
    private static readonly FunctionCubeMetadata.FittingDollData EquipDoll = new(Gender.Male, 0);
    private static readonly FunctionCubeMetadata.FittingDollData OutfitDoll = new(Gender.Female, 1);

    [Test]
    public void CanWear_SkinOnlyOnOutfitDoll() {
        Assert.Multiple(() => {
            Assert.That(FittingDollRules.CanWear(EquipDoll, false, Gender.All), Is.EqualTo(FittingDollRules.Error.None));
            Assert.That(FittingDollRules.CanWear(EquipDoll, true, Gender.All), Is.EqualTo(FittingDollRules.Error.ItemIsSkin));
            Assert.That(FittingDollRules.CanWear(OutfitDoll, true, Gender.All), Is.EqualTo(FittingDollRules.Error.None));
            Assert.That(FittingDollRules.CanWear(OutfitDoll, false, Gender.All), Is.EqualTo(FittingDollRules.Error.ItemIsEquip));
        });
    }

    [Test]
    public void CanWear_GenderNamesTheDoll() {
        Assert.Multiple(() => {
            Assert.That(FittingDollRules.CanWear(EquipDoll, false, Gender.Male), Is.EqualTo(FittingDollRules.Error.None));
            Assert.That(FittingDollRules.CanWear(EquipDoll, false, Gender.Female), Is.EqualTo(FittingDollRules.Error.Male));
            Assert.That(FittingDollRules.CanWear(OutfitDoll, true, Gender.Male), Is.EqualTo(FittingDollRules.Error.Female));
        });
    }

    [Test]
    public void CanWear_RefusesItemBoundToAnotherCharacter() {
        Assert.Multiple(() => {
            Assert.That(FittingDollRules.CanWear(EquipDoll, false, Gender.All, boundToOther: true), Is.EqualTo(FittingDollRules.Error.InvalidBinding));
            Assert.That(FittingDollRules.CanWear(OutfitDoll, true, Gender.All, boundToOther: true), Is.EqualTo(FittingDollRules.Error.InvalidBinding));
            Assert.That(FittingDollRules.CanWear(EquipDoll, false, Gender.All, boundToOther: false), Is.EqualTo(FittingDollRules.Error.None));
            Assert.That((int) FittingDollRules.Error.InvalidBinding, Is.EqualTo(0x12));
        });
    }

    // A bow bound to the character dressing the doll is its own; only a binding to another character is refused.
    [Test]
    public void BoundToOther_ComparesTheBoundCharacter() {
        Assert.Multiple(() => {
            Assert.That(FittingDollRules.BoundToOther(null, 1001), Is.False);
            Assert.That(FittingDollRules.BoundToOther(new ItemBinding(1001, "Tester"), 1001), Is.False);
            Assert.That(FittingDollRules.BoundToOther(new ItemBinding(1002, "Other"), 1001), Is.True);
            Assert.That(FittingDollRules.BoundToOther(new ItemBinding(0, ""), 1001), Is.False);
        });
    }

    [Test]
    public void Allowed_OnlyInTheIndoorHome() {
        Assert.Multiple(() => {
            Assert.That(FittingDollRules.Allowed(Constant.DefaultHomeMapId), Is.True);
            Assert.That(FittingDollRules.Allowed(99000001), Is.False);
        });
    }

    [Test]
    public void SlotFits() {
        Assert.Multiple(() => {
            Assert.That(FittingDollRules.SlotFits(EquipSlot.CP, [EquipSlot.CP]), Is.True);
            Assert.That(FittingDollRules.SlotFits(EquipSlot.CL, [EquipSlot.CP]), Is.False);
            Assert.That(FittingDollRules.SlotFits(EquipSlot.LH, [EquipSlot.OH]), Is.True);
            Assert.That(FittingDollRules.SlotFits(EquipSlot.RH, [EquipSlot.OH]), Is.True);
            Assert.That(FittingDollRules.SlotFits(EquipSlot.OH, [EquipSlot.OH]), Is.False);
            Assert.That(FittingDollRules.SlotFits(EquipSlot.SK, [EquipSlot.SK]), Is.False);
            Assert.That(FittingDollRules.SlotFits(EquipSlot.RH, [EquipSlot.RH, EquipSlot.LH]), Is.True);
        });
    }

    [Test]
    public void Displaced_OverallTakesOffTopAndPants() {
        var worn = new List<(EquipSlot, IReadOnlyCollection<EquipSlot>, int)> {
            (EquipSlot.CL, [EquipSlot.CL], 1),
            (EquipSlot.PA, [EquipSlot.PA], 2),
            (EquipSlot.CP, [EquipSlot.CP], 3),
        };
        Assert.That(FittingDollRules.Displaced(EquipSlot.CL, [EquipSlot.CL, EquipSlot.PA], worn), Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public void Displaced_PantsTakeOffOverallOnly() {
        var worn = new List<(EquipSlot, IReadOnlyCollection<EquipSlot>, int)> {
            (EquipSlot.CL, [EquipSlot.CL, EquipSlot.PA], 1),
            (EquipSlot.CP, [EquipSlot.CP], 3),
        };
        Assert.That(FittingDollRules.Displaced(EquipSlot.PA, [EquipSlot.PA], worn), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void Displaced_OneHandedKeepsTheOtherHand() {
        var worn = new List<(EquipSlot, IReadOnlyCollection<EquipSlot>, int)> {
            (EquipSlot.LH, [EquipSlot.OH], 1),
            (EquipSlot.RH, [EquipSlot.OH], 2),
        };
        Assert.Multiple(() => {
            Assert.That(FittingDollRules.Displaced(EquipSlot.RH, [EquipSlot.OH], worn), Is.EqualTo(new[] { 2 }));
            Assert.That(FittingDollRules.Displaced(EquipSlot.RH, [EquipSlot.RH, EquipSlot.LH], worn), Is.EquivalentTo(new[] { 1, 2 }));
        });
    }
}
