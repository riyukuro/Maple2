using M2dXmlGenerator;
using Maple2.Database.Extensions;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.File.Parser;
using Maple2.File.Parser.Xml.Item;
using Maple2.File.Parser.Xml.Table;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.File.Ingest.Utils;
using System.Xml;
using Slot = Maple2.File.Parser.Xml.Item.Slot;

namespace Maple2.File.Ingest.Mapper;

public class ItemMapper : TypeMapper<ItemMetadata> {
    private readonly ItemParser parser;
    private readonly TableParser tableParser;
    private readonly bool newXml;
    private readonly M2dReader? playedReader;
    private readonly string language;

    public static Dictionary<int, ItemMetadata> ItemMetadataById { get; } = new();

    public ItemMapper(M2dReader xmlReader, string language, bool newXml, M2dReader? playedReader = null) {
        parser = new ItemParser(xmlReader, language);
        tableParser = PackTables.NewParser(xmlReader, language);
        this.newXml = newXml;
        this.playedReader = playedReader;
        this.language = language;
    }

    protected override IEnumerable<ItemMetadata> Map() {
        // The items the played client has item data for (PlayedClientItems); null without a played client or when
        // its pack has no item data, and then no item is marked.
        HashSet<int>? known = null;
        Dictionary<int, string>? playedTags = null;
        Dictionary<int, (int Type, int SubType)>? playedTypes = null;
        Dictionary<int, int>? playedRanks = null;
        Func<XmlElement, bool> available = node =>
            FeatureLocaleFilter.FeatureEnabled(node.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(node.GetAttribute("locale"));
        if (playedReader != null) {
            List<XmlDocument> itemFiles = playedReader.Files.Where(file => PlayedClientItems.IsItemFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)).ToList();
            known = PlayedClientItems.Ids(itemFiles, available);
            if (known.Count == 0) {
                known = null;
            } else {
                // The option-lock tags the played client tests (PlayedClientItems.LockTag).
                playedTags = PlayedClientItems.Tags(itemFiles, available);
                // The types that decide the client's bag tab (PlayedClientItems.ClientType).
                playedTypes = PlayedClientItems.Types(itemFiles, available);
                // The ranks the client takes as the rarity of every copy (ItemMetadataProperty.ClientRank).
                playedRanks = PlayedClientItems.Ranks(itemFiles, available);
            }
        }
        int notInPlayed = 0, inPlayed = 0, lockTagsChanged = 0, typesChanged = 0, clientRanks = 0, clientRanksDiffer = 0, playedUnknown = 0;

        Dictionary<int, int> itemExtractionTryCount = tableParser.ParseItemExtraction()
            .ToDictionary(entry => entry.Id, entry => entry.Extraction.TryCount);

        var itemSetBonuses = new Dictionary<int, List<int>>();
        foreach ((int id, _, SetItemInfo info) in tableParser.ParseSetItemInfo()) {
            foreach (int itemId in info.itemIDs) {
                itemSetBonuses.TryAdd(itemId, []);
                itemSetBonuses[itemId].Add(id);
            }
        }

        bool skinSocketEnabled = FeatureLocaleFilter.FeatureEnabled("SkinSocket");
        bool moveDisableEnabled = FeatureLocaleFilter.FeatureEnabled("ItemStoreMoveDisable01");
        HashSet<int> packIds = [];
        foreach ((int id, string name, ItemData data) in parser.Parse()) {
            packIds.Add(id);
            ItemMetadata itemMetadata = Build(id, name, data, false);
            ItemMetadataById[id] = itemMetadata;
            if (known != null) {
                if (itemMetadata.Property.NotInPlayedClient) notInPlayed++;
                else inPlayed++;
            }

            yield return itemMetadata;
        }

        // The items only the played client has: the data folder's pack (a development pack) lacks them, and the
        // client shows them when the server sends them (boxes, gear, furniture and scrolls added after the pack).
        // They are read from the played client's itemdata; an item both have keeps the pack's row.
        int playedOnly = 0, playedNoEnvironment = 0, playedOptions = 0, playedNoModel = 0, playedHousing = 0;
        int playedSetRows = 0, playedExtractionRows = 0;
        var playedByRange = new SortedDictionary<int, int>();
        if (playedReader != null && known != null) {
            Dictionary<int, string> playedNames = new ItemParser(playedReader, PackTables.PlayedStringFolder(playedReader.Files.Select(file => file.Name), language, "itemname.xml")).ItemNames();
            Dictionary<int, XmlElement> models = PlayedModels();
            HashSet<int> playedSetItems = PlayedTableIds("table/setiteminfo.xml", "set", "itemIDs", available);
            HashSet<int> playedExtractionItems = PlayedTableIds("table/itemextraction.xml", "key", "TargetItemID", available);
            foreach ((int id, XmlElement? environment) in PlayedOnlyEnvironments(packIds, available)) {
                if (environment == null) {
                    playedNoEnvironment++;
                    continue;
                }
                XmlElement? model = null;
                if (int.TryParse(environment["tool"]?.GetAttribute("itemPreset"), out int preset) && preset != 0
                    && !models.TryGetValue(preset, out model) && environment["slots"] == null) {
                    playedNoModel++;
                }
                if (PlayedItemData.HasClientOption(environment)) playedOptions++;
                ItemData data = PlayedItemData.Read(environment, model);
                ItemMetadata itemMetadata = Build(id, playedNames.GetValueOrDefault(id, string.Empty), data, true);
                if (itemMetadata.Housing != null) playedHousing++;
                if (playedSetItems.Contains(id) && itemMetadata.Property.SetOptionIds.Length == 0) playedSetRows++;
                if (playedExtractionItems.Contains(id) && !itemExtractionTryCount.ContainsKey(id)) playedExtractionRows++;
                ItemMetadataById[id] = itemMetadata;
                playedOnly++;
                playedByRange[id / 1000000] = playedByRange.GetValueOrDefault(id / 1000000) + 1;
                yield return itemMetadata;
            }
        }

        if (known != null) {
            Console.WriteLine($"items: {notInPlayed} not in the played client's item data (the server creates none of them), {inPlayed} in it");
            Console.WriteLine($"items: {lockTagsChanged} option-lock tags taken from the played client's item data");
            Console.WriteLine($"items: {typesChanged} types taken from the played client's item data (another bag tab)");
            Console.WriteLine($"items: {clientRanks} client ranks from the played client's item data ({clientRanksDiffer} differ from the server pack's rank)");
            Console.WriteLine($"items: {playedOnly} only in the played client's item data, read from it (by id / 1000000: "
                + string.Join(", ", playedByRange.Select(entry => $"{entry.Key}: {entry.Value}")) + ")");
            Console.WriteLine($"items: {playedNoEnvironment} only in the played client's item data skipped (no environment the client keeps)");
            Console.WriteLine($"items: of those read, {playedOptions} name client option rows (left out: no options), "
                + $"{playedNoModel} name a model the played client lacks (no slots), {playedUnknown} name a function, slot or tag the server lacks (left out), "
                + $"{playedHousing} are furniture with no housing row (defaults), {playedSetRows} are in the played client's set table "
                + $"but in no server set, {playedExtractionRows} are in its extraction table but not the server's");
        }

        ItemMetadata Build(int id, string name, ItemData data, bool played) {
            int transferType = data.limit.transferType;
            int tradableCount = data.property.tradableCount;
            int tradableCountDeduction = data.property.tradableCountDeduction;
            int repackingLimitCount = data.property.rePackingLimitCount;
            int repackingItemConsumeCount = data.property.rePackingItemConsumeCount;
            int[] repackingScrollIds = data.property.globalRePackingScrollID;
            if (FeatureLocaleFilter.FeatureEnabled("GlobalTransferType")) {
                transferType = data.limit.globalTransferType ?? transferType;
                tradableCount = data.property.globalTradableCount ?? tradableCount;
                tradableCountDeduction = data.property.globalTradableCountDeduction ?? tradableCountDeduction;
                repackingLimitCount = data.property.globalRePackingLimitCount ?? repackingLimitCount;
                repackingItemConsumeCount = data.property.globalRePackingItemConsumeCount ?? repackingItemConsumeCount;
                repackingScrollIds = data.property.globalRePackingScrollID ?? repackingScrollIds;
            }
            if (FeatureLocaleFilter.FeatureEnabled("GlobalTransferTypeNA")) {
                transferType = data.limit.globalTransferTypeNA ?? transferType;
                tradableCount = data.property.globalTradableCountNA ?? tradableCount;
            }

            long expirationTimestamp = 0;
            if (data.life.expirationPeriod.Length > 0) {
                expirationTimestamp = new DateTime(data.life.expirationPeriod[0], data.life.expirationPeriod[1], data.life.expirationPeriod[2], data.life.expirationPeriod[3], data.life.expirationPeriod[4],
                    data.life.expirationPeriod[5]).ToEpochSeconds();
            }

            // A weeks or months item ends on the calendar (Item.CalendarExpiry, from the type and count kept below); the
            // duration only marks it as timed. A count of 0 is 1, as the client's tooltip counts it.
            long expirationDuration = 0;
            int weeksMonths = Math.Max(data.life.numberOfWeeksMonths, 1);
            if (data.life.expirationType > 0) {
                expirationDuration = data.life.expirationType switch {
                    1 => // Week
                        DateTime.UnixEpoch.AddDays(7 * weeksMonths).ToEpochSeconds(),
                    2 => // Month
                        DateTime.UnixEpoch.AddMonths(1 * weeksMonths).ToEpochSeconds(),
                    _ => 0,
                };
            } else if (data.life.usePeriod > 0) {
                expirationDuration = (long) TimeSpan.FromMinutes(data.life.usePeriod).TotalSeconds;
            }

            var hairList = new List<DefaultHairMetadata>();
            // parse default hair positions
            foreach (Slot slot in data.slots.slot.Where(dataSlots => dataSlots.name == "HR")) {

                // not sure what the difference/significance is within the multiple scale entries. Currently just using the first one
                float minScale = slot.scale.ElementAtOrDefault(0)?.min ?? 0f;
                float maxScale = slot.scale.ElementAtOrDefault(0)?.max ?? 0f;
                switch (slot.asset.Count) {
                    case 3: // Hair has front and back positionable hair section
                        for (int index = 0; index < slot.asset[1].custom.Count; index++) {
                            hairList.Add(new DefaultHairMetadata(
                                BackPosition: slot.asset[1].custom[index].position,
                                BackRotation: slot.asset[1].custom[index].rotation,
                                FrontPosition: slot.asset[2].custom[index].position,
                                FrontRotation: slot.asset[2].custom[index].rotation,
                                MinScale: minScale,
                                MaxScale: maxScale));
                        }
                        break;
                    case 2: // Hair has one positionable hair section
                        foreach (Slot.Custom custom in slot.asset[1].custom) {
                            hairList.Add(new DefaultHairMetadata(
                                BackPosition: custom.position,
                                BackRotation: custom.rotation,
                                MinScale: minScale,
                                MaxScale: maxScale));
                        }
                        break;
                    default: // No positionable hair section
                        hairList.Add(new DefaultHairMetadata());
                        break;
                }
            }

            ItemMetadataSkill? skill = data.skill.skillID == 0 && data.objectWeaponSkill.skillID == 0 ? null : new ItemMetadataSkill(
                Id: data.skill.skillID,
                Level: data.skill.skillID != 0 ? data.skill.skillLevel : (short) 0,
                WeaponId: data.objectWeaponSkill.skillID,
                WeaponLevel: data.objectWeaponSkill.skillID != 0 ? data.objectWeaponSkill.skillLevel : (short) 0);
            ItemFunction functionType = ItemFunction.None;
            bool hasFunction = !string.IsNullOrWhiteSpace(data.function.name);
            if (hasFunction && played && !Enum.TryParse(data.function.name, out functionType)) {
                hasFunction = false;
                playedUnknown++;
            } else if (hasFunction && !played) {
                functionType = Enum.Parse<ItemFunction>(data.function.name);
            }
            ItemMetadataFunction? function = !hasFunction ? null : new ItemMetadataFunction(
                Type: functionType,
                Name: data.function.name, // Temp duplicate data makes it easier to read DB
                Parameters: data.function.parameter,
                OnlyShadowWorld: data.function.onlyShadowContinent == 1);

            bool hasOption = data.option.@static > 0 || data.option.constant > 0 || data.option.random > 0 || data.option.optionID > 0;
            int levelFactor = (int) data.option.optionLevelFactor;
            if (FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd01") || FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd03") || FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd04")) {
                levelFactor = (int) (data.option.globalOptionLevelFactor ?? levelFactor);
            }
            ItemMetadataOption? option = !hasOption ? null : new ItemMetadataOption(
                StaticId: data.option.@static,
                StaticType: (ItemOptionMakeType) data.option.staticMakeType,
                RandomId: data.option.random,
                ItemOptionType: (ItemOptionMakeType) data.option.randomMakeType,
                ConstantId: data.option.constant,
                ConstantType: (ItemOptionMakeType) data.option.constantMakeType,
                LevelFactor: levelFactor,
                PickId: data.option.optionID);
            ItemMetadataMusic? music = data.property.type != 12 ? null : new ItemMetadataMusic(
                PlayCount: data.MusicScore.playCount,
                MasteryValue: data.MusicScore.masteryValue,
                MasteryValueMax: data.MusicScore.masteryValueMax,
                IsCustomNote: data.MusicScore.isCustomNote,
                NoteLengthMax: data.MusicScore.noteLengthMax,
                FileName: data.MusicScore.fileName,
                PlayTime: data.MusicScore.playTime);
            var housingCategory = HousingCategory.None;
            if (!string.IsNullOrEmpty(data.housing.categoryTag)) {
                string[] tags = data.housing.categoryTag.Split(',');
                housingCategory = Enum.Parse<HousingCategory>(tags[0]);
            }
            ItemMetadataHousing? housing = data.property.type != 6 ? null : new ItemMetadataHousing(
                TrophyId: data.housing.trophyID,
                TrophyLevel: data.housing.trophyLevel,
                InteriorLevel: data.housing.interiorLevel,
                HousingCategory: housingCategory,
                IsNotAllowedInBlueprint: data.housing.doNotInstallBlueprint,
                MaidId: data.maid?.maidID ?? 0,
                Filter: data.housing.filter,
                CategoryIndex: data.housing.categoryIndex);
            ItemMetadataInstall? install = data.property.type != 6 ? null : new ItemMetadataInstall(
                IsSolidCube: data.install.cubeProp == 1,
                FunctionId: data.install.funcCode,
                ObjectCubeId: data.install.objCode,
                IndoorPortal: data.install.indoor != 0,
                MapAttribute: Enum.TryParse<MapAttribute>(data.install.mapAttribute, true, out MapAttribute mapAttribute) ? mapAttribute : MapAttribute.none);

            ItemTag serverTag = ItemTag.None;
            if (!string.IsNullOrWhiteSpace(data.basic.stringTag)) {
                if (!played) {
                    serverTag = Enum.Parse<ItemTag>(data.basic.stringTag);
                } else if (!Enum.TryParse(data.basic.stringTag, out serverTag)) {
                    playedUnknown++;
                }
            }
            ItemTag tag = playedTags == null ? serverTag : PlayedClientItems.LockTag(serverTag, playedTags.GetValueOrDefault(id));
            if (tag != serverTag) lockTagsChanged++;
            var itemMetadata = new ItemMetadata(
                Id: id,
                Name: name,
                SlotNames: SlotNames(data, played),
                Mesh: data.ucc.mesh,
                DefaultHairs: hairList.ToArray(),
                Life: new ItemMetadataLife(
                    ExpirationDuration: expirationDuration,
                    ExpirationTimestamp: expirationTimestamp,
                    ExpirationType: data.life.expirationType is 1 or 2 ? data.life.expirationType : 0,
                    WeeksMonths: data.life.expirationType is 1 or 2 ? data.life.numberOfWeeksMonths : 0
                ),
                Property: new ItemMetadataProperty(
                    IsSkin: data.property.skin,
                    SkinType: data.property.skinType,
                    SlotMax: data.property.slotMax,
                    Type: data.property.type,
                    SubType: data.property.subtype,
                    Category: data.property.category,
                    BlackMarketCategory: data.property.blackMarketCategory,
                    Tag: tag,
                    Group: data.property.itemGroup,
                    Collection: data.property.collection,
                    GearScore: data.property.gearScore,
                    PetId: data.pet.petID,
                    Ride: data.ride.rideMonster,
                    TradableCount: tradableCount,
                    TradableCountDeduction: tradableCountDeduction,
                    RepackCount: repackingLimitCount,
                    RepackConsumeCount: repackingItemConsumeCount,
                    RepackScrollIds: repackingScrollIds,
                    DisableDrop: data.property.disableDrop,
                    SocketId: ItemSocketRules.SocketId(data.property.socketDataId, data.property.skinType, skinSocketEnabled),
                    IsFragment: data.property.functionTags == "piece",
                    // A set lists a piece under its origin id when it has one (SetItemRules.SetKey).
                    SetOptionIds: itemSetBonuses.GetValueOrDefault(SetItemRules.SetKey(id, data.basic.originID))?.ToArray() ?? [],
                    SellPrices: data.property.sell.price,
                    CustomSellPrices: data.property.sell.priceCustom,
                    ShopId: data.Shop?.systemShopID ?? 0,
                    LimitBreakMaxLevel: data.property.unlimitedEnchantMaxGrade
                ) {
                    Fusionable = data.fusion.fusionable != 0,
                    MoveDisable = moveDisableEnabled && data.property.moveDisable,
                    IconCode = data.property.iconCode,
                    Rank = data.property.rank,
                    Survival = data.property.survival != 0,
                    RemakeDisable = data.property.remakeDisable,
                    NotInPlayedClient = known != null && !known.Contains(id),
                },
                Customize: new ItemMetadataCustomize(
                    ColorPalette: data.customize.colorPalette,
                    DefaultColorIndex: data.customize.defaultColorIndex
                ),
                Limit: new ItemMetadataLimit(
                    Gender: (Gender) data.limit.genderLimit,
                    Level: data.limit.levelLimit,
                    TransferType: (TransferType) transferType,
                    TradeMaxRarity: data.limit.tradeLimitRank,
                    ShopSell: data.limit.shopSell,
                    EnableBreak: data.limit.enableBreak,
                    EnableEnchant: !data.limit.exceptEnchant,
                    EnableMeretMarket: data.limit.enableRegisterMeratMarket,
                    EnableSocketTransfer: data.limit.enableSocketTransfer,
                    RequireVip: data.limit.vip,
                    RequireWedding: data.limit.wedding,
                    GlamorForgeCount: itemExtractionTryCount.GetValueOrDefault(id),
                    JobLimits: data.limit.jobLimit.Select(job => (JobCode) job).ToArray(),
                    JobRecommends: data.limit.recommendJobs.Select(job => (JobCode) job).ToArray(),
                    // CItemData reads <PCBang PCBang="..."/> as a bool (itemdata.cpp:375).
                    RequirePcBang: (data.PCBang?.PCBangValue ?? 0) != 0,
                    // ItemLimit::disableJobLimit, read by ItemLimit::IsJobAllowed (itemdatastruct.cpp:694); null when empty.
                    DisableJobLimits: data.limit.disableJobLimit.Length == 0 ? null : data.limit.disableJobLimit.Select(job => (JobCode) job).ToArray()
                ),
                Skill: skill,
                Function: function,
                AdditionalEffects: data.AdditionalEffect.id
                .Zip(data.AdditionalEffect.level, (skillId, level) => new ItemMetadataAdditionalEffect(skillId, level, data.AdditionalEffect.dropEffect))
                .ToArray(),
                Option: option,
                Music: music,
                Housing: housing,
                Install: install
            );

            if (playedTypes != null && playedTypes.TryGetValue(id, out (int Type, int SubType) playedType)) {
                ItemMetadataProperty clientTyped = PlayedClientItems.ClientType(itemMetadata.Property, playedType);
                if (clientTyped != itemMetadata.Property) {
                    itemMetadata = itemMetadata with { Property = clientTyped };
                    typesChanged++;
                }
            }

            if (playedRanks != null && playedRanks.TryGetValue(id, out int clientRank)) {
                itemMetadata = itemMetadata with { Property = itemMetadata.Property with { ClientRank = clientRank } };
                clientRanks++;
                if (itemMetadata.Property.Rank != clientRank) clientRanksDiffer++;
            }
            return itemMetadata;
        }

