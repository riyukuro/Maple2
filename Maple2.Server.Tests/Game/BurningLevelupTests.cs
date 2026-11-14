using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// The burning levelup (GameEventBurningLevelup): the row reader, the event list entry, the rules, the bonus levels
// of the picked character, the completion at the end level and the reward claim (EventReward 15).
public class BurningLevelupTests {
    private const int EventId = 380010;
    private const int Reward = 20303380;

    private TestGameSession test = null!;

    private static BurningLevelup Data(int[]? targets = null, RewardItem[]? rewards = null, BurningLevelup.JumpingOption? jumping = null) {
        return new BurningLevelup(9, 70, 2, 90000838, "s_popup", "s_bottom", "s_desc", "s_title", 47, targets ?? [],
            rewards ?? [new RewardItem(Reward, 6, 1)], jumping);
    }

    private static string Text(string text) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString(text);
        return Convert.ToHexString(writer.ToArray());
    }

    #region Rules
    [TestCase(9, 0)] // the begin level is not a burning level
    [TestCase(10, 2)]
    [TestCase(11, 1)]
    [TestCase(12, 0)]
    [TestCase(13, 2)]
    [TestCase(15, 0)]
    [TestCase(66, 0)]
    [TestCase(67, 2)]
    [TestCase(68, 1)]
    [TestCase(69, 0)] // the last earned level below the end (KR numbers never reach the cap)
    [TestCase(70, 0)]
    [TestCase(80, 0)]
    public void BonusLevel_IsTheKrStep(int level, int bonus) {
        Assert.That(BurningLevelupRules.BonusLevel(Data(), level), Is.EqualTo(bonus));
    }

    // The cap: a bonus past the end level adds the levels up to it (end 68: 67 adds 1, not 2).
    [Test]
    public void BonusLevel_StopsAtTheEndLevel() {
        BurningLevelup end68 = Data() with { EndLevel = 68 };
        Assert.That(BurningLevelupRules.BonusLevel(end68, 67), Is.EqualTo(1));
        Assert.That(BurningLevelupRules.BonusLevel(end68, 68), Is.EqualTo(0));
    }

    [Test]
    public void BonusLevel_WithoutABonus_IsZero() {
        BurningLevelup none = Data() with { Bonus = 0 };
        Assert.That(Enumerable.Range(1, 80).Select(level => BurningLevelupRules.BonusLevel(none, level)), Is.All.EqualTo(0));
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("0", false)]
    [TestCase("x", false)]
    [TestCase("1", true)]
    [TestCase("2000000000001", true)]
    public void IsSet_IsANumberOtherThanZero(string? value, bool set) {
        Assert.That(BurningLevelupRules.IsSet(value), Is.EqualTo(set));
    }

    private static GameEvent Running(int id, BurningLevelup data) {
        return new GameEvent(new GameEventMetadata(id, GameEventType.BurningLevelup, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1),
            TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", ""));
    }

    // A re-ingest moved the replayed event's end (E1 -> E2) during the run: the pick stored at E1 follows the event to
    // E2, so after E1 the login still reads it (no second popup, no second pick); other events' and types' values drop.
    [Test]
    public void LoginValues_FollowTheMovedEventEnd() {
        GameEvent running = Running(380006, Data());
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var picked = new GameEventUserValue(GameEventUserValueType.BurningLevelupCharacter, now - 60, 380006);
        picked.SetValue("1001");
        var reached = new GameEventUserValue(GameEventUserValueType.BurningLevelupReached, now - 60, 380006);
        reached.SetValue("1");
        var otherEvent = new GameEventUserValue(GameEventUserValueType.BurningLevelupCharacter, now + 60, 380007);
        otherEvent.SetValue("1002");
        var otherType = new GameEventUserValue(GameEventUserValueType.ItemBoxReceivedDay, now + 60, 380006);
        otherType.SetValue("1");

        List<GameEventUserValue> values = BurningLevelupRules.LoginValues([picked, reached, otherEvent, otherType], [running], now);

        Assert.That(values.Select(value => (value.Type, value.Value, value.ExpirationTime)), Is.EqualTo(new[] {
            (GameEventUserValueType.BurningLevelupCharacter, "1001", running.EndTime),
            (GameEventUserValueType.BurningLevelupReached, "1", running.EndTime),
        }));
        Assert.That(BurningLevelupRules.CheckPick([running], true, 20, 0,
            eventId => values.FirstOrDefault(value => value.EventId == eventId && value.Type == GameEventUserValueType.BurningLevelupCharacter)?.Long() ?? 0,
            (eventId, type) => values.Any(value => value.EventId == eventId && value.Type == type && BurningLevelupRules.IsSet(value.Value))).Result,
            Is.EqualTo(BurningLevelupRules.Pick.NoEvent));
        // Without the move (the stored E1 behind now) nothing would be read and the pick offered again.
        Assert.That(BurningLevelupRules.LoginValues([picked], [], now), Is.Empty);
    }

    [Test]
    public void CheckPick_TakesTheLowestEventTheAccountHasNotPickedFor() {

        GameEvent[] running = [Running(380011, Data()), Running(380010, Data()), Running(380012, Data([(int) JobCode.Knight]))];
        var picks = new Dictionary<int, long>();
        var flags = new HashSet<(int, GameEventUserValueType)>();
        (BurningLevelupRules.Pick, int?) Check(bool owned = true, int level = 1, int job = (int) JobCode.Newbie) {
            (BurningLevelupRules.Pick result, GameEvent? found) = BurningLevelupRules.CheckPick(running, owned, level, job,
                id => picks.GetValueOrDefault(id), (id, type) => flags.Contains((id, type)));
            return (result, found?.Id);
        }

        Assert.That(Check(), Is.EqualTo((BurningLevelupRules.Pick.Ok, (int?) 380010)));
        Assert.That(Check(owned: false), Is.EqualTo((BurningLevelupRules.Pick.NotOwned, (int?) null)));
        Assert.That(Check(level: 70), Is.EqualTo((BurningLevelupRules.Pick.Level, (int?) 380010)));
        picks[380010] = 5;
        Assert.That(Check(), Is.EqualTo((BurningLevelupRules.Pick.Ok, (int?) 380011)));
        flags.Add((380011, GameEventUserValueType.BurningLevelupReached));
        Assert.That(Check(), Is.EqualTo((BurningLevelupRules.Pick.NoEvent, (int?) null)));
        Assert.That(Check(job: (int) JobCode.Knight), Is.EqualTo((BurningLevelupRules.Pick.Ok, (int?) 380012)));
    }

    [Test]
    public void LevelSteps_AddsTheBonusOnEachLevelReachedAndKeepsTheExpLeft() {
        Dictionary<int, long> next = Enumerable.Range(1, 79).ToDictionary(level => level, _ => 100L);
        int Bonus(int level) => BurningLevelupRules.BonusLevel(Data(), level);

        Assert.That(ExperienceManager.LevelSteps(9, 150, next, 80, Bonus), Is.EqualTo((12, 50L)));
        Assert.That(ExperienceManager.LevelSteps(9, 250, next, 80, Bonus), Is.EqualTo((15, 50L)));
        Assert.That(ExperienceManager.LevelSteps(9, 250, next, 80, _ => 0), Is.EqualTo((11, 50L)));
        Assert.That(ExperienceManager.LevelSteps(67, 150, next, 80, Bonus), Is.EqualTo((69, 50L)));
        Assert.That(ExperienceManager.LevelSteps(1, 50, next, 80, Bonus), Is.EqualTo((1, 50L)));
        Assert.That(ExperienceManager.LevelSteps(79, 150, next, 80, _ => 5), Is.EqualTo((80, 50L)));
    }
    #endregion

    #region Reader
    private const string Info = "<ms2><v beginLevel=\"9\" endLevel=\"70\" burningBonus=\"2\" additionalEffectId=\"90000838\""
                                + " setCharacterPopupStringKey=\"s_popup\" setCharacterPopupBottomStringKey=\"s_bottom\""
                                + " descPopupStringKey=\"s_desc\" rewardUiTitleStringKey=\"s_title\" equipNametagSymbolID=\"47\" /></ms2>";
    private const string Items = "<items><v itemID=\"20303380\" count=\"1\" grade=\"6\" /></items>";
    private const string Jumping = "<ms2><jumpingOption tutorialEndFieldId=\"2000062\" jumpingItemDescPopupStringKey=\"s_jd\""
                                   + " jumpingItemNotifyStringKey=\"s_jn\" jumpingCompleteStringKey=\"s_jc\">"
                                   + "<item itemID=\"20001322\" count=\"1\" grade=\"1\" /></jumpingOption></ms2>";

    [Test]
    public void Reader_ReadsTheKrRow() {
        BurningLevelup? data = EventXmlReader.BurningLevelup(Info, Items, null, null, _ => 50);
        Assert.That(data, Is.Not.Null);
        Assert.That((data!.BeginLevel, data.EndLevel, data.Bonus, data.AdditionalEffectId, data.NametagSymbolId), Is.EqualTo((9, 70, 2, 90000838, (short) 47)));
        Assert.That((data.SetCharacterPopupStringKey, data.SetCharacterPopupBottomStringKey, data.DescPopupStringKey, data.RewardUiTitleStringKey),
            Is.EqualTo(("s_popup", "s_bottom", "s_desc", "s_title")));
        Assert.That(data.Targets, Is.Empty);
        Assert.That(data.Rewards, Is.EqualTo(new[] { new RewardItem(Reward, 6, 1) }));
        Assert.That(data.Jumping, Is.Null);
    }

    [Test]
    public void Reader_ReadsTargetsAndAJumpingItemWithATargetLevel() {
        BurningLevelup? data = EventXmlReader.BurningLevelup(Info, Items, "10, 20,x,10", Jumping, id => id == 20001322 ? 50 : null);
        Assert.That(data!.Targets, Is.EqualTo(new[] { 10, 20 }));
        Assert.That(data.Jumping, Is.EqualTo(new BurningLevelup.JumpingOption(new RewardItem(20001322, 1, 1), 50, 2000062, "s_jd", "s_jn", "s_jc")));

        Assert.That(EventXmlReader.BurningLevelup(Info, Items, null, Jumping, _ => null)!.Jumping, Is.Null);
    }

    [Test]
    public void Reader_WithoutRewardItems_KeepsNoTargetsAndNoJumping() {
        BurningLevelup? data = EventXmlReader.BurningLevelup(Info, null, "10", Jumping, _ => 50);
        Assert.That(data, Is.Not.Null);
        Assert.That((data!.Rewards.Length, data.Targets.Length, data.Jumping), Is.EqualTo((0, 0, (BurningLevelup.JumpingOption?) null)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("<ms2/>")]
    [TestCase("<ms2")]
    public void Reader_WithoutTheInfoElement_DropsTheRow(string? value1) {
        Assert.That(EventXmlReader.BurningLevelup(value1, Items, null, null, _ => 50), Is.Null);
    }

    private static ItemMetadata Potion(TransferType transfer, string parameters, ItemFunction function = ItemFunction.LevelPotion) {
        ItemMetadata item = TestGameSession.Metadata(20001322, function: new ItemMetadataFunction(function, function.ToString(), parameters, false));
        return item with { Limit = item.Limit with { TransferType = transfer } };
    }

    [Test]
    public void JumpingTargetLevel_IsACharacterBoundLevelPotionsTarget() {
        const string level50 = "<v targetLevel=\"50\" />";
        Assert.That(EventXmlReader.JumpingTargetLevel(Potion(TransferType.BindOnLoot, level50)), Is.EqualTo(50));
        Assert.That(EventXmlReader.JumpingTargetLevel(Potion(TransferType.BindOnEquip, level50)), Is.EqualTo(50));
        Assert.That(EventXmlReader.JumpingTargetLevel(Potion(TransferType.BindOnUse, level50)), Is.EqualTo(50));
        Assert.That(EventXmlReader.JumpingTargetLevel(Potion(TransferType.Tradable, level50)), Is.Null);
        Assert.That(EventXmlReader.JumpingTargetLevel(Potion(TransferType.BindOnLoot, level50, ItemFunction.None)), Is.Null);
        Assert.That(EventXmlReader.JumpingTargetLevel(Potion(TransferType.BindOnLoot, "<v targetLevel=\"65536\" />")), Is.Null);
        Assert.That(EventXmlReader.JumpingTargetLevel(Potion(TransferType.BindOnLoot, "<v />")), Is.Null);
        Assert.That(EventXmlReader.JumpingTargetLevel(null), Is.Null);
    }
    #endregion

    #region Packet
    // GameEvent::vfunc_4 reads the id, then BurningLevelupContext::Read (gameeventcontext.cpp:2022) the rest.
    [Test]
    public void Packet_IsTheContextRead() {
        var session = new TestGameSession();
        var jumping = new BurningLevelup.JumpingOption(new RewardItem(20001322, 1, 2), 50, 2000062, "a", "b", "c");
        GameEvent written = session.AddEvent(EventId, GameEventType.BurningLevelup, Data([10], jumping: jumping));
        var writer = new ByteWriter();
        written.WriteTo(writer);

        var end = new ByteWriter();
        end.WriteLong(written.EndTime);
        const string reward = "14CE3501" + "0600" + "01000000" + "00000000";
        const string jumpItem = "2A323101" + "0100" + "02000000" + "00000000";
        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(Text("BurningLevelup") + "6ACC0500" + "6ACC0500" + Convert.ToHexString(end.ToArray())
            + "09000000" + "46000000" + "02000000" + "C64D5D05"
            + Text("s_popup") + Text("s_bottom") + Text("s_desc") + Text("s_title")
            + "2F00" + "0100" + "0A000000" + "01000000" + reward
            + "01" + jumpItem + "32000000" + "BE841E00" + Text("a") + Text("b") + Text("c")));
    }

    [Test]
    public void Replay_KeepsAnEventOnlyWithItemsTheClientShows() {
        var data = Data(rewards: [new RewardItem(Reward, 6, 1)]);
        GameEventMetadata row = new(EventId, GameEventType.BurningLevelup, DateTime.UnixEpoch, DateTime.UnixEpoch.AddDays(1), TimeSpan.Zero,
            TimeSpan.Zero, [], data, "", "", "", "");
        Assert.That(AttendGiftReplay.Types, Does.Contain(GameEventType.BurningLevelup));
        Assert.That(AttendGiftReplay.ClientHasRewards(row, _ => 1), Is.True);
        Assert.That(AttendGiftReplay.ClientHasRewards(row, _ => 0), Is.False);
    }
    #endregion

    #region Session
    // picked: the account's 2000 value is this character; reached, taken: 2001 and 2002 are set.
    private GameSession Login(bool picked = true, bool reached = false, bool taken = false, BurningLevelup? data = null) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Reward));
        GameEvent gameEvent = test.AddEvent(EventId, GameEventType.BurningLevelup, data ?? Data());
        var values = new List<GameEventUserValue>();
        void Value(GameEventUserValueType type, string value) {
            var stored = new GameEventUserValue(type, gameEvent.EndTime, EventId);
            stored.SetValue(value);
            values.Add(stored);
        }
        Value(GameEventUserValueType.BurningLevelupCharacter, picked ? test.CharacterId.ToString() : (test.CharacterId + 1).ToString());
        if (reached) {
            Value(GameEventUserValueType.BurningLevelupReached, "1");
        }
        if (taken) {
            Value(GameEventUserValueType.BurningLevelupRewardTaken, "1");
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveAccountEventValues(test.AccountId, values), Is.True);
        }
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private static void Claim(GameSession session, int eventId = EventId) {
        new EventRewardHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(15);
            writer.WriteInt(eventId);
        }));
    }

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    private static string? Value(GameSession session, GameEventUserValueType type) => session.GameEvent.Peek(type, EventId);

    private string? StoredValue(GameEventUserValueType type) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAccountEventValues(test.AccountId).FirstOrDefault(value => value.EventId == EventId && value.Type == type)?.Value;
    }

    [Test]
    public void Bonus_IsOnlyThePickedCharacters() {
        Assert.That(Login().GameEvent.BurningLevelupBonus(10), Is.EqualTo(2));
        Assert.That(Login(picked: false).GameEvent.BurningLevelupBonus(10), Is.EqualTo(0));
        Assert.That(Login(reached: true).GameEvent.BurningLevelupBonus(10), Is.EqualTo(0));
    }

    [Test]
    public void Reach_AtTheEndLevel_SetsAndStoresTheCompletion() {
        GameSession session = Login();

        session.GameEvent.BurningLevelupReach(69);
        Assert.That(Value(session, GameEventUserValueType.BurningLevelupReached), Is.Null.Or.EqualTo("0"));

        session.GameEvent.BurningLevelupReach(70);
        Assert.That(Value(session, GameEventUserValueType.BurningLevelupReached), Is.EqualTo("1"));
        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupReached), Is.EqualTo("1"));
        Assert.That(session.GameEvent.BurningLevelupEvent(), Is.Null);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Reach_OfACharacterNotPicked_SetsNothing() {
        GameSession session = Login(picked: false);

        session.GameEvent.BurningLevelupReach(70);

        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupReached), Is.Null);
    }

    [Test]
    public void Claim_GivesTheRewardsOnceAndAnswers() {
        GameSession session = Login(reached: true);

        Claim(session);

        Assert.That(Held(session, Reward), Is.EqualTo(1));
        Assert.That(session.Item.Inventory.Find(Reward).Single().Rarity, Is.EqualTo(6));
        Assert.That(Value(session, GameEventUserValueType.BurningLevelupRewardTaken), Is.EqualTo("1"));
        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupRewardTaken), Is.EqualTo("1"));
        Assert.That(Convert.ToHexString(test.TakeSent()[^1]), Is.EqualTo(Convert.ToHexString(BurningLevelupPacket.RewardTaken().ToArray())));
        Assert.That(test.Errors, Is.Empty);

        Claim(session);
        Assert.That(Held(session, Reward), Is.EqualTo(1));
    }

    [Test]
    public void Claim_BeforeTheEndLevel_IsRefused() {
        GameSession session = Login();

        Claim(session);

        Assert.That(Held(session, Reward), Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Claim_AfterTheRewardWasTaken_IsRefused() {
        GameSession session = Login(reached: true, taken: true);

        Claim(session);

        Assert.That(Held(session, Reward), Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Claim_ByACharacterNotPicked_IsRefused() {
        GameSession session = Login(picked: false, reached: true);

        Claim(session);

        Assert.That(Held(session, Reward), Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Claim_OfAnotherEvent_IsRefused() {
        GameSession session = Login(reached: true);

        Claim(session, EventId + 1);

        Assert.That(Held(session, Reward), Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }
    #endregion
}
