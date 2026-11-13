using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The item box event (ItemBox): EventReward 2 from the dialog opened in the event's field gives the box item once a
// day, keeps the day (user value 1400) for the account and replies 2 with whether it was given.
public class EventItemBoxTests {
    private const int EventId = 620004;
    private const int BoxItem = 20301063;
    private const int OtherField = 2000065;

    private TestGameSession test = null!;
    private GameEvent gameEvent = null!;

    // The test character's map (TestGameSession).
    private const int CharacterField = 2000023;

    private GameSession Login(int fieldId = CharacterField) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(BoxItem));
        gameEvent = test.AddEvent(EventId, GameEventType.ItemBox, new ItemBox(fieldId, BoxItem, "title", "desc", "banner"));
        GameSession session = test.Start();
        Assert.That(session.Field!.MapId, Is.EqualTo(CharacterField));
        test.TakeSent();
        return session;
    }

    private static void Open(GameSession session) =>
        new EventRewardHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteByte(2)));

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private List<string> SentHex() => test.TakeSent().Select(Convert.ToHexString).ToList();

    private static int Held(GameSession session) => session.Item.Inventory.Find(BoxItem).Sum(item => item.Amount);

    private Dictionary<GameEventUserValueType, string> StoredAccountValues() {
        using var db = test.Storage.Context();
        return db.GetAccountEventValues(test.AccountId).Where(value => value.EventId == EventId)
            .ToDictionary(value => value.Type, value => value.Value);
    }

    private string UserValue(GameSession session) => Hex(GameEventUserValuePacket.Update(
        session.GameEvent.Get(GameEventUserValueType.ItemBoxReceivedDay, EventId, gameEvent.EndTime)));

    [Test]
    public void ItemBox_Open_GivesTheBoxAndKeepsTheDayForTheAccount() {
        GameSession session = Login();
        string today = ItemBoxDay(DateTime.Now);

        Open(session);

        Assert.That(Held(session), Is.EqualTo(1));
        Assert.That(session.GameEvent.Peek(GameEventUserValueType.ItemBoxReceivedDay, EventId), Is.EqualTo(today));
        // The item's packets, then 1400 and the reply.
        Assert.That(SentHex()[^2..], Is.EqualTo(new[] { UserValue(session), Hex(EventRewardPacket.OpenEventBox(true)) }));
        // Stored with the box, not at logout, and for the account, not the character.
        Assert.That(StoredAccountValues().GetValueOrDefault(GameEventUserValueType.ItemBoxReceivedDay), Is.EqualTo(today));
        Assert.That(test.StoredEventValues(EventId), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void ItemBox_SecondOpenOfTheDay_IsRefused() {
        GameSession session = Login();
        Open(session);
        test.TakeSent();

        Open(session);

        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(EventRewardPacket.OpenEventBox(false)) }));
        Assert.That(Held(session), Is.EqualTo(1));
    }

    // The next day the box is given again.
    [Test]
    public void ItemBox_NextDay_GivesTheBoxAgain() {
        GameSession session = Login();
        DateTime now = DateTime.Now;
        session.GameEvent.Clock = () => now;
        Open(session);
        session.GameEvent.Clock = () => now.AddDays(1);

        Open(session);

        Assert.That(Held(session), Is.EqualTo(2));
        Assert.That(session.GameEvent.Peek(GameEventUserValueType.ItemBoxReceivedDay, EventId), Is.EqualTo(ItemBoxDay(now.AddDays(1))));
    }

    // Outside the event's field the dialog does not open; a claim from there gets nothing.
    [Test]
    public void ItemBox_OpenInAnotherField_IsRefused() {
        GameSession session = Login(OtherField);

        Open(session);

        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(EventRewardPacket.OpenEventBox(false)) }));
        Assert.That(Held(session), Is.EqualTo(0));
        Assert.That(StoredAccountValues(), Is.Empty);
    }

    // uiitemeventopenboxdialog.cpp:139: 2, then the bool.
    [Test]
    public void ItemBox_ReplyBytes() {
        Assert.That(Hex(EventRewardPacket.OpenEventBox(true))[4..], Is.EqualTo("0201"));
        Assert.That(Hex(EventRewardPacket.OpenEventBox(false))[4..], Is.EqualTo("0200"));
    }

    // ItemBoxEventContext::Read: the event id, field, box item, three strings and the event id again.
    [Test]
    public void ItemBox_EventBytes() {
        var metadata = new GameEventMetadata(EventId, GameEventType.ItemBox, DateTime.Now, DateTime.Now.AddDays(1),
            TimeSpan.Zero, TimeSpan.Zero, [], new ItemBox(2000064, BoxItem, "t", "d", "b"), "", "", "", "");
        var writer = new ByteWriter();
        new GameEvent(metadata).WriteTo(writer);
        string hex = Convert.ToHexString(writer.ToArray());
        Assert.That(hex, Does.EndWith("E4750900" + "C0841E00" + "07C53501" + "01007400" + "01006400" + "01006200" + "E4750900"));
    }

    [Test]
    public void ItemBox_IngestReadsTheBox() {
        ItemBox? box = ServerTableMapper.ReadItemBox(
            "<ms2 fieldID=\"02000064\" boxItemID=\"20301063\" title=\"t\" desc=\"d\" bannerPath=\"b.png\"/>");
        Assert.That(box, Is.EqualTo(new ItemBox(2000064, 20301063, "t", "d", "b.png")));
        Assert.Multiple(() => {
            Assert.That(ServerTableMapper.ReadItemBox(""), Is.Null);
            Assert.That(ServerTableMapper.ReadItemBox("<ms2 fieldID=\"0\" boxItemID=\"20301063\"/>"), Is.Null);
            Assert.That(ServerTableMapper.ReadItemBox("<ms2 fieldID=\"2000064\"/>"), Is.Null);
            Assert.That(ServerTableMapper.ReadItemBox("<ms2"), Is.Null);
        });
    }

    [Test]
    public void ItemBox_Rules() {
        Assert.Multiple(() => {
            Assert.That(EventItemBoxRules.Day(new DateTime(2017, 9, 24, 19, 5, 0)), Is.EqualTo(20170924));
            Assert.That(EventItemBoxRules.CanOpen(0, 20170924), Is.True);
            Assert.That(EventItemBoxRules.CanOpen(20170923, 20170924), Is.True);
            Assert.That(EventItemBoxRules.CanOpen(20170924, 20170924), Is.False);
        });
    }

    private static string ItemBoxDay(DateTime time) => EventItemBoxRules.Day(time).ToString();
}
