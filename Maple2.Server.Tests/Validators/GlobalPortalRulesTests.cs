using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class GlobalPortalRulesTests {
    [Test]
    public void Captions_AreAlwaysThree() {
        Assert.That(GlobalPortalRules.Captions(["a", "b", "c"]), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(GlobalPortalRules.Captions(["a"]), Is.EqualTo(new[] { "a", "", "" }));
        Assert.That(GlobalPortalRules.Captions([]), Is.EqualTo(new[] { "", "", "" }));
        Assert.That(GlobalPortalRules.Captions(["a", "b", "c", "d"]), Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [TestCase(0, 3, ExpectedResult = true)]
    [TestCase(2, 3, ExpectedResult = true)]
    [TestCase(3, 3, ExpectedResult = false)]
    [TestCase(-1, 3, ExpectedResult = false)]
    [TestCase(1, 1, ExpectedResult = false)]
    [TestCase(3, 5, ExpectedResult = false)]
    [TestCase(0, 0, ExpectedResult = false)]
    public bool EntryValid(int index, int count) => GlobalPortalRules.EntryValid(index, count);
}
