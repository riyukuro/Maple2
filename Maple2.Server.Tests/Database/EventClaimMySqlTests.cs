using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Database;

// The event reward claims that store their gift and their once-only mark in one transaction (EventClaim): the
// collect item group, the burning levelup reward, a long-term attendance day, a stamp row and the stamp card's
// complete reward, a time run step and its final reward, the item box, the gallery reward, a Mapleopoly lap, the
// reactor event's daily reward, an RPS reward and a couple dance step. Each is claimed through its handler (the
// reactor through ReactorEvent.Check and Claim, which StateReactHandler calls). A claim stores the reward's rows and
// the mark together, and a second claim gives nothing; a save that fails at the mark leaves none of the reward's rows
// stored (the in-memory store cannot show the rollback), the mark in memory as it was, and the claim open: the next
// claim gives it. Runs only when MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game database.
public class EventClaimMySqlTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int EventId = 7301;
    private const int Reward = 20000001;
    private const int Piece = 20000002;
    private const int Other = 20000003;
    private const int QuestA = 60100001;
    // The test character's map (TestGameSession).
    private const int CharacterField = 2000023;

    private static readonly string[] Paths = [
        "collect", "burning", "longterm", "stamp", "stampcomplete", "timerunstep", "timerunfinal", "itembox", "gallery",
        "mapleopoly", "reactor", "rps", "coupledance",
    ];

    private TestGameSession test = null!;
    private DbContextOptions plain = null!;
    private readonly MarkFault fault = new();
    private IDisposable? held;
    private GameEvent gameEvent = null!;
    private GameEventUserValueType mark;
    private Action<GameSession> claim = null!;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("a claim's rollback needs a real database: set MS2_TEST_GAME_DB");
        }
    }

    [TearDown]
    public void TearDown() {
        held?.Dispose();
        held = null;
        fault.Fail = false;
    }

    private GameSession Login(string path) {
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        test = new TestGameSession(new DbContextOptionsBuilder().UseMySql(Connection, version).AddInterceptors(fault).Options);
        test.Register(TestGameSession.Metadata(Reward), TestGameSession.Metadata(Piece), TestGameSession.Metadata(Other));
        var reward = new RewardItem(Reward, 1, 2);
        var other = new RewardItem(Other, 1, 1);
        var stamps = new[] { new StampEvent.Stamp(QuestA, false, 0, 0) };
        var timeRunQuests = new[] { new TimeRunEvent.Quest(QuestA, 10, 1) };
        Action<GameSession>? after = null;
        GameEventData data;
        switch (path) {
            case "collect":
                test.Store(TestGameSession.Metadata(Piece));
                test.Store(TestGameSession.Metadata(Other));
                data = new CollectItemGroup("collect", [new RewardItem(Piece, 1, 1), new RewardItem(Other, 1, 1)], reward);
                mark = GameEventUserValueType.CollectItemGroup;
                claim = session => Event(session, 19, writer => writer.WriteInt(EventId));
                break;
            case "burning":
                data = new BurningLevelup(1, 70, 2, 0, "", "", "", "", 0, [], [reward], null);
                mark = GameEventUserValueType.BurningLevelupRewardTaken;
                claim = session => Event(session, 15, writer => writer.WriteInt(EventId));
                break;
            case "longterm":
                data = new LongTermAttendGift("calendar", "tag", 5, 30, [new LongTermAttendGift.Reward(1, reward, default)]);
                after = session => Hold(session, GameEventUserValueType.LongTermAttendDays, "1");
                mark = GameEventUserValueType.LongTermAttendClaimedDays;
                claim = session => Event(session, 25, writer => writer.WriteInt(1));
                break;
            case "stamp":
                data = new StampEvent("tag", 1, stamps, [new StampEvent.Reward(1, false, reward, default)], other);
                after = _ => test.SetQuest(QuestA, QuestState.Completed);
                mark = GameEventUserValueType.StampRewardsClaimed;
                claim = session => Event(session, 0, writer => {
                    writer.WriteInt(1);
                    writer.WriteByte(0);
                });
                break;
            case "stampcomplete":
                data = new StampEvent("tag", 1, stamps, [new StampEvent.Reward(1, false, other, default)], reward);
                after = _ => test.SetQuest(QuestA, QuestState.Completed);
                mark = GameEventUserValueType.StampCompleteRewardClaimed;
                claim = session => Event(session, 1);
                break;
            case "timerunstep":
                data = new TimeRunEvent(0, timeRunQuests, new Dictionary<int, RewardItem> { [10] = reward }, other);
                after = _ => test.SetQuest(QuestA, QuestState.Completed);
                mark = GameEventUserValueType.TimeRunStepRewardKey;
                claim = session => Event(session, 4, writer => writer.WriteInt(10));
                break;
            case "timerunfinal":
                data = new TimeRunEvent(0, timeRunQuests, new Dictionary<int, RewardItem>(), reward);
                after = _ => test.SetQuest(QuestA, QuestState.Completed);
                mark = GameEventUserValueType.TimeRunFinalRewardReceived;
                claim = session => Event(session, 3);
                break;
            case "itembox":
                data = new ItemBox(CharacterField, Reward, "title", "desc", "banner");
                mark = GameEventUserValueType.ItemBoxReceivedDay;
                claim = session => Event(session, 2);
                break;
            case "gallery":
                data = new Gallery([QuestA], [reward], 3, "image");
                after = _ => test.SetQuest(QuestA, QuestState.Completed);
                mark = GameEventUserValueType.GalleryClaimReward;
                claim = session => Event(session, 9);
                break;
            case "mapleopoly": {
                    // A two-tile board: the token on the forward tile (position 1) walks to 2, the first lap.
                    var none = new ItemComponent(0, 0, 0, ItemTag.None);
                    data = new BlueMarble(new ItemComponent(Other, -1, 1, ItemTag.None),
                        [new BlueMarble.Round(0, new ItemComponent(Reward, -1, 2, ItemTag.None))],
                        [new BlueMarble.Slot(BlueMarbleSlotType.Start, 0, none), new BlueMarble.Slot(BlueMarbleSlotType.Forward, 1, none)]);
                    mark = GameEventUserValueType.MapleopolyTotalTrips;
                    // Each claim is a roll that lands on the forward tile and its result: the lap is paid while the trips
                    // stored are below it.
                    claim = session => {
                        Hold(session, GameEventUserValueType.MapleopolyTotalSlotCount, "1");
                        session.GameEvent.Mapleopoly.Rolled();
                        new MapleopolyHandler { ItemMetadata = null! }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));
                    };
                    break;
                }
            case "reactor":
                data = new Reactor([CharacterField], 1);
                mark = GameEventUserValueType.ReactorRewardCount;
                // As StateReactHandler: the reactor's event check (the day's count under DailyCount), then the reward.
                claim = session => {
                    var reactor = new ReactorTable.Entry(1, 0, 0, EventId, [], []);
                    if (ReactorEvent.Check(session, reactor, out GameEvent? found) && found != null) {
                        ReactorEvent.Claim(session, found, [session.Field!.ItemDrop.CreateItem(Reward, 1, 2)!]);
                    }
                };
                break;
            case "rps":
                data = new Rps(Other, [new Rps.RewardData(0, [reward])], "");
                mark = GameEventUserValueType.RPSRewardsClaimed;
                claim = session => Microgame(session, RpsRules.ClaimReward, writer => writer.WriteInt(0));
                break;
            case "coupledance":
                data = new CoupleDance([[reward]], 1, [], [], 1, 0, []);
                after = session => Hold(session, GameEventUserValueType.CoupleDanceRewardState, CoupleDanceRules.Complete("", 1, false)!);
                mark = GameEventUserValueType.CoupleDanceRewardState;
                claim = session => Microgame(session, CoupleDanceRules.ClaimReward, writer => {
                    writer.WriteInt(EventId);
                    writer.WriteInt(0);
                });
                break;
            default:
                throw new ArgumentException(path);
        }
        gameEvent = test.AddEvent(EventId, TypeOf(data), data);
        if (path == "burning") {
            // The account picked this character, which reached the end level.
            var values = new List<GameEventUserValue>();
            foreach ((GameEventUserValueType type, string value) in new[] {
                         (GameEventUserValueType.BurningLevelupCharacter, test.CharacterId.ToString()),
                         (GameEventUserValueType.BurningLevelupReached, "1"),
                     }) {
                var stored = new GameEventUserValue(type, gameEvent.EndTime, EventId);
                stored.SetValue(value);
                values.Add(stored);
            }
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.SaveAccountEventValues(test.AccountId, values), Is.True);
        }
        // As a session that took the account lock at its entry (GameSession.EnterServer).
        held = GameStorage.HoldOwners(test.AccountId, test.CharacterId);
        GameSession started = test.Start();
        after?.Invoke(started);
        test.TakeSent();
        return started;
    }

    private static GameEventType TypeOf(GameEventData data) => data switch {
        CollectItemGroup => GameEventType.CollectItemGroup,
        BurningLevelup => GameEventType.BurningLevelup,
        LongTermAttendGift => GameEventType.LongTermAttendGift,
        StampEvent => GameEventType.StampEvent,
        TimeRunEvent => GameEventType.TimeRunEvent,
        ItemBox => GameEventType.ItemBox,
        Gallery => GameEventType.Gallery,
        BlueMarble => GameEventType.BlueMarble,
        Reactor => GameEventType.reactor,
        CoupleDance => GameEventType.CoupleDance,
        Rps => GameEventType.RPS,
        _ => throw new ArgumentException(data.GetType().Name),
    };

    private void Hold(GameSession session, GameEventUserValueType type, string value) {
        session.GameEvent.Get(type, EventId, gameEvent.EndTime);
        session.GameEvent.Set(EventId, type, value);
    }

    private static void Event(GameSession session, byte command, Action<ByteWriter>? body = null) {
        new EventRewardHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            body?.Invoke(writer);
        }));
    }

    private static void Microgame(GameSession session, short mode, Action<ByteWriter> body) {
        new MicrogameHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteShort(mode);
            body(writer);
        }));
    }

    private static int Held(GameSession session) => session.Item.Inventory.Find(Reward).Sum(item => item.Amount);

    // The reward's stored amount: the character's rows and those of the character's mails.
    private int StoredReward() {
        using var context = new Ms2Context(plain);
        List<long> mails = context.Mail.AsNoTracking().Where(mail => mail.ReceiverId == test.CharacterId).Select(mail => mail.Id).ToList();
        return context.Item.AsNoTracking()
            .Where(item => item.ItemId == Reward && (item.OwnerId == test.CharacterId || mails.Contains(item.OwnerId)))
            .Sum(item => item.Amount);
    }

    // The stored mark, the character's or the account's.
    private string? StoredMark() {
        using var context = new Ms2Context(plain);
        return context.GameEventUserValue.AsNoTracking()
                   .Where(value => value.CharacterId == test.CharacterId && value.EventId == EventId && value.Type == mark)
                   .Select(value => value.Value).FirstOrDefault()
               ?? context.AccountEventValue.AsNoTracking()
                   .Where(value => value.AccountId == test.AccountId && value.EventId == EventId && value.Type == mark)
                   .Select(value => value.Value).FirstOrDefault();
    }

    [TestCaseSource(nameof(Paths))]
    public void Claim_StoresTheRewardWithTheMark_AndASecondClaimGivesNothing(string path) {
        GameSession session = Login(path);

        claim(session);

        int given = Held(session);
        string? marked = session.GameEvent.Peek(mark, EventId);
        Assert.That(given, Is.GreaterThan(0));
        Assert.That(StoredReward(), Is.EqualTo(given));
        Assert.That(marked, Is.Not.Null.And.Not.Empty);
        Assert.That(StoredMark(), Is.EqualTo(marked));
        Assert.That(test.Errors, Is.Empty);

        claim(session);

        Assert.That(Held(session), Is.EqualTo(given));
        Assert.That(StoredReward(), Is.EqualTo(given));
        Assert.That(session.GameEvent.Peek(mark, EventId), Is.EqualTo(marked));
    }

    // The save fails at the mark, after the reward's rows were written in the same transaction: none of them stays, and
    // the stored mark is the one before the claim.
    [TestCaseSource(nameof(Paths))]
    public void Claim_TheSaveFails_GivesNothingAndLeavesTheClaimOpen(string path) {
        GameSession session = Login(path);
        string? before = session.GameEvent.Peek(mark, EventId);
        string? storedBefore = StoredMark();

        fault.Fail = true;
        claim(session);
        fault.Fail = false;

        Assert.That(fault.Failed, Is.GreaterThan(0), "the claim reached its store");
        Assert.That(Held(session), Is.EqualTo(0));
        Assert.That(StoredReward(), Is.EqualTo(0));
        // The claim's Get holds an empty value for an event not yet claimed; empty and none are the same open claim.
        Assert.That(session.GameEvent.Peek(mark, EventId) ?? string.Empty, Is.EqualTo(before ?? string.Empty));
        Assert.That(StoredMark(), Is.EqualTo(storedBefore));

        test.Errors.Clear();
        claim(session);

        Assert.That(Held(session), Is.GreaterThan(0));
        Assert.That(StoredReward(), Is.EqualTo(Held(session)));
        Assert.That(StoredMark(), Is.Not.Null.And.EqualTo(session.GameEvent.Peek(mark, EventId)));
    }

    // Fails a save that writes an event value (the character's or the account's).
    private sealed class MarkFault : SaveChangesInterceptor {
        public bool Fail;
        public int Failed;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            if (Fail && eventData.Context != null && eventData.Context.ChangeTracker.Entries()
                    .Any(entry => entry.State is EntityState.Added or EntityState.Modified
                                  && entry.Entity is Maple2.Database.Model.GameEventUserValue or Maple2.Database.Model.AccountEventValue)) {
                Failed++;
                throw new DbUpdateException("The test made this save fail");
            }
            return result;
        }
    }
}
