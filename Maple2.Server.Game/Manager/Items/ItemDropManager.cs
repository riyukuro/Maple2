using Maple2.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using Maple2.Tools;

namespace Maple2.Server.Game.Manager.Items;

public class ItemDropManager {
    private readonly FieldManager field;

    public ItemDropManager(FieldManager field) {
        this.field = field;
    }

    // level is the NPC's level. It gates both group-level and item-level min/max level checks within
    // globalDropItemBox/globalDropItemSet (e.g. meso amounts in group 103 scale with NPC level).
    // session is the player the drop is rolled for: a quest-only entry (constraintsQuest) is for a player whose quest
    // still needs the item here (QuestManager.NeedsItem), and never drops when no player is given.
    public ICollection<Item> GetGlobalDropItems(int globalDropBoxId, int level = 0, GameSession? session = null) {
        if (!field.ServerTableMetadata.GlobalDropItemBoxTable.DropGroups.TryGetValue(globalDropBoxId, out Dictionary<int, IList<GlobalDropItemBoxTable.Group>>? dropGroup)) {
            return new List<Item>();
        }

        ICollection<Item> results = new List<Item>();

        foreach ((int groupId, IList<GlobalDropItemBoxTable.Group> list) in dropGroup) {
            foreach (GlobalDropItemBoxTable.Group group in list) {
                if (!field.ServerTableMetadata.GlobalDropItemBoxTable.Items.TryGetValue(group.GroupId, out IList<GlobalDropItemBoxTable.Item>? items)) {
                    continue;
                }

                // Check if NPC level meets the group's level requirements.
                if (group.MinLevel > level || (group.MaxLevel > 0 && group.MaxLevel < level)) {
                    continue;
                }

                // Check map and continent conditions.
                if (group.MapTypeCondition != 0 && group.MapTypeCondition != field.Metadata.Property.Type) {
                    continue;
                }

                if (group.ContinentCondition != 0 && group.ContinentCondition != field.Metadata.Property.Continent) {
                    continue;
                }

                // Implement OwnerDrop???

                var itemsAmount = new WeightedSet<GlobalDropItemBoxTable.Group.DropCount>();
                foreach (GlobalDropItemBoxTable.Group.DropCount dropCount in group.DropCounts) {
                    itemsAmount.Add(dropCount, dropCount.Probability);
                }

                int amount = itemsAmount.Count > 0 ? itemsAmount.Get().Amount : 0;
                if (amount == 0) {
                    continue;
                }

                var weightedItems = new WeightedSet<GlobalDropItemBoxTable.Item>();

                // Randomize list in order to get true random items when pulled from weightedItems.
                foreach (GlobalDropItemBoxTable.Item itemEntry in items.OrderBy(_ => Random.Shared.Next())) {
                    if (itemEntry.MinLevel > level || (itemEntry.MaxLevel > 0 && itemEntry.MaxLevel < level)) {
                        continue;
                    }

                    if (itemEntry.MapIds.Length > 0 && !itemEntry.MapIds.Contains(field.Metadata.Id)) {
                        continue;
                    }

                    if (itemEntry.QuestConstraint && (session == null || !session.Quest.NeedsItem(itemEntry.Id, field.Metadata.Id))) {
                        continue;
                    }
                    weightedItems.Add(itemEntry, itemEntry.Weight);
                }

                if (weightedItems.Count == 0) {
                    continue;
                }

                for (int i = 0; i < amount; i++) {
                    GlobalDropItemBoxTable.Item selectedItem = weightedItems.Get();

                    int itemAmount = Random.Shared.Next(selectedItem.DropCount.Min, Math.Max(selectedItem.DropCount.Min, selectedItem.DropCount.Max) + 1);
                    Item? createdItem = CreateItem(selectedItem.Id, selectedItem.Rarity, itemAmount);
                    if (createdItem == null) {
                        continue;
                    }

                    results.Add(createdItem);
                }
            }
        }

        return results;
    }

