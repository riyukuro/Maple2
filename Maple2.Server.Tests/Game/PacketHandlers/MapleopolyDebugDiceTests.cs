using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Mapleopoly 0x008D mode 5, the board dialog's GM dice button (uibluemarblegamedialog.c:535): two i32 dice. A GM's
// roll moves the token by the two dice and costs what a roll costs; a double gives a free roll. The board is made up.
public class MapleopolyDebugDiceTests {
    private const int EventId = 7102;
    private const int Ticket = 20000003;

    private static TestGameSession Start(bool gm, out Item tickets) {
        var test = new TestGameSession();
        tickets = test.Store(TestGameSession.Metadata(Ticket, type: 2), amount: 3);
        var none = new ItemComponent(0, 0, 0, ItemTag.None);
        test.AddEvent(EventId, GameEventType.BlueMarble, new BlueMarble(new ItemComponent(Ticket, -1, 1, ItemTag.None), [], [
            new BlueMarble.Slot(BlueMarbleSlotType.Start, 0, none),
            new BlueMarble.Slot(BlueMarbleSlotType.Forward, 1, none),
        ]));
        GameSession session = test.Start();
        if (gm) {
            session.Player.AdminPermissions = AdminPermissions.GameMaster;
        }
        test.TakeSent();
        return test;
    }

    private static void Dice(TestGameSession test, int dice1, int dice2) {
        new MapleopolyHandler { ItemMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(dice1);
            writer.WriteInt(dice2);
        }));
    }

    private static int Held(GameSession session) => session.Item.Inventory.Find(Ticket).Sum(item => item.Amount);

    // A double 2: the token moves 4, one free roll is given and one ticket is paid.
    [Test]
    public void FromAGm_RollsTheChosenDice() {
        TestGameSession test = Start(gm: true, out _);

        Dice(test, 2, 2);

        Assert.Multiple(() => {
            Assert.That(test.Session.GameEvent.Peek(GameEventUserValueType.MapleopolyTotalSlotCount, EventId), Is.EqualTo("4"));
            Assert.That(test.Session.GameEvent.Peek(GameEventUserValueType.MapleopolyFreeRollAmount, EventId), Is.EqualTo("1"));
            Assert.That(Held(test.Session), Is.EqualTo(2));
            Assert.That(test.TakeSent().Count(packet => TestGameSession.Op(packet) == SendOp.Mapleopoly), Is.EqualTo(1));
        });
    }

    // Dice off 1-6 and a player's button are dropped: no roll, no ticket.
    [TestCase(true, 0, 3)]
    [TestCase(true, 3, 7)]
    [TestCase(false, 2, 2)]
    public void OffDiceOrFromAPlayer_IsDropped(bool gm, int dice1, int dice2) {
        TestGameSession test = Start(gm, out _);

        Dice(test, dice1, dice2);

        Assert.That(Held(test.Session), Is.EqualTo(3));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mapleopoly), Is.Empty);
    }
}
