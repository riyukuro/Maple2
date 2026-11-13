using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Commands;

// The GM item command makes an item at the item's own rarity unless one is given, as drops and rewards do
// (ItemDropManager.CreateItem): a stackable of rank 4 (Chaos Onyx 40100024) made at rarity 1 was a bag row of its own
// that the card game's rank 4 reward never stacked onto. The item data here is made up but for the id and its option.
public class ItemCommandTests {
    private const int ItemId = 40100024;

    private static readonly ItemMetadata Metadata = TestGameSession.Metadata(ItemId, slotMax: 9999) with {
        Option = new ItemMetadataOption(0, default, 0, default, 4, default, 0, 0),
    };

    private static (TestGameSession Test, GameSession Session) Start() {
        var test = new TestGameSession();
        test.Register(Metadata);
        test.SetTable(new ItemOptionPickTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionPickTable.Option>>()));
        test.SetTable(new ItemOptionConstantTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>>()));
        test.SetTable(new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        test.SetTable(new ItemOptionStaticTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();
        return (test, session);
    }

    private static int Run(GameSession session, params string[] args) {
        var console = new TestConsole();
        int code = new ItemCommand(session, session.ItemMetadata).Invoke(args, console);
        Assert.That(console.Error.ToString(), Is.Empty);
        return code;
    }

    [Test]
    public void NoRarity_TheItemsOwn_StacksWithRewards() {
        (_, GameSession session) = Start();
        Assert.That(session.Item.Inventory.Add(new Item(Metadata, 4, 260), true), Is.True);

        Assert.That(Run(session, $"{ItemId}", "-a", "2"), Is.EqualTo(0));

        Item[] rows = session.Item.Inventory.Find(ItemId).ToArray();
        Assert.Multiple(() => {
            Assert.That(rows, Has.Length.EqualTo(1));
            Assert.That(rows[0].Rarity, Is.EqualTo(4));
            Assert.That(rows[0].Amount, Is.EqualTo(262));
        });
    }

    [Test]
    public void GivenRarity_IsKept() {
        (_, GameSession session) = Start();

        Assert.That(Run(session, $"{ItemId}", "-r", "2"), Is.EqualTo(0));

        Assert.That(session.Item.Inventory.Find(ItemId).Select(item => item.Rarity), Is.EqualTo(new[] { 2 }));
    }
}
