using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A pet's talent lock candy (function LockItemOptionPet): the client takes the candy the player put in the
// ingredient slot and lets the player keep up to its parameter of talents, one always rolling
// (TabContentsPetRemakeOption FUN_140b93460: maxOptionLockCount = min(options - 1, parameter)).
public class ChangeAttributesPetLockTests {
    private const int SingleCandyId = 30000923;
    private const int TwoCandyId = 30001345;
    private const int PetId = 61000001;
    private TestGameSession test = null!;

    private static ItemMetadata Candy(int id, string parameter) {
        ItemMetadata metadata = TestGameSession.Metadata(id, function: new ItemMetadataFunction(ItemFunction.LockItemOptionPet, "LockItemOptionPet", parameter, false));
        return metadata with { Property = metadata.Property with { Tag = ItemTag.LockItemOptionPet } };
    }

    // GetLockConsumeItem asks the account's premium club (ConfigManager.IsPremiumClubActive), which reads only the player.
    private GameSession Start() {
        GameSession session = test.Start();
        var config = (ConfigManager) RuntimeHelpers.GetUninitializedObject(typeof(ConfigManager));
        typeof(ConfigManager).GetField("session", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(config, session);
        session.Config = config;
        return session;
    }

    private Item Held(GameSession session, int itemId) {
        Item item = session.Field!.ItemDrop.CreateItem(itemId)!;
        Assert.That(session.Item.Inventory.Add(item, commit: true), Is.True);
        return session.Item.Inventory.Find(itemId).Single();
    }

    private static Item Pet(int talents) {
        ItemMetadata metadata = TestGameSession.Metadata(PetId);
        var pet = new Item(metadata with { Limit = metadata.Limit with { Level = 50 } }, rarity: 4, initialize: false) { Stats = new ItemStats() };
        BasicAttribute[] kinds = [BasicAttribute.Strength, BasicAttribute.Dexterity, BasicAttribute.Intelligence, BasicAttribute.Luck];
        pet.Stats[ItemStats.Type.Random] = new ItemStats.Option(kinds.Take(talents).ToDictionary(kind => kind, _ => new BasicOption(10)));
        return pet;
    }

    // The chosen Two Talent candy keeps two of a pet's four talents and is the one taken. Before, the server kept
    // at most one option, so the client's two-lock request was refused, and it took the first pet lock item in the
    // bag (here the single candy) whatever the player chose.
    [Test]
    public void TwoTalentCandy_KeepsTwo() {
        test = new TestGameSession();
        test.Register(Candy(SingleCandyId, "1"), Candy(TwoCandyId, "2"));
        GameSession session = Start();
        Held(session, SingleCandyId);
        Item two = Held(session, TwoCandyId);
        Item pet = Pet(4);

        Item? lockItem;
        lock (session.Item) {
            lockItem = ChangeAttributesHandler.GetLockConsumeItem(session, pet, two.Uid);
        }
        Assert.That(lockItem?.Uid, Is.EqualTo(two.Uid));
        int limit = ChangeAttributesHandler.LockLimit(pet, lockItem!);
        Assert.Multiple(() => {
            Assert.That(limit, Is.EqualTo(2));
            Assert.That(ItemRemakeRules.LocksAllowed(2, limit), Is.True);
            Assert.That(ItemRemakeRules.LocksAllowed(3, limit), Is.False);
        });
    }

    // One talent always rolls: a two-talent pet keeps one with any candy.
    [Test]
    public void OneTalentRolls() {
        test = new TestGameSession();
        test.Register(Candy(TwoCandyId, "2"));
        GameSession session = Start();
        Item two = Held(session, TwoCandyId);
        Assert.That(ChangeAttributesHandler.LockLimit(Pet(2), two), Is.EqualTo(1));
        Assert.That(ItemRemakeRules.PetLockLimit(3, 1), Is.EqualTo(0));
    }

    // A pet lock needs the candy the client sent; no candy chosen, or one not in the bag, keeps nothing.
    [Test]
    public void UnchosenCandy_NotTaken() {
        test = new TestGameSession();
        test.Register(Candy(SingleCandyId, "1"));
        GameSession session = Start();
        Held(session, SingleCandyId);
        lock (session.Item) {
            Assert.That(ChangeAttributesHandler.GetLockConsumeItem(session, Pet(4), 0), Is.Null);
        }
    }

    // Only a LockItemOptionPet item with a parameter above 0 is a candy (FUN_140b96e60).
    [TestCase(ItemFunction.LockItemOptionPet, "3", 3)]
    [TestCase(ItemFunction.LockItemOptionPet, "0", 0)]
    [TestCase(ItemFunction.LockItemOptionPet, "", 0)]
    [TestCase(ItemFunction.None, "2", 0)]
    public void CandyLocks(ItemFunction function, string parameter, int expected) {
        Assert.That(ItemRemakeRules.CandyLocks(function, parameter), Is.EqualTo(expected));
    }
}
