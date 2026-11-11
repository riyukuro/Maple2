using System.Numerics;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record ItemMetadata(
    int Id,
    string? Name,
    EquipSlot[] SlotNames,
    string Mesh,
    DefaultHairMetadata[] DefaultHairs,
    ItemMetadataLife Life,
    ItemMetadataProperty Property,
    ItemMetadataCustomize Customize,
    ItemMetadataLimit Limit,
    ItemMetadataSkill? Skill,
    ItemMetadataFunction? Function,
    ItemMetadataAdditionalEffect[] AdditionalEffects,
    ItemMetadataOption? Option,
    ItemMetadataMusic? Music,
    ItemMetadataHousing? Housing,
    ItemMetadataInstall? Install) : ISearchResult;

public record ItemMetadataLife(
    long ExpirationDuration,
    long ExpirationTimestamp);

public record ItemMetadataProperty(
    bool IsSkin,
    int SkinType, // 99 = Template
    int SlotMax,
    int Type,
    int SubType,
    string Category,
    string BlackMarketCategory,
    ItemTag Tag,
    int Group,
    int Collection,
    int GearScore,
    int PetId,
    int Ride,
    int TradableCount,
    int TradableCountDeduction,
    int RepackCount,
    int RepackConsumeCount,
    int[] RepackScrollIds,
    bool DisableDrop,
    int SocketId,
    bool IsFragment,
    int[] SetOptionIds,
    long[] SellPrices,
    long[] CustomSellPrices,
    int ShopId,
    int LimitBreakMaxLevel) {
    // The item xml's fusion element, attribute fusionable (client itemdatastruct.cpp:1559, default 0).
    // Null in a row that was ingested before this field existed: such a row is not judged.
    public bool? Fusionable { get; init; }
    // Set at ingest when MS2_PLAYED_CLIENT_XML names the pack of the played client and that pack has no item data
    // for this id (PlayedClientItems). The client reads an item packet into an item object even when it finds no
    // item data for the id and then dereferences the missing data (CItem::ReadFromStream, commonfiles/item.cpp:2655,
    // after CItem::FUN_140523c40 failed, item.cpp:2760), so the server creates no such item (PlayedClientItems.CanCreate).
    // False when the option is not set; the JSON store omits false values, so the default must be false.
    public bool NotInPlayedClient { get; init; }
}

public record ItemMetadataCustomize(
    int ColorPalette,
    int DefaultColorIndex);

public record ItemMetadataLimit(
    Gender Gender,
    int Level,
    TransferType TransferType, // [0-7]
    int TradeMaxRarity,
    bool ShopSell,
    bool EnableBreak,
    bool EnableEnchant,
    bool EnableMeretMarket,
    bool EnableSocketTransfer,
    bool RequireVip,
    bool RequireWedding,
    int GlamorForgeCount,
    JobCode[] JobLimits,
    JobCode[] JobRecommends);

public record ItemMetadataSkill(
    int Id,
    short Level,
    int WeaponId,
    short WeaponLevel);

public record ItemMetadataFunction(
    ItemFunction Type,
    string Name,
    string Parameters,
    bool OnlyShadowWorld);

public record ItemMetadataAdditionalEffect(
    int Id,
    short Level,
    bool PickUpEffect);

public record ItemMetadataOption(
    int StaticId,
    ItemOptionMakeType StaticType,
    int RandomId,
    ItemOptionMakeType ItemOptionType,
    int ConstantId,
    ItemOptionMakeType ConstantType,
    int LevelFactor,
    int PickId);

public record ItemMetadataMusic(
    int PlayCount,
    int MasteryValue,
    int MasteryValueMax,
    bool IsCustomNote,
    int NoteLengthMax,
    string FileName,
    int PlayTime);

public record ItemMetadataHousing(
    int TrophyId,
    int TrophyLevel,
    int InteriorLevel,
    HousingCategory HousingCategory,
    bool IsNotAllowedInBlueprint,
    int MaidId = 0 // <maid maidID>: the maid the furnishing places (MaidTable), 0 for other furnishings
);

public record ItemMetadataInstall(
    bool IsSolidCube,
    int FunctionId,
    int ObjectCubeId,
    bool IndoorPortal,
    MapAttribute MapAttribute);

public record DefaultHairMetadata(
    Vector3 BackPosition = default,
    Vector3 BackRotation = default,
    Vector3 FrontPosition = default,
    Vector3 FrontRotation = default,
    float MinScale = 0f,
    float MaxScale = 0f);
