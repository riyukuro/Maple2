using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Maid crafting. Client: CManufactureManager::OnPacket (0xA9) and ReadManufacture (two longs, the recipe, the start,
// the lead time, the total), the craft request checks of UIManufactureDialog and the recipe tables
// (CMaidRecipeTable, CMaidRecipeGroupTable). All ids are made up.
public class MaidCraftTests {
    private const int MaidId = 3;
    private const int RecipeId = 4101;

    private static ItemMetadata Metadata(int id, int type = 18, ItemTag tag = ItemTag.None, int? rank = null, int? iconCode = null) {
        var property = new ItemMetadataProperty(false, 0, 100, type, 0, string.Empty, string.Empty, tag, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0) {
            Rank = rank,
            IconCode = iconCode,
        };
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, null, [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [],
            null, null, null, null);
    }

    private static Item NewItem(long uid, ItemMetadata metadata, int amount, int rarity = 1) {
        return new Item(metadata, rarity, amount, initialize: false) { Uid = uid };
    }

    private static Maid NewMaid(int level = 1, int mood = 0) {
        return new Maid { Uid = 77, OwnerId = 5, ItemId = 50400001, MaidId = MaidId, Level = level, Mood = mood, CreationTime = 1000, ExpiryTime = 999999 };
    }

    private static Func<string, string> Attrs(Dictionary<string, string> values) {
        return name => values.GetValueOrDefault(name, string.Empty);
    }

    private static MaidTable.Recipe Recipe(params MaidTable.Ingredient[] ingredients) {
        return new MaidTable.Recipe(RecipeId, ingredients, 0, [300, 200, 100], 50, 5, 20000001, 2, 3);
    }

    [Test]
    public void RecipeRow_ReadsIngredientsLeadTimesAndProduct() {
        MaidTable.Recipe? recipe = MaidTable.RecipeRow(Attrs(new Dictionary<string, string> {
            ["Id"] = "4101",
            ["FirstIngredientItemID"] = "30000001",
            ["FirstIngredientCount"] = "4",
            ["SecondIngredientItemID"] = "FreeChannelChatCoupon",
            ["SecondIngredientCount"] = "2",
            ["ThirdIngredientItemID"] = "0",
            ["WorkbenchType"] = "28",
            ["LeadTimeNormal"] = "300",
            ["LeadTimeGood"] = "200",
            ["LeadTimeVeryGood"] = "100",
            ["MaidExp"] = "50",
            ["MaidMood"] = "5",
            ["ProductItemID"] = "20000001",
            ["ProductCount"] = "2",
            ["ProductRank"] = "9",
        }));
        Assert.That(recipe, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(recipe!.Ingredients, Is.EqualTo(new[] {
                new MaidTable.Ingredient(30000001, ItemTag.None, 4),
                new MaidTable.Ingredient(0, ItemTag.FreeChannelChatCoupon, 2),
            }));
            Assert.That(recipe.WorkbenchType, Is.EqualTo(28));
            Assert.That(recipe.LeadTimes, Is.EqualTo(new[] { 300, 200, 100 }));
            Assert.That(recipe.ProductItemId, Is.EqualTo(20000001));
            Assert.That(recipe.ProductCount, Is.EqualTo(2));
            Assert.That(recipe.ProductRank, Is.EqualTo(6));
        });
    }

    [Test]
    public void RecipeRow_LeavesOutUncountedIngredients_AndMakesAtLeastOneProduct() {
        MaidTable.Recipe? recipe = MaidTable.RecipeRow(Attrs(new Dictionary<string, string> {
            ["Id"] = "4101",
            ["FirstIngredientItemID"] = "30000001",
            ["FirstIngredientCount"] = "0",
            ["SecondIngredientItemID"] = "FreeChannelChatCoupon",
            ["SecondIngredientCount"] = "-1",
            ["ThirdIngredientItemID"] = "30000002",
            ["ThirdIngredientCount"] = "3",
            ["ProductItemID"] = "20000001",
            ["ProductCount"] = "0",
        }));
        Assert.Multiple(() => {
            Assert.That(recipe?.Ingredients, Is.EqualTo(new[] { new MaidTable.Ingredient(30000002, ItemTag.None, 3) }));
            Assert.That(recipe?.ProductCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void RecipeRow_UnknownTagOrBadId_DropsTheRecipe() {
        Assert.Multiple(() => {
            Assert.That(MaidTable.RecipeRow(Attrs(new Dictionary<string, string> { ["Id"] = "4101", ["FirstIngredientItemID"] = "NoSuchTag" })), Is.Null);
            Assert.That(MaidTable.RecipeRow(Attrs(new Dictionary<string, string> { ["Id"] = "4101", ["FirstIngredientItemID"] = "-5" })), Is.Null);
            Assert.That(MaidTable.RecipeRow(Attrs(new Dictionary<string, string> { ["Id"] = "0" })), Is.Null);
            Assert.That(MaidTable.RecipeRow(Attrs(new Dictionary<string, string> { ["Id"] = "4101" }))?.Ingredients, Is.Empty);
        });
    }

    [Test]
    public void GroupRow_SortsByLevel_AndACountMismatchIsNull() {
        Assert.Multiple(() => {
            Assert.That(MaidTable.GroupRow("4103,4101,4102", "5,1,1"), Is.EqualTo(new[] {
                new MaidTable.RecipeLevel(4101, 1), new MaidTable.RecipeLevel(4102, 1), new MaidTable.RecipeLevel(4103, 5),
            }));
            Assert.That(MaidTable.GroupRow("4101,4102", "1"), Is.Null);
        });
    }

    [Test]
    public void IngredientNeed_AndExpGain_FollowTheRates() {
        Assert.Multiple(() => {
            Assert.That(MaidRules.IngredientNeed(10, 0), Is.EqualTo(10));
            Assert.That(MaidRules.IngredientNeed(10, 0.25f), Is.EqualTo(7));
            Assert.That(MaidRules.IngredientNeed(1, 0.5f), Is.EqualTo(1));
            Assert.That(MaidRules.IngredientNeed(3, 1.0f), Is.EqualTo(1));
            Assert.That(MaidRules.ExpGain(50, 0), Is.EqualTo(50));
            Assert.That(MaidRules.ExpGain(50, 0.2f), Is.EqualTo(60));
        });
    }

    [Test]
    public void LeadTime_ByMoodGrade() {
        MaidTable.Recipe recipe = Recipe();
        Assert.Multiple(() => {
            Assert.That(MaidRules.LeadTime(recipe, 10), Is.EqualTo(300));
            Assert.That(MaidRules.LeadTime(recipe, 50), Is.EqualTo(200));
            Assert.That(MaidRules.LeadTime(recipe, 90), Is.EqualTo(100));
            Assert.That(MaidRules.LeadTime(recipe with { LeadTimes = [300] }, 90), Is.EqualTo(0));
        });
    }

    [Test]
    public void Fills_ByIdAndRank_ByTagAtAnyRarity_NotLocked() {
        var byId = new MaidTable.Ingredient(30000001, ItemTag.None, 1);
        var byTag = new MaidTable.Ingredient(0, ItemTag.FreeChannelChatCoupon, 1);
        Item plain = NewItem(1, Metadata(30000001), 5, rarity: 4);
        Item ranked = NewItem(2, Metadata(30000001, rank: 2), 5, rarity: 2);
        Item wrongRank = NewItem(3, Metadata(30000001, rank: 2), 5, rarity: 3);
        Item gear = NewItem(4, Metadata(30000001, type: 1, rank: 2), 1, rarity: 5);
        Item tagged = NewItem(5, Metadata(30000099, tag: ItemTag.FreeChannelChatCoupon, rank: 2), 1, rarity: 4);
        Item locked = NewItem(6, Metadata(30000001), 5);
        locked.IsLocked = true;
        // Unlocked but still inside the unlock wait: the client's lock filter skips it too.
        Item unlocking = NewItem(7, Metadata(30000001), 5);
        unlocking.UnlockTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        Item unlocked = NewItem(8, Metadata(30000001), 5);
        unlocked.UnlockTime = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        Assert.Multiple(() => {
            Assert.That(MaidRules.Fills(byId, plain), Is.True);
            Assert.That(MaidRules.Fills(byId, ranked), Is.True);
            Assert.That(MaidRules.Fills(byId, wrongRank), Is.False);
            Assert.That(MaidRules.Fills(byId, gear), Is.True);
            Assert.That(MaidRules.Fills(byTag, tagged), Is.True);
            Assert.That(MaidRules.Fills(byTag, plain), Is.False);
            Assert.That(MaidRules.Fills(byId, locked), Is.False);
            Assert.That(MaidRules.Fills(byId, unlocking), Is.False);
            Assert.That(MaidRules.Fills(byId, unlocked), Is.True);
        });
    }

    [Test]
    public void TakeIngredients_CountsEachItemOnce_AndIsNullWhenShort() {
        Item first = NewItem(1, Metadata(30000001, tag: ItemTag.FreeChannelChatCoupon), 3);
        Item second = NewItem(2, Metadata(30000001, tag: ItemTag.FreeChannelChatCoupon), 2);
        MaidTable.Recipe recipe = Recipe(
            new MaidTable.Ingredient(30000001, ItemTag.None, 4),
            new MaidTable.Ingredient(0, ItemTag.FreeChannelChatCoupon, 1));
        Assert.Multiple(() => {
            Assert.That(MaidRules.TakeIngredients([first, second], recipe, 0), Is.EqualTo(new[] { (1L, 3), (2L, 2) }));
            Assert.That(MaidRules.TakeIngredients([first], recipe, 0), Is.Null);
            // At a 50% cut the id ingredient needs 2, the tag one still 1.
            Assert.That(MaidRules.TakeIngredients([first], recipe, 0.5f), Is.EqualTo(new[] { (1L, 3) }));
        });
    }

    [Test]
    public void HasWorkbench_NeedsAnOwnedCubeWithTheIconCode() {
        var bench = new PlotCube(Metadata(50300001, iconCode: 28)) { Type = PlotCube.CubeType.Construction };
        var other = new PlotCube(Metadata(50300002, iconCode: 29)) { Type = PlotCube.CubeType.Construction };
        Assert.Multiple(() => {
            Assert.That(MaidRules.HasWorkbench(0, []), Is.True);
            Assert.That(MaidRules.HasWorkbench(28, [other, bench]), Is.True);
            Assert.That(MaidRules.HasWorkbench(30, [other, bench]), Is.False);
        });
    }

    [Test]
    public void CanStart_NeedsTheOwnerTheGroupLevelAndNoRunningCraft() {
        MaidTable.Recipe recipe = Recipe();
        var groups = new Dictionary<int, IReadOnlyList<MaidTable.RecipeLevel>> { [MaidId] = [new MaidTable.RecipeLevel(RecipeId, 2)] };
        Maid crafting = NewMaid(level: 2);
        MaidRules.StartCraft(crafting, recipe, 1000);
        Assert.Multiple(() => {
            Assert.That(MaidRules.CanStart(NewMaid(level: 2), 5, recipe, groups), Is.True);
            Assert.That(MaidRules.CanStart(NewMaid(level: 1), 5, recipe, groups), Is.False);
            Assert.That(MaidRules.CanStart(NewMaid(level: 2), 6, recipe, groups), Is.False);
            Assert.That(MaidRules.CanStart(NewMaid(level: 2), 5, null, groups), Is.False);
            Assert.That(MaidRules.CanStart(NewMaid(level: 2), 5, recipe, null), Is.False);
            Assert.That(MaidRules.CanStart(crafting, 5, recipe, groups), Is.False);
        });
    }

    [Test]
    public void Craft_StartsAtTheMoodLeadTime_IsReadyAfterIt_AndFinishGivesExpAndMood() {
        MaidTable.Recipe recipe = Recipe();
        Maid maid = NewMaid(mood: 50);
        Assert.That(MaidRules.ReceiveResult(maid, 1000), Is.EqualTo(MaidRules.CraftNoRecord));
        MaidRules.StartCraft(maid, recipe, 1000);
        var exps = new Dictionary<int, int> { [1] = 100, [2] = 100 };
        Assert.Multiple(() => {
            Assert.That(maid.CraftDuration, Is.EqualTo(200));
            Assert.That(MaidRules.ReceiveResult(maid, 1199), Is.EqualTo(MaidRules.CraftNotReady));
            Assert.That(MaidRules.ReceiveResult(maid, 1200), Is.EqualTo(MaidRules.CraftReceived));
            Assert.That(MaidRules.Finish(maid, recipe, 0.2f, exps, 10), Is.EqualTo(60));
            Assert.That(maid.Exp, Is.EqualTo(60));
            Assert.That(maid.Mood, Is.EqualTo(55));
            Assert.That(MaidRules.IsCrafting(maid), Is.False);
        });
    }

    [Test]
    public void FailedReceive_NeverAnswersReceived() {
        MaidTable.Recipe recipe = Recipe();
        Maid maid = NewMaid(mood: 50);
        Assert.That(MaidRules.FailedReceiveResult(maid, 1000), Is.EqualTo(MaidRules.CraftNoRecord));
        MaidRules.StartCraft(maid, recipe, 1000);
        Assert.Multiple(() => {
            Assert.That(MaidRules.FailedReceiveResult(maid, 1199), Is.EqualTo(MaidRules.CraftNotReady));
            Assert.That(MaidRules.FailedReceiveResult(maid, 1200), Is.Null);
        });
    }

    [Test]
    public void Packet_ListAddRemove_AreTheManufactureRecords() {
        Maid maid = NewMaid();
        MaidRules.StartCraft(maid, Recipe(), 1700000000);
        ByteWriter writer = MaidCraftItemPacket.List([maid]);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.MaidCraftItem));
            Assert.That(reader.ReadByte(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
            Assert.That(reader.ReadInt(), Is.EqualTo(RecipeId));
            Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
            Assert.That(reader.ReadInt(), Is.EqualTo(300));
            Assert.That(reader.ReadInt(), Is.EqualTo(300));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 8 + 8 + 4 + 8 + 4 + 4));
            Assert.That(MaidCraftItemPacket.Add(maid).Length, Is.EqualTo(2 + 1 + 8 + 8 + 4 + 8 + 4 + 4));
            Assert.That(MaidCraftItemPacket.Remove(77).ToArray().Skip(2), Is.EqualTo(new byte[] { 3, 77, 0, 0, 0, 0, 0, 0, 0 }));
        });
    }

    [Test]
    public void Packet_ResultsAndReward() {
        Assert.Multiple(() => {
            Assert.That(MaidCraftItemPacket.CraftResult(1).ToArray().Skip(2), Is.EqualTo(new byte[] { 4, 1, 0, 0, 0 }));
            Assert.That(MaidCraftItemPacket.ReceiveResult(2).ToArray().Skip(2), Is.EqualTo(new byte[] { 6, 2, 0, 0, 0 }));
            Assert.That(MaidCraftItemPacket.CancelResult(4).ToArray().Skip(2), Is.EqualTo(new byte[] { 10, 4, 0, 0, 0 }));
        });
        ByteWriter writer = MaidCraftItemPacket.Reward(20000001, 2, 3, 60);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.MaidCraftItem));
            Assert.That(reader.ReadByte(), Is.EqualTo(11));
            Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(reader.ReadByte(), Is.EqualTo(3));
            Assert.That(reader.ReadInt(), Is.EqualTo(60));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadByte(), Is.EqualTo(0));
            Assert.That(reader.ReadByte(), Is.EqualTo(0));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 4 + 1 + 4 + 4 + 1 + 1));
        });
    }
}
