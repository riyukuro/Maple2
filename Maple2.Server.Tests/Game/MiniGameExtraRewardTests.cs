using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// mini_game_give_reward on a TestGameSession: the user-opened games' extra reward (content 1008) once per game and
// userMassiveExtraRewardMax times a day; other content as the script calls it.
public class MiniGameExtraRewardTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string UserOpen = "UserOpenMiniGameExtraReward";
    private const int UserOpenReward = 1008, MiniGameReward = 1005, Meso = 700;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Start(bool rewardRow = true, int maxPerDay = 5) {
        test = new TestGameSession();
        test.SetServerTable(new ConstantsTable(
            bagSlotTabGameCount: [48, 48], bagSlotTabSkinCount: [48, 48], bagSlotTabSummonCount: [48, 48],
            bagSlotTabMaterialCount: [48, 48], bagSlotTabMasteryCount: [48, 48], bagSlotTabLifeCount: [48, 48],
            bagSlotTabQuestCount: [48, 48], bagSlotTabGemCount: [48, 48], bagSlotTabPetCount: [48, 48],
            bagSlotTabActiveSkillCount: [48, 48], bagSlotTabCoinCount: [48, 48], bagSlotTabBadgeCount: [48, 48],
            bagSlotTabMiscCount: [48, 48], bagSlotTabLapenShardCount: [48, 48], bagSlotTabPieceCount: [48, 48],
            userMassiveExtraRewardMax: maxPerDay));
        var rows = new Dictionary<int, RewardContentTable.Base> {
            [MiniGameReward] = new(MiniGameReward, 100001, 0, 0f, 0f, 0, 0),
        };
        if (rewardRow) {
            rows[UserOpenReward] = new(UserOpenReward, 100001, 0, 0f, 0f, 0, 0);
        }
        test.SetTable(new RewardContentTable(rows, new Dictionary<int, RewardContentTable.Item>(),
            new Dictionary<int, long> { [100001] = Meso }, new Dictionary<int, Dictionary<int, long>>(), new Dictionary<int, long>()));
        test.Start();
        test.TakeSent();
    }

    private TriggerContext Context() {
        var owner = (FieldTrigger) RuntimeHelpers.GetUninitializedObject(typeof(FieldTrigger));
        GC.SuppressFinalize(owner);
        typeof(FieldEntity<TriggerModel>).GetField("<Field>k__BackingField", Any)!.SetValue(owner, session.Field);
        typeof(FieldEntity<TriggerModel>).GetField("<Value>k__BackingField", Any)!.SetValue(owner, new TriggerModel(1, "main", Vector3.Zero, Vector3.Zero));
        if (session.Field!.TriggerObjects == null) {
            var objects = (TriggerCollection) RuntimeHelpers.GetUninitializedObject(typeof(TriggerCollection));
            typeof(TriggerCollection).GetField(nameof(TriggerCollection.Boxes), Any)!.SetValue(objects, new Dictionary<int, TriggerBox>());
            typeof(FieldManager).GetField(nameof(FieldManager.TriggerObjects), Any)!.SetValue(session.Field, objects);
        }
        TriggerContext context = TriggerTestBase.UninitializedContext(owner);
        return context;
    }

    private void StartRecord() {
        session.MiniGameRecord = new MiniGameUserRecord(session.CharacterId) { TotalRounds = 1, ShowResult = true, MinRound = 1 };
    }

    // Winner box 0 is every player of the field.
    private void GiveReward(string contentType) => Context().MiniGameGiveReward(0, contentType, "");

    private List<byte[]> Notices() => test.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == SendOp.PlayerHost && packet[2] == 5).ToList();

    private Character Character => session.Player.Value.Character;

    [Test]
    public void UserOpen_PaysOncePerGame() {
        Start();
        StartRecord();
        long before = session.Currency.Meso;

        GiveReward(UserOpen);
        GiveReward(UserOpen);

        Assert.That(session.Currency.Meso - before, Is.EqualTo(Meso));
        Assert.That(session.MiniGameRecord!.Rewards[DungeonRewardType.Meso], Is.EqualTo(Meso));
        Assert.That(Character.MassiveExtraRewardCount, Is.EqualTo(1));
        Assert.That(Notices(), Has.Count.EqualTo(1));
    }

    [Test]
    public void UserOpen_SixthClearOfTheDayPaysNothing() {
        Start();
        long before = session.Currency.Meso;
        for (int game = 0; game < 6; game++) {
            StartRecord();
            GiveReward(UserOpen);
        }

        Assert.That(session.Currency.Meso - before, Is.EqualTo(5 * Meso));
        Assert.That(Character.MassiveExtraRewardCount, Is.EqualTo(5));
        Assert.That(session.MiniGameRecord!.Rewards[DungeonRewardType.Meso], Is.EqualTo(0));
        Assert.That(Notices(), Has.Count.EqualTo(5));
    }

    // A player who was not in the box at the game's start, or who left the field, has no record.
    [Test]
    public void NoRecord_NotPaid() {
        Start();
        long before = session.Currency.Meso;

        GiveReward(UserOpen);

        Assert.That(session.Currency.Meso, Is.EqualTo(before));
        Assert.That(Character.MassiveExtraRewardCount, Is.EqualTo(0));
        Assert.That(Notices(), Is.Empty);
    }

    [Test]
    public void NoRewardRow_NoCountAndNoNotice() {
        Start(rewardRow: false);
        StartRecord();

        GiveReward(UserOpen);

        Assert.That(Character.MassiveExtraRewardCount, Is.EqualTo(0));
        Assert.That(Notices(), Is.Empty);
    }

    // Other content keeps paying each call; the scripts call it once per game, right before end_mini_game.
    [Test]
    public void OtherContent_NotLimitedByTheFlag() {
        Start();
        StartRecord();
        long before = session.Currency.Meso;

        GiveReward("miniGame");
        GiveReward("miniGame");

        Assert.That(session.Currency.Meso - before, Is.EqualTo(2 * Meso));
        Assert.That(Character.MassiveExtraRewardCount, Is.EqualTo(0));
        Assert.That(Notices(), Is.Empty);
    }

    // mini_game_give_reward (15 KR uses) as the scripts write it: the winners are the players in winner_box_id
    // (box 301 here, as in 9 of the uses' games), paid the content's reward; a player outside the box is not.
    [Test]
    public void ScriptLine_PaysThePlayersInTheWinnerBox() {
        Start();
        StartRecord();
        TriggerContext context = Context();
        ((Dictionary<int, TriggerBox>) session.Field!.TriggerObjects.Boxes)[301] = new TriggerBox(new Ms2TriggerBox(Vector3.Zero, new Vector3(300, 300, 300), 301, true));
        var document = new XmlDocument();
        document.LoadXml("""<action name="mini_game_give_reward" winner_box_id="301" content_type="miniGame" />""");
        IAction action = TriggerFunctionMapping.ActionMap["mini_game_give_reward"](document.DocumentElement!.Attributes);
        long before = session.Currency.Meso;

        session.Player.Position = Vector3.Zero;
        action.Execute(context);
        Assert.That(session.Currency.Meso - before, Is.EqualTo(Meso));

        session.Player.Position = new Vector3(5000, 0, 0);
        action.Execute(context);
        Assert.That(session.Currency.Meso - before, Is.EqualTo(Meso));
    }
}
