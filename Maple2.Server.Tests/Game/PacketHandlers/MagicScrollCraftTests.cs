using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// EnchantScroll 4: a magic scroll recipe of the mastery window's scroll tab. Synthetic recipe in the shape of
// the KR rows: one tagged ingredient, a meso price and a reward scroll.
public class MagicScrollCraftTests {
    private const int RecipeId = 20994101;
    private const int ScrollId = 31001033;
    private const int StoneId = 33000160;
    private const int OtherStoneId = 33000279; // another item with the same tag
    private const int Meso = 50000;

    private TestGameSession test = null!;

    private static ItemMetadata Stone(int id) {
        ItemMetadata metadata = TestGameSession.Metadata(id, slotMax: 99999);
        return metadata with { Property = metadata.Property with { Tag = ItemTag.CharmStone } };
    }

    private GameSession Start(int stones, int otherStones, long meso) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ScrollId));
        if (stones > 0) test.Store(Stone(StoneId), stones);
        if (otherStones > 0) test.Store(Stone(OtherStoneId), otherStones);
        test.Register(Stone(StoneId));
        test.SetTable(new EnchantScrollRecipeTable(new Dictionary<int, EnchantScrollRecipeTable.Entry> {
            [RecipeId] = new(RecipeId, 1, 1, new ItemComponent(ScrollId, 4, 1, ItemTag.None), Meso,
                [new ItemComponent(StoneId, 4, 2000, ItemTag.CharmStone)], []),
        }));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Currency.Meso = meso;
        test.TakeSent();
        return session;
    }

    private void Craft(GameSession session, int recipeId = RecipeId) {
        new EnchantScrollHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(recipeId);
        }));
    }

    private static int Count(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    // The client counts the ingredient by its tag: stones of either id pay for it.
    [Test]
    public void Craft_PaysWithTaggedItemsAndGivesTheScroll() {
        GameSession session = Start(1500, 1000, Meso + 7);

        Craft(session);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(Count(session, StoneId) + Count(session, OtherStoneId), Is.EqualTo(500));
        Assert.That(session.Currency.Meso, Is.EqualTo(7));
        Assert.That(Count(session, ScrollId), Is.EqualTo(1));
        // Mode 4: i32 count, then i32 id and u16 rank per item.
        byte[] reply = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.EnchantScroll);
        Assert.That(reply, Has.Length.EqualTo(13));
        Assert.That(reply[2], Is.EqualTo(4));
        Assert.That(BitConverter.ToInt32(reply, 3), Is.EqualTo(1));
        Assert.That(BitConverter.ToInt32(reply, 7), Is.EqualTo(ScrollId));
        Assert.That(BitConverter.ToInt16(reply, 11), Is.EqualTo(4));
    }

    [TestCase(1999, Meso)] // one stone short
    [TestCase(2000, Meso - 1)] // one meso short
    public void Craft_ShortOfStonesOrMesoGivesNothing(int stones, long meso) {
        GameSession session = Start(stones, 0, meso);

        Craft(session);

        Assert.That(Count(session, StoneId), Is.EqualTo(stones));
        Assert.That(session.Currency.Meso, Is.EqualTo(meso));
        Assert.That(Count(session, ScrollId), Is.EqualTo(0));
        Assert.That(test.TakeSent().Exists(packet => TestGameSession.Op(packet) == SendOp.EnchantScroll), Is.False);
    }

    [Test]
    public void Craft_OfAnUnknownRecipeGivesNothing() {
        GameSession session = Start(2000, 0, Meso);

        Craft(session, RecipeId + 1);

        Assert.That(Count(session, StoneId), Is.EqualTo(2000));
        Assert.That(Count(session, ScrollId), Is.EqualTo(0));
    }

    // The KR row shape; the client drops a row without a reward scroll and one its filter refuses.
    [Test]
    public void Ingest_ReadsTheRecipeRows() {
        var document = new XmlDocument();
        document.LoadXml("<ms2>"
            + "<recipe id=\"20994101\" recipeType=\"1\" slot=\"3\" rewardScroll=\"31001033,4,1\" requireMeso=\"50000\" "
            + "requireItem1=\"33000160:CharmStone,4,2000\" requireItem2=\"33000161:ProtectStone,4,2000\" requireItem3=\"\" "
            + "requireItem4=\"\" requireItem5=\"\" rewardItem1=\"\" rewardItem2=\"\" rewardItem3=\"\" rewardItem4=\"\" rewardItem5=\"\" feature=\"A\" />"
            + "<recipe id=\"20994102\" recipeType=\"1\" slot=\"3\" rewardScroll=\"\" requireMeso=\"1\" feature=\"A\" />"
            + "<recipe id=\"20994103\" recipeType=\"1\" slot=\"3\" rewardScroll=\"31001034,4,1\" requireMeso=\"1\" feature=\"B\" />"
            + "</ms2>");

        Dictionary<int, EnchantScrollRecipeTable.Entry> rows = TableMapper.ReadEnchantScrollRecipes(document, node => node.GetAttribute("feature") == "A");

        Assert.That(rows.Keys, Is.EqualTo(new[] { 20994101 }));
        EnchantScrollRecipeTable.Entry row = rows[20994101];
        Assert.That(row.RewardScroll, Is.EqualTo(new ItemComponent(31001033, 4, 1, ItemTag.None)));
        Assert.That(row.RequiredMeso, Is.EqualTo(50000));
        Assert.That(row.Slot, Is.EqualTo(3));
        Assert.That(row.RequiredItems, Is.EqualTo(new[] {
            new ItemComponent(33000160, 4, 2000, ItemTag.CharmStone),
            new ItemComponent(33000161, 4, 2000, ItemTag.ProtectStone),
        }));
        Assert.That(row.RewardItems, Is.Empty);
    }
}