    // level gates which drop groups are eligible. For mob drops this is the player's character level
    // (MinLevel in individualDropItemTable is a player requirement, not NPC level — e.g. Shinjo's Feather
    // requires player level 30+). For other callers (e.g. housing rewards) it may be a different score.
    public ICollection<Item> GetIndividualDropItems(GameSession session, int level, int individualDropBoxId) {
        if (!field.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(individualDropBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return new List<Item>();
        }

        return GetAllGroups(session, level, entryDict.Values.ToList()).ToList();
    }

    // The items a select box (SelectItemBox) gives for the rows the player picked in its dialog, or none when a row
    // is not in the list the client showed.
    public ICollection<Item> GetSelectBoxItems(GameSession session, int individualDropBoxId, IReadOnlyCollection<int> rows) {
        if (!field.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(individualDropBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return new List<Item>();
        }

        Character character = session.Player.Value.Character;
        List<IndividualDropItemTable.Item> list = SelectBoxRows(entryDict.Values,
            itemId => field.ItemMetadata.TryGet(itemId, out ItemMetadata? metadata) ? metadata.Limit : null,
            character.Job.Code(), character.Gender);
        if (rows.Count == 0 || rows.Any(row => row < 0 || row >= list.Count)) {
            return new List<Item>();
        }

        return rows.SelectMany(row => CreateIndividualDropBoxItems(list[row], character)).ToList();
    }

    // The rows the client's select box dialog lists (ItemDropTable::GetBoxItems with the player's job group and
    // gender), which the index it sends counts: every group in table order, each with its entries in order. An entry
    // whose item has no data is left out; a smart gender group leaves out the other gender's items, and a group with a
    // smart drop rate the items not recommended for the player's job. An entry with a second item id is two rows (the
    // client lists the second item as a row of its own, with the entry's count, rank and enchant level); each row gives
    // only the item it shows. A random open of the box gives both.
    // The group's drop counts and minimum level are not looked at.
    internal static List<IndividualDropItemTable.Item> SelectBoxRows(IEnumerable<IndividualDropItemTable.Entry> groups,
            Func<int, ItemMetadataLimit?> limit, JobCode job, Gender gender) {
        var rows = new List<IndividualDropItemTable.Item>();
        foreach (IndividualDropItemTable.Entry group in groups) {
            foreach (IndividualDropItemTable.Item itemEntry in group.Items) {
                ItemMetadataLimit? itemLimit = limit(itemEntry.Ids.FirstOrDefault());
                if (itemLimit == null) {
                    continue;
                }
                if (group.SmartGender && itemLimit.Gender != Gender.All && itemLimit.Gender != gender) {
                    continue;
                }
                if (group.SmartDropRate > 0 && !IsRecommendedInSelectBox(itemLimit.JobRecommends, job)) {
                    continue;
                }

                if (itemEntry.Ids.Length > 1 && itemEntry.Ids[1] != 0) {
                    rows.Add(itemEntry with { Ids = [itemEntry.Ids[0]] });
                    rows.Add(itemEntry with { Ids = [itemEntry.Ids[1]] });
                } else {
                    rows.Add(itemEntry);
                }
            }
        }

        return rows;
    }

    // ItemLimit::IsJobRecommended: an item without recommendJobs (stored as [None]) is for every job; a beginner
    // counts as a knight.
    private static bool IsRecommendedInSelectBox(JobCode[] recommends, JobCode job) {
        if (recommends is [] or [JobCode.None]) {
            return true;
        }

        return recommends.Contains(job == JobCode.Newbie ? JobCode.Knight : job);
    }

    // A box rolled without a player (trigger item spawns, which anyone may pick up): each group rolled as for a player,
    // by the entries' own weights; entries for a quest or another map are skipped.
    public ICollection<Item> GetIndividualDropItems(int individualDropBoxId) {
        if (!field.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(individualDropBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return new List<Item>();
        }

        return PickBoxEntriesWithoutPlayer(entryDict.Values, field.Metadata.Id)
            .SelectMany(itemEntry => CreateIndividualDropBoxItems(itemEntry)).ToList();
    }

    // A box a trigger spawns, rolled with the players on the field. The client rolls a box group for one player
    // (ItemDropTable::DropIndividualGroup, commonfiles/itemdroptable.cpp:1560, with that player's ItemDropContext): one
    // picker over the rows that player may get, and its drop count (PickDropCount) once, quest-only rows and the others
    // alike. So each player whose started quest still needs one of a group's quest-only rows (constraintsQuest) on this
    // map (QuestManager.NeedsItem) rolls the group for themselves, over the plain rows and the quest rows they need; a
    // picked quest row drops as their own item, so two players needing different items of one group each get theirs.
    // A group no player needs a quest row of is rolled once over its plain rows.
    // Chosen here: the plain rows drop once for the field, as shared items: those the first such player's roll picks;
    // the other players' rolls give only their quest rows.
    public SpawnBoxDrops GetSpawnBoxItems(int individualDropBoxId, IReadOnlyCollection<GameSession> players) {
        var drops = new SpawnBoxDrops([], []);
        if (!field.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(individualDropBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return drops;
        }

        foreach (IndividualDropItemTable.Entry group in entryDict.Values) {
            List<GameSession> questers = players
                .Where(session => group.Items.Any(itemEntry => itemEntry.QuestConstraint && IsEntryAvailable(session, itemEntry)))
                .ToList();
            if (questers.Count == 0) {
                foreach (IndividualDropItemTable.Item itemEntry in PickSpawnGroup(group, null)) {
                    drops.Shared.AddRange(CreateIndividualDropBoxItems(itemEntry));
                }
                continue;
            }

            for (int i = 0; i < questers.Count; i++) {
                GameSession session = questers[i];
                foreach (IndividualDropItemTable.Item itemEntry in PickSpawnGroup(group, session)) {
                    if (itemEntry.QuestConstraint) {
                        drops.Owned.AddRange(CreateIndividualDropBoxItems(itemEntry, session.Player.Value.Character).Select(item => (session, item)));
                    } else if (i == 0) {
                        drops.Shared.AddRange(CreateIndividualDropBoxItems(itemEntry));
                    }
                }
            }
        }
        return drops;
    }

    // One roll of a spawn box group by the rows' own weights: the plain rows for this map, and the quest-only rows the
    // player needs (none without a player).
    private IEnumerable<IndividualDropItemTable.Item> PickSpawnGroup(IndividualDropItemTable.Entry group, GameSession? session) {
        return PickGroupEntries(group, group.Items,
            itemEntry => itemEntry.QuestConstraint
                ? session != null && IsEntryAvailable(session, itemEntry)
                : itemEntry.MapIds.Length == 0 || itemEntry.MapIds.Contains(field.Metadata.Id),
            _ => false, itemEntry => itemEntry.Weight);
    }

    public record SpawnBoxDrops(List<Item> Shared, List<(GameSession Session, Item Item)> Owned);

    internal static IEnumerable<IndividualDropItemTable.Item> PickBoxEntriesWithoutPlayer(IEnumerable<IndividualDropItemTable.Entry> groups, int mapId) {
        return PickBoxEntries(groups, mapId, _ => false);
    }

    // Each group rolled once by the entries' own weights; an entry for another map is skipped, a quest-only entry
    // unless questNeeded says a player needs it.
    internal static IEnumerable<IndividualDropItemTable.Item> PickBoxEntries(IEnumerable<IndividualDropItemTable.Entry> groups, int mapId,
            Func<IndividualDropItemTable.Item, bool> questNeeded, Random? random = null) {
        return groups.SelectMany(group => PickGroupEntries(group, group.Items,
            itemEntry => (itemEntry.MapIds.Length == 0 || itemEntry.MapIds.Contains(mapId)) && (!itemEntry.QuestConstraint || questNeeded(itemEntry)),
            _ => false, itemEntry => itemEntry.Weight, random));
    }

    private IEnumerable<Item> GetAllGroups(GameSession session, int level, List<IndividualDropItemTable.Entry> entry) {
        var items = new List<Item>();
        foreach (IndividualDropItemTable.Entry group in entry) {
            if (group.MinLevel > level) {
                continue;
            }

            ICollection<IndividualDropItemTable.Item> itemEntries = group.Items;
            if (group.SmartGender) {
                itemEntries = GetGenderedEntries(itemEntries, session.Player.Value.Character.Gender).ToList();
            }

            JobCode job = session.Player.Value.Character.Job.Code();
            IEnumerable<IndividualDropItemTable.Item> picked = PickGroupEntries(group, itemEntries,
                itemEntry => IsEntryAvailable(session, itemEntry),
                itemId => IsItemJobRecommended(itemId, job),
                itemEntry => GetWeightByJob(itemEntry.Ids.FirstOrDefault(), job, itemEntry.Weight, itemEntry.ProperJobWeight, itemEntry.ImproperJobWeight));
            foreach (IndividualDropItemTable.Item selectedItem in picked) {
                items = items.Concat(CreateIndividualDropBoxItems(selectedItem, session.Player.Value.Character)).ToList();
            }
        }

        return items;
    }

    // The group's drop count: one of its DropCounts rows by probability (a row without a count gives nothing).
    private static int RollDropCount(IndividualDropItemTable.Entry group, Random? random = null) {
        var itemsAmount = new WeightedSet<IndividualDropItemTable.Entry.DropCount>(random);
        foreach (IndividualDropItemTable.Entry.DropCount dropCount in group.DropCounts) {
            itemsAmount.Add(dropCount, dropCount.Probability);
        }
        return itemsAmount.Count > 0 ? itemsAmount.Get().Count : 0;
    }

    private bool IsEntryAvailable(GameSession session, IndividualDropItemTable.Item itemEntry) {
        // A quest-only row (constraintsQuest; KR has 432) drops only for a player whose started quest still needs the
        // item here, as a quest-only global drop does (QuestManager.NeedsItem).
        if (itemEntry.QuestConstraint && !session.Quest.NeedsItem(itemEntry.Ids[0], field.Metadata.Id)) {
            return false;
        }
        return itemEntry.MapIds.Length == 0 || itemEntry.MapIds.Contains(field.Metadata.Id);
    }

    // The entries one rolled group of an individual drop box gives: its drop count rolled, then that many entries
    // picked by weight (by the player's job when the group has a smart drop rate). A smart drop group whose entries
    // have no weight at all (neither the plain nor the job weights) gives one of the available entries for the
    // player's job, each as likely, once. A smart group with job weights only is rolled by them like any other.
    internal static IEnumerable<IndividualDropItemTable.Item> PickGroupEntries(IndividualDropItemTable.Entry group,
            ICollection<IndividualDropItemTable.Item> itemEntries, Func<IndividualDropItemTable.Item, bool> available,
            Func<int, bool> jobRecommended, Func<IndividualDropItemTable.Item, int> jobWeight, Random? random = null) {
        random ??= Random.Shared;
        int amount = RollDropCount(group, random);
        if (amount == 0) {
            yield break;
        }

        if (group.SmartDropRate > 0 && itemEntries.All(x => x.Weight == 0 && x.ProperJobWeight == 0 && x.ImproperJobWeight == 0)) {
            List<IndividualDropItemTable.Item> jobEntries = itemEntries
                .Where(x => x.Ids.Length > 0 && jobRecommended(x.Ids.FirstOrDefault()) && available(x)).ToList();
            if (jobEntries.Count > 0) {
                yield return jobEntries[random.Next(jobEntries.Count)];
            }
            yield break;
        }

        // A smart group whose rows carry the plain weight only (no row has a job weight, as in box 330002235's
        // group 1) is rolled by that weight; the job weights are what carries the job preference.
        bool jobWeights = group.SmartDropRate > 0 && itemEntries.Any(x => x.ProperJobWeight != 0 || x.ImproperJobWeight != 0);
        var weightedItems = new WeightedSet<IndividualDropItemTable.Item>(random);
        foreach (IndividualDropItemTable.Item itemEntry in itemEntries.OrderBy(_ => random.Next()).ToList()) {
            if (!available(itemEntry)) {
                continue;
            }
            if (!jobWeights) {
                weightedItems.Add(itemEntry, itemEntry.Weight);
                continue;
            }
            // A smart drop entry without a weight for the player's job never drops (when none has one the group gives
            // nothing, instead of another job's entry).
            int weight = jobWeight(itemEntry);
            if (weight > 0) {
                weightedItems.Add(itemEntry, weight);
            }
        }

        if (weightedItems.Count == 0) {
            yield break;
        }

        for (int i = 0; i < amount; i++) {
            yield return weightedItems.Get();
        }
    }

    private IEnumerable<Item> CreateIndividualDropBoxItems(IndividualDropItemTable.Item selectedItem, Character? character = null, int rarity = -1) {
        int itemAmount = RollAmount(selectedItem);

        if (rarity <= 0) {
            rarity = RollRarity(selectedItem);
        }

        foreach (int itemId in selectedItem.Ids) {
            Item? createdItem = CreateItem(itemId, rarity, itemAmount);
            if (createdItem == null) {
                continue;
            }

            if (createdItem.Transfer?.RemainTrades > 0 && selectedItem.DeductTradeCount) {
                createdItem.Transfer.RemainTrades--;
            }

            if (selectedItem.DeductRepackLimit && createdItem.Transfer != null) {
                createdItem.Transfer.RepackageCount++;
            }

            if (selectedItem.Bind && character != null) {
                createdItem.Transfer?.Bind(character);
            }

            if (selectedItem.EnchantLevel > 0) {
                createdItem.Enchant = ItemEnchantManager.GetEnchant(field.ServerTableMetadata.EnchantOptionTable, createdItem, selectedItem.EnchantLevel);
            }

            createdItem.AnnounceOnOpen = selectedItem.Announce;

            // The row's socketDataID replaces the item's own socket row (23 of today's 49 rows differ from the item's,
            // e.g. accessories without one get 1003-1005).
            if (selectedItem.SocketDataId != 0) {
                ItemSocket? sockets = field.ItemStatsCalc.GetSockets(createdItem, selectedItem.SocketDataId);
                if (sockets != null) {
                    createdItem.Socket = sockets;
                    if (createdItem.Transfer != null) {
                        createdItem.Transfer.SocketTransfer = field.ItemStatsCalc.SocketTransferAtCreation(createdItem, selectedItem.SocketDataId);
                    }
                }
            }

            // A drop row with disableBreak sets CItemTransfer m_b3F (no dismantle, CItemBreakManager::CanBreakItem).
            if (selectedItem.DisableBreak && createdItem.Transfer != null) {
                createdItem.Transfer.DisableBreak = true;
            }

            yield return createdItem;
        }
    }

    // The entry's count: one of minCount..maxCount, each as likely. A row without a maxCount (or with one below its
    // minCount) gives its minCount.
    internal static int RollAmount(IndividualDropItemTable.Item itemEntry, Random? random = null) {
        int min = itemEntry.DropCount.Min;
        int max = Math.Max(min, itemEntry.DropCount.Max);
        return (random ?? Random.Shared).Next(min, max + 1);
    }

    // The entry's rank: one of its rarity rows by probability, or -1 (the item's own rank) without rows.
    internal static int RollRarity(IndividualDropItemTable.Item itemEntry, Random? random = null) {
        var raritySet = new WeightedSet<IndividualDropItemTable.Item.Rarity>(random);
        foreach (IndividualDropItemTable.Item.Rarity rarityEntry in itemEntry.Rarities) {
            raritySet.Add(rarityEntry, rarityEntry.Probability);
        }

        return raritySet.Count > 0 ? raritySet.Get().Grade : -1;
    }

    private IEnumerable<IndividualDropItemTable.Item> GetGenderedEntries(ICollection<IndividualDropItemTable.Item> itemEntries, Gender gender) {
        foreach (IndividualDropItemTable.Item itemEntry in itemEntries) {
            if (!field.ItemMetadata.TryGet(itemEntry.Ids.FirstOrDefault(), out ItemMetadata? itemMetadata)) {
                continue;
            }

            if (itemMetadata.Limit.Gender != Gender.All && itemMetadata.Limit.Gender != gender) {
                continue;
            }

            yield return itemEntry;
        }
    }

    private int GetWeightByJob(int itemId, JobCode job, int weight, int jobWeight, int improperJobWeight) {
        if (!field.ItemMetadata.TryGet(itemId, out ItemMetadata? itemMetadata)) {
            return weight;
        }

        return JobWeight(itemMetadata.Limit.JobRecommends, job, weight, jobWeight, improperJobWeight);
    }

    // A smart drop row's weight for a job: properJobWeight when the item recommends the job (or every job),
    // improperJobWeight when it recommends other jobs, the plain weight when it recommends none. A box made of
    // one group per job (KR 320000497: 11 groups of one row, each with a plain weight too) shows a row of
    // another job is not rolled by its plain weight: the box would give every job's item.
    internal static int JobWeight(JobCode[] recommends, JobCode job, int weight, int properJobWeight, int improperJobWeight) {
        if (recommends.Length == 0) {
            return weight;
        }
        if (!recommends.Contains(job) && !recommends.Contains(JobCode.None)) {
            return improperJobWeight;
        }
        return properJobWeight;
    }

    private bool IsItemJobRecommended(int itemId, JobCode job) {
        if (!field.ItemMetadata.TryGet(itemId, out ItemMetadata? itemMetadata)) {
            return false;
        }

        if (itemMetadata.Limit.JobRecommends.Length == 0) {
            return true;
        }

        return itemMetadata.Limit.JobRecommends.Contains(job) || itemMetadata.Limit.JobRecommends.Contains(JobCode.None);
    }

    public Item? CreateItem(int itemId, int rarity = -1, int amount = 1, bool rollMax = false) {
        if (!field.ItemMetadata.TryGet(itemId, out ItemMetadata? itemMetadata) || !PlayedClientItems.CanCreate(itemMetadata)) {
            return null;
        }

        if (rarity <= 0) {
            rarity = Item.DefaultRarity(itemMetadata);
        }

        var item = new Item(itemMetadata, rarity, amount);
        item.Stats = field.ItemStatsCalc.GetStats(item, rollMax);
        item.Socket = field.ItemStatsCalc.GetSockets(item);
        if (item.Transfer != null) {
            item.Transfer.SocketTransfer = field.ItemStatsCalc.SocketTransferAtCreation(item);
        }

        if (item.Appearance != null) {
            item.Appearance.Color = GetColor(item.Metadata.Customize);
        }

        return item;
    }

    private EquipColor GetColor(ItemMetadataCustomize metadata) {
        // Item has no color
        if (metadata.ColorPalette == 0 ||
            !field.TableMetadata.ColorPaletteTable.Entries.TryGetValue(metadata.ColorPalette, out IReadOnlyDictionary<int, ColorPaletteTable.Entry>? palette)) {
            return EquipColor.None;
        }

        // Item has random color
        if (metadata.DefaultColorIndex < 0) {
            // random entry from palette
            int index = Random.Shared.Next(palette.Count);
            ColorPaletteTable.Entry randomEntry = palette.Values.ElementAt(index);
            return new EquipColor(randomEntry.Primary, randomEntry.Secondary, randomEntry.Tertiary, metadata.ColorPalette, index);
        }

        // Item has specified color
        if (palette.TryGetValue(metadata.DefaultColorIndex, out ColorPaletteTable.Entry? entry)) {
            return new EquipColor(entry.Primary, entry.Secondary, entry.Tertiary, metadata.ColorPalette, metadata.DefaultColorIndex);
        }

        return EquipColor.None;
    }
}