        EquipSlot[] SlotNames(ItemData data, bool played) {
            var slots = new List<EquipSlot>();
            foreach (Slot slot in data.slots.slot.Where(slot => !string.IsNullOrEmpty(slot.name))) {
                if (!played) {
                    slots.Add(Enum.Parse<EquipSlot>(slot.name, true));
                } else if (Enum.TryParse(slot.name, true, out EquipSlot equipSlot)) {
                    slots.Add(equipSlot);
                } else {
                    playedUnknown++;
                }
            }
            return slots.ToArray();
        }
    }

    // The <environment> the played client keeps of each item of its item data that the data folder's pack lacks,
    // null for one with no environment the client keeps. When the files hold an id twice the first one kept stays,
    // as CItemDataTable::LoadItemDataFile inserts into its item map (itemdatatable.cpp:743).
    private IEnumerable<(int Id, XmlElement? Environment)> PlayedOnlyEnvironments(HashSet<int> packIds, Func<XmlElement, bool> available) {
        var result = new SortedDictionary<int, XmlElement?>();
        foreach (PackFileEntry file in playedReader!.Files.Where(file => PlayedClientItems.IsItemFile(file.Name))) {
            XmlNodeList? items = playedReader.GetXmlDocument(file).DocumentElement?.SelectNodes("item");
            if (items == null) {
                continue;
            }
            foreach (XmlElement item in items.OfType<XmlElement>()) {
                if (!int.TryParse(item.GetAttribute("id"), out int id) || id == 0 || packIds.Contains(id)
                    || result.GetValueOrDefault(id) != null) {
                    continue;
                }
                result[id] = PlayedNpcStats.SelectEnvironment(item, available,
                    feature => FeatureLocaleFilter.Features.GetValueOrDefault(feature), FeatureLocaleFilter.Locale);
            }
        }
        return result.Select(entry => (entry.Key, entry.Value));
    }

    // The played client's item models (itemmodel/<nnn>.xml) by id; when the files hold an id twice the later one
    // is kept.
    private Dictionary<int, XmlElement> PlayedModels() {
        var models = new Dictionary<int, XmlElement>();
        foreach (PackFileEntry file in playedReader!.Files.Where(file => file.Name.StartsWith(ItemModelFolder, StringComparison.OrdinalIgnoreCase))) {
            XmlNodeList? entries = playedReader.GetXmlDocument(file).DocumentElement?.SelectNodes("ItemModel");
            if (entries == null) {
                continue;
            }
            foreach (XmlElement model in entries.OfType<XmlElement>()) {
                if (int.TryParse(model.GetAttribute("id"), out int id)) {
                    models[id] = model;
                }
            }
        }
        return models;
    }

    private const string ItemModelFolder = "itemmodel/";

    // The item ids an attribute of a played client table's rows lists (comma separated), for the ingest's counts.
    private HashSet<int> PlayedTableIds(string fileName, string element, string attribute, Func<XmlElement, bool> available) {
        var ids = new HashSet<int>();
        PackFileEntry? file = playedReader!.Files.FirstOrDefault(entry => entry.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        XmlNodeList? rows = file == null ? null : playedReader.GetXmlDocument(file).DocumentElement?.SelectNodes(element);
        if (rows == null) {
            return ids;
        }
        foreach (XmlElement row in rows.OfType<XmlElement>().Where(available)) {
            foreach (string value in row.GetAttribute(attribute).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
                if (int.TryParse(value, out int id)) {
                    ids.Add(id);
                }
            }
        }
        return ids;
    }
}
