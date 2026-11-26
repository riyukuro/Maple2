using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Commands;

// The item command's enhancement options: an enchant, a limit break, unlocked sockets with gems, and bonus stat or
// special lines, each checked against the server's own tables. A request the item cannot take is a chat error and
// adds nothing.
public class ItemCommandEnhancementsTests {
    private const int GearId = 11200001;   // id type 12: an accessory, the gem kind that fits equipPart 3
    private const int LuaGearId = 11200006; // another accessory, with no socketDataId: the client's Lua socket rule
    private const int GemId = 40200001;
    private const int GemId2 = 40200002;

    private static ItemMetadata GearMetadata() {
        ItemMetadata metadata = TestGameSession.Metadata(GearId);
        return metadata with {
            Property = metadata.Property with {
                Type = 1, SubType = 0, SocketId = 7, LimitBreakMaxLevel = 5, Rank = 3,   // the command takes the slot from the id
            },
            Limit = metadata.Limit with { Level = 50, EnableEnchant = true },
        };
    }

    private static ItemMetadata LuaGearMetadata() {
        ItemMetadata metadata = TestGameSession.Metadata(LuaGearId);
        return metadata with {
            Property = metadata.Property with { Type = 1, SubType = 0, SocketId = 0, Rank = 4 },
            Limit = metadata.Limit with { Level = 50, EnableEnchant = true },
        };
    }

    private static ItemMetadata GemMetadata(int id) {
        ItemMetadata metadata = TestGameSession.Metadata(id);
        return metadata with { Property = metadata.Property with { Type = 13 } };
    }

