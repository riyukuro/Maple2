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

    // MS2_PLAYED_CLIENT_XML: not set or blank means no second pack.
    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase("  ", null)]
    [TestCase(" /data/Xml.m2d ", "/data/Xml.m2d")]
    public void PackOption(string? value, string? expected) {
        Assert.That(ClientPriceConstants.PackOption(value), Is.EqualTo(expected));
    }

    // The data folder's rows, then the played client's: the played client's row wins where it has one, and a
    // constant it has no row for keeps the data folder's value. Made-up numbers.
    [Test]
    public void Select_PlayedClientRowsWinOverTheDataFolder() {
        (string, string)[] dataFolder = [("SlotExpandPrice", "900"), ("NameChangeFee", "100")];
        (string, string)[] played = [("SlotExpandPrice", "300")];
        Dictionary<string, string> client = ClientPriceConstants.Select(dataFolder.Concat(played));
        List<ClientPriceConstants.Override> result = ClientPriceConstants.Differences(
            ClientPriceConstants.Select([("SlotExpandPrice", "900"), ("NameChangeFee", "50")]), client);
        Assert.That(result.Select(entry => (entry.Key, entry.ClientValue)),
            Is.EquivalentTo(new[] { ("SlotExpandPrice", "300"), ("NameChangeFee", "100") }));
    }

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

    // The PvP result dialogs' rewards: the client shows its own table's item and counts, so its rows win. The KR server
    // pack's one punch item 90090010 is no client item; the KMS2 client table has 30001533 and 9 for rank 1.
    [Test]
    public void Differences_ClientValueWinsForPvpRewards() {
        List<ClientPriceConstants.Override> result = ClientPriceConstants.Differences(
            ClientPriceConstants.Select([("PvpOnePunchRewardItem", "90090010"), ("PvpOnePunchReward1Count", "3"),
                ("PvpFFARewardItem", "90000006"), ("pvpBtiRewardWinnerCount", "30"), ("PvpOnePunchScoreNpcKill", "-50")]),
            ClientPriceConstants.Select([("PvpOnePunchRewardItem", "30001533"), ("PvpOnePunchReward1Count", "9"),
                ("PvpFFARewardItem", "90000006"), ("pvpBtiRewardWinnerCount", "40"), ("PvpOnePunchScoreNpcKill", "-10")]));

        Assert.That(result.Select(entry => (entry.Key, entry.ClientValue)), Is.EquivalentTo(new[] {
            ("PvpOnePunchRewardItem", "30001533"), ("PvpOnePunchReward1Count", "9"), ("pvpBtiRewardWinnerCount", "40"),
        }));
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

    // The meret air taxi: the played KR client's table has no row, so the client asks its built-in 390, while the
    // server pack and the data folder's client table say 90; a played client row, where its table has one, wins.
    [Test]
    public void SelectClient_BuiltInPriceWinsOverTheServerPack() {
        List<ClientPriceConstants.Override> noRow = ClientPriceConstants.Differences(
            ClientPriceConstants.Select([("MeratAirTaxiPrice", "90")]),
            ClientPriceConstants.SelectClient([("MeratAirTaxiPrice", "90")], [("MaxChatLength", "100")]));
        Assert.That(noRow.Single().Key, Is.EqualTo("MeratAirTaxiPrice"));
        Assert.That(noRow.Single().ClientValue, Is.EqualTo("390"));
        Assert.That(noRow.Single().ServerValue, Is.EqualTo("90"));

        List<ClientPriceConstants.Override> playedRow = ClientPriceConstants.Differences(
            ClientPriceConstants.Select([("MeratAirTaxiPrice", "90")]),
            ClientPriceConstants.SelectClient([("MeratAirTaxiPrice", "90")], [("MeratAirTaxiPrice", "15")]));
        Assert.That(playedRow.Single().ClientValue, Is.EqualTo("15"));

        Assert.That(ClientPriceConstants.Differences(
            ClientPriceConstants.Select([("MeratAirTaxiPrice", "390")]), ClientPriceConstants.SelectClient([], [])), Is.Empty);
    }

    // With no played client pack the data folder's pack is the client: its row wins over the built-in value, and
    // with no row there the built-in value stands.
    [Test]
    public void SelectClient_NoPlayedPack_DataFolderIsTheClient() {
        Assert.That(ClientPriceConstants.SelectClient([("MeratAirTaxiPrice", "90")], null)["MeratAirTaxiPrice"], Is.EqualTo("90"));
        Assert.That(ClientPriceConstants.SelectClient([("MaxChatLength", "100")], null)["MeratAirTaxiPrice"], Is.EqualTo("390"));
    }

    [Test]
    public void Select_LaterRowReplacesEarlier() {
        Dictionary<string, string> rows = ClientPriceConstants.Select([("MeratAirTaxiPrice", "90"), (" meratairtaxiprice ", "15")]);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows["MeratAirTaxiPrice"], Is.EqualTo("15"));
    }
}
