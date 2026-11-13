using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemExchangeScroll : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemExchangeScroll;

    private enum Command : byte {
        SetTarget = 0,
        Exchange = 1,
        ForceFillIngredient = 3,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Exchange:
                HandleExchange(session, packet);
                break;
            case Command.SetTarget:
                HandleSetTarget(session, packet);
                break;
            case Command.ForceFillIngredient:
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }

                HandleForceFillIngredient(session, packet);
                break;
        }
    }

    // Client UIItemExchangeScrollDialog::SendItemExchangeScrollMode1 (uiitemexchangescrolldialog.cpp:575-598):
    // long scroll uid, long target uid, int count. The client refuses count 0 with code 7.
    private void HandleExchange(GameSession session, IByteReader packet) {
        (long scrollItemUid, long _, int quantity) = ItemExchangeScrollRules.ReadExchange(packet);
        if (!ItemExchangeScrollRules.CountAllowed(quantity)) {
            session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_count_check));
            return;
        }

        lock (session.Item) {
            Item? scrollItem = session.Item.Inventory.Get(scrollItemUid);
            if (scrollItem == null) {
                session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_scroll_invalid));
                return;
            }

            if (!int.TryParse(scrollItem.Metadata.Function?.Parameters, out int scrollId) || !TableMetadata.ItemExchangeScrollTable.Entries.TryGetValue(scrollId, out ItemExchangeScrollMetadata? scrollMetadata)) {
                session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_unknown));
                return;
            }

            // The fee of all exchanges, tested and taken as one amount.
            long fee = ItemExchangeScrollRules.Fee(scrollMetadata.RequiredMeso, quantity);
            if (fee < 0 || session.Currency.Meso < fee) {
                session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_money_invalid));
                return;
            }

            // The scrolls of all exchanges, taken once over the stacks.
            List<Item> scrolls = session.Item.Inventory.Find(scrollMetadata.RecipeScroll.ItemId, scrollMetadata.RecipeScroll.Rarity)
                .Where(scroll => !scroll.IsLockedOrUnlocking()).ToList();
            int[]? take = ItemExchangeScrollRules.TakeFromStacks(scrolls.Select(scroll => scroll.Amount).ToList(), (long) scrollMetadata.RecipeScroll.Amount * quantity);
            long rewardAmount = (long) scrollMetadata.RewardItem.Amount * quantity;
            if (take == null || rewardAmount is <= 0 or > int.MaxValue) {
                session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_count_invalid));
                return;
            }

            // The reward is made before anything is taken.
            Item? rewardItem = session.Field?.ItemDrop.CreateItem(scrollMetadata.RewardItem.ItemId, scrollMetadata.RewardItem.Rarity, (int) rewardAmount);
            if (rewardItem == null) {
                session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_unknown));
                return;
            }

            if (!session.Item.Inventory.ConsumeItemComponents(scrollMetadata.RequiredItems, quantity)) {
                session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_count_invalid));
                return;
            }

            for (int i = 0; i < scrolls.Count; i++) {
                if (take[i] > 0 && !session.Item.Inventory.Consume(scrolls[i].Uid, take[i])) {
                    Logger.Fatal("Failed to consume item {ItemUid}", scrolls[i].Uid);
                    throw new InvalidOperationException($"Fatal: Consuming item: {scrolls[i].Uid}");
                }
            }

            session.Currency.ChangeMeso(-fee);
            if (!session.Item.Inventory.Add(rewardItem, true)) {
                session.Item.MailItem(rewardItem);
            }
        }
        session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_ok));
    }

    // Client ItemExchangeSlot::vfunc_18 (itemexchangeslot.cpp:87): an item dropped on the target slot of a
    // scroll: long scroll uid, long item uid. The reply is ItemExchange kind 0 with the cost for that target.
    // The scroll table has one cost list per scroll, so that list is sent for every target.
    private void HandleSetTarget(GameSession session, IByteReader packet) {
        long scrollItemUid = packet.ReadLong();
        long itemUid = packet.ReadLong();

        Item? scrollItem;
        bool hasTarget;
        // Both lookups under the item lock, as every other inventory read of this handler.
        lock (session.Item) {
            scrollItem = session.Item.Inventory.Get(scrollItemUid);
            hasTarget = session.Item.Inventory.Get(itemUid) != null;
        }
        if (scrollItem == null) {
            session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_scroll_invalid));
            return;
        }
        if (!int.TryParse(scrollItem.Metadata.Function?.Parameters, out int scrollId) || !TableMetadata.ItemExchangeScrollTable.Entries.TryGetValue(scrollId, out ItemExchangeScrollMetadata? scrollMetadata)) {
            session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_unknown));
            return;
        }
        if (!hasTarget) {
            return; // the client looks the target up in its inventory and drops the reply without it
        }

        session.Send(ItemExchangeScrollPacket.SetTarget(scrollItemUid, itemUid, scrollMetadata.RequiredItems));
    }

    // Client UIItemExchangeScrollDialog (uiitemexchangescrolldialog.cpp:885 and :902, only at admin level 0x32
    // and above): two GM buttons that give the ingredients of the open scroll. One sends count 10, the other
    // ("max") sends count 0. The caller has passed the GM account check. Each required item is given count
    // times; for count 0 a full stack is given, which is this server's reading of "max". The reply is kind 3.
    private void HandleForceFillIngredient(GameSession session, IByteReader packet) {
        long scrollItemUid = packet.ReadLong();
        long itemUid = packet.ReadLong();
        int count = packet.ReadInt();
        if (count is < 0 or > 10 || session.Field == null) {
            return;
        }

        int scrollId;
        lock (session.Item) {
            Item? scrollItem = session.Item.Inventory.Get(scrollItemUid);
            if (scrollItem == null) {
                session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_scroll_invalid));
                return;
            }
            if (!int.TryParse(scrollItem.Metadata.Function?.Parameters, out scrollId) || !TableMetadata.ItemExchangeScrollTable.Entries.TryGetValue(scrollId, out ItemExchangeScrollMetadata? scrollMetadata)) {
                session.Send(ItemExchangeScrollPacket.Error(ItemExchangeScrollError.s_itemslot_exchange_unknown));
                return;
            }

            foreach (ItemComponent component in scrollMetadata.RequiredItems) {
                if (!ItemMetadata.TryGet(component.ItemId, out ItemMetadata? metadata)) {
                    continue; // an ingredient named only by its tag has no single item to give
                }

                int amount = count > 0 ? count : Math.Max(1, metadata.Property.SlotMax);
                Item? item = session.Field.ItemDrop.CreateItem(component.ItemId, component.Rarity, amount);
                if (item == null) {
                    continue;
                }
                if (!session.Item.Inventory.Add(item, true)) {
                    session.Item.MailItem(item);
                }
            }
        }

        Logger.Information("ItemExchangeScroll GM fill {Count} for scroll {ScrollId} by {CharacterId}", count, scrollId, session.CharacterId);
        session.Send(ItemExchangeScrollPacket.RefreshIngredients());
    }
}
