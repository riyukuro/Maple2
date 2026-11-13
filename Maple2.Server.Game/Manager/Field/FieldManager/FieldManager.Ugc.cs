using System.Collections.Concurrent;
using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Ugc;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Manager.Field;

public partial class FieldManager {
    public readonly ConcurrentDictionary<int, Plot> Plots = new();
    public readonly ConcurrentDictionary<long, FieldUgcBanner> Banners = new();
    private DateTimeOffset lastBannerUpdate = DateTimeOffset.MinValue;

    private void UpdateBanners() {
        if (DateTimeOffset.UtcNow - lastBannerUpdate < TimeSpan.FromMinutes(1)) {
            return;
        }

        DateTimeOffset dateTimeOffset = DateTimeOffset.UtcNow;
        foreach (FieldUgcBanner ugcBanner in Banners.Values) {
            ugcBanner.Update(FieldTick);
        }

        using GameStorage.Request db = GameStorage.Context();
        foreach (UgcBanner ugcBanner in Banners.Values) {
            List<BannerSlot> expiredSlots = ugcBanner.SlotsSnapshot().Where(x => x.Expired && UgcRules.BannerSlotSaved(x.Id)).ToList(); // a slot not written yet has no row

            db.RemoveBannerSlots(expiredSlots);
            lock (ugcBanner.Slots) {
                foreach (BannerSlot slot in expiredSlots) {
                    ugcBanner.Slots.Remove(slot);
                }
            }
        }

        lastBannerUpdate = dateTimeOffset;
    }

    public bool UpdatePlotInfo(PlotInfo plotInfo) {
        if (MapId != plotInfo.MapId || !Plots.ContainsKey(plotInfo.Number)) {
            return false;
        }

        if (plotInfo is Plot plot) {
            Plots[plot.Number] = plot;
        } else {
            plot = Plots[plotInfo.Number];
            plot.OwnerId = plotInfo.OwnerId;
            plot.Name = plotInfo.Name;
            plot.ExpiryTime = plotInfo.ExpiryTime;
            plot.MapId = plotInfo.MapId;
        }

        Broadcast(CubePacket.UpdatePlot(plot));
        return true;
    }

    // Tells the receivers of the mails a plot save sent (a mannequin's items given back) that they have mail.
    public static void NotifyMailed(GameSession session, IEnumerable<(long CharacterId, long MailId)> mailed) {
        foreach ((long characterId, long mailId) in mailed) {
            try {
                session.World.MailNotification(new MailNotificationRequest {
                    CharacterId = characterId,
                    MailId = mailId,
                });
            } catch { /* ignored */ }
        }
    }

