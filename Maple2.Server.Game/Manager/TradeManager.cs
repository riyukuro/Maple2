using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Network;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;
using static Maple2.Model.Error.TradeError;

namespace Maple2.Server.Game.Manager;

public class TradeManager : IDisposable {
    private readonly ILogger logger = Log.Logger.ForContext<TradeManager>();

    private enum TradeState {
        Completed = 0,
        Requested = 1,
        Acknowledged = 2,
        Started = 3,
        Finalized = 4,
    }

    private readonly Trader sender;
    private readonly Trader receiver;
    private TradeState state;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => sender.Session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private readonly object mutex = new();

    public TradeManager(GameSession sender, GameSession receiver) {
        this.sender = new Trader(sender);
        this.receiver = new Trader(receiver);
        state = TradeState.Requested;

        // Send a trade request
        receiver.Send(TradePacket.Request(sender.Player));

        // End the trade if not accepted before |TradeRequestDuration|.
        string receiverName = receiver.Player.Value.Character.Name;
        Task.Factory.StartNew(() => {
            Thread.Sleep(TimeSpan.FromSeconds(Constants.TradeRequestDuration));
            lock (mutex) {
                if (state is not (TradeState.Requested or TradeState.Acknowledged)) {
                    return;
                }

                sender.Send(TradePacket.Error(s_trade_error_timeout, name: receiverName));
                Dispose();
            }
        });
    }

    public void Dispose() {
        lock (mutex) {
            ClearTraders();

            // Nothing to cancel if not started. The request is over, so the request timer has nothing to report.
            if (state < TradeState.Started) {
                state = TradeState.Completed;
                return;
            }

            EndTrade(false);
        }
    }

    // Called by |receiver| to acknowledge trade request.
    // False when |caller| is not the one asked or the request is over; the state is tested inside the lock, so
    // a request that timed out or was cancelled at the same moment is not brought back.
    public bool Acknowledge(GameSession caller) {
        lock (mutex) {
            if (state != TradeState.Requested || !IsReceiver(caller)) {
                return false;
            }

            state = TradeState.Acknowledged;
            // Inside the lock: a request that ends at the same moment either refuses this or clears it after.
            receiver.Session.Trade = this;
        }
        sender.Session.Send(TradePacket.Acknowledge());
        return true;
    }

    // Takes this trade from its two players. A player who is in another trade by now keeps that one.
    // Called with the lock held.
    private void ClearTraders() {
        if (sender.Session.Trade == this) {
            sender.Session.Trade = null;
        }
        if (receiver.Session.Trade == this) {
            receiver.Session.Trade = null;
        }
    }

    // True while |caller|'s own request is waiting for the other player's answer.
    public bool IsPendingRequestOf(GameSession caller) {
        lock (mutex) {
            return state is TradeState.Requested or TradeState.Acknowledged && caller == sender.Session;
        }
    }

    // The trade's two players; nobody else may act on it.
    public bool IsTrader(GameSession caller) => caller == sender.Session || caller == receiver.Session;

    // Called by |receiver| to accept trade request.
    public void Accept(GameSession caller) {
        lock (mutex) {
            if (state != TradeState.Acknowledged || !IsReceiver(caller)) {
                return;
            }

            state = TradeState.Started;
            // Sent inside the lock: a cancel at the same moment sends its end of the trade after the start.
            sender.Session.Send(TradePacket.StartTrade(receiver.Session.CharacterId));
            receiver.Session.Send(TradePacket.StartTrade(sender.Session.CharacterId));
        }
    }

    // Called by |receiver| when it cannot show the trade request.
    // Ends the request at once with the notice |sender| would get when the request times out.
    public void Busy(GameSession caller) {
        lock (mutex) {
            if (state != TradeState.Requested || !IsReceiver(caller)) {
                return;
            }

            state = TradeState.Completed;
            sender.Session.Send(TradePacket.Error(s_trade_error_timeout, name: receiver.Session.PlayerName));
            ClearTraders();
        }
    }

