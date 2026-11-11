using System.Linq;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// Login into a saved field that cannot be made (no navmesh file): which fields are tried instead.
public class FieldEntryRulesTests {
    [Test]
    public void ReturnField_ThenDefault() {
        Assert.That(FieldEntryRules.Fallbacks(2000064, 2000001, 2000062).ToArray(), Is.EqualTo(new[] { 2000001, 2000062 }));
    }

    [Test]
    public void FailedFieldAndRepeats_AreLeftOut() {
        Assert.That(FieldEntryRules.Fallbacks(2000064, 2000064, 2000062).ToArray(), Is.EqualTo(new[] { 2000062 }));
        Assert.That(FieldEntryRules.Fallbacks(2000064, 2000062, 2000062).ToArray(), Is.EqualTo(new[] { 2000062 }));
        Assert.That(FieldEntryRules.Fallbacks(2000062, 2000062, 2000062).ToArray(), Is.Empty);
        Assert.That(FieldEntryRules.Fallbacks(2000064, 0, 2000062).ToArray(), Is.EqualTo(new[] { 2000062 }));
    }

    [Test]
    public void ReturnMap_GmOnlyFieldForPlainAccount_IsDefault() {
        Assert.That(FieldEntryRules.ReturnMap(2000064, true, 50, false, 2000062), Is.EqualTo(2000062));
        Assert.That(FieldEntryRules.ReturnMap(2000064, true, 50, true, 2000062), Is.EqualTo(2000064));
        Assert.That(FieldEntryRules.ReturnMap(2000001, true, 0, false, 2000062), Is.EqualTo(2000001));
    }

    [Test]
    public void ReturnMap_UnknownField_IsDefault() {
        Assert.That(FieldEntryRules.ReturnMap(123, false, 0, true, 2000062), Is.EqualTo(2000062));
    }
}
