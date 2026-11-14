using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Mastery 1 (grade reward) and 2 (craft), Fishing 0 in a house, and the end of an improvisation.
public class LifeSkillRefusalTests {
    private const int RewardItemId = 30000001;
    private const int InstrumentItemId = 34000001;

    private TestGameSession test = null!;

    // Mining: grade 1 at 0 without an item, grade 2 at 50 with one item.
    private GameSession StartMastery(int materialId = 0) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardItemId));
        if (materialId != 0) {
            test.Store(TestGameSession.Metadata(materialId, slotMax: 100), amount: 10);
        }
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>> {
            [MasteryType.Mining] = new Dictionary<int, MasteryRewardTable.Entry> {
                [1] = new(0, 0, 0, 0),
                [2] = new(50, RewardItemId, 1, 3),
            },
        }));
        test.SetTable(new MasteryRecipeTable(new Dictionary<int, MasteryRecipeTable.Entry>()));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();
        return session;
    }

    private MasteryHandler Handler(GameSession session) {
        return new MasteryHandler {
            ItemMetadata = session.ItemMetadata,
            TableMetadata = test.TableMetadata,
        };
    }

    private void Send(GameSession session, byte command, int id) {
        Handler(session).Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteInt(id);
        }));
    }

    // Mastery 3: i16 error.
    private static void AssertMasteryError(List<byte[]> sent, MasteryError error) {
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(SendOp.Mastery));
        Assert.That(sent[0], Has.Length.EqualTo(5));
        Assert.That(sent[0][2], Is.EqualTo(3));
        Assert.That(sent[0][3] | sent[0][4] << 8, Is.EqualTo((int) error));
    }

    private static int MiningId(int grade) => (int) MasteryType.Mining * 1000 + grade;

    [Test]
    public void Reward_OfAGradeIsGivenOnce() {
        GameSession session = StartMastery();
        session.Player.Value.Character.Mastery.Mining = 50;

        Send(session, 1, MiningId(2));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Exists(packet => TestGameSession.Op(packet) == SendOp.Mastery && packet[2] == 1), Is.True);
        Assert.That(session.Player.Value.Unlock.MasteryRewardsClaimed.ContainsKey(MiningId(2)), Is.True);

        Send(session, 1, MiningId(2));
        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_unknown);
    }

    [Test]
    public void Reward_BelowTheGradeValueIsRefused() {
        GameSession session = StartMastery();
        session.Player.Value.Character.Mastery.Mining = 49;

        Send(session, 1, MiningId(2));

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_invalid_level);
        Assert.That(session.Player.Value.Unlock.MasteryRewardsClaimed, Is.Empty);
    }

    // The id is type * 1000 + grade: 3102 is grade 102 (not in the table), never grade 2 a second time.
    [TestCase(102)]
    [TestCase(1)] // a grade without an item
    [TestCase(3)] // no such grade
    public void Reward_OfAGradeWithoutItemIsRefused(int grade) {
        GameSession session = StartMastery();
        session.Player.Value.Character.Mastery.Mining = 5000;

        Send(session, 1, MiningId(grade));

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_unknown);
        Assert.That(session.Player.Value.Unlock.MasteryRewardsClaimed, Is.Empty);
    }

    [TestCase(-1)]
    [TestCase(2)] // a type that does not exist (0) with grade 2
    public void Reward_OfAnIdOutsideTheTableIsRefused(int id) {
        GameSession session = StartMastery();
        session.Player.Value.Character.Mastery.Mining = 5000;

        Send(session, 1, id);

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_unknown);
    }

    private const int RecipeId = 70000001;
    private const int QuestId = 90000710;

    // A smithing tutorial recipe like the KR table's: the quest or a level asked, one item.
    private GameSession StartRecipe(int questId, short requiredLevel, MasteryType type = MasteryType.Blacksmithing, int rewardMastery = 0,
                                    long requiredMeso = 0, int materialId = 0) {
        GameSession session = StartMastery(materialId);
        test.SetTable(new MasteryRecipeTable(new Dictionary<int, MasteryRecipeTable.Entry> {
            [RecipeId] = new(RecipeId, type, NoRewardExp: rewardMastery == 0, RequiredMastery: 0, RequiredMeso: requiredMeso,
                RequiredQuests: questId == 0 ? [] : [questId], RequiredLevel: requiredLevel, RewardExp: 0, RewardMastery: rewardMastery,
                HighRateLimitCount: 5, NormalRateLimitCount: 10,
                RequiredItems: materialId == 0 ? [] : [new ItemComponent(materialId, -1, 1, ItemTag.None)], HabitatMapId: [],
                RewardItems: [new ItemComponent(RewardItemId, 1, 1, ItemTag.None)]),
        }));
        // A craft adds manufacturing experience.
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.manufacturing] = new(1, 1f) }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, _ => 100L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        return session;
    }

    private static int Count(GameSession session, int itemId) {
        return session.Item.Inventory.Find(itemId).Sum(item => item.Amount);
    }

    // The client crafts only while a required quest is in progress (IsQuestActive): a completed one or none
    // is refused with error 3 and gives nothing.
    [TestCase(QuestState.Completed)]
    [TestCase(QuestState.None)]
    public void Craft_NeedsTheQuestInProgress(QuestState state) {
        GameSession session = StartRecipe(QuestId, 0);
        if (state != QuestState.None) {
            test.SetQuest(QuestId, state);
        }

        Send(session, 2, RecipeId);

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_lack_quest);
        Assert.That(Count(session, RewardItemId), Is.EqualTo(0));
    }

    [Test]
    public void Craft_WithTheQuestInProgressGivesTheItem() {
        GameSession session = StartRecipe(QuestId, 0);
        test.SetQuest(QuestId, QuestState.Started);

        Send(session, 2, RecipeId);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(Count(session, RewardItemId), Is.EqualTo(1));
    }

    // A spend on another thread while the material leaves: the fee was held at the check, so the craft is done and
    // only what was left could go. Before, the fee was taken after the material; that take threw on the short
    // balance, and the material was gone with no craft.
    [Test]
    public void Craft_ASpendWhileTheMaterialLeavesCannotSkipTheFee() {
        const int materialId = 30000002;
        GameSession session = StartRecipe(0, 0, requiredMeso: 1000, materialId: materialId);
        session.Currency.Meso = 1500;
        test.TakeSent();
        long spent = -1;
        test.OnSend = packet => {
            if (spent >= 0 || TestGameSession.Op(packet) != SendOp.ItemInventory) {
                return;
            }
            long left = session.Currency.Meso;
            spent = session.Currency.TryTakeMeso(left, notify: false) ? left : 0;
        };

        Assert.DoesNotThrow(() => Send(session, 2, RecipeId));

        Assert.That(spent, Is.EqualTo(500));
        Assert.That(session.Currency.Meso, Is.Zero);
        Assert.That(Count(session, materialId), Is.EqualTo(9));
        Assert.That(Count(session, RewardItemId), Is.EqualTo(1));
    }

    // The material cannot be taken (locked): the craft is refused and the fee stays.
    [Test]
    public void Craft_WithTheMaterialLockedKeepsTheFee() {
        const int materialId = 30000002;
        GameSession session = StartRecipe(0, 0, requiredMeso: 1000, materialId: materialId);
        session.Currency.Meso = 1500;
        foreach (Item material in session.Item.Inventory.Find(materialId)) {
            material.IsLocked = true;
        }
        test.TakeSent();

        Send(session, 2, RecipeId);

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_lack_item);
        Assert.That(session.Currency.Meso, Is.EqualTo(1500));
    }

    // The craft dialog greys out a recipe above the player's level; the server answers error 7.
    [TestCase((short) 49, 0)]
    [TestCase((short) 50, 1)]
    public void Craft_NeedsTheRecipesLevel(short level, int expected) {
        GameSession session = StartRecipe(0, 50);
        session.Player.Value.Character.Level = level;

        Send(session, 2, RecipeId);

        if (expected == 0) {
            AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_invalid_level);
        }
        Assert.That(Count(session, RewardItemId), Is.EqualTo(expected));
    }

    // A craft gives the mastery the craft dialog shows: 30 raised by the type's ability (100 = 10%), and the
    // mastery update carries it. Another type's ability does nothing.
    [TestCase(MasteryType.Blacksmithing, SpecialAttribute.SmithingRewardMastery, 33)]
    [TestCase(MasteryType.Cooking, SpecialAttribute.CookingRewardMastery, 33)]
    [TestCase(MasteryType.Cooking, SpecialAttribute.SmithingRewardMastery, 30)]
    public void Craft_MasteryIsRaisedByTheTypesAbility(MasteryType type, SpecialAttribute ability, int expected) {
        GameSession session = StartRecipe(0, 0, type, rewardMastery: 30);
        session.Stats.Values[ability].AddTotal(100);

        Send(session, 2, RecipeId);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.Mastery[type], Is.EqualTo(expected));
        Assert.That(test.TakeSent().Exists(packet => TestGameSession.Op(packet) == SendOp.Mastery && packet[2] == 0
            && packet[3] == (byte) type && BitConverter.ToInt32(packet, 4) == expected), Is.True);
    }

    [Test]
    public void Craft_OfAnUnknownRecipeIsRefused() {
        GameSession session = StartMastery();

        Send(session, 2, 12345);

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_unknown);
    }

    // The craft dialog never sends a craft of a gathering recipe (it has no craft on the gathering tab), and
    // one costs nothing: refused as an unknown recipe, with no item and no mastery. Before, a mining recipe
    // sent as a craft gave its ore and mastery without the object.
    [TestCase(MasteryType.Mining)]
    [TestCase(MasteryType.Gathering)]
    [TestCase(MasteryType.Breeding)]
    [TestCase(MasteryType.Farming)]
    public void Craft_OfAGatheringRecipeIsRefused(MasteryType type) {
        GameSession session = StartRecipe(0, 0, type, rewardMastery: 30);

        Send(session, 2, RecipeId);

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_unknown);
        Assert.That(Count(session, RewardItemId), Is.EqualTo(0));
        Assert.That(session.Mastery[type], Is.EqualTo(0));
    }

    // Fishing 2: i16 error. The client refuses by itself in a house (error 8).
    [Test]
    public void Fishing_InAHouseIsRefused() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterHome();
        session.Fishing = new FishingManager(session, test.TableMetadata, test.ServerTableMetadata);
        test.TakeSent();

        new FishingHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(1);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(SendOp.Fishing));
        Assert.That(sent[0][2], Is.EqualTo(2));
        Assert.That(sent[0][3] | sent[0][4] << 8, Is.EqualTo((int) FishingError.s_fishing_error_ugcmap));
        Assert.That(session.GuideObject, Is.Null);
    }

    private static int FieldInstruments(GameSession session) {
        FieldInfo field = typeof(FieldManager).GetField("fieldInstruments", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((ICollection) field.GetValue(session.Field)!).Count;
    }

    // PlayInstrument 4 ends a score; an improvisation ends with 2, gives nothing and leaves no instrument behind.
    [Test]
    public void Improvisation_EndsWithItsOwnStopAndGivesNothing() {
        test = new TestGameSession();
        ItemMetadata instrument = TestGameSession.Metadata(InstrumentItemId,
            function: new ItemMetadataFunction(ItemFunction.OpenInstrument, "OpenInstrument", "6", false), type: 6);
        test.Register(instrument);
        test.SetTable(new InstrumentTable(new Dictionary<int, InstrumentMetadata> {
            [6] = new(6, 0, 3, 1, 0, 0),
        }));
        Item item = test.Store(instrument);
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        var handler = new InstrumentHandler {
            TableMetadata = test.TableMetadata,
            BanWordStorage = null!,
        };

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(item.Uid);
        }));
        Assert.That(session.Instrument, Is.Not.Null);
        Assert.That(session.Instrument!.Improvising, Is.True);
        Assert.That(FieldInstruments(session), Is.EqualTo(1));
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(4)));
        Assert.That(session.Instrument, Is.Not.Null);
        Assert.That(session.Player.Value.Character.Mastery.Instrument, Is.EqualTo(0));
        Assert.That(test.TakeSentOps(), Has.None.EqualTo(SendOp.ExpUp));

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(2)));
        Assert.That(session.Instrument, Is.Null);
        Assert.That(FieldInstruments(session), Is.EqualTo(0));
        Assert.That(session.Player.Value.Character.Mastery.Instrument, Is.EqualTo(0));
    }
}
