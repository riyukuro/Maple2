using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using BagItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// An upgrade row of the exchange scroll table (type 2, as the KMS2 dev pack's ReverseShop04 rows): the dialog places
// the row's requireEquip as the target (FUN_140f52ca0, uiitemexchangescrolldialog.cpp:482), shows what the reward keeps
// of it (FUN_140f514c0, :284-291) and sends mode 1 with the target uid and count 1. Before, the row was an exchange:
// the target was neither checked nor taken, and the reward kept nothing of it.
public class ItemExchangeScrollUpgradeTests {
    private const int ScrollId = 30000786;
    private const int TargetId = 11300917;
    private const int RewardId = 11300928;
    private const int CrystalId = 40100001;

    private TestGameSession test = null!;
    private BagItem scroll = null!;
    private BagItem target = null!;

    private GameSession Start(int targetId = TargetId, int enchants = 3, bool targetAsMaterial = false,
            TransferType rewardType = TransferType.Tradable, int rewardTradeMaxRarity = 4, int rewardTrades = 0) {
        test = new TestGameSession();
        test.SetTable(new ItemExchangeScrollTable(new Dictionary<int, ItemExchangeScrollMetadata> {
            [ScrollId] = new(new ItemComponent(ScrollId, 5, 1, ItemTag.None), new ItemComponent(RewardId, 5, 1, ItemTag.None), 0, 0,
                targetAsMaterial ? [new ItemComponent(TargetId, 5, 1, ItemTag.None)] : [],
                Type: ItemExchangeScrollType.Upgrade, MaxGrade: 10, RequireEquip: new ItemComponent(TargetId, 5, 1, ItemTag.None)),
        }));
        var enchantOptions = new Dictionary<int, EnchantOptionMetadata>();
        for (int level = 1; level <= 15; level++) {
            enchantOptions[level] = new EnchantOptionMetadata(level, 13, level, 5, 0.02f, 0, 100, [BasicAttribute.Defense]);
        }
        test.SetServerTable(new EnchantOptionTable(enchantOptions));
        scroll = test.Store(TestGameSession.Metadata(ScrollId, type: 1, slotMax: 1,
            function: new ItemMetadataFunction(ItemFunction.ItemExchangeScroll, "ItemExchangeScroll", $"{ScrollId}", false)));
        target = test.Store(TestGameSession.Metadata(targetId, type: 1, slotMax: 1));
        ItemMetadata rewardRow = TestGameSession.Metadata(RewardId, type: 1, slotMax: 1);
        test.Register(rewardRow with {
            Limit = rewardRow.Limit with { TransferType = rewardType, TradeMaxRarity = rewardTradeMaxRarity },
            Property = rewardRow.Property with { TradableCount = rewardTrades },
        });
        GameSession session = test.Start();
        test.TakeSent();
        scroll = session.Item.Inventory.Get(scroll.Uid)!;
        scroll.Rarity = 5;
        target = session.Item.Inventory.Get(target.Uid)!;
        target.Rarity = 5;
        target.Enchant = new ItemEnchant(enchants, 1234);
        var basic = new Dictionary<BasicAttribute, BasicOption>[5];
        basic[4] = new Dictionary<BasicAttribute, BasicOption> { [BasicAttribute.Strength] = new BasicOption(7) };
        target.Stats = new ItemStats(basic.Select(group => group ?? new Dictionary<BasicAttribute, BasicOption>()).ToArray(),
            [], CrystalId);
        return session;
    }

    private ItemExchangeScroll Handler(GameSession session) =>
        new() { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata };

