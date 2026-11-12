using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// CharacterAbility (c2s 0x89, s2c 0xDF) driven through the handler on a TestGameSession: learn (mode 2), reset
// (mode 1) and the GM learn-all (mode 3). The ability, quest and trophy ids are made up.
public class CharacterAbilityHandlerTests {
    private const int OpenQuest = 90000300;
    private const int ResetMeso = 300000;
    private const int CoolMinutes = 60;
    private const int LearnTrophy = 90000301;
    private const int ResetTrophy = 90000302;
    private const int Strength = 90000311; // category 1, level 50
    private const int Luck = 90000312;     // category 2, level 55
    private const int Haste = 90000313;    // category 2, level 60

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private readonly CharacterAbilityHandler handler = new();

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            CharacterAbilityOpenQuestID = OpenQuest,
            CharacterAbilityDefaultPoint = 1,
            CharacterAbilityResetMeso = ResetMeso,
            CharacterAbilityResetCoolTimeMinute = CoolMinutes,
        });
        test.SetTable(new CharacterAbilityTable(new Dictionary<int, CharacterAbilityTable.Entry> {
            [Strength] = new(Strength, 1, 50, 90000321, 1),
            [Luck] = new(Luck, 2, 55, 90000322, 1),
            [Haste] = new(Haste, 2, 60, 0, 0),
        }));
        test.AddAchievement(
            CategoryTrophy(LearnTrophy, 2),
            TestGameSession.Trophy(ResetTrophy, ConditionType.character_ability_reset, [1000]));
        test.Start();
        typeof(ConfigManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, session);
        // An empty effect store: the learned abilities' effects are looked up and not found, so no buff is made.
        var options = new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Field, new SkillMetadataStorage(new MetadataContext(options)));
        Character.Level = 55;
        test.SetQuest(OpenQuest, QuestState.Completed);
        test.TakeSent();
    }

    // Trophies 23100216-23100220 name the ability's category as the condition's target.
    private static AchievementMetadata CategoryTrophy(int id, int category) {
        var condition = new ConditionMetadata(ConditionType.character_ability_learn, 1000, null, new ConditionMetadata.Parameters(Integers: [category]));
        return new AchievementMetadata(id, $"{id}", false, AchievementCategory.Life, [],
            new Dictionary<int, AchievementMetadataGrade> { [1] = new(1, condition, null) }, false);
    }

    private Character Character => session.Player.Value.Character;

    private void Send(byte mode, int? abilityId = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            if (abilityId != null) {
                writer.WriteInt(abilityId.Value);
            }
        }));
    }

    private List<SendOp> Sent() {
        List<SendOp> ops = test.TakeSentOps();
        Assert.That(test.Errors, Is.Empty);
        return ops;
    }

    private static ByteReader Body(byte[] packet) {
        var reader = new ByteReader(packet, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.CharacterAbility));
        return reader;
    }

    #region Learn (mode 2)
    [Test]
    public void Learn_AddsTheAbilityAndAnswersWithItsId() {
        Send(CharacterAbilityRules.Learn, Strength);

        Assert.That(Character.Abilities, Is.EqualTo(new[] { Strength }));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.CharacterAbility }));
        ByteReader reader = Body(sent[0]);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(Strength));
        // Category 1 is not the trophy's code.
        Assert.That(test.TrophyCounter(LearnTrophy), Is.Null);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Learn_CountsTheTrophyOfTheCategory() {
        Send(CharacterAbilityRules.Learn, Luck);

        Assert.That(Character.Abilities, Is.EqualTo(new[] { Luck }));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.CharacterAbility, SendOp.Achieve }));
        Assert.That(test.TrophyCounter(LearnTrophy), Is.EqualTo(1));
    }

    [Test]
    public void Learn_RefusedWithoutTheOpenQuest() {
        test.SetQuest(OpenQuest, QuestState.Started);
        Send(CharacterAbilityRules.Learn, Strength);

        Assert.That(Character.Abilities, Is.Empty);
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void Learn_RefusedBelowTheOpenLevel() {
        Character.Level = 49;
        Send(CharacterAbilityRules.Learn, Strength);

        Assert.That(Character.Abilities, Is.Empty);
        Assert.That(Sent(), Is.Empty);
    }

    // Level 55: 5 level points and 1 default point. Not a table row, a level not reached, learned twice and
    // no point left are each refused without an answer.
    [Test]
    public void Learn_RefusedForUnknownHigherLearnedOrNoPoint() {
        Send(CharacterAbilityRules.Learn, 90000399);
        Send(CharacterAbilityRules.Learn, Haste);
        Assert.That(Character.Abilities, Is.Empty);
        Assert.That(Sent(), Is.Empty);

        Send(CharacterAbilityRules.Learn, Strength);
        test.TakeSent();
        Send(CharacterAbilityRules.Learn, Strength);
        Assert.That(Character.Abilities, Is.EqualTo(new[] { Strength }));
        Assert.That(Sent(), Is.Empty);

        Character.Abilities.AddRange([1, 2, 3, 4, 5]);
        Send(CharacterAbilityRules.Learn, Luck);
        Assert.That(Character.Abilities, Does.Not.Contain(Luck));
        Assert.That(Sent(), Is.Empty);
    }
    #endregion

    #region Reset (mode 1)
    [Test]
    public void Reset_TakesTheMesoClearsTheListAndSendsLoadThenTrophy() {
        Character.Abilities.AddRange([Strength, Luck]);
        session.Currency.Meso = ResetMeso + 5;
        test.TakeSent();
        long before = DateTime.Now.ToEpochSeconds();

        Send(CharacterAbilityRules.Reset);

        Assert.That(session.Currency.Meso, Is.EqualTo(5));
        Assert.That(Character.Abilities, Is.Empty);
        Assert.That(Character.AbilityResetTime, Is.GreaterThanOrEqualTo(before));
        // KMS2 order: the meso, the load packet (no mode 1 answer), the trophy.
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Meso, SendOp.CharacterAbility, SendOp.Achieve }));
        ByteReader reader = Body(sent[1]);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(Character.AbilityResetTime));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(test.TrophyCounter(ResetTrophy), Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Reset_RefusedWithoutTheMeso() {
        Character.Abilities.Add(Strength);
        session.Currency.Meso = ResetMeso - 1;
        test.TakeSent();

        Send(CharacterAbilityRules.Reset);

        Assert.That(Character.Abilities, Is.EqualTo(new[] { Strength }));
        Assert.That(session.Currency.Meso, Is.EqualTo(ResetMeso - 1));
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void Reset_RefusedWithNothingLearned() {
        session.Currency.Meso = ResetMeso;
        test.TakeSent();

        Send(CharacterAbilityRules.Reset);

        Assert.That(session.Currency.Meso, Is.EqualTo(ResetMeso));
        Assert.That(Character.AbilityResetTime, Is.EqualTo(0));
        Assert.That(Sent(), Is.Empty);
    }

    // The cool time runs from the last reset: refused one minute short of it, taken once it is over.
    [Test]
    public void Reset_WaitsForTheCoolTime() {
        Character.Abilities.Add(Strength);
        session.Currency.Meso = ResetMeso;
        long now = DateTime.Now.ToEpochSeconds();
        Character.AbilityResetTime = now - (CoolMinutes - 1) * 60;
        test.TakeSent();

        Send(CharacterAbilityRules.Reset);
        Assert.That(Character.Abilities, Is.EqualTo(new[] { Strength }));
        Assert.That(Sent(), Is.Empty);

        Character.AbilityResetTime = now - CoolMinutes * 60 - 1;
        Send(CharacterAbilityRules.Reset);
        Assert.That(Character.Abilities, Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(0));
    }
    #endregion

    #region LearnAll (mode 3, GM)
    [Test]
    public void LearnAll_DroppedForAPlayer() {
        Send(CharacterAbilityRules.LearnAll);

        Assert.That(Character.Abilities, Is.Empty);
        Assert.That(test.TakeSentOps(), Is.Empty);
    }

    // Every table ability not learned yet, in id order, then one load packet with the whole list; no trophy.
    [Test]
    public void LearnAll_GivesAGmEveryAbility() {
        session.Player.AdminPermissions = AdminPermissions.GameMaster;
        Character.Abilities.Add(Luck);

        Send(CharacterAbilityRules.LearnAll);

        Assert.That(Character.Abilities, Is.EqualTo(new[] { Luck, Strength, Haste }));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.CharacterAbility }));
        ByteReader reader = Body(sent[0]);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { Luck, Strength, Haste }));
        Assert.That(test.TrophyCounter(LearnTrophy), Is.Null);
        Assert.That(test.Errors, Is.Empty);
    }
    #endregion

    // The learned list and the reset time are columns of the character row.
    [Test]
    public void Save_KeepsTheListAndTheResetTime() {
        Character.Abilities.AddRange([Strength, Luck]);
        Character.AbilityResetTime = 1700000000;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveCharacter(Character), Is.True);
        }

        Character stored = test.StoredCharacter();
        Assert.That(stored.Abilities, Is.EqualTo(new[] { Strength, Luck }));
        Assert.That(stored.AbilityResetTime, Is.EqualTo(1700000000));
    }
}
