using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The card game page (CardReverseGameDialog, 0x86 in, 0xDA out): the deck of the used Ui object's BonusGame row, a
// shuffle for its cost item, three picks by weight.
public class CardReverseGameTests {
    private const int GameId = 6;
    private const int CoinId = 30000536;
    private static readonly int[] CardIds = [11200047, 20000302, 40100023, 20000004];

    private static BonusGameTable Table(int[]? cards = null) {
        cards ??= CardIds;
        return new BonusGameTable(
            new Dictionary<int, BonusGameTable.Game> { [GameId] = new(GameId, new ItemComponent(CoinId, 1, 1, ItemTag.None), []) },
            new Dictionary<int, BonusGameTable.Drop> {
                [GameId] = new(GameId, cards.Select((id, i) => new BonusGameTable.Drop.Item(new ItemComponent(id, i == 0 ? 4 : 1, i + 1, ItemTag.None), 100 * (i + 1), false)).ToArray()),
            });
    }

    private static InteractObjectMetadata Row(int id, InteractObjectMetadataUi? ui) {
        return new InteractObjectMetadata(id, InteractType.Ui, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0, WeaponItemId: 0,
            new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), [], ui);
    }

    [Test]
    public void Of_TakesTheUiValueAsTheGameId() {
        Assert.That(CardReverseGame.Of(new InteractObjectMetadataUi("CardReverseGameDialog", "6"), Table())?.Game.Id, Is.EqualTo(GameId));
        Assert.That(CardReverseGame.Of(new InteractObjectMetadataUi("RouletteDialog", "6"), Table()), Is.Null);
        Assert.That(CardReverseGame.Of(new InteractObjectMetadataUi("CardReverseGameDialog", "7"), Table()), Is.Null);
        Assert.That(CardReverseGame.Of(new InteractObjectMetadataUi("CardReverseGameDialog", "6"), Table([])), Is.Null);
        Assert.That(CardReverseGame.Of(null, Table()), Is.Null);
    }

    // No pick before a shuffle; a shuffle gives three, each a card not yet opened.
    [Test]
    public void Picks_ThreeDistinctCardsPerShuffle() {
        CardReverseGame game = CardReverseGame.Of(new InteractObjectMetadataUi("CardReverseGameDialog", "6"), Table())!;
        var random = new Random(5);
        Assert.That(game.Pick(random), Is.EqualTo(-1));
        for (int round = 0; round < 20; round++) {
            game.Shuffle();
            int[] picks = [game.Pick(random), game.Pick(random), game.Pick(random)];
            Assert.That(picks, Is.Unique);
            Assert.That(picks, Is.All.InRange(0, CardIds.Length - 1));
            Assert.That(game.Pick(random), Is.EqualTo(-1));
        }
        game.Shuffle();
        game.Reset();
        Assert.That(game.Pick(random), Is.EqualTo(-1));
    }

    // A deck of three cards opens each once in a shuffle.
    [Test]
    public void Picks_SmallDeckOpensEachCardOnce() {
        CardReverseGame game = CardReverseGame.Of(new InteractObjectMetadataUi("CardReverseGameDialog", "6"), Table(CardIds[..3]))!;
        game.Shuffle();
        Assert.That(new[] { game.Pick(), game.Pick(), game.Pick() }, Is.EquivalentTo(new[] { 0, 1, 2 }));
    }

    private static int Coins(GameSession session) => session.Item.Inventory.Find(CoinId).Sum(item => item.Amount);

    private static void Send(GameSession session, byte command) {
        new CardReverseGameHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteByte(command)));
    }

    private static List<byte[]> Sent(TestGameSession test) => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.CardReverseGame).ToList();

    // The object 31000002 (ui value 6) used: the page's load gets the deck; a shuffle takes a coin; three picks give
    // their cards; a shuffle with no coin left gets result 0.
    [Test]
    public void Game_DeckShuffleAndPicks() {
        var test = new TestGameSession();
        test.Store(TestGameSession.Metadata(CoinId), 2);
        foreach (int id in CardIds) {
            test.Register(TestGameSession.Metadata(id));
        }
        test.SetServerTable(Table());
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(63000031));
        test.AddInteract(Row(31000002, new InteractObjectMetadataUi("CardReverseGameDialog", "6")),
            new InteractUiObject("ab", new Ms2SimpleUiObject(31000002, Vector3.Zero, Vector3.Zero)));
        new InteractObjectHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteInt(1);
            writer.WriteString("ab");
        }));
        test.TakeSent();

        Send(session, 0);
        CardReverseGame game = session.CardReverseGame!;
        Assert.That(Sent(test).Select(Convert.ToHexString), Is.EqualTo(new[] {
            "DA00" + "00" + "98C5C901" + "01000000" + "04000000" +
            "2FE6AA00" + "04" + "01000000" +
            "2E2E3101" + "01" + "02000000" +
            "B7E06302" + "01" + "03000000" +
            "042D3101" + "01" + "04000000",
        }));

        Send(session, 2);
        Assert.That(Sent(test), Is.Empty, "no pick before a shuffle");

        Send(session, 1);
        Assert.That(Sent(test).Select(Convert.ToHexString), Is.EqualTo(new[] { "DA0001" }));
        Assert.That(Coins(session), Is.EqualTo(1));

        var opened = new List<int>();
        for (int i = 0; i < 3; i++) {
            Send(session, 2);
            List<byte[]> sent = Sent(test);
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0][2], Is.EqualTo(2));
            opened.Add(BitConverter.ToInt32(sent[0], 3));
        }
        Assert.That(opened, Is.Unique);
        Send(session, 2);
        Assert.That(Sent(test), Is.Empty, "three picks a shuffle");
        foreach (int index in opened) {
            BonusGameTable.Drop.Item card = game.Drop.Items[index];
            Assert.That(session.Item.Inventory.Find(card.ItemComponent.ItemId).Sum(item => item.Amount), Is.EqualTo(card.ItemComponent.Amount));
        }

        Send(session, 1);
        Assert.That(Coins(session), Is.EqualTo(0));
        Sent(test);
        Send(session, 1);
        Assert.That(Sent(test).Select(Convert.ToHexString), Is.EqualTo(new[] { "DA000300" }));
        Assert.That(test.Errors, Is.Empty);
    }

    // The page's messages from a player who used no card game object are dropped.
    [Test]
    public void Game_NoObjectUsed_SendsNothing() {
        var test = new TestGameSession();
        test.SetServerTable(Table());
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(63000031));
        test.AddInteract(Row(31000001, new InteractObjectMetadataUi("RouletteDialog", "6")),
            new InteractUiObject("ab", new Ms2SimpleUiObject(31000001, Vector3.Zero, Vector3.Zero)));
        new InteractObjectHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteInt(1);
            writer.WriteString("ab");
        }));
        test.TakeSent();

        Send(session, 0);
        Send(session, 1);
        Assert.That(session.CardReverseGame, Is.Null);
        Assert.That(Sent(test), Is.Empty);
    }
}
