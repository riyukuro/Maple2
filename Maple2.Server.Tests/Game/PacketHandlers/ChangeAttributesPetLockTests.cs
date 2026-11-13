using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
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

    // A KR candy is item type 15, which goes to the Catalyst tab (ModelExtensions.TabOfType).
    private static ItemMetadata Candy(int id, string parameter, int type = 0) {
        ItemMetadata metadata = TestGameSession.Metadata(id, function: new ItemMetadataFunction(ItemFunction.LockItemOptionPet, "LockItemOptionPet", parameter, false), type: type);
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

    // The request body the scroll dialog sends after its command byte 1 (UIItemRemakeScrollDialog,
    // uiitemremakescrolldialog.cpp:470-520; RemakeOption::vfunc_1 @0x140617a90): Encode8 scroll uid, Encode8 item
    // uid, Encode8 ingredient uid (the candy), a bool, Encode1 the lock count, then per lock Encode1 its kind
    // (0 basic, 1 special; FUN_140619290) and Encode2 its id.
    private static ByteReader Request(long scrollUid, long itemUid, long ingredientUid, params (byte Kind, short Id)[] locks) {
        var bytes = new List<byte>();
        bytes.AddRange(System.BitConverter.GetBytes(scrollUid));
        bytes.AddRange(System.BitConverter.GetBytes(itemUid));
        bytes.AddRange(System.BitConverter.GetBytes(ingredientUid));
        bytes.Add(0);
        bytes.Add((byte) locks.Length);
        foreach ((byte kind, short id) in locks) {
            bytes.Add(kind);
            bytes.AddRange(System.BitConverter.GetBytes(id));
        }
        return new ByteReader(bytes.ToArray(), 0);
    }

    // The pet of the live check: three talents, Melee Damage (special 24), Movement Speed (basic 14) and Critical
    // Damage (basic 18).
    private static Item LivePet() {
        ItemMetadata metadata = TestGameSession.Metadata(PetId);
        var pet = new Item(metadata with { Limit = metadata.Limit with { Level = 50 } }, rarity: 4, initialize: false) { Stats = new ItemStats() };
        pet.Stats[ItemStats.Type.Random] = new ItemStats.Option(
            new Dictionary<BasicAttribute, BasicOption> {
                [BasicAttribute.MovementSpeed] = new BasicOption(0.05f),
                [BasicAttribute.CriticalDamage] = new BasicOption(54),
            },
            new Dictionary<SpecialAttribute, SpecialOption> { [SpecialAttribute.MeleeDamage] = new SpecialOption(0.022f) });
        return pet;
    }

    // The live case: the Two Talent candy in the dialog's slot, a Single Talent candy ahead of it in the bag, both
    // of item type 15 (the Catalyst tab), and the client's request with two locks and with one. Before, the lock
    // item was looked for only in the Misc tab, so a candy was never found and every request with a lock was
    // refused (lack_lock_consume_item), while one without a lock went through.
    [TestCase(new byte[] { 1, 0 }, new short[] { 24, 14 })]
    [TestCase(new byte[] { 0 }, new short[] { 18 })]
    public void ClientRequest_CatalystCandy_Kept(byte[] kinds, short[] ids) {
        test = new TestGameSession();
        test.Register(Candy(SingleCandyId, "1", type: 15), Candy(TwoCandyId, "2", type: 15));
        GameSession session = Start();
        Item single = Held(session, SingleCandyId);
        Item two = Held(session, TwoCandyId);
        Assert.That((single.Inventory, two.Inventory), Is.EqualTo((InventoryType.Catalyst, InventoryType.Catalyst)));
        Item pet = LivePet();

        ByteReader packet = Request(1, pet.Uid, two.Uid, kinds.Zip(ids).ToArray());
        packet.ReadLong();
        packet.ReadLong();
        long ingredientUid = packet.ReadLong();
        packet.ReadByte();
        List<ItemRemakeRules.Lock>? locks = ItemRemakeRules.ReadLocks(packet);
        Assert.That(locks, Is.Not.Null);

        ChangeAttributesScrollError error;
        Item? lockItem;
        lock (session.Item) {
            error = ChangeAttributesScrollHandler.CheckLocks(session, pet, ingredientUid, locks!, out lockItem);
        }
        Assert.Multiple(() => {
            Assert.That(error, Is.EqualTo(ChangeAttributesScrollError.none));
            Assert.That(lockItem?.Uid, Is.EqualTo(two.Uid));
            Assert.That(packet.Available, Is.EqualTo(0));
        });
    }

    // Three locks on a three-talent pet: one talent always rolls, so the two-talent candy's request is refused.
    [Test]
    public void ClientRequest_AllTalents_Refused() {
        test = new TestGameSession();
        test.Register(Candy(TwoCandyId, "2", type: 15));
        GameSession session = Start();
        Item two = Held(session, TwoCandyId);
        ByteReader packet = Request(1, 2, two.Uid, (1, 24), (0, 14), (0, 18));
        packet.ReadLong();
        packet.ReadLong();
        packet.ReadLong();
        packet.ReadByte();
        List<ItemRemakeRules.Lock> locks = ItemRemakeRules.ReadLocks(packet)!;
        lock (session.Item) {
            Assert.That(ChangeAttributesScrollHandler.CheckLocks(session, LivePet(), two.Uid, locks, out _),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake));
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
