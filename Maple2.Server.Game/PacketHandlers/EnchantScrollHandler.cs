using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using static Maple2.Model.Error.EnchantScrollError;

namespace Maple2.Server.Game.PacketHandlers;

public class EnchantScrollHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.EnchantScroll;

    private enum Command : byte {
        Preview = 1,
        Enchant = 2,
        Manufacture = 4,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public TableMetadataStorage TableMetadata { private get; init; } = null!;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Preview:
                HandlePreview(session, packet);
                return;
            case Command.Enchant:
                HandleEnchant(session, packet);
                return;
            case Command.Manufacture:
                HandleManufacture(session, packet);
                return;
        }
    }

    private void HandlePreview(GameSession session, IByteReader packet) {
        long scrollUid = packet.ReadLong();
        long itemUid = packet.ReadLong();

        if (!TryGetMetadata(session, scrollUid, out EnchantScrollMetadata? metadata)) {
            session.Send(EnchantScrollPacket.Error(s_enchantscroll_invalid_scroll));
            return;
        }

        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null) {
            session.Send(EnchantScrollPacket.Error(s_enchantscroll_invalid_item));
            return;
        }

        EnchantScrollError error = IsCompatibleScroll(item, metadata);
        if (error != s_enchantscroll_ok) {
            session.Send(EnchantScrollPacket.Error(error));
            return;
        }

        Dictionary<BasicAttribute, BasicOption> minOptions;
        Dictionary<BasicAttribute, BasicOption> maxOptions;
        if (metadata.Type == EnchantScrollType.Random) {
            int minRoll = Math.Max(metadata.Enchants.Min(), item.Enchant?.Enchants ?? 0);
            minOptions = GetCumulativeEnchant(session, item, minRoll).BasicOptions;
            maxOptions = GetCumulativeEnchant(session, item, metadata.Enchants.Max()).BasicOptions;
        } else {
            minOptions = GetCumulativeEnchant(session, item, metadata.Enchants.Max()).BasicOptions;
            maxOptions = minOptions;
        }

        session.Send(EnchantScrollPacket.Preview(item, metadata.Type, minOptions, maxOptions));
    }

    private void HandleEnchant(GameSession session, IByteReader packet) {
        long scrollUid = packet.ReadLong();
        long itemUid = packet.ReadLong();

        lock (session.Item) {
            if (!TryGetMetadata(session, scrollUid, out EnchantScrollMetadata? metadata)) {
                session.Send(EnchantScrollPacket.Error(s_enchantscroll_invalid_scroll));
                return;
            }

            Item? item = session.Item.Inventory.Get(itemUid);
            if (item == null) {
                session.Send(EnchantScrollPacket.Error(s_enchantscroll_invalid_item));
                return;
            }

            EnchantScrollError error = IsCompatibleScroll(item, metadata);
            if (error != s_enchantscroll_ok) {
                session.Send(EnchantScrollPacket.Error(error));
                return;
            }

            if (!session.Item.Inventory.Consume(scrollUid, 1)) {
                session.Send(EnchantScrollPacket.Error(s_enchantscroll_invalid_scroll));
                return;
            }

            // Update item enchant stats
            int enchantLevel = RollEnchant(metadata, Random.Shared);
            // Ensure that you cannot randomize an enchant lower than current item.
            item.Enchant ??= new ItemEnchant();
            if (enchantLevel > item.Enchant.Enchants) {
                ItemEnchant computed = GetCumulativeEnchant(session, item, enchantLevel);
                item.Enchant.Enchants = computed.Enchants;
                item.Enchant.BasicOptions.Clear();
                foreach ((BasicAttribute attribute, BasicOption option) in computed.BasicOptions) {
                    item.Enchant.BasicOptions[attribute] = option;
                }
            }

            session.Send(EnchantScrollPacket.Enchant(item));
        }
    }

    // The grade an enchant gives: weighted by the server table's successProp (one weight per grade; the random
    // scrolls' weights, e.g. 4970,3600,1000,500,90,40 for +6..+11), or equally likely when the data has no weights.
    public static int RollEnchant(EnchantScrollMetadata metadata, Random random) {
        int[] enchants = metadata.Enchants;
        int[]? rates = metadata.Rates;
        if (rates == null || rates.Length != enchants.Length || rates.Any(rate => rate < 0)) {
            return enchants[random.Next(enchants.Length)];
        }
        long total = rates.Sum(rate => (long) rate);
        if (total <= 0) {
            return enchants[random.Next(enchants.Length)];
        }
        long roll = random.NextInt64(total);
        for (int i = 0; i < enchants.Length; i++) {
            roll -= rates[i];
            if (roll < 0) {
                return enchants[i];
            }
        }
        return enchants[^1];
    }

    private static EnchantScrollError IsCompatibleScroll(Item item, EnchantScrollMetadata metadata) {
        if (item.Metadata.Limit.Level < metadata.MinLevel || item.Metadata.Limit.Level > metadata.MaxLevel) {
            return s_enchantscroll_invalid_level;
        }
        if (!metadata.ItemTypes.Contains(item.Type.Type)) {
            return s_enchantscroll_invalid_slot;
        }
        if (!metadata.Rarities.Contains(item.Rarity)) {
            return s_enchantscroll_invalid_rank;
        }
        if ((item.Enchant?.Enchants ?? 0) >= metadata.Enchants.Max()) {
            return s_enchantscroll_invalid_grade;
        }
        // No idea what it even means for an item to be unstable, but you can't enchant something to 0.
        if (metadata.Enchants is [0]) {
            return s_enchantscroll_not_breaking_item;
        }

        return s_enchantscroll_ok;
    }

    private static ItemEnchant GetCumulativeEnchant(GameSession session, Item item, int targetLevel) {
        var cumulative = new ItemEnchant();
        for (int i = 1; i <= targetLevel; i++) {
            ItemEnchant levelEnchant = ItemEnchantManager.GetEnchant(session, item, i);
            foreach ((BasicAttribute attribute, BasicOption option) in levelEnchant.BasicOptions) {
                if (cumulative.BasicOptions.TryGetValue(attribute, out BasicOption existing)) {
                    cumulative.BasicOptions[attribute] = existing + option;
                } else {
                    cumulative.BasicOptions[attribute] = option;
                }
            }
        }
        cumulative.Enchants = targetLevel;
        return cumulative;
    }

    private bool TryGetMetadata(GameSession session, long scrollUid, [NotNullWhen(true)] out EnchantScrollMetadata? metadata) {
        Item? scroll = session.Item.Inventory.Get(scrollUid);
        if (scroll == null) {
            metadata = null;
            return false;
        }

        if (scroll.Metadata.Function?.Type != ItemFunction.EnchantScroll) {
            metadata = null;
            return false;
        }
        if (!int.TryParse(scroll.Metadata.Function.Parameters, out int enchantId)) {
            metadata = null;
            return false;
        }
        if (!TableMetadata.EnchantScrollTable.Entries.TryGetValue(enchantId, out metadata)) {
            return false;
        }

        // Extra sanity check on enchant scroll data.
        return metadata.Enchants.Length > 0 && metadata.ItemTypes.Length > 0 && metadata.Rarities.Length > 0;
    }

    // Client UIMasteryDialog::OnCastingFinished: a magic scroll recipe (craft kind 1, CEnchantScrollRecipe) of
    // the mastery window's scroll tab finished casting; a mastery recipe (kind 0) goes through Mastery 2. The
    // client sends one of these per craft of a repeat and checks the meso and the ingredients itself first
    // (CraftMagicScroll, GetMaxCraftCount kind 1), so a refusal here gets no answer.
    private void HandleManufacture(GameSession session, IByteReader packet) {
        int recipeId = packet.ReadInt();
        if (!TableMetadata.EnchantScrollRecipeTable.Entries.TryGetValue(recipeId, out EnchantScrollRecipeTable.Entry? recipe)) {
            return;
        }

        // The rewards are made by the field; nothing is taken without one.
        if (session.Field == null) {
            return;
        }

        // The meso is taken first and given back when the ingredients are short, so a meso change on another
        // thread cannot leave the ingredients taken and the meso not.
        if (!session.Currency.TryTakeMeso(recipe.RequiredMeso, notify: false)) {
            return;
        }

        // The ingredients are counted as the client counts them (a tag by any item of the tag, an id at any rank);
        // locked and expired items are not taken, and the check and the take are one step under the inventory lock.
        if (!session.Item.Inventory.ConsumeCraftIngredients(recipe.RequiredItems)) {
            session.Currency.ReturnMeso(recipe.RequiredMeso);
            return;
        }
        session.Currency.NotifyMeso();

        var given = new List<(int, short)>();
        IEnumerable<ItemComponent> rewards = recipe.RewardScroll == null ? recipe.RewardItems : recipe.RewardItems.Prepend(recipe.RewardScroll);
        foreach (ItemComponent reward in rewards) {
            Item? item = session.Field?.ItemDrop.CreateItem(reward.ItemId, reward.Rarity, reward.Amount);
            if (item == null) {
                continue;
            }

            given.Add((item.Id, (short) item.Rarity));
            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }
        // Client OnEnchantScroll mode 4: the notice of the items made, then the mastery window is refreshed.
        session.Send(EnchantScrollPacket.Reward(given));
    }
}
