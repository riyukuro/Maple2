using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The KR rules of an Ophelia try that the server's grade tables do not hold (constants.xml, the client's texts).
public class ItemEnchantKrRulesTests {
    private const int OnyxId = 40100023, ChaosOnyxId = 40100024, FragmentId = 40100001;
    private TestGameSession test = null!;
    private Item gear = null!;

    private static ItemMetadata Material(int id, ItemTag tag) {
        ItemMetadata metadata = TestGameSession.Metadata(id, type: 2, slotMax: 30000);
        return metadata with { Property = metadata.Property with { Tag = tag } };
    }

    private GameSession Login(int rarity, ItemEnchant enchant) {
        test = new TestGameSession();
        ItemMetadata gearMetadata = TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1);
        gear = test.Store(gearMetadata with { Limit = gearMetadata.Limit with { Level = 50 } });
        test.Store(Material(OnyxId, ItemTag.Onix), amount: 30000);
        test.Store(Material(ChaosOnyxId, ItemTag.ChaosOnix), amount: 30000);
        test.Store(Material(FragmentId, ItemTag.CrystalPiece), amount: 30000);
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        // KR constants.xml.
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            enchantSuccessBroadcastingLevel = 15,
            EnchantFailStackTakeMaxCount = 1000,
            EnchantEquipIngredientOpenLevel = 11,
            EnchantEquipIngredientOpenRank = 4,
        });
        GameSession session = test.Start();
        Item item = session.Item.Inventory.Get(gear.Uid)!;
        item.Rarity = rarity;
        item.Enchant = enchant;
        test.TakeSent();
        return session;
    }

    private static int Held(GameSession session, ItemTag tag) => session.Item.Inventory.Find(tag).Sum(item => item.Amount);

    // ItemEnchantPacket.Failure: mode 11, uid, the item, int, int, long, then the charges gained.
    private static int? FailureGain(IEnumerable<byte[]> sent) {
        byte[]? packet = sent.LastOrDefault(p => p.Length > 3 && (SendOp) (p[0] | p[1] << 8) == SendOp.ItemEnchant && p[2] == 11);
        if (packet == null) {
            return null;
        }
        return new ByteReader(packet, packet.Length - 4).ReadInt();
    }

    // A failure adds its grade's charges up to the stock limit of 1000 (EnchantFailStackTakeMaxCount); the result
    // shows what was added. Grade 9 fails 60% of tries; a success puts the item back to 9.
    [Test]
    public void Ophelia_FailChargesStopAtTheStockLimit() {
        GameSession session = Login(4, new ItemEnchant(enchants: 9, charges: 999));
        Item item = session.Item.Inventory.Get(gear.Uid)!;
        var gains = new List<int>();
        for (int i = 0; i < 60 && gains.Count < 2; i++) {
            item.Enchant!.Enchants = 9;
            Assert.That(session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid), Is.True);
            test.TakeSent();
            Assert.That(session.ItemEnchant.Enchant(gear.Uid), Is.True);
            if (item.Enchant.Enchants == 9) {
                gains.Add(FailureGain(test.TakeSent()) ?? -1);
            }
        }
        Assume.That(gains, Has.Count.EqualTo(2), "two failures in 60 tries at 40%");

        Assert.Multiple(() => {
            Assert.That(item.Enchant!.Charges, Is.EqualTo(1000));
            Assert.That(gains, Is.EqualTo(new[] { 1, 0 }));
            Assert.That(test.Errors, Is.Empty);
        });
    }
}
