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

// item_exist counts the item the player holds, as the client shows it (CQuestManager::UpdateQuestProgress): items held
// before the start count, an added stack counts what it adds (not its whole amount again), and a removed or used item
// takes the count down, also below a met count. The count stops at the condition's value.
public class QuestHeldItemTests {
    private const int QuestItem = 30000043;
    private const int QuestId = 10001270;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private ItemMetadata item = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        item = TestGameSession.Metadata(QuestItem, slotMax: 999);
        test.Register(item);
        // What a quest start reads: no alliance missions.
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
    }

    // The quest that needs 15 of the item (the monsters of map 2000136 drop it).
    private static QuestMetadata Data() => TestGameSession.QuestData(QuestId) with {
        Conditions = [new ConditionMetadata(ConditionType.item_exist, 15, new ConditionMetadata.Parameters(Integers: [QuestItem]),
            new ConditionMetadata.Parameters(Integers: [2000136]))],
    };

    // Logs in holding the amount, then accepts the quest.
    private Quest Accept(int held) {
        if (held > 0) {
            test.Store(item, held);
        }
        test.Start();
        session = test.Session;
        test.Register(Data());
        Assume.That(session.Item.Inventory.Find(QuestItem).Sum(entry => entry.Amount), Is.EqualTo(held));
        Assume.That(session.Quest.Start(QuestId, bypassRequirements: true), Is.EqualTo(QuestError.none));
        Assume.That(session.Quest.TryGetQuest(QuestId, out Quest? quest), Is.True);
        test.TakeSent();
        return quest!;
    }

    private void Pick(int amount) => Assume.That(session.Item.Inventory.Add(test.Store(item, amount)), Is.True);
    private long Stack() => session.Item.Inventory.Find(QuestItem).Single().Uid;

    private string[] Updates() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Quest).Select(Convert.ToHexString).ToArray();
    private static string Hex(Quest quest) => Convert.ToHexString(QuestPacket.Update(quest).ToArray());

    [TestCase(0, 0)]
    [TestCase(7, 7)]
    [TestCase(15, 15)]
    [TestCase(20, 15)]
    public void Start_CountsTheHeldItems(int held, int counter) {
        Quest quest = Accept(held);

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(counter));
    }

    // One more on a stack of 5 is 6 (the whole stack was added again before: 5 + 6).
    [Test]
    public void Pickup_CountsTheHeldTotal() {
        Quest quest = Accept(5);

        Pick(1);

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(6));
        Assert.That(Updates(), Is.EqualTo(new[] { Hex(quest) }));
    }

    [Test]
    public void Remove_LowersAMetCount() {
        Quest quest = Accept(15);

        Assert.That(session.Item.Inventory.Remove(Stack(), out _, 4), Is.True);

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(11));
        Assert.That(Updates(), Is.EqualTo(new[] { Hex(quest) }));
    }

    [TestCase(-1, 0)]
    [TestCase(10, 5)]
    public void Consume_LowersTheCount(int amount, int counter) {
        Quest quest = Accept(15);

        Assert.That(session.Item.Inventory.Consume(Stack(), amount), Is.True);

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(counter));
    }

    // Above the value the count stays at the value: no update.
    [Test]
    public void AboveTheValue_NoUpdate() {
        Quest quest = Accept(20);

        Assert.That(session.Item.Inventory.Remove(Stack(), out _, 3), Is.True);

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(15));
        Assert.That(Updates(), Is.Empty);
    }

    // Another item does not move the quest.
    [Test]
    public void AnotherItem_NoUpdate() {
        Quest quest = Accept(5);
        ItemMetadata other = TestGameSession.Metadata(QuestItem + 1);

        Assume.That(session.Item.Inventory.Add(test.Store(other, 3)), Is.True);

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(5));
        Assert.That(Updates(), Is.Empty);
    }

    // A counter stored before held counts (pickups added up) is set to the held count on login.
    [Test]
    public void Login_CountsAgain() {
        test.Store(item, 4);
        test.Start();
        session = test.Session;
        Quest quest = test.StartQuest(Data());
        quest.Conditions[0].Counter = 15;

        session.Quest.Load();

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(4));
    }

    [Test]
    public void HeldItem_OfItemExistOnly() {
        Assert.Multiple(() => {
            Assert.That(QuestRules.HeldItem(Data().Conditions[0]), Is.EqualTo(QuestItem));
            Assert.That(QuestRules.HeldItem(new ConditionMetadata(ConditionType.item_add, 1, new ConditionMetadata.Parameters(Integers: [QuestItem]), null)), Is.EqualTo(0));
            Assert.That(QuestRules.HeldItem(new ConditionMetadata(ConditionType.item_exist, 1, null, null)), Is.EqualTo(0));
        });
    }
}
