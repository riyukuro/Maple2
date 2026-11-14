using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Commands;
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
    private const int JumpingItem = 20001322;
    private const int TutorialEnd = 2000062;

    // The KR jumping row's shape on the test numbers: a level 30 potion given in the tutorial's end field.
    private static BurningLevelup JumpingData() {
        return Data(jumping: new BurningLevelup.JumpingOption(new RewardItem(JumpingItem, 3, 1), 30, TutorialEnd, "s_desc_popup", "s_notify", "s_complete"));
    }

    // GetBonusLevel of the level set: 10 lands on 12, 11 on 12, 12 stays; the end and the cap hold.
    [TestCase(5, 5)]
    [TestCase(9, 9)]
    [TestCase(10, 12)]
    [TestCase(11, 12)]
    [TestCase(12, 12)]
    [TestCase(67, 69)]
    [TestCase(70, 70)]
    public void Landing_IsTheLevelPlusItsBonus(int level, int landed) {
        Assert.That(BurningLevelupRules.Landing(Data(), level, 99), Is.EqualTo(landed));
        Assert.That(BurningLevelupRules.Landing(Data(), 10, 11), Is.EqualTo(11));
    }

    [Test]
    public void GivesJumpingItem_InTheTutorialEndFieldBelowTheTarget_Once() {
        Assert.That(BurningLevelupRules.GivesJumpingItem(JumpingData(), TutorialEnd, 1, false), Is.True);
        Assert.That(BurningLevelupRules.GivesJumpingItem(JumpingData(), TutorialEnd, 29, false), Is.True);
        Assert.That(BurningLevelupRules.GivesJumpingItem(JumpingData(), TutorialEnd, 30, false), Is.False);
        Assert.That(BurningLevelupRules.GivesJumpingItem(JumpingData(), TutorialEnd + 1, 1, false), Is.False);
        Assert.That(BurningLevelupRules.GivesJumpingItem(JumpingData(), TutorialEnd, 1, true), Is.False);
        Assert.That(BurningLevelupRules.GivesJumpingItem(Data(), TutorialEnd, 1, false), Is.False);
        BurningLevelup noField = JumpingData() with { Jumping = JumpingData().Jumping! with { TutorialEndFieldId = 0 } };
        Assert.That(BurningLevelupRules.GivesJumpingItem(noField, 0, 1, false), Is.False);
    }

    [TestCase(1, 30, true)]
    [TestCase(29, 31, true)]
    [TestCase(1, 29, false)]
    [TestCase(30, 31, false)]
    [TestCase(31, 20, false)]
    public void JumpsPast_IsTheCrossingOfTheTarget(int oldLevel, int newLevel, bool jumps) {
        Assert.That(BurningLevelupRules.JumpsPast(JumpingData(), oldLevel, newLevel), Is.EqualTo(jumps));
        Assert.That(BurningLevelupRules.JumpsPast(Data(), oldLevel, newLevel), Is.False);
    }

    [Test]
    public void ReleasesPick_OnlyThePickedCharacterBeforeTheEndAndTheJump() {
        Assert.That(BurningLevelupRules.ReleasesPick(JumpingData(), 7, 7, false, false), Is.True);
        Assert.That(BurningLevelupRules.ReleasesPick(JumpingData(), 7, 8, false, false), Is.False);
        Assert.That(BurningLevelupRules.ReleasesPick(JumpingData(), 0, 0, false, false), Is.False);
        Assert.That(BurningLevelupRules.ReleasesPick(JumpingData(), 7, 7, true, false), Is.False);
        Assert.That(BurningLevelupRules.ReleasesPick(JumpingData(), 7, 7, false, true), Is.False);
        Assert.That(BurningLevelupRules.ReleasesPick(Data(), 7, 7, false, true), Is.True, "a 2004 without a jumping option means nothing");
    }

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
    public void Reader_WithoutRewardItems_KeepsNoTargetsAndNoJumpingData() {
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
    // picked: the account's 2000 value is this character; reached, taken, notified, jumped: 2001-2004 are set;
    // setup runs before the login (items stored in the bag).
    private GameSession Login(bool picked = true, bool reached = false, bool taken = false, BurningLevelup? data = null,
                              bool notified = false, bool jumped = false, Action<TestGameSession>? setup = null) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Reward), Potion(TransferType.BindOnLoot, "<v targetLevel=\"30\"/>"));
        setup?.Invoke(test);
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
        if (notified) {
            Value(GameEventUserValueType.BurningLevelupJumpingNotify, "1");
        }
        if (jumped) {
            Value(GameEventUserValueType.BurningLevelupJumpingDone, "1");
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveAccountEventValues(test.AccountId, values), Is.True);
        }
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { characterMaxLevel = 99 });
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

        session.GameEvent.BurningLevelupReach(68, 69);
        Assert.That(Value(session, GameEventUserValueType.BurningLevelupReached), Is.Null.Or.EqualTo("0"));

        session.GameEvent.BurningLevelupReach(69, 70);
        Assert.That(Value(session, GameEventUserValueType.BurningLevelupReached), Is.EqualTo("1"));
        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupReached), Is.EqualTo("1"));
        Assert.That(session.GameEvent.BurningLevelupEvent(), Is.Null);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Reach_OfACharacterNotPicked_SetsNothing() {
        GameSession session = Login(picked: false);

        session.GameEvent.BurningLevelupReach(69, 70);

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

    // The jumping item (EventReward 0x11, GameEventBurningLevelupCli::FUN_140ce7250): on entering the tutorial's end
    // field the picked character gets the item once, 2003 is set and stored, and 0x11 + the event id opens the
    // client's notify box.
    [Test]
    public void FieldEnter_OfTheTutorialEndField_GivesTheJumpingItemOnce() {
        GameSession session = Login(data: JumpingData());
        test.EnterMap(TestGameSession.Map(TutorialEnd));

        session.GameEvent.BurningLevelupFieldEnter();

        Assert.That(Held(session, JumpingItem), Is.EqualTo(1));
        Assert.That(session.Item.Inventory.Find(JumpingItem).Single().Rarity, Is.EqualTo(3));
        Assert.That(Value(session, GameEventUserValueType.BurningLevelupJumpingNotify), Is.EqualTo("1"));
        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupJumpingNotify), Is.EqualTo("1"));
        Assert.That(Convert.ToHexString(test.TakeSent()[^1]), Is.EqualTo("F700" + "11" + "6ACC0500"));
        Assert.That(test.Errors, Is.Empty);

        session.GameEvent.BurningLevelupFieldEnter();
        Assert.That(Held(session, JumpingItem), Is.EqualTo(1));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [TestCase("field")]
    [TestCase("not picked")]
    [TestCase("level")]
    [TestCase("notified")]
    [TestCase("no option")]
    [TestCase("jumped")]
    public void FieldEnter_Otherwise_GivesNothing(string why) {
        GameSession session = Login(picked: why != "not picked", data: why == "no option" ? Data() : JumpingData(), notified: why == "notified",
            jumped: why == "jumped");
        test.EnterMap(TestGameSession.Map(why == "field" ? TutorialEnd + 1 : TutorialEnd));
        if (why == "level") {
            session.Player.Value.Character.Level = 30;
        }

        session.GameEvent.BurningLevelupFieldEnter();

        Assert.That(Held(session, JumpingItem), Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    private Item StorePotion(TestGameSession session, int targetLevel) {
        return session.Store(TestGameSession.Metadata(20301234,
            function: new ItemMetadataFunction(ItemFunction.LevelPotion, "LevelPotion", $"<v targetLevel=\"{targetLevel}\"/>", false), type: 2));
    }

    // In a field that is not a tutorial's (the session's own field has no properties).
    private void UsePotion(GameSession session, Item potion) {
        if (session.Field!.Metadata.Property == null) {
            test.EnterMap(TestGameSession.Map(2000062));
        }
        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteLong(potion.Uid)));
    }

    // A potion to a level whose GetBonusLevel is not 0 lands where a level up by exp would: 10 lands on 12.
    [TestCase(10, 12)]
    [TestCase(11, 12)]
    [TestCase(12, 12)]
    public void Potion_LandsOnTheBonusLevel(int target, int landed) {
        Item potion = null!;
        GameSession session = Login(setup: t => potion = StorePotion(t, target));

        UsePotion(session, potion);

        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(landed));
        Assert.That(test.World.PlayerUpdates.Select(update => update.Level), Is.EqualTo(new[] { landed }));
    }

    [Test]
    public void Potion_OfACharacterNotPicked_AddsNoBonus() {
        Item potion = null!;
        GameSession session = Login(picked: false, setup: t => potion = StorePotion(t, 10));

        UsePotion(session, potion);

        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(10));
    }

    // The jumping potion (the KR item's own function): its use takes the level past the target, which is the jumping
    // done (2004): the client shows its jumpingComplete box on the same level change.
    [Test]
    public void ThePotionPastTheJumpingTarget_IsTheJumpingDone() {
        Item potion = null!;
        GameSession session = Login(data: JumpingData(), setup: t => potion = StorePotion(t, 30));

        UsePotion(session, potion);

        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(30));
        Assert.That(Value(session, GameEventUserValueType.BurningLevelupJumpingDone), Is.EqualTo("1"));
        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupJumpingDone), Is.EqualTo("1"));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void ALevelUpPastTheJumpingTarget_IsTheJumpingDone() {
        GameSession session = Login(data: JumpingData());

        session.GameEvent.BurningLevelupReach(29, 31);

        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupJumpingDone), Is.EqualTo("1"));
    }

    [Test]
    public void ALevelChangeNotCrossingTheTarget_IsNoJump() {
        GameSession session = Login(data: JumpingData());

        session.GameEvent.BurningLevelupReach(30, 33);
        session.GameEvent.BurningLevelupReach(1, 29);

        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupJumpingDone), Is.Null);
    }

    // The client refuses a potion to the character's level or below (s_err_cannot_already_target_level_potion), in a
    // tutorial field (s_tutorial_levelup_item_not_use) and while dead (unit_140d1ae30_itemfunctiondispatch.cpp:1567):
    // the server keeps the potion and the level.
    [TestCase("level")]
    [TestCase("below")]
    [TestCase("tutorial")]
    [TestCase("dead")]
    public void Potion_TheClientRefuses_IsKept(string why) {
        Item potion = null!;
        GameSession session = Login(setup: t => potion = StorePotion(t, 20));
        session.Player.Value.Character.Level = why == "below" ? (short) 25 : why == "level" ? (short) 20 : (short) 5;
        if (why == "tutorial") {
            MapMetadata map = TestGameSession.Map(52000001);
            test.EnterMap(map with { Property = map.Property with { TutorialType = 1 } });
        }
        if (why == "dead") {
            typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(session.Player, true);
        }
        short level = session.Player.Value.Character.Level;

        UsePotion(session, potion);

        Assert.That(session.Item.Inventory.Get(potion.Uid)?.Amount, Is.EqualTo(1));
        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(level));
        Assert.That(test.TakeSentOps(), Does.Not.Contain(SendOp.LevelUp));
    }

    // The tables GM `level` reads (the prestige stat point recount).
    private static void GmTables(TestGameSession t) {
        t.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        t.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
    }

    // GM `level` lands where a level up would, too, and says the level it set.
    [Test]
    public void GmLevel_LandsOnTheBonusLevel() {
        GameSession session = Login(setup: GmTables);
        PacketHandlers.ProgressionOrderTests.LoadConfig(test, session);
        var console = new TestConsole();

        Assert.That(new PlayerCommand(session, null!).Invoke(["level", "10"], console), Is.EqualTo(0), console.Error.ToString());

        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(12));
        Assert.That(console.Out.ToString(), Does.Contain("12"));
    }

    [Test]
    public void GmLevel_PastTheJumpingTarget_IsTheJumpingDone() {
        GameSession session = Login(data: JumpingData(), setup: GmTables);
        PacketHandlers.ProgressionOrderTests.LoadConfig(test, session);

        Assert.That(new PlayerCommand(session, null!).Invoke(["level", "30"], new TestConsole()), Is.EqualTo(0));

        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupJumpingDone), Is.EqualTo("1"));
    }
    #endregion
}
