using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

public class ConstantsTableDefaultsTests {
    // A price whose row is filtered out for the server's locale must not fall back to 0 (a free action).
    // The fallbacks are the client's own built-in values.
    [Test]
    public void MeretPrices_DefaultToTheClientsBuiltInValues() {
        var constants = new ConstantsTable();

        Assert.That(constants.MeratAirTaxiPrice, Is.EqualTo(390));
        Assert.That(constants.InventoryExpandPrice1Row, Is.EqualTo(390));
        Assert.That(constants.MeratConsumeChannelChat, Is.EqualTo(2));
        Assert.That(constants.MeratConsumeWorldChat, Is.EqualTo(5));
        Assert.That(constants.PetChangeNameMerat, Is.EqualTo(100));
        Assert.That(constants.SkillBookTreeAddTabFeeMerat, Is.EqualTo(1000));
        Assert.That(constants.UGCShopAdFeeMerat, Is.EqualTo(100));
    }
}
