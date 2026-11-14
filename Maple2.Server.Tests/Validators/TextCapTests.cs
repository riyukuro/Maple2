using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Validators;

// Text caps taken from the client's own input limits (the client constants UgcTagLengthMax, MaidNameLengthMax and
// MaidDescLengthMax, KR 6, 12 and 16).
public class TextCapTests {
    // The design market's tag line: the register dialog's three tag inputs, each at most UgcTagLengthMax, joined
    // with ", ". A comma typed inside one input is the player's text, not a fourth tag.
    [TestCase("", ExpectedResult = true)]
    [TestCase("cat, hat, dog", ExpectedResult = true)]
    [TestCase("abcdef, ghijkl, mnopqr", ExpectedResult = true)]
    [TestCase("a,b,c, d", ExpectedResult = true)]
    [TestCase("abcdef, ghijkl, mnopqrs", ExpectedResult = false)]
    [TestCase("abcdefghijklmnopqrstuvw", ExpectedResult = false)]
    public bool MarketTags_ThreeOfTheClientsLength(string tagText) => MeretMarketHandler.TagsAllowed(tagText, 6);

    [Test]
    public void MarketTags_NoConstant_LengthUnchecked() {
        Assert.That(MeretMarketHandler.TagsAllowed(new string('a', 50), 0), Is.True);
    }

    // A maid profile's texts: each at most the larger of the two limits the dialog gives its movie.
    [Test]
    public void MaidProfile_EachTextAtMostTheLargerLimit() {
        var fits = new MaidProfile { Name = new string('a', 12), Like = new string('b', 16) };
        var tooLong = new MaidProfile { BelongTo = new string('c', 17) };
        var heldSpecialty = new MaidProfile { Specialty = new string('d', 40) };
        Assert.Multiple(() => {
            Assert.That(MaidRules.ProfileTextsFit(fits, 12, 16), Is.True);
            Assert.That(MaidRules.ProfileTextsFit(tooLong, 12, 16), Is.False);
            Assert.That(MaidRules.ProfileTextsFit(tooLong, 0, 0), Is.True, "no constants: unchecked");
            Assert.That(MaidRules.ProfileTextsFit(heldSpecialty, 12, 16), Is.True, "the specialty is not the dialog's to edit");
        });
    }
}
