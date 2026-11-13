using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public class Item : IByteSerializable, IByteDeserializable {
    public readonly ItemMetadata Metadata;
    public readonly InventoryType Inventory;
    public readonly ItemType Type;

    public long Uid { get; init; }
    public int Rarity { get; set; } // set: a pet evolution raises it in place (PetHandler)
    public short Slot = -1;
    public ItemGroup Group = ItemGroup.Default;

    public int Id => Metadata.Id;
    public int Amount;

    public long CreationTime;
    public long ExpiryTime;

    public int TimeChangedOption;
    public int RemainUses;
    public bool IsLocked;
    public long UnlockTime;
    public short GlamorForges;
    public int GachaDismantleId;
    // Not saved: the drop row (isAnnounce) asks for a world announcement when this item comes out of an item box.
    public bool AnnounceOnOpen;

    public ItemAppearance? Appearance;
    public ItemStats? Stats;
    public ItemEnchant? Enchant;
    public ItemLimitBreak? LimitBreak;

    public ItemTransfer? Transfer;
    public ItemSocket? Socket;
    public ItemCoupleInfo? CoupleInfo;
    public ItemBinding? Binding;

    #region Special Types
    public UgcItemLook? Template;
    public ItemBlueprint? Blueprint;
    public ItemPet? Pet;
    public ItemCustomMusicScore? Music;
    public ItemBadge? Badge;
    #endregion

    public Item(ItemMetadata metadata, int rarity = 1, int amount = 1, bool initialize = true) {
        Metadata = metadata;
        Rarity = rarity;
        Amount = amount;
        Inventory = Metadata.Inventory();
        Type = new ItemType(metadata.Id);

        // Skip initialization of fields, this is done if we will initialize separately.
        if (!initialize) {
            return;
        }

        GlamorForges = (short) Metadata.Limit.GlamorForgeCount;
        Appearance = Metadata.SlotNames.FirstOrDefault(EquipSlot.Unknown) switch {
            EquipSlot.HR => new HairAppearance(EquipColor.None),
            EquipSlot.FD => new DecalAppearance(EquipColor.None),
            EquipSlot.CP => new CapAppearance(EquipColor.None),
            _ => new ItemAppearance(EquipColor.None),
        };

        Transfer = new ItemTransfer(TransferFlagOf(Metadata, Rarity), Metadata.Property.TradableCount);
        Enchant = new ItemEnchant();

        ExpiryTime = GetExpiryTime();

        if (Metadata.Music != null) {
            RemainUses = Metadata.Music.PlayCount;
        }

        FillSubType();
    }

    // The client reads an item's ugc, pet, music or gem block by the item's data (CItem::ReadFromStream,
    // commonfiles/item.cpp), the server writes the one it holds. A row stored without one (the db row's SubType
    // empty) gets the block its data asks for, so the item is written in the layout the client reads; a row
    // that holds one keeps it.
    public void FillSubType() {
        if (Template != null || Blueprint != null || Pet != null || Music != null || Badge != null) {
            return;
        }
        if (Metadata.Mesh != string.Empty || Metadata.Property.Type == 22) {
            Template = new UgcItemLook();
            Blueprint = new ItemBlueprint();
        } else if (Inventory == InventoryType.Pets) {
            Pet = new ItemPet();
        } else if (Metadata.Music?.IsCustomNote == true) {
            Music = new ItemCustomMusicScore();
        } else if (Inventory == InventoryType.Badge) {
            Badge = new ItemBadge(Id);
        }
    }

    // newRow: the copy has no uid, so adding it makes a row of its own (a piece split off an item).
    public Item Clone(bool newRow = false) {
        return new Item(Metadata, Rarity, Amount, false) {
            Uid = newRow ? 0 : Uid,
            CreationTime = CreationTime,
            ExpiryTime = ExpiryTime,
            TimeChangedOption = TimeChangedOption,
            RemainUses = RemainUses,
            IsLocked = IsLocked,
            UnlockTime = UnlockTime,
            GlamorForges = GlamorForges,
            GachaDismantleId = GachaDismantleId,
            Appearance = Appearance?.Clone(),
            Stats = Stats?.Clone(),
            Enchant = Enchant?.Clone(),
            LimitBreak = LimitBreak?.Clone(),
            Transfer = Transfer?.Clone(),
            Socket = Socket?.Clone(),
            CoupleInfo = CoupleInfo?.Clone(),
            Binding = Binding?.Clone(),
            Template = Template?.Clone(),
            Blueprint = Blueprint?.Clone(),
            Pet = Pet?.Clone(),
            Music = Music?.Clone(),
            Badge = Badge?.Clone(),
        };
    }

    public Item Mutate(ItemMetadata metadata, int? rarity = null) {
        return new Item(metadata, rarity ?? Rarity, Amount, false) {
            Uid = Uid,
            CreationTime = CreationTime,
            ExpiryTime = ExpiryTime,
            TimeChangedOption = TimeChangedOption,
            RemainUses = RemainUses,
            IsLocked = IsLocked,
            UnlockTime = UnlockTime,
            GlamorForges = GlamorForges,
            GachaDismantleId = GachaDismantleId,
            Appearance = Appearance,
            Stats = Stats,
            Enchant = Enchant,
            LimitBreak = LimitBreak,
            Transfer = Transfer,
            Socket = Socket,
            CoupleInfo = CoupleInfo,
            Binding = Binding,
            Template = Template,
            Blueprint = Blueprint,
            Pet = Pet,
            Music = Music,
            Badge = Badge,
        };
    }

    // The transfer rule an item of this data is made with at this rarity. KMS2 sends a gem at rank 4 with this rule's
    // flag at 4: bind-on-loot gems as 8 (bind, no trade bit) and untradeable ones as 0, and rank 4 tradable gear at
    // rarity 4 as 16 (limited trade; KMS sessions, builds 2486-2546).
    public static TransferFlag TransferFlagOf(ItemMetadata metadata, int rarity) {
        bool zeroTrades = metadata.Property.TradableCount <= 0;
        bool belowRarity = rarity < metadata.Limit.TradeMaxRarity;
        switch (metadata.Limit.TransferType) {
            case TransferType.Tradable:
                if (belowRarity) {
                    return TransferFlag.Trade | TransferFlag.Split;
                }
                return zeroTrades ? TransferFlag.None : TransferFlag.LimitTrade;
            case TransferType.Untradeable:
                return zeroTrades ? TransferFlag.None : TransferFlag.LimitTrade;
            case TransferType.BindOnLoot:
            case TransferType.BindOnEquip:
            case TransferType.BindOnUse:
            case TransferType.BindOnTrade:
            case TransferType.BindPet: // summon/enchant/reroll
                var result = TransferFlag.Bind;
                if (zeroTrades) {
                    if (belowRarity) {
                        result |= TransferFlag.Trade | TransferFlag.Split;
                    }
                } else {
                    result |= TransferFlag.LimitTrade;
                }
                return result;
            case TransferType.BlackMarketOnly:
                // KMS2 sends every black-market-only item as 4, whatever its rarity (TradeMaxRarity 4, rarity 4: 1306
                // items of 20 ids across builds 2502-2549).
                return TransferFlag.Trade;
            default:
                return TransferFlag.None;
        }
    }

    private long GetExpiryTime() {
        if (Metadata.Life.ExpirationTimestamp > 0) {
#if DEBUG
            return (long) (DateTime.Now.AddYears(1).ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
#else
            return MadeExpiry(Metadata.Life, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
#endif
        }

        if (Metadata.Life.ExpirationDuration > 0) {
            return (long) (DateTime.Now.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds + Metadata.Life.ExpirationDuration;
        }
        return 0;
    }

    // The expiry an item is made with: its fixed end date while that is ahead (FixedExpiry), else its lifetime from
    // now (usePeriod, weeks or months), else none. An item of a past end date and a lifetime lasts that lifetime.
    public static long MadeExpiry(ItemMetadataLife life, long now) {
        long fixedEnd = FixedExpiry(life.ExpirationTimestamp, now);
        if (fixedEnd > 0) {
            return fixedEnd;
        }
        return life.ExpirationDuration > 0 ? now + life.ExpirationDuration : 0;
    }

    // The expiry an item of a fixed end date (life expirationTimestamp) is made with. Every such date in the item data
    // is past (the latest is 2020-03-01), and the client takes the expiry the server sends (CItem::IsExpired,
    // commonfiles/item.cpp:1303): an item made with one was expired from the start, so no price or deck took it (a
    // card game's coins, 30000536). Chosen here: a date already past when the item is made gives no expiry.
    public static long FixedExpiry(long timestamp, long now) => timestamp > now ? timestamp : 0;

    // Whether a stored expiry is its item's past fixed end date: a row made before FixedExpiry, expired from the start.
    public static bool HoldsPastFixedEnd(long stored, long timestamp, long now) =>
        stored > 0 && stored == timestamp && FixedExpiry(timestamp, now) == 0;

    // The expiry a stored item is loaded with. A row holding its item's past fixed end date loads with the expiry the
    // item is made with now (MadeExpiry), which its next save stores (the loaded values differ, so the row is not
    // seeded as saved).
    public static long LoadedExpiry(long stored, ItemMetadataLife life, long now) =>
        HoldsPastFixedEnd(stored, life.ExpirationTimestamp, now) ? MadeExpiry(life, now) : stored;

    // The rarity an item is made at when its source names none (a table row of grade 0, a mail or GM create). The
    // client's default rarity of an item id is its data's rank (GetItemRank, commonfiles/itemdata.cpp:2084: a shop's
    // held count, npcshopproductbase.cpp:475, and a maid ingredient's, manufacturemanager.cpp:310, count only that
    // rarity), so an item whose option constant is no rarity (a gem's is its own id) is made at its rank, not 1.
    // Chosen here: an option constant of 1 to 6 still gives the rarity (438 items whose rank differs from it, all
    // rank 1, keep the rarity the server made them at); an item of neither is made at 1.
    public static int DefaultRarity(ItemMetadata metadata) {
        if (metadata.Option?.ConstantId is > 0 and < 7) {
            return metadata.Option.ConstantId;
        }
        return metadata.Property.Rank is > 0 and < 7 ? metadata.Property.Rank.Value : 1;
    }

    // The rarity a stored item is loaded with. A stackable item (a gem, a type 19 catalyst) whose option constant is
    // no rarity and that is stored at 1 was made before DefaultRarity, at 1; it loads at its default rarity, which its
    // next save stores, so it stacks with the copies made since and a gem upgrade that takes it by id and rank counts
    // it. Other rows keep theirs: gear is not stacked, and a stored rarity of 1 may be the one its source named.
    public static int LoadedRarity(int stored, ItemMetadata metadata) =>
        stored == 1 && metadata.Property.SlotMax > 1 && metadata.Option?.ConstantId is not (> 0 and < 7)
            ? DefaultRarity(metadata) : stored;

    // The transfer flag a stored item is loaded with: a row whose rarity LoadedRarity raised takes its rule's flag at
    // that rarity when its flag is still the one its rule gave at the stored rarity (nothing changed it since), as a
    // copy made now has it; so it stacks with those copies. Its trades left and binding stay.
    public static TransferFlag LoadedTransferFlag(TransferFlag stored, int storedRarity, int rarity, ItemMetadata metadata) =>
        rarity != storedRarity && stored == TransferFlagOf(metadata, storedRarity) ? TransferFlagOf(metadata, rarity) : stored;

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Amount);
        writer.WriteInt(-1);
        writer.WriteLong(CreationTime);
        writer.WriteLong(ExpiryTime);
        writer.WriteLong();
        writer.WriteInt(TimeChangedOption);
        writer.WriteInt(RemainUses);
        writer.WriteBool(IsLocked);
        writer.WriteLong(UnlockTime);
        writer.WriteShort(GlamorForges);
        writer.WriteBool(false);
        writer.WriteInt(GachaDismantleId);

        // The client reads the extra floats by the model's first slot (FD decal, HR hair) or type 1 subtype 13 (cap;
        // CItemData::CreateExtraData, itemdata.cpp:1430-1451); the ctor picks the class by SlotNames[0] (HR, FD, CP),
        // which gives the same class for every KR item the client has. The Default (color only) is for an item
        // without one; none is made: the ctor, Clone, Mutate and the DB row always carry an appearance.
        writer.WriteClass<ItemAppearance>(Appearance ?? ItemAppearance.Default);
        writer.WriteClass<ItemStats>(Stats ?? ItemStats.Default);
        writer.WriteClass<ItemEnchant>(Enchant ?? ItemEnchant.Default);
        writer.WriteClass<ItemLimitBreak>(LimitBreak ?? ItemLimitBreak.Default);

        // The client reads these blocks with independent ifs (ugc, gem, custom music, pet; CItem::ReadFromStream,
        // commonfiles/item.cpp:2671-2698). At most one applies to any item in the KR and NA data: ugc meshes are on
        // types 1, 6 and 9 only (type 22 has none), badges are type 7, pets 11 and custom scores 12, so the else-if
        // chain writes the same bytes.
        if (Template != null && Blueprint != null) {
            writer.WriteClass<UgcItemLook>(Template);
            writer.WriteClass<ItemBlueprint>(Blueprint);
        } else if (Pet != null) {
            writer.WriteClass<ItemPet>(Pet);
        } else if (Music != null) {
            writer.WriteClass<ItemCustomMusicScore>(Music);
        } else if (Badge != null) {
            writer.WriteClass<ItemBadge>(Badge); // TODO
        }

        writer.WriteClass<ItemTransfer>(Transfer ?? ItemTransfer.Default);
        writer.WriteClass<ItemSocket>(Socket ?? ItemSocket.Default);
        writer.WriteClass<ItemCoupleInfo>(CoupleInfo ?? ItemCoupleInfo.Default);
        // The character that put a moveDisable item into the storage (CItem's last block, commonfiles/item.cpp
        // FUN_14052a480/FUN_14052a4f0); empty for every other item.
        writer.WriteClass<ItemBinding>(Binding ?? ItemBinding.Default);
    }

    public void ReadFrom(IByteReader reader) {
        Amount = reader.ReadInt();
        reader.ReadInt();
        CreationTime = reader.ReadLong();
        ExpiryTime = reader.ReadLong();
        reader.ReadLong();
        TimeChangedOption = reader.ReadInt();
        RemainUses = reader.ReadInt();
        IsLocked = reader.ReadBool();
        UnlockTime = reader.ReadLong();
        GlamorForges = reader.ReadShort();
        reader.ReadBool();
        reader.ReadInt();

        Appearance = reader.ReadClass<ItemAppearance>();
        Stats = reader.ReadClass<ItemStats>();
        Enchant = reader.ReadClass<ItemEnchant>();
        LimitBreak = reader.ReadClass<ItemLimitBreak>();

        if (Template != null && Blueprint != null) {
            Template = reader.ReadClass<UgcItemLook>();
            Blueprint = reader.ReadClass<ItemBlueprint>();
        } else if (Pet != null) {
            Pet = reader.ReadClass<ItemPet>();
        } else if (Music != null) {
            Music = reader.ReadClass<ItemCustomMusicScore>();
        } else if (Badge != null) {
            Badge = reader.ReadClass<ItemBadge>();
        }

        Transfer = reader.ReadClass<ItemTransfer>();
        Socket = reader.ReadClass<ItemSocket>();
        CoupleInfo = reader.ReadClass<ItemCoupleInfo>();
        Binding = reader.ReadClass<ItemBinding>();
    }
}
