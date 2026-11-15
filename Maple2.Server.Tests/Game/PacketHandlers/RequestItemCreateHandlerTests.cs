using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestItemCreate (the client's GM item create window): GM with the item flag only, the amount and rank limits,
// the stack cap, one slot per single and the inventory-full notice; the client has no reply of its own.
public class RequestItemCreateHandlerTests {
    private const int Gear = 11400001;
    private const int Stack = 20000001;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private RequestItemCreateHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Gear, EquipSlot.CL, type: 1, slotMax: 1), TestGameSession.Metadata(Stack, slotMax: 50));
        session = test.Start();
        handler = new RequestItemCreateHandler();
        Allow(AdminPermissions.GameMaster | AdminPermissions.SpawnItem);
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() => session?.Dispose();

    private void Allow(AdminPermissions permissions) => session.Player.Value.Account.AdminPermissions = permissions;

    private void Create(int itemId, int amount = 1, byte rarity = 1) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(itemId);
            writer.WriteInt(amount);
            writer.WriteByte(rarity);
            writer.WriteInt(0); // period
            writer.WriteInt(0); // level
            writer.WriteInt(0); // enchant
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteInt(-1);
            writer.WriteInt(-1);
            writer.WriteInt(0);
            for (int i = 0; i < 10; i++) {
                writer.WriteByte(0);
            }
        }));
    }

    private int Held(int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);
    private int Slots(int itemId) => session.Item.Inventory.Find(itemId).Count();

    [Test]
    public void NotAGm_IsDroppedAndCreatesNothing() {
        Allow(AdminPermissions.None);

        Create(Stack, 5);

        Assert.That(Held(Stack), Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void GmWithoutTheItemFlag_IsDropped() {
        Allow(AdminPermissions.GameMaster);

        Create(Stack, 5);

        Assert.That(Held(Stack), Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Stackable_GivesOneStackOfTheAmount() {
        Create(Stack, 7);

        Assert.That(Held(Stack), Is.EqualTo(7));
        Assert.That(Slots(Stack), Is.EqualTo(1));
    }

    [Test]
    public void Stackable_IsCappedAtOneFullStack() {
        Create(Stack, 500);

        Assert.That(Held(Stack), Is.EqualTo(50));
        Assert.That(Slots(Stack), Is.EqualTo(1));
    }

    [Test]
    public void NonStackable_GivesOneItemPerSlot() {
        Create(Gear, 3);

        Assert.That(Slots(Gear), Is.EqualTo(3));
    }

    [Test]
    public void NonStackable_IsLimitedToTheFreeSlots() {
        short free = session.Item.Inventory.FreeSlots(InventoryType.Gear);

        Create(Gear, free + 20);

        Assert.That(Slots(Gear), Is.EqualTo(free));
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Gear), Is.EqualTo(0));
    }

    [Test]
    public void AmountZeroOrNegative_IsRefusedWithoutANotice() {
        Create(Stack, 0);
        Create(Stack, -4);

        Assert.That(Held(Stack), Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [TestCase((byte) 0)]
    [TestCase((byte) 7)]
    [TestCase((byte) 255)]
    public void RankOutsideOneToSix_IsRefused(byte rarity) {
        Create(Stack, 1, rarity);

        Assert.That(Held(Stack), Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void UnknownItem_IsRefusedWithoutANotice() {
        Create(99999999, 1);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void RankSixIsAccepted() {
        Create(Gear, 1, 6);

        Assert.That(session.Item.Inventory.Find(Gear).Single().Rarity, Is.EqualTo(6));
    }

    [Test]
    public void FullInventory_AnswersTheInventoryFullNoticeAndGivesNothing() {
        Create(Gear, 1000);
        test.TakeSent();

        Create(Gear, 1);

        Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Contain(SendOp.ItemInventory));
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Gear), Is.EqualTo(0));
    }
}