    private void CommitPlot(GameSession session) {
        Home home = session.Player.Value.Home;
        if (this is HomeFieldManager homeField) {
            if (session.AccountId == homeField.OwnerId && home.Indoor.MapId == MapId && Plots.TryGetValue(home.Indoor.Number, out Plot? indoorPlot) && !indoorPlot.IsPlanner) {
                SavePlot(indoorPlot, homeField);
            }
        }

        if (home.Outdoor != null && home.Outdoor.MapId == MapId && Plots.TryGetValue(home.Outdoor.Number, out Plot? outdoorPlot)) {
            SavePlot(outdoorPlot, null);
        }

        // The cubes are copied under the plot's edit lock (a build helper may edit them on another thread), with
        // the rows the plot has at that point; the copy is written outside it. Rows written after the copy (a place
        // meanwhile) are kept, a cube removed meanwhile is not written again, and a cube turned meanwhile has its
        // rotation written again, so the rows stay what the plot holds. A remove between the save's read of the rows
        // and its write fails the write (its update finds no row); the save then takes a new copy, at most 3 times.
        void SavePlot(Plot plot, HomeFieldManager? homeField) {
            const int attempts = 3;
            for (int attempt = 1; ; attempt++) {
                using GameStorage.Request saveDb = GameStorage.Context();
                bool? done = TrySavePlot(saveDb, plot, homeField);
                if (done != null) {
                    return;
                }
                if (attempt == attempts) {
                    logger.Fatal("Failed to save plot cubes: {PlotId}", plot.Id);
                    throw new InvalidOperationException($"Failed to save plot cubes: {plot.Id}");
                }
                logger.Warning("Saving plot {PlotId} failed (attempt {Attempt}), saving a new copy", plot.Id, attempt);
            }
        }

        // false: not saved (outdated), true: saved, null: the write failed (nothing was swapped in).
        bool? TrySavePlot(GameStorage.Request db, Plot plot, HomeFieldManager? homeField) {
            Dictionary<Vector3B, PlotCube> held;
            List<PlotCube> copy;
            HashSet<long> rows;
            lock (plot.EditLock) {
                // A field whose cubes are no longer the home's saves nothing: its list would replace the home's
                // cubes. The next entry loads the home again (Factory). Checked with the copy taken, under one lock.
                if (homeField != null) {
                    if (homeField.Outdated(db, plot, out rows)) {
                        logger.Warning("Home {PlotId} of {OwnerId} not saved: its cubes were written since this field loaded them", plot.Id, homeField.OwnerId);
                        return false;
                    }
                } else {
                    rows = db.GetCubeIds(plot.Id);
                }
                held = new Dictionary<Vector3B, PlotCube>(plot.Cubes);
                copy = held.Values.Select(Snapshot).ToList();
            }

            var mailed = new List<(long CharacterId, long MailId)>();
            ICollection<PlotCube>? results = db.SaveCubes(plot, copy, session.CharacterId, mailed, deletable: rows);
            if (results == null) {
                return null;
            }
            NotifyMailed(session, mailed);

            Dictionary<Vector3B, PlotCube> saved = results.ToDictionary(result => result.Position);
            lock (Plots) {
                lock (plot.EditLock) {
                    plot.StrayRows.ExceptWith(rows);
                    var turned = new List<(PlotCube Cube, float Rotation)>();
                    foreach ((Vector3B position, PlotCube cube) in held) {
                        // Only a cube still held as it was copied is replaced by its saved row.
                        if (!plot.Cubes.TryGetValue(position, out PlotCube? now) || !ReferenceEquals(now, cube)) {
                            continue;
                        }
                        if (!saved.TryGetValue(position, out PlotCube? result)) {
                            // Its row was gone (deleted outside this instance): the cube is kept, so the instance is
                            // now outdated and is loaded again once it empties.
                            continue;
                        }
                        if (cube.Interact?.PortalSettings is not null && result.Interact?.PortalSettings is not null && result.ItemId == cube.ItemId) {
                            result.Interact = cube.Interact;
                        }
                        if (result.Rotation != cube.Rotation) {
                            turned.Add((result, cube.Rotation)); // turned after the copy
                        }
                        plot.Cubes[position] = result;
                    }
                    // Written under the lock, as a rotation is (HousingManager.TryRotateCube). A turn whose row cannot be
                    // written is undone, so the cube stays what its row holds.
                    foreach ((PlotCube cube, float rotation) in turned) {
                        if (db.SetCubeRotation(cube.Id, rotation)) {
                            cube.Rotation = rotation;
                        } else {
                            logger.Error("Failed to save the rotation of cube {CubeId} at position {Position}.", cube.Id, cube.Position);
                        }
                    }
                    homeField?.Saved(plot);
                }
            }
            return true;
        }
    }

    // A copy of the cube's saved fields, read under the plot's edit lock.
    private static PlotCube Snapshot(PlotCube cube) {
        return new PlotCube(cube.Metadata, cube.Id, cube.Template) {
            Type = cube.Type,
            Position = cube.Position,
            Rotation = cube.Rotation,
            PlotId = cube.PlotId,
            Interact = cube.Interact,
            ExpiryTime = cube.ExpiryTime,
        };
    }

