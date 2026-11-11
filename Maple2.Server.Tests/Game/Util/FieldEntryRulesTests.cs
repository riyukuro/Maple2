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
}
