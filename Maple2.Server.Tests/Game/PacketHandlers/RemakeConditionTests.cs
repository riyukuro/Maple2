using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// KR 89020030: <condition type="item_remake_option" value="1" /> (a quest) and achieve 23200038 grade 1:
// <condition type="pet_remake_option" value="1" />. A paid ChangeAttributes 0 (the npc remake of a gear item, the pet
// remake page of a pet) counts the row of its kind only: the gear change the quest, the pet change the trophy.
public class RemakeConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int EarringId = 11200002;
    private const int PetId = 61000001;
    private const int RandomId = 9001;

    private static ItemMetadata Tagged(int id, ItemTag tag) {
        ItemMetadata metadata = TestGameSession.Metadata(id, type: 4, slotMax: 9999);
        return metadata with { Property = metadata.Property with { Tag = tag } };
    }

    private static ItemMetadata WithOption(ItemMetadata metadata) => metadata with {
        Limit = metadata.Limit with { Level = 50 },
        Option = new ItemMetadataOption(0, default, RandomId, default, 0, default, 0, 0),
    };

    [TestCase(false, 1L, null)]
    [TestCase(true, 0L, 1L)]
    public void Change_89020030_23200038_CountsItsKind(bool pet, long questCount, long? trophyCount) {
        var test = new TestGameSession();
        test.AddAchievement(TestGameSession.Trophy(23200038, ConditionType.pet_remake_option, [1, 30]));
        var entries = new ItemOption.Entry[] {
            new(BasicAttribute.Strength, Values: new ItemOption.Range<int>(1, 1)),
            new(BasicAttribute.Health, Values: new ItemOption.Range<int>(1, 1)),
        };
        var option = new ItemOption(1f, new ItemOption.Range<int>(2, 2), entries);
        test.SetTable(new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>> {
            [RandomId] = new Dictionary<int, ItemOption> { [4] = option },
        }));
        var variation = new ItemEquipVariationTable(new Dictionary<BasicAttribute, ItemEquipVariationTable.Set<int>[]>(),
            new Dictionary<BasicAttribute, ItemEquipVariationTable.Set<float>[]>(),
            new Dictionary<SpecialAttribute, ItemEquipVariationTable.Set<int>[]>(),
            new Dictionary<SpecialAttribute, ItemEquipVariationTable.Set<float>[]>());
        foreach (string name in new[] { "accVariationTable", "petVariationTable" }) {
            typeof(Maple2.Database.Storage.TableMetadataStorage).GetField(name, Any)!
                .SetValue(test.TableMetadata, new System.Lazy<ItemEquipVariationTable>(() => variation));
        }

        Item stored;
        if (pet) {
            test.RegisterPet(PetId);
            ItemMetadata metadata = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
            stored = test.Store(WithOption(metadata with { Property = metadata.Property with { PetId = PetId } }), rarity: 4);
        } else {
            stored = test.Store(WithOption(TestGameSession.Metadata(EarringId, slot: EquipSlot.EA, type: 1, slotMax: 1)), rarity: 4);
        }
        test.Store(Tagged(40100001, ItemTag.CrystalPiece), 9999);
        test.Store(Tagged(40100002, ItemTag.RedCrystal), 9999);
        test.Store(Tagged(40100003, ItemTag.MetaCell), 9999);
        test.Store(Tagged(40100004, ItemTag.GreenCrystal), 9999);
        GameSession session = test.Start();
        session.Currency.Meso = 100_000_000;
        Quest quest = test.StartQuest(TestGameSession.QuestData(89020030) with {
            Conditions = [new ConditionMetadata(ConditionType.item_remake_option, 1, null, null)],
        });
        Item item = session.Item.Inventory.Get(stored.Uid)!;
        item.Stats = new ItemStats();
        item.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Strength] = new BasicOption(1);
        item.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Health] = new BasicOption(1);
        test.TakeSent();

        new ChangeAttributesHandler { ItemStatsCalc = new ItemStatsCalculator { TableMetadata = test.TableMetadata } }.Handle(session,
            TestGameSession.Packet(writer => {
                writer.WriteByte(0);
                writer.WriteLong(item.Uid);
                writer.WriteLong(0);
                writer.WriteByte(0);
            }));

        Assert.That(session.ChangeAttributesItem?.Uid, Is.EqualTo(item.Uid));
        Assert.That(quest.Conditions.Values.Single().Counter, Is.EqualTo(questCount));
        Assert.That(test.TrophyCounter(23200038), Is.EqualTo(trophyCount));
    }
}