    public void PlaceCube(GameSession session, HeldCube cubeItem, in Vector3B position, float rotation) {
        if (!session.ItemMetadata.TryGet(cubeItem.ItemId, out ItemMetadata? itemMetadata)) {
            logger.Error("Failed to get item metadata for cube {cubeId}.", cubeItem.ItemId);
            return;
        }
        Plot? plot;
        switch (session.HeldCube) {
            case PlotCube _:
                if (itemMetadata.Install is null || itemMetadata.Housing is null) {
                    logger.Error("Item {CubeItemItemId} is not a housing item.", cubeItem.ItemId);
                    return;
                }
                plot = session.Housing.GetBuildFieldPlot(out GameSession owner);
                if (plot == null) {
                    return;
                }

                if (!owner.Housing.TryPlaceCube(cubeItem, plot, itemMetadata, position, rotation, out PlotCube? plotCube, helper: owner == session ? null : session)) {
                    return;
                }

                if (!plot.IsPlanner) {
                    owner.ConditionUpdate(ConditionType.interior_point, counter: HousingManager.DecorationScore(plot));
                }
                session.Field!.Broadcast(CubePacket.PlaceCube(session.Player.ObjectId, plot, plotCube));

                if (plot.MapId is not Constant.DefaultHomeMapId) {
                    session.World.UpdateFieldPlot(new FieldPlotRequest {
                        IgnoreChannel = GameServer.GetChannel(),
                        MapId = session.Field.MapId,
                        PlotNumber = plot.Number,
                        UpdateBlock = new FieldPlotRequest.Types.UpdateBlock {
                            BlockUid = plotCube.Id,
                            Add = new FieldPlotRequest.Types.UpdateBlock.Types.Add { },
                        },
                    });
                }

                if (plot.IsPlanner) {
                    return;
                }
                break;
            case LiftableCube liftable:
                plot = session.Field!.Plots[0];

                FieldLiftable? fieldLiftable = session.Field.AddLiftable($"4_{position.ConvertToInt()}", liftable.Liftable);
                if (fieldLiftable == null) {
                    return;
                }

                session.HeldCube = null;
                fieldLiftable.Count = 1;
                fieldLiftable.State = LiftableState.Default;
                fieldLiftable.Position = position;
                fieldLiftable.Rotation = new Vector3(0, 0, rotation);
                fieldLiftable.FinishTick = session.Field.FieldTick + fieldLiftable.Value.ItemLifetime + fieldLiftable.Value.FinishTime;

                if (session.Field.Entities.LiftableTargetBoxes.TryGetValue(position, out LiftableTargetBox? liftableTarget)) {
                    session.ConditionUpdate(ConditionType.item_move, codeLong: cubeItem.ItemId, targetLong: liftableTarget.LiftableTarget);
                }

                Broadcast(LiftablePacket.Add(fieldLiftable));
                var cube = new PlotCube(itemMetadata, liftable.Id) {
                    Type = PlotCube.CubeType.Liftable,
                    Position = position,
                    Rotation = rotation,
                };
                if (plot.Cubes.TryAdd(position, cube)) {
                    Broadcast(CubePacket.PlaceLiftable(session.Player.ObjectId, liftable, position, rotation));
                }
                Broadcast(SetCraftModePacket.Stop(session.Player.ObjectId));
                Broadcast(LiftablePacket.Update(fieldLiftable));
                break;
        }

        session.ConditionUpdate(ConditionType.install_item, codeLong: cubeItem.ItemId);
    }

    public void UpdateAllPlots(int plotNumber = 0) {
        using GameStorage.Request db = GameStorage.Context();
        Plots.TryGetValue(plotNumber, out Plot? oldPlot);
        foreach (Plot plot in db.LoadPlotsForMap(MapId)) {
            Plots[plot.Number] = plot;
        }

        lock (Plots) {
            List<Plot> allPlots = Plots.Values.ToList();
            List<Plot> updateExpirationTime = allPlots.Where(x => x.State is not PlotState.Open).ToList();

            Plots.TryGetValue(plotNumber, out Plot? plot);
            if (oldPlot?.State is PlotState.Pending && plot?.State is PlotState.Open) {
                updateExpirationTime.Add(plot);
            }
            Broadcast(LoadCubesPacket.PlotOwners(allPlots.Where(x => x.State is PlotState.Taken).ToList()));
            Broadcast(LoadCubesPacket.PlotState(allPlots));
            Broadcast(LoadCubesPacket.PlotExpiry(updateExpirationTime));
        }
        // A forfeited or expired plot comes back without its cubes: its maids go.
        SyncMaids();
    }
}
