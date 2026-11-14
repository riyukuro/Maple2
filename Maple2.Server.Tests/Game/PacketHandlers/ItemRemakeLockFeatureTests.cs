using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The remake dialogs offer option locks only with OptionBlock01 on, and for the item in them only with its kind's
// lock feature (CanLockOptions, itemremakescrolldata.cpp:437): with them off the client sends no lock.
public class ItemRemakeLockFeatureTests {
    private const int EarringId = 11200002;
    private const int ScrollId = 20000010;

    private TestGameSession test = null!;

    [TestCase(11200002, ExpectedResult = "OptionBlockAccessory")]
    [TestCase(11400002, ExpectedResult = "OptionBlockArmor")]
    [TestCase(12200002, ExpectedResult = "OptionBlockArmor")] // subtype 22, an overall
    [TestCase(13000002, ExpectedResult = "OptionBlockWeapon")]
    [TestCase(14100002, ExpectedResult = "OptionBlockWeapon")]
    [TestCase(15600002, ExpectedResult = "OptionBlockWeapon")]
    [TestCase(15700002, ExpectedResult = null)] // a two-hand subtype the client does not list
    [TestCase(61000001, ExpectedResult = "PetRemakeBlock01")]
    [TestCase(20000001, ExpectedResult = null)]
    public string? LockFeature_OfTheItemKind(int itemId) => ItemRemakeRules.LockFeature(new ItemType(itemId));

    // A rarity 4 level 50 earring (or the given gear) with a Strength option, held in the bag.
    private (GameSession Session, Item Item) Start(params string[] off) => Start(EarringId, EquipSlot.EA, off);

    private (GameSession Session, Item Item) Start(int itemId, EquipSlot slot, params string[] off) {
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(itemId, slot: slot, type: 1, slotMax: 1);
        Item stored = test.Store(metadata with { Limit = metadata.Limit with { Level = 50 } });
        ItemMetadata scroll = TestGameSession.Metadata(ScrollId,
            function: new ItemMetadataFunction(ItemFunction.ItemRemakeScroll, "ItemRemakeScroll", "1", false));
        test.Store(scroll);
        test.SetTable(new ItemRemakeScrollTable(new Dictionary<int, ItemRemakeScrollMetadata>()));
        GameSession session = test.Start();
        // The lock item search asks the premium club (ConfigManager.IsPremiumClubActive), which reads only the player.
        var config = (ConfigManager) RuntimeHelpers.GetUninitializedObject(typeof(ConfigManager));
        typeof(ConfigManager).GetField("session", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(config, session);
        session.Config = config;
        Item item = session.Item.Inventory.Get(stored.Uid)!;
        item.Rarity = 4;
        item.Stats = new ItemStats();
        item.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Strength] = new BasicOption(1);
        item.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Dexterity] = new BasicOption(1);
        if (off.Length > 0) {
            test.SetFeaturesOff(off);
        }
        test.TakeSent();
        return (session, item);
    }

    private static int LastInt(byte[] packet) => BitConverter.ToInt32(packet, packet.Length - 4);

    // ChangeAttributes 0 (the npc remake) with the Strength option kept: with OptionBlock01 off it is refused at once;
    // with it on it goes on to the lock item, which the player does not have.
    [TestCase(false, ExpectedResult = ChangeAttributesError.s_itemremake_error_server_impossible)]
    [TestCase(true, ExpectedResult = ChangeAttributesError.s_itemremake_error_server_fail_lack_lock_consume_item)]
    public ChangeAttributesError NpcRemake_GearLockNeedsOptionBlock(bool on) {
        (GameSession session, Item item) = on ? Start() : Start(ItemRemakeRules.OptionLockFeature);

        new ChangeAttributesHandler { ItemStatsCalc = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(item.Uid);
            writer.WriteLong(0);
            writer.WriteByte(1);
            writer.WriteBool(false);
            writer.WriteShort(BasicAttribute.Strength.ClientStat());
        }));

        return (ChangeAttributesError) LastInt(test.TakeSent().Single());
    }

    // ChangeAttributesScroll 1 with the Strength option kept: with the item kind's feature off it is refused before
    // the scroll's own checks; with it on the scroll check answers (no row for it here).
    [TestCase("OptionBlockAccessory", ExpectedResult = true)]
    [TestCase("OptionBlockArmor", ExpectedResult = false)]
    public bool ScrollLock_NeedsTheItemKindsFeature(string off) => ScrollLockRefused(Start(off));

    // A kind CanLockOptions does not sort (subtype 57, a two-hand weapon) is offered no lock even with every feature on.
    [Test]
    public void ScrollLock_RefusedForAKindWithoutLocks() {
        Assert.That(ScrollLockRefused(Start(15700002, EquipSlot.RH)), Is.True);
    }

    private bool ScrollLockRefused((GameSession Session, Item Item) start) {
        (GameSession session, Item item) = start;
        Item scroll = session.Item.Inventory.Find(ScrollId).Single();

        new ChangeAttributesScrollHandler { TableMetadata = test.TableMetadata, ItemStatsCalc = null! }.Handle(session,
            TestGameSession.Packet(writer => {
                writer.WriteByte(1);
                writer.WriteLong(scroll.Uid);
                writer.WriteLong(item.Uid);
                writer.WriteLong(0);
                writer.WriteBool(false);
                writer.WriteByte(1);
                writer.WriteBool(false);
                writer.WriteShort(BasicAttribute.Strength.ClientStat());
            }));

        return (ChangeAttributesScrollError) LastInt(test.TakeSent().Single()) == ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake;
    }
}
