using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

// The maid craft dialog's requests (UIManufactureDialog, client/ms2/ui/uimanufacturedialog.cpp), each for a maid of the
// owner's home, named by its uid. Mode 4 starts a recipe of table/maidrecipe.xml, 6 receives a finished product, 10
// cancels a craft. Mode 5 (the dialog's debug craft), 7 (receive at once for merets; the cost is client Lua), 8 and 9
// (the jackpot receive; its rates are server-only) are not handled. All maid and inventory changes run on the field
// thread of the maid's field, after the maid is found still placed there and owned by the requester.
public class MaidCraftItemHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.MaidCraftItem;

    private enum Command : byte {
        Craft = 4,
        Receive = 6,
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
                HandleCraft(session, packet);
                return;
            case Command.Receive:
                HandleReceive(session, packet);
                return;
            case Command.Cancel:
                HandleCancel(session, packet);
                return;
        }
    }

    private void HandleCraft(GameSession session, IByteReader packet) {
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
            if (!MaidRules.HasWorkbench(recipe.WorkbenchType, ownedCubes)) {
                session.Send(MaidCraftItemPacket.CraftResult(MaidRules.CraftNotEnough));
                return;
            }
            float reduceRate = (float) session.Player.Stats.Values[SpecialAttribute.ReduceMaidRecipe].Multiplier();
            // The inventory lock is held from the count to the take, so the counted items are the ones taken; the
            // craft is stored before anything is taken, so a craft another channel started first takes nothing.
            lock (session.Item) {
                IList<Item> owned = session.Item.Inventory.Filter(item => recipe.Ingredients.Any(ingredient => MaidRules.Fills(ingredient, item)));
                List<(long Uid, int Amount)>? taken = MaidRules.TakeIngredients(owned, recipe, reduceRate);
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
                if (!session.Item.Inventory.CanAdd(product, product.Amount)) {
                    return; // no client code for a full bag: the craft stays until there is room
                }
                // The craft is cleared in storage first, tested against the one this field holds: of two receives
                // of the same craft (the home open in two channels) only one clears it and only that one gives the
                // product. The other answers from the stored craft.
                (int recipeId, long start, int duration) = (found.CraftRecipeId, found.CraftStartTime, found.CraftDuration);
                if (!field.SetMaidCraft(found, 0, 0, 0)) {
                    SendRecord(session, found);
                    session.Send(MaidCraftItemPacket.ReceiveResult(MaidRules.ReceiveResult(found, now)));
                    return;
                }
                int amount = product.Amount, rarity = product.Rarity;
                if (!session.Item.Inventory.Add(product, true)) {
                    // The bag took nothing after all: the craft is stored again, to be received later.
                    if (!field.SetMaidCraft(found, recipeId, start, duration)) {
                        Logger.Error("Maid craft {RecipeId} of maid {MaidUid}: product {ItemId} not added and the craft not restored",
                            recipeId, maidUid, recipe.ProductItemId);
                    }
                    return;
                }
                int gained = MaidRules.Finish(found, recipe, expRate, exps, affinityMax);
                field.SaveMaid(found);
                session.Send(MaidCraftItemPacket.Remove(maidUid));
                session.Send(MaidCraftItemPacket.ReceiveResult(MaidRules.CraftReceived));
                session.Send(MaidCraftItemPacket.Reward(recipe.ProductItemId, amount, rarity, gained));
            }
        });
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
            if (!MaidRules.StillPlaced(maid, found) || found.OwnerId != session.AccountId || session.Field != field
                || !MaidRules.IsCrafting(found)) {
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

    // The craft record of a maid as stored, after another channel changed it: the record while it crafts, else its
    // removal.
    private static void SendRecord(GameSession session, Maid maid) {
        session.Send(MaidRules.IsCrafting(maid) ? MaidCraftItemPacket.Add(maid) : MaidCraftItemPacket.Remove(maid.Uid));
    }
}
