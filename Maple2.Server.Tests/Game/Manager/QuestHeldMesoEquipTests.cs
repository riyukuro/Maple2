using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

// meso_donation and equip_exist count what the player has, as the client shows it (CQuestManager::UpdateQuestProgress):
// the meso held, and the distinct listed items worn. Both count from the quest's start, go up and down, and stop at
// the condition's value.
public class QuestHeldMesoEquipTests {
    private const int FundingQuest = 93000139;
    private const int OutfitQuest = 95000000;
    private const int Cap = 11300001;
    private const int Cap2 = 11300002;
    private const int Coat = 11400001;

    private TestGameSession test = null!;
    private GameSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
    }

    private static QuestMetadata Funding() => TestGameSession.QuestData(FundingQuest) with {
        Conditions = [new ConditionMetadata(ConditionType.meso_donation, 5000, null, null)],
    };

    // Two of the three listed items worn complete it.
    private static QuestMetadata Outfit() => TestGameSession.QuestData(OutfitQuest) with {
        Conditions = [new ConditionMetadata(ConditionType.equip_exist, 2, new ConditionMetadata.Parameters(Integers: [Cap, Cap2, Coat]), null)],
    };

    private Quest Accept(QuestMetadata metadata) {
        test.Register(metadata);
        Assume.That(session.Quest.Start(metadata.Id, bypassRequirements: true), Is.EqualTo(QuestError.none));
        Assume.That(session.Quest.TryGetQuest(metadata.Id, out Quest? quest), Is.True);
        test.TakeSent();
        return quest!;
    }

    private string[] Updates() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Quest).Select(Convert.ToHexString).ToArray();
    private static string Hex(Quest quest) => Convert.ToHexString(QuestPacket.Update(quest).ToArray());

    [TestCase(0, 0)]
    [TestCase(3000, 3000)]
    [TestCase(5000, 5000)]
    [TestCase(80000, 5000)]
    public void Funding_StartCountsTheMeso(long meso, int counter) {
        test.Start();
        session = test.Session;
        session.Player.Value.Currency.Meso = meso;

        Quest quest = Accept(Funding());

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(counter));
    }

    [Test]
    public void Funding_GainAndSpendMoveTheCount() {
        test.Start();
        session = test.Session;
        Quest quest = Accept(Funding());

        session.Currency.Meso += 4000;
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(4000));
        Assert.That(Updates(), Is.EqualTo(new[] { Hex(quest) }));

        session.Currency.AddMeso(2000, ExpMessageCode.s_msg_take_exp);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(5000));
        Assert.That(Updates(), Is.EqualTo(new[] { Hex(quest) }));

        // 6000 to 5500 stays at the value: no update; spending below it takes a met count back.
        session.Currency.Meso -= 500;
        Assert.That(Updates(), Is.Empty);
        session.Currency.Meso -= 1500;
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(4000));
        Assert.That(Updates(), Is.EqualTo(new[] { Hex(quest) }));
    }

    // The completion takes no meso (no field of the funding quests says it does).
    [Test]
    public void Funding_CompleteKeepsTheMeso() {
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.Start();
        session = test.Session;
        session.Currency.Meso = 7000;
        Quest quest = Accept(Funding());

        Assert.That(session.Quest.Complete(quest), Is.True);

        Assert.That(session.Currency.Meso, Is.EqualTo(7000));
    }

    [Test]
    public void Outfit_StartCountsTheWornItems() {
        test.Store(TestGameSession.Metadata(Cap, EquipSlot.CP, slotMax: 1), group: ItemGroup.Outfit, slot: EquipSlot.CP);
        test.Store(TestGameSession.Metadata(Coat, EquipSlot.CL, slotMax: 1), group: ItemGroup.Gear, slot: EquipSlot.CL);
        test.Start();
        session = test.Session;

        Quest quest = Accept(Outfit());

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(2));
    }

    [Test]
    public void Outfit_EquipAndUnequipMoveTheCount() {
        var cap = test.Store(TestGameSession.Metadata(Cap, EquipSlot.CP, slotMax: 1));
        var other = test.Store(TestGameSession.Metadata(Cap + 10, EquipSlot.CP, slotMax: 1));
        test.Start();
        session = test.Session;
        Quest quest = Accept(Outfit());

        Assert.That(session.Item.Equips.Equip(cap.Uid, EquipSlot.CP, isSkin: true), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
        Assert.That(Updates(), Is.EqualTo(new[] { Hex(quest) }));

        // An unlisted item in its place takes the count down.
        Assert.That(session.Item.Equips.Equip(other.Uid, EquipSlot.CP, isSkin: true), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Assert.That(Updates(), Is.EqualTo(new[] { Hex(quest) }));

        Assert.That(session.Item.Equips.Unequip(other.Uid), Is.True);
        Assert.That(Updates(), Is.Empty);
    }

    [Test]
    public void Outfit_LoginCountsAgain() {
        test.Store(TestGameSession.Metadata(Cap, EquipSlot.CP, slotMax: 1), group: ItemGroup.Outfit, slot: EquipSlot.CP);
        test.Start();
        session = test.Session;
        Quest quest = test.StartQuest(Outfit());
        quest.Conditions[0].Counter = 2;

        session.Quest.Load();

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }

    [Test]
    public void EquippedCount_DistinctListedIds() {
        ConditionMetadata condition = Outfit().Conditions[0];
        Assert.Multiple(() => {
            Assert.That(QuestRules.EquippedCount(condition, [Cap, Cap, Coat, 12345]), Is.EqualTo(2));
            Assert.That(QuestRules.EquippedCount(condition, []), Is.EqualTo(0));
            Assert.That(QuestRules.EquippedCount(new ConditionMetadata(ConditionType.equip_exist, 1, null, null), [Cap]), Is.EqualTo(0));
            Assert.That(QuestRules.HeldCounter(condition, QuestRules.EquippedCount(condition, [Cap, Cap2, Coat])), Is.EqualTo(2));
        });
    }

    [Test]
    public void IsHeldCount_OfTheThreeKinds() {
        Assert.Multiple(() => {
            Assert.That(QuestRules.IsHeldCount(Funding().Conditions[0]), Is.True);
            Assert.That(QuestRules.IsHeldCount(Outfit().Conditions[0]), Is.True);
            Assert.That(QuestRules.IsHeldCount(new ConditionMetadata(ConditionType.equip_exist, 1, null, null)), Is.False);
            Assert.That(QuestRules.IsHeldCount(new ConditionMetadata(ConditionType.meso, 1, null, null)), Is.False);
        });
    }
}
