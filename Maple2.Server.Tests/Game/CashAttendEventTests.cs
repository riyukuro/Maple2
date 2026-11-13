using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// CashAttendEvent (ItemFunction 7; KR 20300636, 20301532, 20301825, 30000717, no parameter): the client sends the
// plain item use (Unk_Inventory::RequestUseItem, inventory.cpp:1200). The item starts the AttendGift event whose
// item requirement (kind 2) names it, for the requirement's days. Before, the server had no case for the
// function: the item stayed, and a kind 2 event was never attended.
public class CashAttendEventTests {
    private const int EventId = 7101;
    private const int ItemId = 20301532;
    private const int RewardId = 20000001;
    private TestGameSession test = null!;
    private GameEvent gameEvent = null!;
    private Item ticket = null!;

    private static AttendGift Gift(int requiredItemId, int days) {
        return new AttendGift([new RewardItem(RewardId, 1, 1)], "gift", "title", "content", string.Empty, 0,
            new AttendGift.Require(AttendGiftRequirement.ItemId, requiredItemId, days));
    }

    private GameSession Start(int requiredItemId = ItemId, int days = 92, DateTime? end = null, int amount = 2) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardId));
        ticket = test.Store(TestGameSession.Metadata(ItemId,
            function: new ItemMetadataFunction(ItemFunction.CashAttendEvent, "CashAttendEvent", string.Empty, false)), amount: amount);
        gameEvent = test.AddEvent(EventId, GameEventType.AttendGift, Gift(requiredItemId, days), end: end);
        GameSession session = test.Start();
        // For the session save.
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [],
                [SkillRank.Awakening] = [],
            }, []),
        }));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Config = new ConfigManager(db, session);
        }
        session.Survival = new SurvivalManager(session);
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session) {
        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(ticket.Uid);
            writer.WriteUnicodeString(string.Empty);
        }));
    }

    private void Claim(GameSession session) {
        new AttendanceHandler { ItemMetadata = null! }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));
    }

    private static string? StartEnd(GameSession session) => session.GameEvent.Peek(GameEventUserValueType.AttendanceItemStartEnd, EventId);

    [Test]
    public void Use_StartsTheEventForItsDays() {
        GameSession session = Start(end: DateTime.Now.AddDays(200));
        long before = DateTime.Now.ToEpochSeconds();

        Use(session);

        Assert.That(session.Item.Inventory.Get(ticket.Uid)!.Amount, Is.EqualTo(1));
        long end = long.Parse(StartEnd(session)!);
        Assert.That(end, Is.InRange(before + 92 * 86400L, before + 92 * 86400L + 60));
        List<byte[]> sent = test.TakeSent();
        // Attendance 6 with the one start, then 7 with the event id.
        Assert.That(sent, Has.Some.EqualTo(AttendancePacket.StartedEvents([(EventId, end)]).ToArray()));
        Assert.That(sent.Last(), Is.EqualTo(AttendancePacket.SelectEvent(EventId).ToArray()));
        Assert.That(test.Errors, Is.Empty);
    }

    // The start and the used item are stored at once, together: a save of the whole bag before the session save (a
    // rename's) then can not store the used item without the start. Before, the start waited for the session save.
    [Test]
    public void Use_StoresTheStartWithTheUsedItem() {
        GameSession session = Start();
        Use(session);
        long end = long.Parse(StartEnd(session)!);

        Assert.That(test.StoredEventValues(EventId)[GameEventUserValueType.AttendanceItemStartEnd], Is.EqualTo(end.ToString()));
        Assert.That(test.Row(ticket.Uid)!.Amount, Is.EqualTo(1));
        // The session save afterwards writes the same.
        Assert.That(session.SessionSave(), Is.True, string.Join("\n", test.Errors));
        Assert.That(test.StoredEventValues(EventId)[GameEventUserValueType.AttendanceItemStartEnd], Is.EqualTo(end.ToString()));
        Assert.That(test.Row(ticket.Uid)!.Amount, Is.EqualTo(1));
    }

    // A save that fails stores and changes nothing: the item stays, the event is not started, no reply.
    [Test]
    public void Use_FailedSave_KeepsTheItemAndStartsNothing() {
        GameSession session = Start();
        test.FailSave = _ => true;
        Use(session);
        test.FailSave = null;

        Assert.That(session.Item.Inventory.Get(ticket.Uid)!.Amount, Is.EqualTo(2));
        Assert.That(test.Row(ticket.Uid)!.Amount, Is.EqualTo(2));
        Assert.That(StartEnd(session), Is.Null);
        Assert.That(test.StoredEventValues(EventId).ContainsKey(GameEventUserValueType.AttendanceItemStartEnd), Is.False);
        Assert.That(test.TakeSent(), Has.None.EqualTo(AttendancePacket.SelectEvent(EventId).ToArray()));
    }

    // The last item of its stack leaves the bag; its row goes to owner 0 with the start, and the session save after
    // does not bring it back.
    [Test]
    public void Use_LastOfItsStack_LeavesTheBag() {
        GameSession session = Start(amount: 1);
        Use(session);

        Assert.That(session.Item.Inventory.Get(ticket.Uid), Is.Null);
        Assert.That(test.Row(ticket.Uid)!.OwnerId, Is.EqualTo(0));
        Assert.That(StartEnd(session), Is.Not.Null);
        Assert.That(session.SessionSave(), Is.True, string.Join("\n", test.Errors));
        Assert.That(test.Row(ticket.Uid)!.OwnerId, Is.EqualTo(0));
    }

    // The start ends with the event when the event ends first.
    [Test]
    public void Use_StartEndsNoLaterThanTheEvent() {
        GameSession session = Start(days: 92, end: DateTime.Now.AddDays(10));
        Use(session);
        Assert.That(long.Parse(StartEnd(session)!), Is.EqualTo(gameEvent.EndTime));
    }

    // A second use while the start runs: error 5, and the item stays.
    [Test]
    public void Use_Again_IsRefusedWithTheUsedError() {
        GameSession session = Start();
        Use(session);
        test.TakeSent();

        Use(session);

        Assert.That(test.TakeSent(), Is.EqualTo(new[] { AttendancePacket.Error(AttendanceError.s_attendGift_item_attend_already_used).ToArray() }));
        Assert.That(session.Item.Inventory.Get(ticket.Uid)!.Amount, Is.EqualTo(1));
    }

    // No live event names the item: error 6, and the item stays.
    [Test]
    public void Use_WithoutItsEvent_IsRefusedWithTheNotFoundError() {
        GameSession session = Start(requiredItemId: 20301825);

        Use(session);

        Assert.That(test.TakeSent(), Is.EqualTo(new[] { AttendancePacket.Error(AttendanceError.s_attendGift_item_attend_not_found_evnet).ToArray() }));
        Assert.That(session.Item.Inventory.Get(ticket.Uid)!.Amount, Is.EqualTo(2));
        Assert.That(StartEnd(session), Is.Null);
    }

    // The kind 2 event is attended once started, not before.
    [Test]
    public void Claim_OnlyAfterTheStart() {
        GameSession session = Start();
        Claim(session);
        Assert.That(session.GameEvent.Peek(GameEventUserValueType.AttendanceRewardsClaimed, EventId), Is.Null.Or.EqualTo("0"));

        Use(session);
        Claim(session);

        Assert.That(session.GameEvent.Peek(GameEventUserValueType.AttendanceRewardsClaimed, EventId), Is.EqualTo("1"));
    }

    // The next login lists the start for Attendance 6.
    [Test]
    public void Login_ListsTheStart() {
        GameSession session = Start();
        Use(session);
        long end = long.Parse(StartEnd(session)!);
        session.SessionSave();

        GameSession again = test.Start();

        Assert.That(again.GameEvent.ItemStartedEvents(), Is.EqualTo(new List<(int, long)> { (EventId, end) }));
    }
}
