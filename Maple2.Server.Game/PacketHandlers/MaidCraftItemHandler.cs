using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

// The maid craft dialog's requests (UIManufactureDialog, client/ms2/ui/uimanufacturedialog.cpp), each for a maid of the
// owner's home, named by its uid. Mode 4 starts a recipe of table/maidrecipe.xml, 6 receives a finished product, 7
// receives it at once for merets (the cost is the client's Lua calcManufactureCompleteByMerat of the seconds left), 10
// cancels a craft. Mode 5 is the dialog's debug craft, for AdminPermissions.Debug. Mode 8 is never sent (no caller of
// CManufactureManager::SendRequest8) and 9, the jackpot receive, is not handled: its rates are server-only. All maid
// and inventory changes run on the field thread of the maid's field, after the maid is found still placed there and
// owned by the requester.
public class MaidCraftItemHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.MaidCraftItem;

    private enum Command : byte {
        Craft = 4,
        DebugCraft = 5,
        Receive = 6,
        MeretReceive = 7,
        Cancel = 10,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Craft:
                HandleCraft(session, packet, false);
                return;
            case Command.DebugCraft:
                if (MaidRules.CanDebugCraft(session.Player.AdminPermissions)) {
                    HandleCraft(session, packet, true);
                }
                return;
            case Command.Receive:
                HandleReceive(session, packet);
                return;
            case Command.MeretReceive:
                HandleMeretReceive(session, packet);
                return;
            case Command.Cancel:
                HandleCancel(session, packet);
                return;
        }
    }

    // Mode 4, and mode 5 with |debug|: the dialog's OnCraftAdmin (uimanufacturedialog.cpp:308), whose button the
    // dialog shows only in debug mode (selectItem gets IsDebugMode, uimanufacturedialog.cpp:632). The client sends
    // it without the ingredient and meso checks of SendRequest4 (manufacturemanager.cpp:346) and has no reader for a
    // mode 5 answer, so it is answered as mode 4 (OnCraftResult). Chosen here: a debug craft needs no ingredients
    // and no workbench and takes nothing; the recipe, the contract and the lead time are as for mode 4.
    private void HandleCraft(GameSession session, IByteReader packet, bool debug) {
        int recipeId = packet.ReadInt();
        long maidUid = packet.ReadLong();
        FieldManager? field = session.Field;
        Maid? found = field?.GetMaid(maidUid);
        if (field == null || found == null || found.OwnerId != session.AccountId) {
            return;
        }
        MaidTable table = TableMetadata.MaidTable;
        MaidTable.Recipe? recipe = table.Recipes?.GetValueOrDefault(recipeId);
        field.Scheduler.Schedule(() => {
            Maid? maid = field.GetMaid(maidUid);
            if (!MaidRules.StillPlaced(maid, found) || session.Field != field) {
                return;
            }
            if (!MaidRules.CanStart(found, session.AccountId, recipe, table.RecipeGroups) || recipe == null) {
                return;
            }
            long now = DateTime.UtcNow.ToEpochSeconds();
            if (found.ExpiryTime <= now) {
                session.Send(MaidCraftItemPacket.CraftResult(MaidRules.CraftExpired));
                return;
            }
            // A product the server cannot make (an item id missing from the item data) is never started, so no
            // ingredients are taken for a craft that could not be received.
            if (field.ItemDrop.CreateItem(recipe.ProductItemId, recipe.ProductRank, recipe.ProductCount) == null) {
                Logger.Warning("Maid recipe {RecipeId}: no product item {ItemId}", recipe.Id, recipe.ProductItemId);
                return;
            }
            IEnumerable<PlotCube> ownedCubes = field.Plots.Values
                .Where(plot => plot.OwnerId == session.AccountId)
                .SelectMany(plot => plot.Cubes.Values)
                .ToList();
            if (!debug && !MaidRules.HasWorkbench(recipe.WorkbenchType, ownedCubes)) {
                session.Send(MaidCraftItemPacket.CraftResult(MaidRules.CraftNotEnough));
                return;
            }
            float reduceRate = (float) session.Player.Stats.Values[SpecialAttribute.ReduceMaidRecipe].Multiplier();
            // The inventory lock is held from the count to the take, so the counted items are the ones taken; the
            // craft is stored before anything is taken, so a craft another channel started first takes nothing.
            lock (session.Item) {
                IList<Item> owned = session.Item.Inventory.Filter(item => recipe.Ingredients.Any(ingredient => MaidRules.Fills(ingredient, item)));
                List<(long Uid, int Amount)>? taken = debug ? [] : MaidRules.TakeIngredients(owned, recipe, reduceRate);
                if (taken == null) {
                    session.Send(MaidCraftItemPacket.CraftResult(MaidRules.CraftNotEnough));
                    return;
                }
                if (!field.SetMaidCraft(found, recipe.Id, now, MaidRules.LeadTime(recipe, found.Mood))) {
                    SendRecord(session, found);
                    return;
                }
                foreach ((long uid, int amount) in taken) {
                    if (session.Item.Inventory.Consume(uid, amount)) {
                        continue;
                    }
                    Logger.Error("Maid craft {RecipeId} of maid {MaidUid}: could not take {Amount} of item {ItemUid}", recipe.Id, maidUid, amount, uid);
                }
            }
            session.Send(MaidCraftItemPacket.Add(found));
            session.Send(MaidCraftItemPacket.CraftResult(MaidRules.CraftStarted));
        });
    }

    private void HandleReceive(GameSession session, IByteReader packet) {
        long maidUid = packet.ReadLong();
        FieldManager? field = session.Field;
        Maid? found = field?.GetMaid(maidUid);
        if (field == null || found == null || found.OwnerId != session.AccountId) {
            return;
        }
        MaidTable table = TableMetadata.MaidTable;
        IReadOnlyDictionary<int, int> exps = table.Exps ?? new Dictionary<int, int>();
        int affinityMax = Constants.MaidAffinityMax;
        field.Scheduler.Schedule(() => {
            Maid? maid = field.GetMaid(maidUid);
            if (!MaidRules.StillPlaced(maid, found) || found.OwnerId != session.AccountId || session.Field != field) {
                return;
            }
            long now = DateTime.UtcNow.ToEpochSeconds();
            RefreshCraft(session, field, found);
            int result = MaidRules.ReceiveResult(found, now);
            if (result != MaidRules.CraftReceived) {
                session.Send(MaidCraftItemPacket.ReceiveResult(result));
                return;
            }
            MaidTable.Recipe? recipe = table.Recipes?.GetValueOrDefault(found.CraftRecipeId);
            if (recipe == null) {
                return; // the recipe left the table: the craft stays until it is cancelled
            }
            Item? product = field.ItemDrop.CreateItem(recipe.ProductItemId, recipe.ProductRank, recipe.ProductCount);
            if (product == null) {
                return;
            }
            float expRate = (float) session.Player.Stats.Values[SpecialAttribute.MaidExp].Multiplier();
            lock (session.Item) {
                if (!session.Item.Inventory.HasRoomFor(product)) {
                    return; // no client code for a full bag: the craft stays until there is room
                }
                // The craft is cleared in storage first, tested against the one this field holds: of two receives
                // of the same craft (the home open in two channels) only one clears it and only that one gives the
                // product. The other answers from the stored craft.
                (int recipeId, long start, int duration) = (found.CraftRecipeId, found.CraftStartTime, found.CraftDuration);
                if (!field.SetMaidCraft(found, 0, 0, 0)) {
                    SendRecord(session, found);
                    int? failed = MaidRules.FailedReceiveResult(found, now);
                    if (failed != null) {
                        session.Send(MaidCraftItemPacket.ReceiveResult(failed.Value));
                    }
                    return;
                }
                int amount = product.Amount, rarity = product.Rarity;
                bool addedAny = false;
                if (!session.Item.Inventory.Add(product, true, onAdded: _ => addedAny = true)) {
                    if (addedAny) {
                        // Part of the product is in the bag: the craft stays received, as a second receive would
                        // give the whole product again.
                        Logger.Error("Maid craft {RecipeId} of maid {MaidUid}: product {ItemId} only partly added",
                            recipeId, maidUid, recipe.ProductItemId);
                    } else {
                        // The bag took nothing: the craft is stored again, to be received later.
                        if (!field.SetMaidCraft(found, recipeId, start, duration)) {
                            Logger.Error("Maid craft {RecipeId} of maid {MaidUid}: product {ItemId} not added and the craft not restored",
                                recipeId, maidUid, recipe.ProductItemId);
                        }
                        return;
                    }
                }
                int level = found.Level;
                int gained = MaidRules.Finish(found, recipe, expRate, exps, affinityMax);
                field.SaveMaid(found);
                CountReceive(session, found, recipe.ProductItemId, level);
                session.Send(MaidCraftItemPacket.Remove(maidUid));
                session.Send(MaidCraftItemPacket.ReceiveResult(MaidRules.CraftReceived));
                session.Send(MaidCraftItemPacket.Reward(recipe.ProductItemId, amount, rarity, gained));
            }
        });
    }

    // A received craft counts for maid_get_item with the product (23500009 "Obtain ... from a ... maid {0} time(s)":
    // one per receive), and a closeness level-up for maid_affinity with the level reached (23500014 "Raise an
    // assistant's closeness to level {0}", values 2 to 10).
    private static void CountReceive(GameSession session, Maid maid, int productItemId, int levelBefore) {
        session.ConditionUpdate(ConditionType.maid_get_item, codeLong: productItemId);
        if (maid.Level > levelBefore) {
            session.ConditionUpdate(ConditionType.maid_affinity, counter: maid.Level);
        }
    }

    // Mode 7 (CManufactureManager::SendRequest7): the craft is received now, ready or not, for the merets the dialog
    // shows (UIManufactureDialog::OnRecieveByMerat). The client tests only the meret balance (vfunc_40; red merets
    // count only with feature 0x102, which FUN_14036d710 tests
    // only while feature 0x43 is on, and 0x43 is off here). The cost is worked out here from the stored craft. Once the craft is
    // cleared the merets are taken, then the product is added; an add that fails or puts in only part of it is taken
    // out again, the merets are given back and the craft is stored again. Every product kind follows this order (a
    // currency, medal or furnishing product is never added in part).
    private void HandleMeretReceive(GameSession session, IByteReader packet) {
        long maidUid = packet.ReadLong();
        FieldManager? field = session.Field;
        Maid? found = field?.GetMaid(maidUid);
        if (field == null || found == null || found.OwnerId != session.AccountId) {
            return;
        }
        MaidTable table = TableMetadata.MaidTable;
        IReadOnlyDictionary<int, int> exps = table.Exps ?? new Dictionary<int, int>();
        int affinityMax = Constants.MaidAffinityMax;
        field.Scheduler.Schedule(() => {
            Maid? maid = field.GetMaid(maidUid);
            if (!MaidRules.StillPlaced(maid, found) || found.OwnerId != session.AccountId || session.Field != field) {
                return;
            }
            RefreshCraft(session, field, found);
            if (!MaidRules.IsCrafting(found)) {
                session.Send(MaidCraftItemPacket.MeretReceiveResult(MaidRules.CraftNoRecord));
                return;
            }
            MaidTable.Recipe? recipe = table.Recipes?.GetValueOrDefault(found.CraftRecipeId);
            if (recipe == null) {
                return; // the recipe left the table: the craft stays until it is cancelled
            }
            Item? product = field.ItemDrop.CreateItem(recipe.ProductItemId, recipe.ProductRank, recipe.ProductCount);
            if (product == null) {
                return;
            }
            long now = DateTime.UtcNow.ToEpochSeconds();
            long cost = Math.Max(0L, Lua.CalcManufactureCompleteByMerat(MaidRules.CraftReadyTime(found) - now));
            float expRate = (float) session.Player.Stats.Values[SpecialAttribute.MaidExp].Multiplier();
            lock (session.Item) {
                if (!HasMerets(session, cost)) {
                    session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_lack_merat));
                    return;
                }
                if (!session.Item.Inventory.HasRoomFor(product)) {
                    return; // no client code for a full bag: the craft stays until there is room
                }
                // As in mode 6, the stored craft is cleared first, so of two receives of one craft only one gives it.
                (int recipeId, long start, int duration) = (found.CraftRecipeId, found.CraftStartTime, found.CraftDuration);
                if (!field.SetMaidCraft(found, 0, 0, 0)) {
                    SendRecord(session, found);
                    if (!MaidRules.IsCrafting(found)) {
                        session.Send(MaidCraftItemPacket.MeretReceiveResult(MaidRules.CraftNoRecord));
                    }
                    return;
                }
                // The merets are taken before the add, under the same lock, so a balance spent meanwhile is found
                // before anything is given (TryTakeMeret checks and takes in one step).
                if (cost > 0 && !session.Currency.TryTakeMeret(cost)) {
                    if (!field.SetMaidCraft(found, recipeId, start, duration)) {
                        Logger.Error("Maid craft {RecipeId} of maid {MaidUid}: the craft not restored after a failed meret receive",
                            recipeId, maidUid);
                    }
                    session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_lack_merat));
                    return;
                }
                int amount = product.Amount, rarity = product.Rarity;
                Dictionary<long, int> held = session.Item.Inventory
                    .Filter(item => item.Id == product.Id && item.Rarity == product.Rarity)
                    .ToDictionary(item => item.Uid, item => item.Amount);
                var added = new List<Item>();
                bool whole = session.Item.Inventory.Add(product, true, onAdded: item => added.Add(item));
                if (!whole) {
                    bool back = TakeBack(session, added, held);
                    if (cost > 0) {
                        session.Currency.ChangeMeret(cost);
                    }
                    Logger.Warning("Maid craft {RecipeId} of maid {MaidUid}: product {ItemId} not added whole, {Cost} merets given back to {CharacterId}",
                        recipeId, maidUid, recipe.ProductItemId, cost, session.CharacterId);
                    if (!back) {
                        Logger.Error("Maid craft {RecipeId} of maid {MaidUid}: product {ItemId} not taken back after a failed meret receive",
                            recipeId, maidUid, recipe.ProductItemId);
                        return;
                    }
                    if (!field.SetMaidCraft(found, recipeId, start, duration)) {
                        Logger.Error("Maid craft {RecipeId} of maid {MaidUid}: the craft not restored after a failed meret receive",
                            recipeId, maidUid);
                    }
                    return;
                }
                Logger.Information("Maid craft {RecipeId} of maid {MaidUid} received for {Cost} merets by {CharacterId}",
                    recipeId, maidUid, cost, session.CharacterId);
                int level = found.Level;
                int gained = MaidRules.Finish(found, recipe, expRate, exps, affinityMax);
                field.SaveMaid(found);
                CountReceive(session, found, recipe.ProductItemId, level);
                session.Send(MaidCraftItemPacket.Remove(maidUid));
                session.Send(MaidCraftItemPacket.MeretReceiveResult(MaidRules.CraftReceived));
                session.Send(MaidCraftItemPacket.Reward(recipe.ProductItemId, amount, rarity, gained));
            }
        });
    }

    private static bool HasMerets(GameSession session, long cost) => session.Currency.CanAddMeret(-cost) == -cost;

    // Takes out of the bag what an add put in: each stack it merged into back to the amount |held| before it, each new
    // row whole. False when a part could not be taken.
    internal static bool TakeBack(GameSession session, IEnumerable<Item> added, IReadOnlyDictionary<long, int> held) {
        bool all = true;
        foreach (Item item in added.DistinctBy(item => item.Uid)) {
            int put = item.Amount - held.GetValueOrDefault(item.Uid);
            if (put > 0 && !session.Item.Inventory.Consume(item.Uid, put)) {
                all = false;
            }
        }
        return all;
    }

    // A cancelled craft gives nothing back.
    private static void HandleCancel(GameSession session, IByteReader packet) {
        long maidUid = packet.ReadLong();
        FieldManager? field = session.Field;
        Maid? found = field?.GetMaid(maidUid);
        if (field == null || found == null || found.OwnerId != session.AccountId) {
            return;
        }
        field.Scheduler.Schedule(() => {
            Maid? maid = field.GetMaid(maidUid);
            if (!MaidRules.StillPlaced(maid, found) || found.OwnerId != session.AccountId || session.Field != field) {
                return;
            }
            RefreshCraft(session, field, found);
            if (!MaidRules.IsCrafting(found)) {
                return;
            }
            if (!field.SetMaidCraft(found, 0, 0, 0)) {
                SendRecord(session, found);
                return;
            }
            session.Send(MaidCraftItemPacket.Remove(maidUid));
            session.Send(MaidCraftItemPacket.CancelResult(MaidRules.CraftCancelled));
        });
    }

    // A copy without a craft may predate one another channel started: it is read again from storage, and a craft
    // found there is sent as the maid's record before the receive or cancel answers from it.
    private static void RefreshCraft(GameSession session, FieldManager field, Maid maid) {
        if (MaidRules.IsCrafting(maid) || !field.ReloadMaidCraft(maid) || !MaidRules.IsCrafting(maid)) {
            return;
        }
        session.Send(MaidCraftItemPacket.Add(maid));
    }

    // The craft record of a maid as stored, after another channel changed it: the record while it crafts, else its
    // removal.
    private static void SendRecord(GameSession session, Maid maid) {
        session.Send(MaidRules.IsCrafting(maid) ? MaidCraftItemPacket.Add(maid) : MaidCraftItemPacket.Remove(maid.Uid));
    }
}
