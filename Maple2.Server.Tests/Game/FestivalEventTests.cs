using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// GameEventFestival: the client keeps each running festival's value1 and value2 (FestivalContext) and lets a
// FestivalEvent item whose itemUseType is not 0 be used only while a festival of that type runs.
public class FestivalEventTests {
    // Before, the table's Festival rows (KR 831001-831004) were dropped at ingest and never sent, so the client
    // refused every FestivalEvent item with "the event has not started".
    [Test]
    public void GameEvent_FestivalIsLaidOutForTheClient() {
        var metadata = new GameEventMetadata(831002, GameEventType.Festival, DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(1000),
            TimeSpan.Zero, TimeSpan.Zero, [], new Festival(1, 1), "1", "1", "", "");
        var gameEvent = new GameEvent(metadata);
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Festival"));
        Assert.That(reader.ReadInt(), Is.EqualTo(831002)); // GameEvent::vfunc_4
        Assert.That(reader.ReadByte(), Is.EqualTo(1)); // FestivalContext::Read m_18
        Assert.That(reader.ReadByte(), Is.EqualTo(1)); // m_1c, the use type
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    private const int ItemId = 20301855;
    private const int BoxId = 340000127;
    private const int PrizeId = 20000001;
    private TestGameSession test = null!;
    private Item festivalItem = null!;

    // KR 20301855: fieldID 2000001, gameEvent Festival, itemUseType 1, dropItem 340000127; the test field is 2000023.
    private GameSession Start(int? festivalType, string fieldId = "2000023", int useType = 1) {
        test = new TestGameSession();
        if (festivalType != null) {
            test.AddEvent(831002, GameEventType.Festival, new Festival(1, festivalType.Value));
        }
        test.Register(TestGameSession.Metadata(PrizeId));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [BoxId] = TestGameSession.DropBox([(PrizeId, 3)]),
        }));
        string parameters = $"<v fieldID=\"{fieldId}\" gameEvent=\"Festival\" itemUseType=\"{useType}\" dropItem=\"{BoxId}\"/>";
        festivalItem = test.Store(TestGameSession.Metadata(ItemId,
            function: new ItemMetadataFunction(ItemFunction.FestivalEvent, "FestivalEvent", parameters, false), type: 10), amount: 2);
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session) {
        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(festivalItem.Uid);
            writer.WriteUnicodeString(string.Empty);
        }));
    }

    private static int Prizes(GameSession session) => session.Item.Inventory.Filter(item => item.Id == PrizeId).Sum(item => item.Amount);

    // Before, the server had no FestivalEvent case: the use the client sent did nothing.
    [Test]
    public void Use_InItsFieldWhileItsFestivalRuns_OpensTheBox() {
        GameSession session = Start(festivalType: 1);
        Use(session);
        Assert.That(session.Item.Inventory.Get(festivalItem.Uid)!.Amount, Is.EqualTo(1));
        Assert.That(Prizes(session), Is.EqualTo(3));
    }

    // The client's three checks, which the server repeats: another festival type, no festival, another field.
    [TestCase(2, "2000023")]
    [TestCase(null, "2000023")]
    [TestCase(1, "2000001")]
    public void Use_OutsideTheClientsRule_IsRefused(int? festivalType, string fieldId) {
        GameSession session = Start(festivalType, fieldId);
        Use(session);
        Assert.That(session.Item.Inventory.Get(festivalItem.Uid)!.Amount, Is.EqualTo(2));
        Assert.That(Prizes(session), Is.EqualTo(0));
    }

    // itemUseType 0 takes any running festival; fieldID 0 any field.
    [TestCase(new[] { 0 }, 0, new[] { 3 }, true)]
    [TestCase(new[] { 2000001, 2000023 }, 0, new[] { 3 }, true)]
    [TestCase(new[] { 0 }, 0, new int[0], false)]
    [TestCase(new[] { 0 }, 1, new[] { 0, 1 }, true)]
    public void FestivalUsable(int[] fieldIds, int useType, int[] festivalTypes, bool expected) {
        Assert.That(ItemUseHandler.FestivalUsable(fieldIds, 2000023, eventRunning: true, useType, festivalTypes), Is.EqualTo(expected));
        Assert.That(ItemUseHandler.FestivalUsable(fieldIds, 2000023, eventRunning: false, useType, festivalTypes), Is.False);
    }
}
