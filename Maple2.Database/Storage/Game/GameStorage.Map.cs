using System.Data;
using System.Diagnostics;
using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Z.EntityFramework.Plus;
using Home = Maple2.Model.Game.Home;
using InteractCube = Maple2.Model.Game.InteractCube;
using HomeLayout = Maple2.Model.Game.HomeLayout;
using Maid = Maple2.Model.Game.Maid;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public IList<Plot> LoadPlotsForMap(int mapId, long ownerId = -1) {
            IQueryable<UgcMap> query;
            if (ownerId >= 0) {
                query = Context.UgcMap.Include(map => map.Cubes)
                    .Where(map => map.MapId == mapId && map.OwnerId == ownerId);
            } else {
                query = Context.UgcMap.Include(map => map.Cubes)
                    .Where(map => map.MapId == mapId && map.MapId != Constant.DefaultHomeMapId);
            }

            return query.AsEnumerable()
                .ToList() // ToList before Select so 'This MySqlConnection is already in use.' exception doesn't occur.
                .Select(ToPlot)
                .Where(plot => plot != null)
                .ToList()!;
        }

        public IList<PlotCube> LoadCubesForOwner(long ownerId) {
            List<PlotCube> plotCubes = Context.UgcMap.Where(map => map.OwnerId == ownerId)
                .Join(Context.UgcMapCube, ugcMap => ugcMap.Id, cube => cube.UgcMapId, (ugcMap, cube) => cube)
                .AsEnumerable()
                .Select(ToPlotCube)
                .Where(cube => cube != null)
                .ToList()!;
            foreach (PlotCube cube in plotCubes) {
                if (cube.Interact?.Metadata.Nurturing is null) continue;

                cube.Interact!.Nurturing = GetNurturing(ownerId, cube.Interact.Metadata.Id, cube.Interact.Metadata.Nurturing);
            }

            return plotCubes;
        }

        // Buys an outdoor plot that has no owner and is past its expiry. On a relational DB the claim is one
        // conditional update (owner 0, expired) in a transaction with the deletes of the old cubes and their maids,
        // so a second buyer of the same plot (another channel) updates no row and gets null. Null on any failure,
        // also on a database error; nothing is written then (the caller refunds the cost).
        public PlotInfo? BuyPlot(string characterName, long ownerId, PlotInfo plot, TimeSpan days) {
            try {
                using IDbContextTransaction? transaction = Context.Database.IsRelational()
                    ? Context.Database.BeginTransaction(IsolationLevel.ReadCommitted)
                    : null;
                UgcMap? ugcMap = Context.UgcMap.AsNoTracking().FirstOrDefault(map => map.Id == plot.Id && !map.Indoor);
                if (ugcMap == null) {
                    return null;
                }

                Debug.Assert(ugcMap.MapId == plot.MapId && ugcMap.Number == plot.Number && ugcMap.ApartmentNumber == plot.ApartmentNumber);
                DateTimeOffset now = DateTimeOffset.Now;
                if (ugcMap.OwnerId != 0 || ugcMap.ExpiryTime >= now) {
                    return null;
                }

                long id = ugcMap.Id;
                DateTimeOffset expiry = DateTimeOffset.UtcNow + days;
                ugcMap.OwnerId = ownerId;
                ugcMap.ExpiryTime = expiry;
                ugcMap.Name = characterName;
                // Made before anything is written, with no fixup save: a plot without metadata is refused with nothing
                // changed.
                PlotInfo? result = ToPlotInfo(ugcMap, fix: false);
                if (result == null) {
                    return null;
                }
                if (transaction != null) {
                    int claimed = Context.UgcMap
                        .Where(map => map.Id == id && !map.Indoor && map.OwnerId == 0 && map.ExpiryTime < now)
                        .ExecuteUpdate(update => update
                            .SetProperty(map => map.OwnerId, ownerId)
                            .SetProperty(map => map.ExpiryTime, expiry)
                            .SetProperty(map => map.Name, characterName));
                    if (claimed != 1) {
                        return null; // bought (or changed) meanwhile
                    }
                    DeletePlotCubes(id);
                    if (!CommitPlotWrite(transaction, "buy", id, ownerId, expiry)) {
                        return null;
                    }
                } else {
                    Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                    UgcMap model = Context.UgcMap.Single(map => map.Id == id);
                    model.OwnerId = ownerId;
                    model.ExpiryTime = expiry;
                    model.Name = characterName;
                    DeletePlotCubes(id);
                    if (!Context.TrySaveChanges()) {
                        return null;
                    }
                }

                return result;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to buy plot {PlotId}", plot.Id);
                return null;
            }
        }

        // Extends a plot of plot.OwnerId that has not expired. On a relational DB it is one conditional update on the
        // owner and expiry that were read, so an extend that raced another change of the plot (another extend, a
        // forfeit, a new buyer) updates no row and gets null. Null on any failure; nothing is written then.
        public PlotInfo? ExtendPlot(PlotInfo plot, TimeSpan days) {
            try {
                UgcMap? model = Context.UgcMap.AsNoTracking().FirstOrDefault(map => map.Id == plot.Id);
                if (model == null || model.OwnerId != plot.OwnerId) {
                    return null;
                }

                Debug.Assert(model.MapId == plot.MapId && model.Number == plot.Number && model.ApartmentNumber == plot.ApartmentNumber);
                if (model.ExpiryTime < DateTimeOffset.Now) {
                    return null;
                }

                long id = model.Id;
                long ownerId = model.OwnerId;
                DateTimeOffset expiry = model.ExpiryTime;
                DateTimeOffset extended = expiry + days;
                model.ExpiryTime = extended;
                PlotInfo? result = ToPlotInfo(model, fix: false);
                if (result == null) {
                    return null;
                }
                if (Context.Database.IsRelational()) {
                    int updated = Context.UgcMap
                        .Where(map => map.Id == id && map.OwnerId == ownerId && map.ExpiryTime == expiry)
                        .ExecuteUpdate(update => update.SetProperty(map => map.ExpiryTime, extended));
                    if (updated != 1) {
                        return null;
                    }
                } else {
                    Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                    Context.UgcMap.Single(map => map.Id == id).ExpiryTime = extended;
                    if (!Context.TrySaveChanges()) {
                        return null;
                    }
                }

                return result;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to extend plot {PlotId}", plot.Id);
                return null;
            }
        }

        // Gives up a plot of ownerId that has not expired, with its cubes and their maids, in one transaction on a
        // relational DB (a conditional update on the owner and expiry read). Null on any failure; nothing is written then.
        public PlotInfo? ForfeitPlot(long ownerId, PlotInfo plot) {
            try {
                using IDbContextTransaction? transaction = Context.Database.IsRelational()
                    ? Context.Database.BeginTransaction(IsolationLevel.ReadCommitted)
                    : null;
                UgcMap? model = Context.UgcMap.AsNoTracking().FirstOrDefault(map => map.Id == plot.Id);
                if (model == null || model.OwnerId != ownerId) {
                    return null;
                }

                Debug.Assert(model.MapId == plot.MapId && model.Number == plot.Number && model.ApartmentNumber == plot.ApartmentNumber);
                if (model.ExpiryTime < DateTimeOffset.Now) {
                    return null;
                }

                long id = model.Id;
                DateTimeOffset expiry = model.ExpiryTime;
                DateTimeOffset now = DateTimeOffset.UtcNow;
                model.OwnerId = 0;
                model.Name = string.Empty;
                model.ExpiryTime = now;
                PlotInfo? result = ToPlotInfo(model, fix: false);
                if (result == null) {
                    return null;
                }
                if (transaction != null) {
                    int updated = Context.UgcMap
                        .Where(map => map.Id == id && map.OwnerId == ownerId && map.ExpiryTime == expiry)
                        .ExecuteUpdate(update => update
                            .SetProperty(map => map.OwnerId, 0L)
                            .SetProperty(map => map.Name, string.Empty)
                            .SetProperty(map => map.ExpiryTime, now));
                    if (updated != 1) {
                        return null;
                    }
                    DeletePlotCubes(id);
                    if (!CommitPlotWrite(transaction, "forfeit", id, ownerId, expiry)) {
                        return null;
                    }
                } else {
                    Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                    UgcMap tracked = Context.UgcMap.Single(map => map.Id == id);
                    tracked.OwnerId = 0;
                    tracked.Name = string.Empty;
                    tracked.ExpiryTime = now;
                    DeletePlotCubes(id);
                    if (!Context.TrySaveChanges()) {
                        return null;
                    }
                }

                return result;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to forfeit plot {PlotId}", plot.Id);
                return null;
            }
        }

        // Takes back an expired outdoor plot of plot.OwnerId: the row is left without an owner (pending sale), its cubes
        // are deleted and go to the owner's furnishing storage as new rows (a stored stack is not raised: a game server
        // that holds the storage writes its own amounts back), and a maid cube's maid moves to its item's row, as on a
        // pickup. createItem makes the item of a cube's item id (null skips the cube). One transaction on a relational
        // DB, on the owner and expiry re-read: a plot extended or given up since it was listed, or a cube removed
        // meanwhile, writes nothing, and a later check takes the plot. False then and on any failure; nothing is
        // written then. The request tracks none of the plot's rows after it, so a later save on it (the next plot of
        // the same check) does not write a failed plot's changes.
        public bool ExpirePlot(PlotInfo plot, Func<int, Maple2.Model.Game.Item?> createItem) {
            try {
                return ExpirePlotRows(plot, createItem);
            } finally {
                Context.ChangeTracker.Clear();
            }
        }

        private bool ExpirePlotRows(PlotInfo plot, Func<int, Maple2.Model.Game.Item?> createItem) {
            try {
                using IDbContextTransaction? transaction = Context.Database.IsRelational()
                    ? Context.Database.BeginTransaction(IsolationLevel.ReadCommitted)
                    : null;
                UgcMap? model = Context.UgcMap.AsNoTracking().FirstOrDefault(map => map.Id == plot.Id);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (model == null || model.Indoor || model.OwnerId <= 0 || model.OwnerId != plot.OwnerId
                    || model.ExpiryTime <= DateTimeOffset.MinValue || model.ExpiryTime > now) {
                    return false;
                }

                long id = model.Id;
                long ownerId = model.OwnerId;
                DateTimeOffset expiry = model.ExpiryTime;
                Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                if (transaction != null) {
                    int updated = Context.UgcMap
                        .Where(map => map.Id == id && map.OwnerId == ownerId && map.ExpiryTime == expiry)
                        .ExecuteUpdate(update => update
                            .SetProperty(map => map.OwnerId, 0L)
                            .SetProperty(map => map.Name, string.Empty)
                            .SetProperty(map => map.ExpiryTime, now));
                    if (updated != 1) {
                        return false;
                    }
                } else {
                    UgcMap tracked = Context.UgcMap.Single(map => map.Id == id);
                    tracked.OwnerId = 0;
                    tracked.Name = string.Empty;
                    tracked.ExpiryTime = now;
                }

                // The cubes are removed as loaded: a cube deleted since (picked up) fails the save, so it is not
                // credited twice.
                List<UgcMapCube> cubes = Context.UgcMapCube.Where(cube => cube.UgcMapId == id).ToList();
                List<long> cubeIds = cubes.Select(cube => cube.Id).ToList();
                Dictionary<long, Model.Maid> maids = Context.Maid.Where(maid => cubeIds.Contains(maid.Id)).ToDictionary(maid => maid.Id);
                Context.UgcMapCube.RemoveRange(cubes);
                var returned = new List<Maple2.Model.Game.Item>();
                var maidItems = new List<(Model.Maid Maid, Maple2.Model.Game.Item Item)>();
                foreach (UgcMapCube cube in cubes) {
                    Maple2.Model.Game.Item? item = createItem(cube.ItemId);
                    if (item == null) {
                        Logger.LogWarning("Expired plot {PlotId}: cube {CubeId} item {ItemId} not returned (no item)", id, cube.Id, cube.ItemId);
                        if (maids.Remove(cube.Id, out Model.Maid? lost)) {
                            Context.Maid.Remove(lost);
                        }
                        continue;
                    }
                    // As FurnishingManager.AddStorage stacks them: by item and design, up to the slot size; a maid
                    // item keeps a row of its own.
                    bool maidItem = item.Metadata.Housing?.MaidId > 0;
                    Maple2.Model.Game.Item? stack = maidItem ? null : returned.FirstOrDefault(existing =>
                        MaidRules.CanJoin(existing, cube.ItemId, cube.Template?.Url, false) && existing.Amount < item.Metadata.Property.SlotMax);
                    if (stack != null) {
                        stack.Amount++;
                        continue;
                    }
                    item.Amount = 1;
                    item.Group = ItemGroup.Furnishing;
                    if (cube.Template != null) {
                        item.Template = cube.Template;
                    }
                    returned.Add(item);
                    if (maids.Remove(cube.Id, out Model.Maid? maid)) {
                        if (maidItem) {
                            maidItems.Add((maid, item));
                        } else {
                            Context.Maid.Remove(maid);
                        }
                    }
                }
                var rows = new Dictionary<Maple2.Model.Game.Item, Model.Item>(ReferenceEqualityComparer.Instance);
                foreach (Maple2.Model.Game.Item item in returned) {
                    Model.Item row = item;
                    row.Id = 0;
                    row.OwnerId = ownerId;
                    Context.Item.Add(row);
                    rows[item] = row;
                }

                if (!Context.TrySaveChanges()) {
                    return false;
                }
                // Each maid is re-keyed to its item's new row (as MoveMaid does on a pickup), in the same transaction.
                if (maidItems.Count > 0) {
                    foreach ((Model.Maid maid, Maple2.Model.Game.Item item) in maidItems) {
                        Model.Maid moved = (Maple2.Model.Game.Maid) maid!;
                        moved.Id = rows[item].Id;
                        Context.Maid.Remove(maid);
                        Context.Maid.Add(moved);
                    }
                    if (!Context.TrySaveChanges()) {
                        return false;
                    }
                }
                return transaction == null || CommitPlotWrite(transaction, "expiry", id, ownerId, expiry);
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to expire plot {PlotId}", plot.Id);
                return false;
            }
        }

        // Commits a plot buy or forfeit. A commit that throws may still have been applied on the server (a lost
        // connection after the COMMIT was sent): the caller then treats it as failed (a buy refunds its cost), so the
        // ids are logged to trace a refund for a plot that was in fact written.
        private bool CommitPlotWrite(IDbContextTransaction transaction, string action, long plotId, long ownerId, DateTimeOffset expiry) {
            try {
                transaction.Commit();
                return true;
            } catch (Exception ex) {
                Logger.LogError(ex, "Plot {Action} commit outcome unknown: plot {PlotId}, owner {OwnerId}, expiry {Expiry}",
                    action, plotId, ownerId, expiry.ToUnixTimeSeconds());
                return false;
            }
        }

        public PlotInfo? GetSoonestPlotFromExpire(TimeSpan ugcHomeSaleWaitingTime) {
            IQueryable<UgcMap> maps = Context.UgcMap.Where(map => map.ExpiryTime > DateTimeOffset.MinValue && !map.Indoor);
            foreach (UgcMap map in maps) {
                if (map.OwnerId == 0) {
                    map.ExpiryTime = map.ExpiryTime.Add(ugcHomeSaleWaitingTime);
                }
            }
            UgcMap? model = maps.OrderBy(map => map.ExpiryTime).FirstOrDefault();

            return model == null ? null : ToPlotInfo(model);
        }

        public List<PlotInfo> GetPlotsToExpire() {
            // Get all plots with expiry time <= now and owner >= 0 (not unowned or pending).
            List<UgcMap> expired = Context.UgcMap
                .Where(map => map.OwnerId >= 0 && map.ExpiryTime <= DateTimeOffset.UtcNow && map.ExpiryTime > DateTimeOffset.MinValue && !map.Indoor)
                .ToList();

            return expired.Select(ToPlotInfo).Where(p => p != null).ToList()!;
        }

        public void SetPlotOpen(long plotId) {
            UgcMap? model = Context.UgcMap.Find(plotId);
            if (model == null) return;

            model.ExpiryTime = DateTimeOffset.MinValue;

            Context.UgcMap.Update(model);
            Context.TrySaveChanges();
        }

        public bool SaveHome(Home home) {
            Model.Home model = home;
            Context.Home.Update(model);
            if (!Context.TrySaveChanges()) {
                return false;
            }

            home.LastModified = model.LastModified.ToEpochSeconds();
            return true;
        }

        public bool SavePlotInfo(PlotInfo plotInfo) {
            UgcMap? model = Context.UgcMap.Find(plotInfo.Id);
            if (model == null) {
                return false;
            }

            model.OwnerId = plotInfo.OwnerId;
            model.MapId = plotInfo.MapId;
            model.Number = plotInfo.Number;
            model.ApartmentNumber = plotInfo.ApartmentNumber;
            model.ExpiryTime = plotInfo.ExpiryTime.FromEpochSeconds();
            model.Name = plotInfo.Name;
            Context.UgcMap.Update(model);

            return Context.TrySaveChanges();
        }

        public bool SavePlotInfoName(long plotInfoId, string name) {
            UgcMap? model = Context.UgcMap.Find(plotInfoId);
            if (model == null) {
                return false;
            }

            model.Name = name;
            Context.UgcMap.Update(model);

            return Context.TrySaveChanges();
        }

        // Saves the plot's cubes: those given are added or updated, every other cube on the plot is deleted. A
        // mannequin among the deleted ones (an older instance of the home that does not hold a cube placed elsewhere)
        // gives its items back in the same transaction as the cube save, one system mail per item row: to the
        // character the item is bound to (if it still exists), else to characterId (the plot owner's character that
        // saves). mailed gets the (character, mail) of each mail, for the notice. Null when the save fails, also on a
        // database error such as a deadlock; nothing is written then.
        // With returns, the deleted cubes are given back: returns gets them (as read in the transaction, so a cube
        // another instance placed meanwhile is among them) and answers where they go (CubeReturn), which is written
        // with the delete; a null answer writes nothing. A deleted cube whose item is unknown is not given back.
        // With deletable, only the rows in it are deleted: a row written after the caller took its copy of the cubes
        // (a place made meanwhile) is kept. A given cube whose row is gone (removed meanwhile) is not written again
        // and is not among the results, also when the row goes between this read and the write (see SaveUpdates).
        public ICollection<PlotCube>? SaveCubes(PlotInfo plotInfo, IEnumerable<PlotCube> cubes, long characterId,
                                                List<(long CharacterId, long MailId)>? mailed = null,
                                                Func<IReadOnlyList<PlotCube>, CubeReturn?>? returns = null,
                                                IReadOnlySet<long>? deletable = null) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            var results = new List<UgcMapCube>();
            var mails = new List<(Model.Item Row, Model.Mail Mail)>();
            CubeReturn? back = null;
            var created = new List<Model.Item>();
            var moved = new List<Maid>();
            var returnMails = new List<Model.Mail>();
            // The existing item rows written here (stack joins, doll rows mailed): what item objects keep for them is
            // forgotten once the transaction has ended (GameStorage.ForgetRows), so their next save writes them.
            var written = new List<long>();
            var updated = new HashSet<long>();
            try {
                // Read committed: the doll rows are read after the cube deletes are written (the cube rows locked),
                // and see the put-ons and swaps other channels committed up to then, not a snapshot taken earlier.
                // The in-memory store has no transactions (the tests).
                using IDbContextTransaction? transaction = Context.Database.IsRelational()
                    ? Context.Database.BeginTransaction(IsolationLevel.ReadCommitted)
                    : null;
                var added = new List<UgcMapCube>();
                HashSet<long> rowIds = Context.UgcMapCube.Where(cube => cube.UgcMapId == plotInfo.Id).Select(cube => cube.Id).ToHashSet();
                foreach (PlotCube cube in cubes) {
                    UgcMapCube model = cube;
                    model.UgcMapId = plotInfo.Id;
                    if (model.Id < Constant.FurnishingBaseId && !rowIds.Contains(model.Id)) {
                        continue; // removed meanwhile: an update of a missing row would fail the whole save
                    }
                    if (model.Id >= Constant.FurnishingBaseId) {
                        model.Id = 0; // This needs to be auto-generated.
                        results.Add(model);
                        added.Add(model);
                    } else {
                        updated.Add(model.Id);
                        results.Add(model);
                        Context.UgcMapCube.Update(model);
                    }
                }
                if (updated.Count > 0 && !SaveUpdates()) {
                    return null;
                }
                foreach (UgcMapCube model in added) {
                    Context.UgcMapCube.Add(model);
                }
                var dropped = new List<long>();
                var droppedRows = new List<UgcMapCube>();
                // Listed first: MySQL has one open reader per connection, and Maid.Find below may query.
                foreach (UgcMapCube cube in Context.UgcMapCube.Where(cube => cube.UgcMapId == plotInfo.Id).ToList()) {
                    if (!updated.Contains(cube.Id) && (deletable == null || deletable.Contains(cube.Id))) {
                        Context.UgcMapCube.Remove(cube);
                        dropped.Add(cube.Id);
                        droppedRows.Add(cube);
                    }
                }
                if (returns != null && droppedRows.Count > 0) {
                    back = returns(droppedRows.Select(ToPlotCube).OfType<PlotCube>().ToList());
                    if (back == null) {
                        return null;
                    }
                }
                var keptMaids = new HashSet<long>(back == null ? [] : back.Joins.Select(join => join.MaidCubeId)
                    .Concat(back.NewStacks.Select(add => add.MaidCubeId))
                    .Concat(back.Mailed.Select(add => add.MaidCubeId))
                    .Where(id => id != 0));
                foreach (long cubeId in dropped) {
                    // A cube dropped here that does not go back to storage or by mail takes its maid with it.
                    Model.Maid? maid = keptMaids.Contains(cubeId) ? null : Context.Maid.Find(cubeId);
                    if (maid != null) {
                        Context.Maid.Remove(maid);
                    }
                }
                if (back != null) {
                    foreach (CubeReturn.Join join in back.Joins) {
                        Model.Item? row = Context.Item.Find(join.Stack.Uid);
                        if (row == null || row.OwnerId != back.AccountId || row.Group != ItemGroup.Furnishing) {
                            Logger.LogError("Furnishing stack {Uid} of account {AccountId} is gone", join.Stack.Uid, back.AccountId);
                            return null;
                        }
                        written.Add(row.Id);
                        ForgetRows([row.Id]);
                        row.Amount = join.Amount;
                        if (join.ExpiryTime is { } expiryTime) {
                            row.ExpiryTime = expiryTime > 0 ? expiryTime.FromEpochSeconds() : default;
                        }
                    }
                    foreach (CubeReturn.Add add in back.NewStacks) {
                        Model.Item model = add.Item;
                        model.Id = 0;
                        model.OwnerId = back.AccountId;
                        model.Group = ItemGroup.Furnishing;
                        Context.Item.Add(model);
                        created.Add(model);
                    }
                    foreach (CubeReturn.Add _ in back.Mailed) {
                        Model.Mail mail = DollItemMail(back.CharacterId);
                        Context.Mail.Add(mail);
                        returnMails.Add(mail);
                    }
                }
                if (!Context.TrySaveChanges()) {
                    return null;
                }
                if (back != null && (back.Mailed.Count > 0 || keptMaids.Count > 0)) {
                    var mailedRows = new List<Model.Item>();
                    for (int i = 0; i < back.Mailed.Count; i++) {
                        Model.Item model = back.Mailed[i].Item;
                        model.Id = 0;
                        model.OwnerId = returnMails[i].Id;
                        model.Group = ItemGroup.Default;
                        model.Slot = -1;
                        Context.Item.Add(model);
                        mailedRows.Add(model);
                    }
                    // The mailed rows first, for their ids.
                    if (!Context.TrySaveChanges()) {
                        return null;
                    }
                    var maidMoves = new List<(long CubeId, long ToUid, bool Stored)>();
                    maidMoves.AddRange(back.Joins.Where(join => join.MaidCubeId != 0).Select(join => (join.MaidCubeId, join.Stack.Uid, true)));
                    maidMoves.AddRange(back.NewStacks.Select((add, i) => (add.MaidCubeId, created[i].Id, true)).Where(move => move.MaidCubeId != 0));
                    maidMoves.AddRange(back.Mailed.Select((add, i) => (add.MaidCubeId, mailedRows[i].Id, false)).Where(move => move.MaidCubeId != 0));
                    // Cube ids and item ids are counted apart, so an item may take the id of a moved cube: every
                    // moved maid leaves its cube first, then each target must be free.
                    var moving = new List<(Model.Maid Model, long ToUid, bool Stored)>();
                    foreach ((long cubeId, long toUid, bool stored) in maidMoves) {
                        Model.Maid? model = Context.Maid.Find(cubeId);
                        if (model == null) {
                            continue; // a maid item placed without a maid row
                        }
                        moving.Add((model, toUid, stored));
                    }
                    var freed = new HashSet<long>(moving.Select(move => move.Model.Id));
                    foreach ((Model.Maid model, long toUid, bool _) in moving) {
                        if (!freed.Contains(toUid) && Context.Maid.Find(toUid) != null) {
                            Logger.LogError("Item {Uid} already has a maid", toUid);
                            return null;
                        }
                    }
                    var rows = new List<(Model.Maid Row, bool Stored)>();
                    foreach ((Model.Maid model, long toUid, bool stored) in moving) {
                        Model.Maid row = (Maid) model!;
                        row.Id = toUid;
                        Context.Maid.Remove(model);
                        rows.Add((row, stored));
                    }
                    if (rows.Count > 0 && !Context.TrySaveChanges()) {
                        return null; // the removes first: a new row may take a removed row's key
                    }
                    foreach ((Model.Maid row, bool stored) in rows) {
                        Context.Maid.Add(row);
                        if (stored) {
                            moved.Add(row!);
                        }
                    }
                    if (!Context.TrySaveChanges()) {
                        return null;
                    }
                }

                List<Model.Item> dollRows = dropped.Count == 0 ? [] : Context.Item.AsNoTracking()
                    .Where(row => dropped.Contains(row.OwnerId) && row.Group == ItemGroup.FittingDoll)
                    .ToList();
                if (dollRows.Count > 0) {
                    foreach (Model.Item row in dollRows) {
                        long bound = row.Transfer?.Binding?.CharacterId ?? 0;
                        if (bound != 0 && !Context.Character.Any(character => character.Id == bound)) {
                            bound = 0; // the bound character was deleted
                        }
                        Model.Mail mail = DollItemMail(bound != 0 ? bound : characterId);
                        Context.Mail.Add(mail);
                        mails.Add((row, mail));
                    }
                    // The mails first, for their ids; the transaction is rolled back when it is disposed uncommitted.
                    if (!Context.TrySaveChanges()) {
                        return null;
                    }
                    foreach ((Model.Item row, Model.Mail mail) in mails) {
                        written.Add(row.Id);
                        ForgetRows([row.Id]);
                        if (!MoveDollRow(row, mail.Id)) {
                            Context.Mail.Remove(mail); // the row left the cube meanwhile: no empty mail
                        }
                    }
                    mails.RemoveAll(entry => Context.Entry(entry.Mail).State == EntityState.Deleted);
                    if (!Context.TrySaveChanges()) {
                        return null;
                    }
                }
                transaction?.Commit();
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to save the cubes of plot {PlotId}", plotInfo.Id);
                return null;
            } finally {
                ForgetRows(written);
            }
            mailed?.AddRange(mails.Select(entry => (entry.Mail.ReceiverId, entry.Mail.Id)));
            if (back != null) {
                back.Created.AddRange(created.Select(ToItem).OfType<Maple2.Model.Game.Item>());
                back.MovedMaids.AddRange(moved);
                mailed?.AddRange(returnMails.Select(mail => (mail.ReceiverId, mail.Id)));
            }

            PlotCube[] plotCubes = results
                .Select(ToPlotCube)
                .Where(cube => cube != null)
                .ToArray()!;
            foreach (PlotCube cube in plotCubes) {
                if (cube.Interact?.Metadata.Nurturing is null) continue;

                cube.Interact!.Nurturing = GetNurturing(plotInfo.OwnerId, cube.Interact.Metadata.Id, cube.Interact.Metadata.Nurturing);
            }

            return plotCubes;

            // The updates are written first, on their own. A row removed after rowIds was read (a build helper's
            // remove on another thread, which writes its delete at once) makes its update find no row: that cube was
            // removed, so it leaves the save and the results, and the other updates are written again. Writing an
            // update again is harmless: MySQL rolls the failed write back to its save point, and the in-memory store
            // (the tests), which writes up to the failing row, gets the same values twice.
            bool SaveUpdates() {
                while (true) {
                    try {
                        Context.SaveChanges();
                        return true;
                    } catch (DbUpdateConcurrencyException ex) {
                        List<EntityEntry> gone = ex.Entries
                            .Where(entry => entry.Entity is UgcMapCube && entry.State == EntityState.Modified)
                            .ToList();
                        if (gone.Count == 0) {
                            Logger.LogError(ex, "Failed to save the cubes of plot {PlotId}", plotInfo.Id);
                            return false;
                        }
                        foreach (EntityEntry entry in gone) {
                            var model = (UgcMapCube) entry.Entity;
                            entry.State = EntityState.Detached;
                            updated.Remove(model.Id);
                            results.Remove(model);
                        }
                    }
                }
            }
        }

        // The system mail that gives back a mannequin's item (the same text as a take-off into a full bag).
        private Model.Mail DollItemMail(long characterId) {
            var mail = new Maple2.Model.Game.Mail {
                Type = MailType.System,
                ReceiverId = characterId,
                Content = "50000000", // id from string/en/systemmailcontentna.xml
            };
            mail.SetDefaultExpiry(game.serverTableMetadata.ConstantsTable.MailExpiryDays, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Model.Mail model = mail;
            model.Id = 0;
            return model;
        }

        // Moves a mannequin's item row (read untracked) into mail mailId, only while it is still on its cube in the
        // doll group: another instance of the home may have taken it off first. On a relational DB it is one
        // conditional update in the caller's transaction; otherwise the row is changed with the caller's next save.
        private bool MoveDollRow(Model.Item row, long mailId) {
            long uid = row.Id;
            long cubeUid = row.OwnerId;
            if (Context.Database.IsRelational()) {
                return Context.Item
                    .Where(item => item.Id == uid && item.OwnerId == cubeUid && item.Group == ItemGroup.FittingDoll)
                    .ExecuteUpdate(update => update
                        .SetProperty(item => item.OwnerId, mailId)
                        .SetProperty(item => item.Group, ItemGroup.Default)
                        .SetProperty(item => item.Slot, (short) -1)) == 1;
            }
            row.OwnerId = mailId;
            row.Group = ItemGroup.Default;
            row.Slot = -1;
            Context.Item.Update(row);
            return true;
        }

        public PlotCube? CreateCube(PlotInfo plotInfo, PlotCube cube) {
            try {
                Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

                UgcMapCube model = cube;
                model.UgcMapId = plotInfo.Id;

                Context.UgcMapCube.Add(model);

                if (!Context.TrySaveChanges()) {
                    return null;
                }

                PlotCube? plotCube = ToPlotCube(model);
                if (plotCube?.Interact?.Metadata.Nurturing is not null) {
                    plotCube.Interact.Nurturing = GetNurturing(plotInfo.OwnerId, plotCube.Interact.Metadata.Id, plotCube.Interact.Metadata.Nurturing);
                }

                return plotCube;
            } catch (Exception e) {
                Logger.LogError(e, "Failed to create cube for plot {PlotId} with item {ItemId}", plotInfo.Id, cube.ItemId);
                return null;
            }
        }

        public bool DeleteCube(PlotCube cube) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMapCube? model = Context.UgcMapCube.Find(cube.Id);
            if (model == null) {
                return false;
            }

            Context.UgcMapCube.Remove(model);
            // The maid row of a maid cube goes with it, unless MoveMaid has moved it to the cube's storage item first.
            if (cube.Metadata.Housing?.MaidId > 0) {
                Model.Maid? maid = Context.Maid.Find(cube.Id);
                if (maid != null) {
                    Context.Maid.Remove(maid);
                }
            }
            return Context.TrySaveChanges();
        }

        // The maid keyed by uid: a placed cube's uid or a stored furnishing item's uid.
        public Maid? GetMaid(long uid) {
            return Context.Maid.Find(uid);
        }

        // Re-keys a maid from one uid to another (cube to storage item on pickup, item to cube on placement), keeping
        // its mood, affinity, contract, profile and running craft. Null when there is no maid at fromUid or toUid is
        // taken.
        public Maid? MoveMaid(long fromUid, long toUid) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            Model.Maid? model = Context.Maid.Find(fromUid);
            if (model == null || fromUid == toUid || Context.Maid.Find(toUid) != null) {
                return null;
            }
            Model.Maid moved = (Maid) model!;
            moved.Id = toUid;
            Context.Maid.Remove(model);
            Context.Maid.Add(moved);
            return Context.TrySaveChanges() ? moved : null;
        }

        // Removes the maid keyed by uid (a stored maid item deleted from the storage); false when there was none.
        public bool DeleteMaid(long uid) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            Model.Maid? model = Context.Maid.Find(uid);
            if (model == null) {
                return false;
            }
            Context.Maid.Remove(model);
            return Context.TrySaveChanges();
        }

        // Deletes a plot's cubes and their maids (the cubes do not go back to storage). On a relational DB the rows
        // go at once, in the caller's transaction; otherwise they are removed with the caller's next save.
        private void DeletePlotCubes(long ugcMapId) {
            if (Context.Database.IsRelational()) {
                // One statement each, maids first (a maid row has the id of its cube).
                Context.Maid.Where(maid => Context.UgcMapCube.Any(cube => cube.Id == maid.Id && cube.UgcMapId == ugcMapId)).ExecuteDelete();
                Context.UgcMapCube.Where(cube => cube.UgcMapId == ugcMapId).ExecuteDelete();
                return;
            }
            List<UgcMapCube> cubes = Context.UgcMapCube.Where(cube => cube.UgcMapId == ugcMapId).ToList();
            List<long> ids = cubes.Select(cube => cube.Id).ToList();
            Context.Maid.RemoveRange(Context.Maid.Where(maid => ids.Contains(maid.Id)).ToList());
            Context.UgcMapCube.RemoveRange(cubes);
        }

        // The maids of an account's homes (the owner list of UserMaid).
        public IList<Maid> GetMaids(long ownerId) {
            return Context.Maid.Where(maid => maid.OwnerId == ownerId)
                .AsEnumerable()
                .Select(maid => (Maid) maid!)
                .ToList();
        }

        // Writes the mood, affinity, contract end and profile of a maid; false when its row is gone. The running craft
        // is written only by SetMaidCraft, which tests the stored craft first.
        public bool SaveMaid(Maid maid) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            Model.Maid? model = Context.Maid.Find(maid.Uid);
            if (model == null) {
                return false;
            }
            model.Mood = maid.Mood;
            model.Level = maid.Level;
            model.Exp = maid.Exp;
            model.ExpiryTime = maid.ExpiryTime.FromEpochSeconds();
            model.Profile = maid.Profile;
            return Context.TrySaveChanges();
        }

        // Writes the running craft of the maid keyed by uid (recipe 0: none) only while the stored craft is still the
        // expected one (recipe and start), in one update that tests the row. A maid's home can be open in several
        // channels, each with its own copy of the maid, so of two requests that change the same craft only one
        // changes the row. False when no row changed (another channel changed the craft, or the row is gone).
        public bool SetMaidCraft(long uid, int expectedRecipeId, long expectedStartTime, int recipeId, long startTime, int duration) {
            try {
                if (!Context.Database.IsRelational()) {
                    return SetMaidCraftTracked(uid, expectedRecipeId, expectedStartTime, recipeId, startTime, duration);
                }
                int rows = Context.Maid
                    .Where(maid => maid.Id == uid && maid.CraftRecipeId == expectedRecipeId && maid.CraftStartTime == expectedStartTime)
                    .ExecuteUpdate(update => update
                        .SetProperty(maid => maid.CraftRecipeId, recipeId)
                        .SetProperty(maid => maid.CraftStartTime, startTime)
                        .SetProperty(maid => maid.CraftDuration, duration));
                return rows == 1;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to set the craft of maid {MaidUid}", uid);
                return false;
            }
        }

        // The same test and write through the change tracker, for a provider without bulk updates (the in-memory
        // database of the tests, where nothing runs between the read and the save).
        private bool SetMaidCraftTracked(long uid, int expectedRecipeId, long expectedStartTime, int recipeId, long startTime, int duration) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            Model.Maid? model = Context.Maid.Find(uid);
            if (model == null || model.CraftRecipeId != expectedRecipeId || model.CraftStartTime != expectedStartTime) {
                return false;
            }
            model.CraftRecipeId = recipeId;
            model.CraftStartTime = startTime;
            model.CraftDuration = duration;
            return Context.TrySaveChanges();
        }

        // The stored craft (recipe, start, lead time) of the maid keyed by uid; null when its row is gone.
        public (int RecipeId, long StartTime, int Duration)? GetMaidCraft(long uid) {
            try {
                var craft = Context.Maid.AsNoTracking()
                    .Where(maid => maid.Id == uid)
                    .Select(maid => new { maid.CraftRecipeId, maid.CraftStartTime, maid.CraftDuration })
                    .FirstOrDefault();
                return craft == null ? null : (craft.CraftRecipeId, craft.CraftStartTime, craft.CraftDuration);
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to read the craft of maid {MaidUid}", uid);
                return null;
            }
        }

        // The maid of a placed cube; null when the cube row is gone or the save fails.
        public Maid? CreateMaid(Maid maid) {
            if (!Context.UgcMapCube.Any(cube => cube.Id == maid.Uid)) {
                return null;
            }
            Model.Maid model = maid;
            Context.Maid.Add(model);
            return Context.TrySaveChanges() ? model : null;
        }

        // Whether the plot (indoor or outdoor) holds any cube row.
        public bool HasCubes(long plotInfoId) {
            return Context.UgcMapCube.Any(cube => cube.UgcMapId == plotInfoId);
        }

        public HashSet<long> GetCubeIds(long plotInfoId) {
            return Context.UgcMapCube.Where(cube => cube.UgcMapId == plotInfoId).Select(cube => cube.Id).ToHashSet();
        }

        // The rotation of each cube row of the plot, by cube id.
        public Dictionary<long, float> GetCubeRotations(long plotInfoId) {
            return Context.UgcMapCube.Where(cube => cube.UgcMapId == plotInfoId).ToDictionary(cube => cube.Id, cube => cube.Rotation);
        }

        // Writes a cube's rotation to its row. False when the row is gone.
        public bool SetCubeRotation(long cubeId, float rotation) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMapCube? model = Context.UgcMapCube.Find(cubeId);
            if (model == null) {
                return false;
            }
            model.Rotation = rotation;
            return Context.TrySaveChanges();
        }

        public PlotCube? GetCube(long cubeId) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMapCube? model = Context.UgcMapCube.Find(cubeId);
            if (model == null) {
                return null;
            }

            return ToPlotCube(model);
        }

        public bool InitUgcMap(IEnumerable<UgcMapMetadata> maps) {
            HashSet<(int MapId, int Number)> existing = Context.UgcMap
                .Select(m => new { m.MapId, m.Number })
                .AsEnumerable()
                .Select(m => (m.MapId, m.Number))
                .ToHashSet();

            foreach (UgcMapMetadata map in maps) {
                if (map.Id == Constant.DefaultHomeMapId) {
                    continue;
                }

                foreach (UgcMapGroup group in map.Plots.Values) {
                    if (existing.Contains((map.Id, group.Number))) {
                        continue;
                    }

                    Context.UgcMap.Add(new UgcMap {
                        MapId = map.Id,
                        Number = group.Number,
                        ApartmentNumber = group.ApartmentNumber,
                    });
                }
            }

            return Context.TrySaveChanges();
        }

        private Plot? ToPlot(UgcMap? ugcMap) {
            if (ugcMap == null || !game.mapMetadata.TryGetUgc(ugcMap.MapId, out UgcMapMetadata? metadata)) {
                return null;
            }

            if (!metadata.Plots.TryGetValue(ugcMap.Number, out UgcMapGroup? group)) {
                return null;
            }

            if (!ValidateAndFixHomeExpiryTime(ugcMap)) {
                return null;
            }

            var plot = new Plot(group) {
                Id = ugcMap.Id,
                OwnerId = ugcMap.OwnerId,
                MapId = ugcMap.MapId,
                Number = ugcMap.Number,
                ApartmentNumber = 0,
                Name = ugcMap.Name,
                ExpiryTime = ugcMap.ExpiryTime.ToUnixTimeSeconds(),
            };

            if (ugcMap.Cubes == null) return plot;

            foreach (UgcMapCube cube in ugcMap.Cubes) {
                PlotCube? plotCube = ToPlotCube(cube);
                if (plotCube == null) {
                    continue;
                }

                if (plotCube.Interact?.Metadata.Nurturing is not null) {
                    plotCube.Interact.Nurturing = GetNurturing(ugcMap.OwnerId, cube.Interact!.ObjectCode, plotCube.Interact.Metadata.Nurturing);
                }

                plot.Cubes.Add(plotCube.Position, plotCube);
            }

            return plot;
        }

        private PlotInfo? ToPlotInfo(UgcMap? ugcMap) => ToPlotInfo(ugcMap, fix: true);

        // fix: false for a row that is not saved as read (a plot buy, extend or forfeit builds the result before its
        // conditional update), so the home expiry fixup does not save anything first.
        private PlotInfo? ToPlotInfo(UgcMap? ugcMap, bool fix) {
            if (ugcMap == null || !game.mapMetadata.TryGetUgc(ugcMap.MapId, out UgcMapMetadata? metadata)) {
                return null;
            }

            if (!metadata.Plots.TryGetValue(ugcMap.Number, out UgcMapGroup? group)) {
                return null;
            }

            if (fix && !ValidateAndFixHomeExpiryTime(ugcMap)) {
                return null;
            }

            return new PlotInfo(group) {
                Id = ugcMap.Id,
                OwnerId = ugcMap.OwnerId,
                MapId = ugcMap.MapId,
                Number = ugcMap.Number,
                Name = ugcMap.Name,
                ApartmentNumber = 0,
                ExpiryTime = ugcMap.ExpiryTime.ToUnixTimeSeconds(),
            };
        }

        private HomeLayout? ToHomeLayout(Model.HomeLayout? model) {
            if (model == null) {
                return null;
            }

            List<PlotCube> cubes = model.Cubes.Select(ToPlotCube)
                .Where(cube => cube != null)
                .ToList()!;

            return new HomeLayout(model.Uid, model.Id, model.Name, model.Area, model.Height, model.Timestamp, cubes);
        }

        // Converts model to interact cube if possible, otherwise returns null.
        private InteractCube? ToInteractCube(Model.InteractCube? model) {
            if (model == null) {
                return null;
            }

            return game.functionCubeMetadata.TryGet(model.ObjectCode, out FunctionCubeMetadata? metadata) ? model.Convert(metadata, model.NoticeSettings, model.PortalSettings) : null;
        }

        private PlotCube? ToPlotCube(HomeLayoutCube? model) {
            if (model == null) {
                return null;
            }

            if (!game.itemMetadata.TryGet(model.ItemId, out ItemMetadata? itemMetadata)) {
                return null;
            }

            return new PlotCube(itemMetadata, model.Id, model.Template) {
                Position = new Vector3B(model.X, model.Y, model.Z),
                Rotation = model.Rotation,
                Interact = ToInteractCube(model.Interact),
                Type = PlotCube.CubeType.Construction,
            };
        }

        private PlotCube? ToPlotCube(UgcMapCube? model) {
            if (model == null) {
                return null;
            }

            if (!game.itemMetadata.TryGet(model.ItemId, out ItemMetadata? itemMetadata)) {
                return null;
            }

            return new PlotCube(itemMetadata, model.Id, model.Template) {
                Position = new Vector3B(model.X, model.Y, model.Z),
                Rotation = model.Rotation,
                Interact = ToInteractCube(model.Interact),
                Type = PlotCube.CubeType.Construction,
                // A cube placed holding its item's past fixed end date loads with none, so a pickup gives back the
                // expiry a new item is made with (FurnishingManager.KeptExpiry), not an expired item.
                ExpiryTime = Maple2.Model.Game.Item.HoldsPastFixedEnd(model.ExpiryTime, itemMetadata.Life.ExpirationTimestamp,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds()) ? 0 : model.ExpiryTime,
            };
        }

        private bool ValidateAndFixHomeExpiryTime(UgcMap ugcMap) {
            if (!string.IsNullOrEmpty(ugcMap.Name) && ugcMap.MapId is Constant.DefaultHomeMapId && ugcMap.ExpiryTime != Home.HomeExpiryTime) {
                Logger.LogError("Plot {Id} for {OwnerId} is initialized but it's ExpiryTime is not set to Home.HomeExpiryTime. Why?", ugcMap.Id, ugcMap.OwnerId);

                ugcMap.ExpiryTime = Home.HomeExpiryTime;
                Context.UgcMap.Update(ugcMap);
                return Context.TrySaveChanges();
            }
            return true;
        }
    }
}