    // Called by |receiver|, after Acknowledge (the request box) or without it (the client's own refusal).
    public void Decline(GameSession caller) {
        lock (mutex) {
            if (state is not (TradeState.Requested or TradeState.Acknowledged) || !IsReceiver(caller)) {
                return;
            }

            state = TradeState.Completed;
        }
        sender.Session.Send(TradePacket.Decline(receiver.Session.PlayerName));
        Dispose();
    }

    // The client checks the same before it sends the offer, so a refusal here means the two sides disagree: logged.
    private void Refused(GameSession caller, Item item, string why) =>
        logger.Information("Trade offer refused for {CharacterId}: item {ItemId} ({Uid}): {Why}", caller.CharacterId, item.Id, item.Uid, why);

    public void AddItem(GameSession caller, long itemUid, int amount, int tradeSlot) {
        if (state != TradeState.Started || !IsTrader(caller)) {
            return;
        }

        (Trader self, Trader other) = GetTraders(caller);
        if (self.Finalized) {
            caller.Send(TradePacket.Error(s_trade_error_latched));
            return;
        }

        lock (mutex)
            lock (caller.Item) {
                // The state is tested again under the lock: a cancel that ended the trade since the test above
                // would otherwise let the item leave the bag for a window nobody gets back.
                if (state != TradeState.Started || self.Finalized) {
                    return;
                }
                if (self.Items.OpenSlots <= 0) {
                    caller.Send(TradePacket.Error(s_trade_error_itemcount));
                    return;
                }
                // A negative amount would take the whole stack; the client sends the count it shows.
                if (!TradeRules.IsOfferAmount(amount)) {
                    return;
                }
                // The client's own check before it sends: no binding, tradeable or limited trades left.
                Item? offered = caller.Item.Inventory.Get(itemUid);
                if (offered == null) {
                    return;
                }
                // Nor a locked item (InvenSlot::TryAddToTrade asks CItemLockManager::CanUseItem first).
                if (!ItemLockRules.IsUsable(offered.IsLocked, offered.UnlockTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
                    Refused(caller, offered, "locked");
                    return;
                }
                // Nor the pet that is out: its PetManager keeps the pet bag (rows owned by the pet's uid) and saves it
                // when the pet is put away, so the receiver who summons it loads the same rows while the giver's are
                // written again. The client offers it (TryAddToTrade has no pet test); storage and dismantle refuse it.
                if (offered.Uid == (caller.Pet?.Pet.Uid ?? 0)) {
                    Refused(caller, offered, "the summoned pet");
                    return;
                }
                // Nor the score being played (TryAddToTrade: s_trade_tradelimit_playing_score, shown by the client).
                if (offered.Uid == (caller.Instrument?.Score?.Uid ?? 0)) {
                    Refused(caller, offered, "the score being played");
                    return;
                }
                if (SurvivalItemRules.IsMatchOnly(offered.Metadata)) { Refused(caller, offered, "survival match item"); return; }
                if (offered.Transfer != null && !TradeRules.IsTradeable(offered.Transfer.Flag, offered.Transfer.RemainTrades, offered.Transfer.Binding != null)) {
                    Refused(caller, offered, $"transfer {offered.Transfer.Flag}, {offered.Transfer.RemainTrades} trades left, bound {offered.Transfer.Binding != null}");
                    return;
                }
                // Nor one holding a gem that may not go with it (CItemSocket FUN_140622790).
                if (!TradeRules.AreGemsTradeable(offered.Socket, id => caller.ItemMetadata.TryGet(id, out ItemMetadata? gem) ? gem.Limit.TransferType : null,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
                    Refused(caller, offered, "a gem may not go with it");
                    return;
                }
                // A part split off a stack stays the caller's row (at no slot) while it is in the window: the trade's
                // end writes it to its new owner, and a crash before that loads it again. Before, it was owner 0 and
                // the crash lost it (the stack's row was already lowered).
                if (!caller.Item.Inventory.Remove(itemUid, out Item? item, amount, splitOwner: caller.CharacterId)) {
                    Refused(caller, offered, $"amount {amount} of {offered.Amount}");
                    return;
                }

                IList<(Item Item, int Added)> results = self.Items.Add(item, true);
                // Sanity check, this should never fail because we ensure an open slot.
                if (results.Sum(result => result.Added) != amount) {
                    // caller.Item.Inventory.Add(item); TODO: refund the item to inventory?
                    throw new InvalidOperationException("AddItem: Trade consistency error");
                }
                if (!SaveStacked(caller, item, results)) {
                    return;
                }

                // KMS2 order: a locked side is unlocked before the change it is shown.
                OnTradeModified();
                foreach ((Item Item, int) result in results) {
                    self.Session.Send(TradePacket.AddItem(true, result.Item));
                    other.Session.Send(TradePacket.AddItem(false, result.Item));
                }
            }
    }

    // The window stacked |item| onto items already in it: their new amounts and |item|'s row (at owner 0 when nothing
    // of it is left) are written in one save, so the trade's end hands over what the window shows and leaves no row of
    // |item| with the caller. Before, a stacked item's row stayed the caller's, and the caller loaded it again after
    // the trade. A failed save takes the stack back and returns |item| to the bag; false then.
    private bool SaveStacked(GameSession caller, Item item, IList<(Item Item, int Added)> results) {
        (Item Item, int Added)[] onto = results.Where(result => !ReferenceEquals(result.Item, item)).ToArray();
        if (onto.Length == 0) {
            return true;
        }
        bool placed = item.Amount > 0;
        using (GameStorage.Request db = caller.GameStorage.Context()) {
            Item[] kept = onto.Select(result => result.Item).Concat(placed ? [item] : []).ToArray();
            if (db.SaveItems(caller.CharacterId, kept, placed ? [] : [item])) {
                return true;
            }
        }
        foreach ((Item stack, int added) in onto) {
            stack.Amount -= added;
            item.Amount += added;
        }
        if (placed) {
            GetTraders(caller).self.Items.Remove(item.Uid, out _);
        }
        logger.Error("Trade offer {Uid} ({ItemId}) of {CharacterId}: the window's stack was not written", item.Uid, item.Id, caller.CharacterId);
        if (!caller.Item.AddOrMail(item, commit: true)) {
            logger.Error("Trade item {Uid} ({ItemId} x{Amount}) of {CharacterId} not returned", item.Uid, item.Id, item.Amount, caller.CharacterId);
        }
        return false;
    }

    public void RemoveItem(GameSession caller, long itemUid, int tradeSlot) {
        if (state != TradeState.Started || !IsTrader(caller)) {
            return;
        }

        (Trader self, Trader other) = GetTraders(caller);
        if (self.Finalized) {
            caller.Send(TradePacket.Error(s_trade_error_latched));
            return;
        }

        lock (mutex)
            lock (caller.Item) {
                if (state != TradeState.Started || self.Finalized) {
                    return;
                }
                // The item goes back to the bag only when the bag takes it; otherwise it stays in the trade window.
                Item? offered = self.Items[(short) tradeSlot];
                if (offered == null || !caller.Item.Inventory.CanAdd(offered)) {
                    if (offered != null) {
                        caller.Send(ItemInventoryPacket.Error(Maple2.Model.Error.ItemInventoryError.s_err_inventory));
                    }
                    return;
                }
                if (!self.Items.RemoveSlot((short) tradeSlot, out Item? item)) {
                    return;
                }

                OnTradeModified();
                self.Session.Send(TradePacket.RemoveItem(true, tradeSlot, itemUid));
                other.Session.Send(TradePacket.RemoveItem(false, tradeSlot, itemUid));
                // The room was checked under the same lock, so the add takes it; should it not, the item's row (the
                // caller's own) goes to the mail and is not dropped. Written at once, so the piece's row and the
                // lowered stack's are one stack again.
                if (!caller.Item.AddOrMail(item, commit: true)) {
                    logger.Error("Trade item {Uid} ({ItemId} x{Amount}) of {CharacterId} not returned", item.Uid, item.Id, item.Amount, caller.CharacterId);
                }
            }
    }

    public void SetMesos(GameSession caller, long amount) {
        // Can never set amount to a negative number!
        if (amount < 0 || state != TradeState.Started || !IsTrader(caller)) {
            return;
        }

        if (!TradeRules.IsMesoOffer(amount, Constants.TradeMinMeso, Constants.TradeMaxMeso)) {
            caller.Send(TradePacket.Error(s_trade_error_invalid_meso));
            return;
        }

        (Trader self, Trader other) = GetTraders(caller);
        if (self.Finalized) {
            caller.Send(TradePacket.Error(s_trade_error_latched));
            return;
        }

        lock (mutex) {
            // Tested again under the lock, or mesos could be taken for a trade a cancel has just ended.
            if (state != TradeState.Started || self.Finalized) {
                return;
            }

            long diff = amount - self.Mesos;
            if (self.Session.Currency.Meso < diff) {
                caller.Send(TradePacket.Error(s_trade_error_meso));
                return;
            }

            self.Session.Currency.ChangeMeso(-diff);
            self.Mesos = amount;

            OnTradeModified();
            self.Session.Send(TradePacket.SetMesos(true, amount));
            other.Session.Send(TradePacket.SetMesos(false, amount));
        }
    }

    public void Finalize(GameSession caller) {
        if (state != TradeState.Started || !IsTrader(caller)) {
            return;
        }

        (Trader self, Trader other) = GetTraders(caller);
        lock (mutex) {
            if (state != TradeState.Started) {
                return;
            }
            if (!self.Finalized) {
                self.Session.Send(TradePacket.Finalize(true));
                other.Session.Send(TradePacket.Finalize(false));
                self.Finalized = true;
            }

            if (self.Finalized && other.Finalized) {
                state = TradeState.Finalized;
            }
        }
    }

    public void Complete(GameSession caller) {
        if (state != TradeState.Finalized || !IsTrader(caller)) {
            return;
        }

        (Trader self, Trader other) = GetTraders(caller);
        lock (mutex) {
            if (state != TradeState.Finalized) {
                return;
            }
            if (!self.Completed) {
                self.Session.Send(TradePacket.Complete(true));
                other.Session.Send(TradePacket.Complete(false));
                self.Completed = true;
            }

            if (self.Completed && other.Completed) {
                // A bag that cannot take what it would receive refuses the finish: nothing moves. The client shows
                // error 8 (s_trade_error_slotcount, "The other player's inventory is full.") as a notice and keeps the
                // trade window open (OnTradeRequestFailed). The dialog's confirm click (TradeDialogEx.onConfirm in
                // uitradedialogex.gfx) greyed Confirm and Cancel; latch enables Confirm again, but nothing in the
                // dialog enables Cancel until it is opened anew, so after a confirm the client waits for the trade
                // end (mode 6), the only answer that closes the window. The trade ends as cancelled after the
                // notices: each offer goes back to its owner and both windows close with the
                // cancelled line (string 0x825, OnTradeFinished @0x14185cee0).
                bool senderTakes = sender.Session.Item.Inventory.TakesAll(receiver.Items);
                bool receiverTakes = receiver.Session.Item.Inventory.TakesAll(sender.Items);
                if (!senderTakes || !receiverTakes) {
                    foreach ((Trader full, Trader giver) in new[] { (sender, receiver), (receiver, sender) }) {
                        if (full == sender ? senderTakes : receiverTakes) {
                            continue;
                        }
                        full.Session.Send(ItemInventoryPacket.Error(Maple2.Model.Error.ItemInventoryError.s_err_inventory));
                        giver.Session.Send(TradePacket.Error(s_trade_error_slotcount));
                    }
                    EndTrade(false);
                    ClearTraders();
                    return;
                }

                // Swap sessions so items are returned to opposite players.
                (self.Session, other.Session) = (other.Session, self.Session);
                EndTrade(true);

                // Since this trade has ended, we can clear it from traders.
                ClearTraders();
            }
        }
    }

    // |mutex| Locking is done externally.
    private void EndTrade(bool success) {
        if (state == TradeState.Completed) {
            return;
        }

        // On success the sessions were swapped: each offer's giver is the other trader's session.
        HandOver(sender, success ? receiver.Session : sender.Session, success);
        HandOver(receiver, success ? sender.Session : receiver.Session, success);

        sender.Session.Send(TradePacket.EndTrade(success));
        receiver.Session.Send(TradePacket.EndTrade(success));
        state = TradeState.Completed;
    }

    // Hands |trader|'s offer to |trader.Session|, in the KMS2 order: the giver's window loses each item (RemoveItem);
    // a player who receives an item gets it as an own offer (AddItem) and its removal; an item leaves the window
    // before it enters the bag, and the mesos come after the items; on a cancel the mesos come back first. No Meso is
    // sent when no mesos move.
    private void HandOver(Trader trader, GameSession giver, bool success) {
        lock (trader.Session.Item) {
            if (!success && trader.Mesos != 0) {
                trader.Session.Currency.ChangeMeso(trader.Mesos);
                trader.Mesos = 0;
            }
            (Item Item, int Slot)[] items = trader.Items.Select(item => (item, (int) item.Slot)).ToArray();
            if (success) {
                foreach ((Item item, int slot) in items) {
                    giver.Send(TradePacket.RemoveItem(true, slot, item.Uid));
                }
            }
            foreach ((Item item, int slot) in items) {
                if (success) {
                    if (item.Transfer?.Flag.HasFlag(TransferFlag.LimitTrade) == true) {
                        item.Transfer.RemainTrades--;
                    }
                    trader.Session.Send(TradePacket.AddItem(true, item));
                }
                trader.Session.Send(TradePacket.RemoveItem(true, slot, item.Uid));
                // The row moves to the receiver at once (commit). Before, it stayed the giver's until the receiver's
                // next save, so the giver loaded the item again on a relog in between. A failed bag write mails the
                // row; when that fails too the row stays with its old owner and the item is logged here.
                if (!trader.Session.Item.AddOrMail(item, commit: true)) {
                    logger.Error("Trade item {Uid} ({ItemId} x{Amount}) not handed to {CharacterId}", item.Uid, item.Id, item.Amount,
                        trader.Session.CharacterId);
                }
            }

            // The receiver (trader.Session after the swap) pays the fee, lowered by their own reduce_meso_trade_fee:
            // the amount the client showed under the other player's offer.
            long fee = success ? TradeRules.Fee(trader.Mesos, Constants.TradeFeePercent, FeeRate(trader.Session)) : 0;
            if (trader.Mesos - fee != 0) {
                trader.Session.Currency.ChangeMeso(trader.Mesos - fee);
            }

            // Clear just to be safe in case of multiple calls
            trader.Clear();
        }
    }

    // CEquipment::GetAbilityRate((SAType) 0x40): the total of reduce_meso_trade_fee over 1000.
    private static float FeeRate(GameSession session) {
        return session.Player.Stats.Values[SpecialAttribute.ReduceMesoTradeFee].Total / 1000f;
    }

    // |mutex| Locking is done externally.
    private void OnTradeModified() {
        if (state != TradeState.Started) {
            throw new InvalidOperationException($"Trade was modified while in an invalid state: {state}");
        }

        if (sender.Finalized) {
            sender.Session.Send(TradePacket.UnFinalize(true));
            receiver.Session.Send(TradePacket.UnFinalize(false));
            sender.Finalized = false;
        }
        if (receiver.Finalized) {
            receiver.Session.Send(TradePacket.UnFinalize(true));
            sender.Session.Send(TradePacket.UnFinalize(false));
            receiver.Finalized = false;
        }
    }

    private (Trader self, Trader other) GetTraders(GameSession caller) {
        if (caller == sender.Session) return (sender, receiver);
        if (caller == receiver.Session) return (receiver, sender);
        throw new ArgumentException($"Invalid trader: {caller}");
    }

    private bool IsReceiver(GameSession caller) => caller == receiver.Session;

    private class Trader {
        public GameSession Session;
        public readonly ItemCollection Items;
        public long Mesos;

        public bool Finalized;
        public bool Completed;

        public Trader(GameSession session) {
            Session = session;
            Items = new ItemCollection(5);
        }

        public void Clear() {
            Mesos = 0;
            for (short i = 0; i < Items.Count; i++) {
                Items.RemoveSlot(i, out _);
            }
        }
    }
}
