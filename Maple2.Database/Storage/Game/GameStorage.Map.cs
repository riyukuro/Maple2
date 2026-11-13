using System.Diagnostics;
using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
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

                cube.Interact!.Nurturing = GetNurturing(ownerId, cube.ItemId, cube.Interact.Metadata.Nurturing);
            }

            return plotCubes;
        }

        public PlotInfo? BuyPlot(string characterName, long ownerId, PlotInfo plot, TimeSpan days) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMap? ugcMap = Context.UgcMap.FirstOrDefault(map => map.Id == plot.Id && !map.Indoor);
            if (ugcMap == null) {
                return null;
            }

            Debug.Assert(ugcMap.MapId == plot.MapId && ugcMap.Number == plot.Number && ugcMap.ApartmentNumber == plot.ApartmentNumber);
            if (ugcMap.OwnerId != 0 || ugcMap.ExpiryTime >= DateTime.Now) {
                return null;
            }

            ugcMap.OwnerId = ownerId;
            ugcMap.ExpiryTime = DateTime.UtcNow + days;
            ugcMap.Name = characterName;
            Context.UgcMap.Update(ugcMap);
            DeletePlotMaids(ugcMap.Id);
            Context.UgcMapCube.Where(cube => cube.UgcMapId == ugcMap.Id).Delete();

            return Context.TrySaveChanges() ? ToPlotInfo(ugcMap) : null;
        }

        public PlotInfo? ExtendPlot(PlotInfo plot, TimeSpan days) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMap? model = Context.UgcMap.Find(plot.Id);
            if (model == null) {
                return null;
            }

            Debug.Assert(model.MapId == plot.MapId && model.Number == plot.Number && model.ApartmentNumber == plot.ApartmentNumber);
            if (model.ExpiryTime < DateTime.Now) {
                return null;
            }

            model.ExpiryTime += days;
            Context.UgcMap.Update(model);

            return Context.TrySaveChanges() ? ToPlotInfo(model) : null;
        }

        public PlotInfo? ForfeitPlot(long ownerId, PlotInfo plot) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMap? model = Context.UgcMap.Find(plot.Id);
            if (model == null || model.OwnerId != ownerId) {
                return null;
            }

            Debug.Assert(model.MapId == plot.MapId && model.Number == plot.Number && model.ApartmentNumber == plot.ApartmentNumber);
            if (model.ExpiryTime < DateTime.Now) {
                return null;
            }

            model.OwnerId = 0;
            model.Name = string.Empty;
            model.ExpiryTime = DateTimeOffset.UtcNow;
            DeletePlotMaids(model.Id);
            Context.UgcMapCube.Where(cube => cube.UgcMapId == model.Id).Delete();
            Context.UgcMap.Update(model);

            return Context.TrySaveChanges() ? ToPlotInfo(model) : null;
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

        public void SetPlotPending(long plotId) {
            // Set expiry time to now
            UgcMap? ugcMap = Context.UgcMap.Find(plotId);
            if (ugcMap == null) return;

            ugcMap.ExpiryTime = DateTimeOffset.UtcNow;
            ugcMap.OwnerId = 0;
            ugcMap.Name = string.Empty;

            Context.UgcMap.Update(ugcMap);
            Context.TrySaveChanges();
        }

        public void SetPlotOpen(long plotId) {
            UgcMap? model = Context.UgcMap.Find(plotId);
            if (model == null) return;

            model.ExpiryTime = DateTimeOffset.MinValue;

            Context.UgcMap.Update(model);
            Context.TrySaveChanges();
        }

        public Plot? GetOutdoorPlotInfo(int plotNumber, int mapId) {
            UgcMap? model = Context.UgcMap.Include(x => x.Cubes).SingleOrDefault(map => map.MapId == mapId && map.Number == plotNumber && !map.Indoor);
            if (model == null) {
                return null;
            }

            return ToPlot(model);
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
        // gives its items back in the same transaction: each row goes into a system mail of its own, to the character
        // the item is bound to (if it still exists), else to characterId (the plot owner's character that saves).
        // mailed gets the (character, mail) of each mail, for the notice. Null when the save fails; nothing is written
        // then.
        public ICollection<PlotCube>? SaveCubes(PlotInfo plotInfo, IEnumerable<PlotCube> cubes, long characterId,
                                                List<(long CharacterId, long MailId)>? mailed = null) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            PlotCube[] given = cubes.ToArray();
            cubes = given;

            HashSet<long> kept = given.Where(cube => cube.Id < Constant.FurnishingBaseId).Select(cube => cube.Id).ToHashSet();
            long[] dropped = Context.UgcMapCube.AsNoTracking()
                .Where(cube => cube.UgcMapId == plotInfo.Id)
                .Select(cube => cube.Id)
                .AsEnumerable()
                .Where(id => !kept.Contains(id))
                .ToArray();
            List<Model.Item> dollRows = dropped.Length == 0 ? [] : Context.Item.AsNoTracking()
                .Where(row => dropped.Contains(row.OwnerId) && row.Group == ItemGroup.FittingDoll)
                .ToList();
            using IDbContextTransaction? transaction = dollRows.Count > 0 ? Context.Database.BeginTransaction() : null;
            var mails = new List<(Model.Item Row, Model.Mail Mail)>();
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
                    if (!MoveDollRow(row, mail.Id)) {
                        Context.Mail.Remove(mail); // the row left the cube meanwhile: no empty mail
                    }
                }
                mails.RemoveAll(entry => Context.Entry(entry.Mail).State == EntityState.Deleted);
            }

            var results = new List<UgcMapCube>();
            var updated = new HashSet<long>();
            foreach (PlotCube cube in cubes) {
                UgcMapCube model = cube;
                model.UgcMapId = plotInfo.Id;
                if (model.Id >= Constant.FurnishingBaseId) {
                    model.Id = 0; // This needs to be auto-generated.
                    results.Add(model);
                    Context.UgcMapCube.Add(model);
                } else {
                    updated.Add(model.Id);
                    results.Add(model);
                    Context.UgcMapCube.Update(model);
                }
            }
            foreach (UgcMapCube cube in Context.UgcMapCube.Where(cube => cube.UgcMapId == plotInfo.Id)) {
                if (!updated.Contains(cube.Id)) {
                    Context.UgcMapCube.Remove(cube);
                    // A cube dropped here does not go back to storage, so its maid goes too.
                    Model.Maid? maid = Context.Maid.Find(cube.Id);
                    if (maid != null) {
                        Context.Maid.Remove(maid);
                    }
                }
            }

            if (!Context.TrySaveChanges()) {
                return null;
            }
            transaction?.Commit();
            mailed?.AddRange(mails.Select(entry => (entry.Mail.ReceiverId, entry.Mail.Id)));

            PlotCube[] plotCubes = results
                .Select(ToPlotCube)
                .Where(cube => cube != null)
                .ToArray()!;
            foreach (PlotCube cube in plotCubes) {
                if (cube.Interact?.Metadata.Nurturing is null) continue;

                cube.Interact!.Nurturing = GetNurturing(plotInfo.OwnerId, cube.ItemId, cube.Interact.Metadata.Nurturing);
            }

            return plotCubes;
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
                    plotCube.Interact.Nurturing = GetNurturing(plotInfo.OwnerId, cube.ItemId, plotCube.Interact.Metadata.Nurturing);
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

        // The maids of a plot's cubes, before the cubes are deleted without going back to storage.
        private void DeletePlotMaids(long ugcMapId) {
            List<long> cubeIds = Context.UgcMapCube.Where(cube => cube.UgcMapId == ugcMapId).Select(cube => cube.Id).ToList();
            if (cubeIds.Count > 0) {
                Context.Maid.Where(maid => cubeIds.Contains(maid.Id)).Delete();
            }
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

        private PlotInfo? ToPlotInfo(UgcMap? ugcMap) {
            if (ugcMap == null || !game.mapMetadata.TryGetUgc(ugcMap.MapId, out UgcMapMetadata? metadata)) {
                return null;
            }

            if (!metadata.Plots.TryGetValue(ugcMap.Number, out UgcMapGroup? group)) {
                return null;
            }

            if (!ValidateAndFixHomeExpiryTime(ugcMap)) {
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
