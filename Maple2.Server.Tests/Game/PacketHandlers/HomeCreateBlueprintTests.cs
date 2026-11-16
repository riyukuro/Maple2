using Maple2.Model.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 63 (make a blueprint item of the home): every refusal ends in ResponseCube 0x3f result 0 (the failed
// answer that closes the client's upload dialog); a missing design entry answers only that, a wallet short of the
// price answers the balance error first, and the made blueprint takes its price, joins the bag and is answered.
public class HomeCreateBlueprintTests {
    private const long Price = 500;
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Start(bool design) {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetTable(new UgcDesignTable(design
            ? new Dictionary<int, UgcDesignTable.Entry> { [Constant.BlueprintId] = new(1, MeretMarketCurrencyType.Meret, Price, 0, 0) }
            : new Dictionary<int, UgcDesignTable.Entry>()));
        test.Register(TestGameSession.Metadata(Constant.BlueprintId, type: 22));
        test.Start();
        session.Housing.InitNewHome("home", null);
        test.EnterHome();
        session.Field!.Players.Clear();
        test.TakeSent();
    }

    private string[] Create() {
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(63)));
        return test.TakeSent().Select(Convert.ToHexString).ToArray();
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void WithoutADesignEntry_OnlyTheFailedAnswer() {
        Start(design: false);

        Assert.That(Create(), Is.EqualTo(new[] { Hex(CubePacket.CreateBlueprintFailed()) }));
    }

    [Test]
    public void ShortOfTheMerets_AnswersTheBalanceErrorThenFailed() {
        Start(design: true);
        session.Currency.Meret = Price - 1;
        test.TakeSent();

        Assert.That(Create(), Is.EqualTo(new[] {
            Hex(CubePacket.Error(UgcMapError.s_err_ugcmap_not_enough_meso_balance)),
            Hex(CubePacket.CreateBlueprintFailed()),
        }));
        Assert.That(session.Currency.Meret, Is.EqualTo(Price - 1));
    }

    [Test]
    public void WithTheMerets_TakesThePriceAndAddsTheBlueprint() {
        Start(design: true);
        session.Currency.Meret = Price + 10;
        test.TakeSent();

        string[] sent = Create();

        Assert.That(sent, Does.Not.Contain(Hex(CubePacket.CreateBlueprintFailed())));
        Assert.That(session.Currency.Meret, Is.EqualTo(10));
        Item? blueprint = session.Item.Inventory.Find(Constant.BlueprintId).SingleOrDefault();
        Assert.That(blueprint?.Blueprint, Is.Not.Null);
        Assert.That(session.StagedUgcItem, Is.SameAs(blueprint));
    }
}