    private void Exchange(GameSession session) {
        Handler(session).Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(scroll.Uid);
            writer.WriteLong(target.Uid);
            writer.WriteInt(1);
        }));
    }

    private void SetTarget(GameSession session) {
        Handler(session).Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(scroll.Uid);
            writer.WriteLong(target.Uid);
        }));
    }

    // The kinds of the ItemExchange answers, with the code of an error (kind 2, a short).
    private List<(byte Kind, short Code)> Answers() {
        var answers = new List<(byte, short)>();
        foreach (byte[] packet in test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemExchange)) {
            var reader = new ByteReader(packet, 2);
            byte kind = reader.ReadByte();
            answers.Add((kind, kind == 2 ? reader.ReadShort() : (short) -1));
        }
        return answers;
    }

    private static bool Held(GameSession session, BagItem item) => session.Item.Inventory.Get(item.Uid) != null;

    [Test]
    public void Upgrade_TakesTheTargetAndCarriesEnchantAndEmpowerment() {
        GameSession session = Start();
        Exchange(session);
        BagItem? reward = session.Item.Inventory.Find(RewardId).SingleOrDefault();
        Assert.Multiple(() => {
            Assert.That(Answers(), Is.EqualTo(new[] { ((byte) 2, (short) ItemExchangeScrollError.s_itemslot_exchange_ok) }));
            Assert.That(Held(session, target), Is.False);
            Assert.That(Held(session, scroll), Is.False);
            Assert.That(reward, Is.Not.Null);
            Assert.That(reward!.Enchant!.Enchants, Is.EqualTo(3));
            Assert.That(reward.Enchant.EnchantExp, Is.EqualTo(1234));
            // Three levels of the reward's own enchant option, 0.02 each.
            Assert.That(reward.Enchant.BasicOptions[BasicAttribute.Defense], Is.EqualTo(new BasicOption(60)));
            Assert.That(reward.Stats![ItemStats.Type.Empowerment].Basic[BasicAttribute.Strength], Is.EqualTo(new BasicOption(7)));
            Assert.That(reward.Stats.MergeId, Is.EqualTo(CrystalId));
        });
    }

    // Code 2: an item that is not the row's requireEquip.
    [Test]
    public void OtherItem_IsRefusedWithUpgradeInvalid() {
        GameSession session = Start(targetId: 11300918);
        Exchange(session);
        Assert.Multiple(() => {
            Assert.That(Answers(), Is.EqualTo(new[] { ((byte) 2, (short) ItemExchangeScrollError.s_itemslot_exchange_upgrade_invalid) }));
            Assert.That(Held(session, target), Is.True);
            Assert.That(Held(session, scroll), Is.True);
            Assert.That(session.Item.Inventory.Find(RewardId), Is.Empty);
        });
    }

    // Code 6: a locked target.
    [Test]
    public void LockedTarget_IsRefusedWithLockstate() {
        GameSession session = Start();
        target.IsLocked = true;
        Exchange(session);
        Assert.Multiple(() => {
            Assert.That(Answers(), Is.EqualTo(new[] { ((byte) 2, (short) ItemExchangeScrollError.s_itemslot_exchange_lockstate_item) }));
            Assert.That(Held(session, target), Is.True);
        });
    }

    // Code 5: an enchant level above the row's maxGrade; maxGrade itself is taken.
    [TestCase(11, ItemExchangeScrollError.s_itemslot_exchange_grade_invalid)]
    [TestCase(10, ItemExchangeScrollError.s_itemslot_exchange_ok)]
    public void EnchantAboveMaxGrade_IsRefusedWithGrade(int enchants, ItemExchangeScrollError answer) {
        GameSession session = Start(enchants: enchants);
        Exchange(session);
        Assert.That(Answers(), Is.EqualTo(new[] { ((byte) 2, (short) answer) }));
    }

    // Chosen here: a target with a gem in a socket is refused (2), since the reward carries no sockets over and the
    // gem would go with the target; an empty unlocked socket does not refuse. Before, the gem was lost.
    [Test]
    public void TargetWithAGem_IsRefusedWithUpgradeInvalid() {
        GameSession session = Start();
        target.Socket = new ItemSocket(3, 2);
        target.Socket.Sockets[1] = new ItemGemstone(40110001, stats: new ItemStats());
        Exchange(session);
        Assert.Multiple(() => {
            Assert.That(Answers(), Is.EqualTo(new[] { ((byte) 2, (short) ItemExchangeScrollError.s_itemslot_exchange_upgrade_invalid) }));
            Assert.That(Held(session, target), Is.True);
            Assert.That(Held(session, scroll), Is.True);
        });
        target.Socket.Sockets[1] = null;
        Exchange(session);
        Assert.That(Answers(), Is.EqualTo(new[] { ((byte) 2, (short) ItemExchangeScrollError.s_itemslot_exchange_ok) }));
    }

    // Chosen here: the reward keeps the stricter of the target's transfer and its own row's. Before, it came with a
    // fresh transfer, so a bound target came back unbound, and a limited one with its trades used up got them back.
    [Test]
    public void BoundTarget_TheRewardStaysBound() {
        GameSession session = Start(rewardTrades: 5);
        target.Transfer = new ItemTransfer(TransferFlag.Bind | TransferFlag.LimitTrade, 2, 1, new ItemBinding(test.CharacterId, "a"));
        Exchange(session);
        BagItem? reward = session.Item.Inventory.Find(RewardId).SingleOrDefault();
        Assert.Multiple(() => {
            Assert.That(reward?.Transfer?.Binding?.CharacterId, Is.EqualTo(test.CharacterId));
            Assert.That(reward?.Transfer?.Flag, Is.EqualTo(TransferFlag.Bind | TransferFlag.LimitTrade));
            Assert.That(reward?.Transfer?.RemainTrades, Is.EqualTo(2));
            Assert.That(reward?.Transfer?.RepackageCount, Is.EqualTo(1));
        });
    }

    // The reward is never more tradable than its own row: a freely tradable target gives an untradable reward when
    // the reward's row is untradable, and free trade only when both are. Before (the target's transfer copied), the
    // untradable reward came out tradable.
    [TestCase(TransferType.Untradeable, 4, TransferFlag.None)]
    [TestCase(TransferType.Tradable, 6, TransferFlag.Trade | TransferFlag.Split)]
    public void TradableTarget_TheRewardKeepsItsOwnRule(TransferType rewardType, int tradeMaxRarity, TransferFlag expected) {
        GameSession session = Start(rewardType: rewardType, rewardTradeMaxRarity: tradeMaxRarity);
        target.Transfer = new ItemTransfer(TransferFlag.Trade | TransferFlag.Split);
        Exchange(session);
        BagItem? reward = session.Item.Inventory.Find(RewardId).SingleOrDefault();
        Assert.Multiple(() => {
            Assert.That(reward?.Transfer?.Flag, Is.EqualTo(expected));
            Assert.That(reward?.Transfer?.RemainTrades, Is.Zero);
            Assert.That(reward?.Transfer?.Binding, Is.Null);
        });
    }

    // A requirement of the target's own id is not paid with the target: alone, it is refused and nothing is taken.
    // Before, the target went as the material, then its own Consume threw after the scroll's materials were gone.
    [Test]
    public void TargetIsNoMaterial() {
        GameSession session = Start(targetAsMaterial: true);
        Exchange(session);
        Assert.Multiple(() => {
            Assert.That(Answers(), Is.EqualTo(new[] { ((byte) 2, (short) ItemExchangeScrollError.s_itemslot_exchange_count_invalid) }));
            Assert.That(Held(session, target), Is.True);
            Assert.That(Held(session, scroll), Is.True);
            Assert.That(session.Item.Inventory.Find(RewardId), Is.Empty);
        });
    }

    // The target slot answers an item the row does not take with its code instead of the cost.
    [Test]
    public void SetTarget_OtherItem_AnswersUpgradeInvalid() {
        GameSession session = Start(targetId: 11300918);
        SetTarget(session);
        Assert.That(Answers(), Is.EqualTo(new[] { ((byte) 2, (short) ItemExchangeScrollError.s_itemslot_exchange_upgrade_invalid) }));
    }

    [Test]
    public void SetTarget_RequireEquip_SendsTheCost() {
        GameSession session = Start();
        SetTarget(session);
        Assert.That(Answers().Select(answer => answer.Kind), Is.EqualTo(new byte[] { 0 }));
    }
}
