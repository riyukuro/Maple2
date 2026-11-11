using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

public class ClientPriceConstantsTests {
    // The KR rows of the four constants the server pack and the client table disagree on.
    private static readonly (string, string)[] ServerRows = [
        ("InventoryExpandPrice1Row", "100"),
        ("MeratConsumeWorldChat", "10"),
        ("MeratConsumeChannelChat", "1"),
        ("SkillBookTreeAddTabFeeMerat", "0"),
        ("PetChangeNameMerat", "100"),
        ("MaxChatLength", "50"),
    ];
    private static readonly (string, string)[] ClientRows = [
        ("InventoryExpandPrice1Row", "390"),
        ("MeratConsumeWorldChat", "30"),
        ("MeratConsumeChannelChat", "3"),
        ("SkillBookTreeAddTabFeeMerat", "990"),
        ("PetChangeNameMerat", "100.0"),
        ("MaxChatLength", "100"),
        ("UGCShopAdFeeMerat", "30"),
    ];

    [Test]
    public void Differences_ClientValueWinsForPrices() {
        List<ClientPriceConstants.Override> result = ClientPriceConstants.Differences(
            ClientPriceConstants.Select(ServerRows), ClientPriceConstants.Select(ClientRows));

        Dictionary<string, string> byKey = result.ToDictionary(entry => entry.Key, entry => entry.ClientValue);
        Assert.That(byKey, Is.EquivalentTo(new Dictionary<string, string> {
            ["InventoryExpandPrice1Row"] = "390",
            ["MeratConsumeWorldChat"] = "30",
            ["MeratConsumeChannelChat"] = "3",
            ["SkillBookTreeAddTabFeeMerat"] = "990",
            ["UGCShopAdFeeMerat"] = "30", // no server row: the client's row is taken
        }));
        Assert.That(result.Single(entry => entry.Key == "InventoryExpandPrice1Row").ServerValue, Is.EqualTo("100"));
        Assert.That(result.Single(entry => entry.Key == "UGCShopAdFeeMerat").ServerValue, Is.Null);
    }

    [Test]
    public void Differences_LeavesEqualAndNonPriceConstants() {
        List<ClientPriceConstants.Override> result = ClientPriceConstants.Differences(
            ClientPriceConstants.Select(ServerRows), ClientPriceConstants.Select(ClientRows));

        Assert.That(result.Any(entry => entry.Key == "PetChangeNameMerat"), Is.False); // 100 and 100.0
        Assert.That(result.Any(entry => entry.Key == "MaxChatLength"), Is.False); // not a price
    }

    // The home bank and doctor cooltimes: the client counts its own row down, so its row wins like a price.
    [Test]
    public void Differences_ClientValueWinsForHomeCooltimes() {
        List<ClientPriceConstants.Override> result = ClientPriceConstants.Differences(
            ClientPriceConstants.Select([("HomeBankCallCooltime", "60"), ("HomeBankCallDuration", "5")]),
            ClientPriceConstants.Select([("HomeBankCallCooltime", "60000"), ("HomeDoctorCallCooltime", "60000"), ("HomeBankCallDuration", "5000")]));

        Assert.That(result.Select(entry => entry.Key), Is.EquivalentTo(new[] { "HomeBankCallCooltime", "HomeDoctorCallCooltime" }));
        Assert.That(result.All(entry => entry.ClientValue == "60000"), Is.True);
    }

    // The multi open cap of item boxes: the client cuts the count with its own row before it sends.
    [Test]
    public void Differences_ClientValueWinsForItemBoxCap() {
        List<ClientPriceConstants.Override> noRow = ClientPriceConstants.Differences(
            ClientPriceConstants.Select([("MaxChatLength", "50")]),
            ClientPriceConstants.Select([("ItemBoxMultiOpenMaxCount", "50"), ("ItemBoxMultiOpenLimitCount", "30")]));
        Assert.That(noRow.Select(entry => entry.Key), Is.EquivalentTo(new[] { "ItemBoxMultiOpenMaxCount" }));
        Assert.That(noRow[0].ClientValue, Is.EqualTo("50"));

        List<ClientPriceConstants.Override> smaller = ClientPriceConstants.Differences(
            ClientPriceConstants.Select([("ItemBoxMultiOpenMaxCount", "20")]),
            ClientPriceConstants.Select([("ItemBoxMultiOpenMaxCount", "50")]));
        Assert.That(smaller.Single().ServerValue, Is.EqualTo("20"));
    }

    [Test]
    public void Select_LaterRowReplacesEarlier() {
        Dictionary<string, string> rows = ClientPriceConstants.Select([("MeratAirTaxiPrice", "90"), (" meratairtaxiprice ", "15")]);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows["MeratAirTaxiPrice"], Is.EqualTo("15"));
    }
}
