using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// The owner bind of the client's CanUseItem (@0x140d1b610, FUN_1406343b0, notice 0xfe): an item bound to another
// character, or one with a gem in its sockets bound to another character, is not used. A binding to the user, or to
// character 0, binds no one else. The item id is made up.
public class ItemUseBindingTests {
    private const long Other = 999;

    // owner: the item's own binding (-1 none, 0 the user); gemOwner: a socketed gem's binding (-1 no gem, 0 the user).
    [TestCase(-1L, -1L, ExpectedResult = true)]
    [TestCase(0L, -1L, ExpectedResult = true)]
    [TestCase(Other, -1L, ExpectedResult = false)]
    [TestCase(-1L, 0L, ExpectedResult = true)]
    [TestCase(-1L, Other, ExpectedResult = false)]
    public bool Use_NotWhenBoundToAnotherCharacter(long owner, long gemOwner) {
        var test = new TestGameSession();
        Item stored = test.Store(TestGameSession.Metadata(20000001));
        test.Start();
        long self = test.CharacterId;
        Item item = test.Session.Item.Inventory.Get(stored.Uid)!;
        if (owner >= 0) {
            item.Transfer = new ItemTransfer(binding: new ItemBinding(owner == 0 ? self : owner, "name"));
        }
        if (gemOwner >= 0) {
            item.Socket = new ItemSocket(3, 1);
            item.Socket.Sockets[0] = new ItemGemstone(40200001, binding: new ItemBinding(gemOwner == 0 ? self : gemOwner, "name"), stats: new ItemStats());
        }
        return ItemUseHandler.UseAllowed(test.Session, item);
    }
}