    private static (TestGameSession Test, GameSession Session) Start() {
        var test = new TestGameSession();
        test.SetServerTable(new EnchantLimitTable(new Dictionary<int, EnchantLimitTable.Limit> {
            [12] = new(MinLevel: 20, MaxLevel: 99, MaxGrade: 15),
        }));
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata> {
            [1] = new(Id: 1, Slot: 12, EnchantLevel: 3, Rarity: 3, Rate: 0.016f, MinLevel: 20, MaxLevel: 99, Attributes: [BasicAttribute.Piercing]),
        }));
        test.SetServerTable(new UnlimitedEnchantOptionTable(new Dictionary<int, Dictionary<int, UnlimitedEnchantOptionTable.Option>> {
            [12] = new() {
                [1] = new(new Dictionary<BasicAttribute, int> { [BasicAttribute.Strength] = 10 },
                          new Dictionary<BasicAttribute, float>(),
                          new Dictionary<SpecialAttribute, int>(),
                          new Dictionary<SpecialAttribute, float>()),
                [2] = new(new Dictionary<BasicAttribute, int>(),
                          new Dictionary<BasicAttribute, float> { [BasicAttribute.Piercing] = 0.005f },
                          new Dictionary<SpecialAttribute, int>(),
                          new Dictionary<SpecialAttribute, float>()),
            },
        }));
        test.SetTable(new ItemSocketTable(new Dictionary<int, IReadOnlyDictionary<int, ItemSocketMetadata>> {
            [7] = new Dictionary<int, ItemSocketMetadata> { [3] = new(3, 1), [0] = new(0, 0) },
        }));
        test.SetTable(new GemstoneUpgradeTable(new Dictionary<int, GemstoneUpgradeTable.Entry> {
            [GemId] = new(Level: 1, NextItemId: 0, Ingredients: [], EquipPart: 3),
            [GemId2] = new(Level: 1, NextItemId: 0, Ingredients: [], EquipPart: 3),
        }));
        test.Register(GearMetadata(), LuaGearMetadata(), GemMetadata(GemId), GemMetadata(GemId2), TestGameSession.Metadata(20000004, type: 2));
        test.SetTable(new ItemOptionPickTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionPickTable.Option>>()));
        test.SetTable(new ItemOptionConstantTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>>()));
        test.SetTable(new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        test.SetTable(new ItemOptionStaticTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();
        return (test, session);
    }

    private static (int Code, string Error) Run(GameSession session, params string[] args) {
        var console = new TestConsole();
        int code = new ItemCommand(session, session.ItemMetadata).Invoke(args, console);
        return (code, console.Error.ToString());
    }

    private static Item? Spawned(GameSession session, int id) => session.Item.Inventory.Find(id).LastOrDefault();

    [Test]
    public void Enchant_SetsTheLevelAndTheTablesOptions() {
        (_, GameSession session) = Start();
        (int code, string error) = Run(session, GearId.ToString(), "--enchant", "3");
        Assert.That(error, Is.Empty);
        Assert.That(code, Is.EqualTo(0));
        Item item = Spawned(session, GearId)!;
        Assert.Multiple(() => {
            Assert.That(item.Enchant!.Enchants, Is.EqualTo(3));
            Assert.That(item.Enchant.BasicOptions[BasicAttribute.Piercing].Value, Is.EqualTo(16));
        });
    }

    [Test]
    public void Enchant_PastTheTableOrTheLimits_IsAnError() {
        (_, GameSession session) = Start();
        Assert.That(Run(session, GearId.ToString(), "--enchant", "16").Error, Does.Contain("0..15"));
        Assert.That(Run(session, GearId.ToString(), "--enchant", "4").Error, Does.Contain("no row in the enchant table"));
        Assert.That(session.Item.Inventory.Find(GearId), Is.Empty);
    }

    [Test]
    public void LimitBreak_AddsEveryLevel() {
        (_, GameSession session) = Start();
        (int code, string error) = Run(session, GearId.ToString(), "--limit-break", "2");
        Assert.That(error, Is.Empty);
        Assert.That(code, Is.EqualTo(0));
        Item item = Spawned(session, GearId)!;
        Assert.Multiple(() => {
            Assert.That(item.LimitBreak!.Level, Is.EqualTo(2));
            Assert.That(item.LimitBreak.BasicOptions[BasicAttribute.Strength].Value, Is.EqualTo(10));
            Assert.That(item.LimitBreak.BasicOptions[BasicAttribute.Piercing].Value, Is.EqualTo(5));
        });
    }

    [Test]
    public void LimitBreak_NeedsEnoughTableLevelsAndNoEnchant() {
        (_, GameSession session) = Start();
        Assert.That(Run(session, GearId.ToString(), "--limit-break", "6").Error, Does.Contain("past the item's maximum 5"));
        Assert.That(Run(session, GearId.ToString(), "--limit-break", "3").Error, Does.Contain("no level 3"));
        Assert.That(Run(session, GearId.ToString(), "--enchant", "1", "--limit-break", "1").Error, Does.Contain("cannot be set together"));
        Assert.That(session.Item.Inventory.Find(GearId), Is.Empty);
    }

    [Test]
    public void Sockets_TakeGemsInOrder() {
        (_, GameSession session) = Start();
        (int code, string error) = Run(session, GearId.ToString(), "--socket", "3", "--gem", GemId.ToString(), "--gem", GemId2.ToString());
        Assert.That(error, Is.Empty);
        Assert.That(code, Is.EqualTo(0));
        Item item = Spawned(session, GearId)!;
        Assert.Multiple(() => {
            Assert.That(item.Socket!.MaxSlots, Is.EqualTo(3));
            Assert.That(item.Socket.UnlockSlots, Is.EqualTo(3));
            Assert.That(item.Socket.Sockets[0]!.ItemId, Is.EqualTo(GemId));
            Assert.That(item.Socket.Sockets[1]!.ItemId, Is.EqualTo(GemId2));
            Assert.That(item.Socket.Sockets[2], Is.Null);
        });
    }

    [Test]
    public void Sockets_UseTheLuaRuleWithoutASocketId() {
        (_, GameSession session) = Start();
        (int code, string error) = Run(session, LuaGearId.ToString(), "--socket", "3", "--gem", GemId.ToString());
        Assert.That(error, Is.Empty);
        Assert.That(code, Is.EqualTo(0));
        Item item = Spawned(session, LuaGearId)!;
        Assert.Multiple(() => {
            Assert.That(item.Socket!.MaxSlots, Is.EqualTo(3));
            Assert.That(item.Socket.UnlockSlots, Is.EqualTo(3));
            Assert.That(item.Socket.Sockets[0]!.ItemId, Is.EqualTo(GemId));
        });
    }

    [Test]
    public void Sockets_RejectPastTheItemOrWithoutGemsThatFit() {
        (_, GameSession session) = Start();
        Assert.That(Run(session, GearId.ToString(), "--socket", "4").Error, Does.Contain("past the item's 3 sockets"));
        Assert.That(Run(session, GearId.ToString(), "--socket", "1", "--gem", "99999999").Error, Does.Contain("not a gem item"));
        Assert.That(Run(session, GearId.ToString(), "--socket", "1", "--gem", GemId.ToString(), "--gem", GemId2.ToString()).Error, Does.Contain("2 ids for 1 unlocked socket"));
        Assert.That(session.Item.Inventory.Find(GearId), Is.Empty);
    }

    [Test]
    public void BonusLines_ReplaceTheRandomGroup() {
        (_, GameSession session) = Start();
        (int code, string error) = Run(session, GearId.ToString(), "--bonus-clear", "--stat", "31=5", "--special", "7=3");
        Assert.That(error, Is.Empty);
        Assert.That(code, Is.EqualTo(0));
        Item item = Spawned(session, GearId)!;
        Assert.Multiple(() => {
            Assert.That(item.Stats![ItemStats.Type.Random].Basic[BasicAttribute.Piercing].Value, Is.EqualTo(5));
            Assert.That(item.Stats[ItemStats.Type.Random].Special[SpecialAttribute.TotalDamage].Value, Is.EqualTo(3));
            Assert.That(item.Stats[ItemStats.Type.Random].Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void BonusLines_RejectBadIdsAndValues() {
        (_, GameSession session) = Start();
        Assert.That(Run(session, GearId.ToString(), "--stat", "35=5").Error, Does.Contain("not a client stat id"));
        Assert.That(Run(session, GearId.ToString(), "--special", "200=5").Error, Does.Contain("not a special ability id"));
        Assert.That(Run(session, GearId.ToString(), "--stat", "31=-1").Error, Does.Contain("0 to 1000000"));
        Assert.That(Run(session, GearId.ToString(), "--stat", "31").Error, Does.Contain("clientId=value"));
        Assert.That(session.Item.Inventory.Find(GearId), Is.Empty);
    }

    [Test]
    public void Enhancements_NeedGear() {
        (_, GameSession session) = Start();
        Assert.That(Run(session, "20000004", "--enchant", "1").Error, Does.Contain("need a gear item"));
    }
}
