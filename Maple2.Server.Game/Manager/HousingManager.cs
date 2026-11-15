using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class HousingManager {
    private readonly GameSession session;
    private readonly TableMetadataStorage tableMetadata;
    private Home Home => session.Player.Value.Home;

    private readonly ILogger logger = Log.Logger.ForContext<HousingManager>();

    public ItemBlueprint? StagedItemBlueprint = null;
    // Uid of the blueprint item the staged blueprint came from; the client names it when it confirms the bill.
    public long StagedBlueprintItemUid;

    public HousingManager(GameSession session, TableMetadataStorage tableMetadata) {
        this.session = session;
        this.tableMetadata = tableMetadata;
    }

    public void SetPlot(PlotInfo? plot) {
        Home.Outdoor = plot;

        session.Field?.Broadcast(CubePacket.UpdateProfile(session.Player));
    }

    public void SetName(string name) {
        if (session.Field is null) return;
        if (!TrophyReportRules.HomeNameAllowed(name, Constant.HomeNameMaxLength)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_center));
            return;
        }

        Home.Indoor.Name = name;
        PlotInfo? plot = Home.Outdoor;
        using GameStorage.Request db = session.GameStorage.Context();
        if (plot != null) {
            plot.Name = name;
            if (!db.SavePlotInfoName(plot.Id, name)) {
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
                return;
            }
            session.World.UpdateFieldPlot(new FieldPlotRequest {
                MapId = session.Field.MapId,
                PlotNumber = plot.Number,
                IgnoreChannel = GameServer.GetChannel(),
                UpdatePlot = new FieldPlotRequest.Types.UpdatePlot(),
            });
        } else {
            if (!db.SavePlotInfo(Home.Indoor)) {
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
                return;
            }
        }

        session.Field.Broadcast(CubePacket.SetHomeName(Home));
        session.Field.Broadcast(CubePacket.UpdateProfile(session.Player));
    }

    public void SetHomeMessage(string message) {
        if (message.Length > Constant.HomeMessageMaxLength) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_center));
            return;
        }

        Home.Message = message;
        if (!SaveHome()) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        session.Field?.Broadcast(CubePacket.SetHomeMessage(Home.Message));
    }

    public void SetPasscode(string passcode) {
        if (passcode.Length != 0 && !HomeOpenRules.IsPrivate(passcode) && !HomeOpenRules.IsPasscode(passcode)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_center));
            return;
        }

        Home.Passcode = passcode;
        using GameStorage.Request db = session.GameStorage.Context();
        if (!SaveHome()) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        session.Send(CubePacket.SetPasscode());
        // The reply changes nothing on the client: the option dialog shows the open type of the house info, so
        // the owner gets it again with the new type.
        session.Send(CubePacket.UpdateProfile(session.Player));

        // A home set to private keeps no visitors: they leave the way a kicked visitor does. The owner can
        // change the setting from any map, so the home is looked up when the owner is not standing in it.
        HomeFieldManager? home = session.Field is HomeFieldManager here && here.OwnerId == session.AccountId
            ? here
            : session.FieldFactory.FindHome(session.AccountId);
        if (home != null) {
            foreach (FieldPlayer visitor in home.Players.Values) {
                if (!HomeOpenRules.MovesOut(passcode, session.AccountId, visitor.Session.AccountId)) {
                    continue;
                }
                visitor.Session.Send(HomeBlockPacket.Result(HomeBlockPacket.Error.Kicked));
                visitor.Session.ReturnField();
            }
        }
    }

    public bool SaveHome() {
        using GameStorage.Request db = session.GameStorage.Context();
        return db.SaveHome(Home);
    }

    public bool SavePlots() {
        using GameStorage.Request db = session.GameStorage.Context();
        return db.SavePlotInfo(Home.Indoor);
    }

    // Retrieves plot directly from field which includes cube data.
    public Plot? GetFieldPlot() {
        if (session.Field == null) {
            return null;
        }

        Plot? plot;
        if (session.Field.MapId == Home.Indoor.MapId) {
            session.Field.Plots.TryGetValue(Home.Indoor.Number, out plot);
            return plot;
        }

        if (Home.Outdoor == null) {
            return null;
        }

        session.Field.Plots.TryGetValue(Home.Outdoor.Number, out plot);
        return plot;
    }

    // The field plot when it belongs to this account: the plot a session may edit.
    public Plot? GetOwnFieldPlot() {
        Plot? plot = GetFieldPlot();
        return plot != null && HomeEditRules.OwnsPlot(plot.OwnerId, session.AccountId) ? plot : null;
    }

    public Plot? GetIndoorPlot() {
        if (session.Field is not HomeFieldManager homeField) {
            return null;
        }

        if (session.AccountId != homeField.OwnerId || session.Field.MapId != Home.Indoor.MapId) return null;

        session.Field.Plots.TryGetValue(Home.Indoor.Number, out Plot? plot);
        return plot;
    }

    public bool SaveFieldPlot(int number) {
        if (session.Field?.Plots.TryGetValue(number, out Plot? plot) != true) {
            return false;
        }

        return true;
    }

    public bool BuyPlot(int plotNumber) {
        PlotInfo? plotInfo = Home.Outdoor;
        if (plotInfo != null) {
            session.Send(plotInfo.Number == plotNumber
                ? CubePacket.Error(UgcMapError.s_ugcmap_my_house)
                : CubePacket.Error(UgcMapError.s_ugcmap_cant_buy_more_than_two_house));
            return false;
        }

        if (session.Field == null || !session.Field.Plots.TryGetValue(plotNumber, out Plot? plot)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_a_buyable));
            return false;
        }

        if (plot.OwnerId != 0 || plot.State != PlotState.Open) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_already_owned));
            return false;
        }

        UgcMapGroup.Cost contract = plot.Metadata.ContractCost;

        int costAmount = contract.Amount;
        GameEvent? saleEvent = session.FindEvent(GameEventType.UGCMapContractSale).FirstOrDefault();
        if (saleEvent != null) {
            float discount = float.Parse(saleEvent.Metadata.Value1, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture) / 10000f;
            costAmount = (int) Math.Ceiling(costAmount * (1 - discount));
        }

        // The check and the charge are one step: the merets can also be taken on another thread (a partner's paid
        // wedding hall), so a charge after the save could fail with the plot already saved.
        if (!TryRemoveCost(session, contract.ItemId, costAmount)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_enough_money));
            return false;
        }

        // The cost is stored in the plot's transaction (TrySavePurchase): left to the next session save, a crash
        // after the plot row was written kept the plot unpaid. A failed buy (owned on another channel, or a failed
        // save) costs nothing: the cost is given back before the account lock is, and the home's outdoor plot is
        // left as it was.
        PlotInfo? bought = null;
        if (!session.TrySavePurchase(db => (bought = db.BuyPlot(session.PlayerName, session.AccountId, plot, TimeSpan.FromDays(contract.Days))) != null,
                () => RefundCost(session, contract.ItemId, costAmount))) {
            bought = null;
        }
        plotInfo = bought;
        if (plotInfo == null) {
            logger.Warning("Failed to buy plot: {PlotId}, {OwnerId}", plot.Id, plot.OwnerId);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return false;
        }

        session.ConditionUpdate(ConditionType.buy_house);
        if (session.Field.UpdatePlotInfo(plotInfo) != true) {
            logger.Warning("Failed to update map plot in field: {PlotId}", plotInfo.Id);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_system_error));
            return false;
        }

        session.Field.Broadcast(CubePacket.BuyPlot(plotInfo));
        SetPlot(plotInfo);
        session.World.UpdateFieldPlot(new FieldPlotRequest {
            IgnoreChannel = GameServer.GetChannel(),
            MapId = session.Field.MapId,
            PlotNumber = plotInfo.Number,
            UpdatePlot = new FieldPlotRequest.Types.UpdatePlot(),
        });
        return true;
    }

    public PlotInfo? ForfeitPlot() {
        PlotInfo? plotInfo = Home.Outdoor;
        if (plotInfo == null) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_dont_have_ownership));
            return null;
        }

        // An expired plot is not forfeited: World takes it back as soon as it expires (FieldPlotExpiryCheck), and the
        // storage refuses it too. The client has no rule of its own; this error is the server's.
        if (plotInfo.ExpiryTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_extension_date));
            return null;
        }

        if (!session.FindField(plotInfo.MapId, out FieldManager? fieldManager)) {
            // Should we instantiate a field manager instead of giving an error?
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_system_error));
            return null;
        }

        if (!fieldManager.Plots.TryGetValue(plotInfo.Number, out Plot? plot)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_system_error));
            return null;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        plotInfo = db.ForfeitPlot(session.AccountId, plot);
        if (plotInfo == null) {
            logger.Warning("Failed to forfeit plot: {PlotId}, {OwnerId}", plot.Id, plot.OwnerId);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return null;
        }

        if (fieldManager.UpdatePlotInfo(plotInfo) != true) {
            logger.Warning("Failed to update map plot in field: {PlotId}", plotInfo.Id);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_system_error));
            return null;
        }
        // The forfeit deleted the plot's cubes (and their maid rows); the cubes are still on the plot in memory.
        fieldManager.RemoveMaids(plot);

        fieldManager.Broadcast(CubePacket.ForfeitPlot(plotInfo));
        SetPlot(null);
        session.World.UpdateFieldPlot(new FieldPlotRequest {
            IgnoreChannel = GameServer.GetChannel(),
            MapId = fieldManager.MapId,
            PlotNumber = plotInfo.Number,
            UpdatePlot = new FieldPlotRequest.Types.UpdatePlot {
                AccountId = session.AccountId,
                Forfeit = true,
            },
        });
        return plotInfo;
    }

    public void ExtendPlot() {
        if (Home.Outdoor == null) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_dont_have_ownership));
            return;
        }

        if (Home.Outdoor.State != PlotState.Taken || Home.Outdoor.ExpiryTime <= DateTime.UtcNow.ToEpochSeconds()) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_expired_salable_group));
            return;
        }

        // Both sides in epoch seconds: FromEpochSeconds gives the local time, which less the UTC clock was off by the
        // server's UTC offset.
        if (Home.Outdoor.ExpiryTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds() > (long) TimeSpan.FromDays(30).TotalSeconds) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_extension_date));
            return;
        }

        UgcMapGroup.Cost extension = Home.Outdoor.Metadata.ExtensionCost;

        int costAmount = extension.Amount;
        GameEvent? saleEvent = session.FindEvent(GameEventType.UGCMapExtensionSale).FirstOrDefault();
        if (saleEvent != null) {
            float discount = float.Parse(saleEvent.Metadata.Value1, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture) / 10000f;
            costAmount = (int) Math.Ceiling(costAmount * (1 - discount));
        }

        if (!TryRemoveCost(session, extension.ItemId, costAmount)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_need_extension_pay));
            return;
        }

        // Stored with the cost, as a buy (BuyPlot). A failed extension costs nothing: the cost is given back.
        PlotInfo outdoor = Home.Outdoor;
        PlotInfo? plotInfo = null;
        if (!session.TrySavePurchase(db => (plotInfo = db.ExtendPlot(outdoor, TimeSpan.FromDays(extension.Days))) != null,
                () => RefundCost(session, extension.ItemId, costAmount))) {
            plotInfo = null;
        }
        if (plotInfo == null) {
            logger.Warning("Failed to extend plot: {PlotId}, {OwnerId}", Home.Outdoor.Id, Home.Outdoor.OwnerId);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        session.ConditionUpdate(ConditionType.extend_house);
        if (session.Field?.UpdatePlotInfo(plotInfo) != true) {
            logger.Warning("Failed to update map plot in field: {PlotId}", Home.Outdoor.Id);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_system_error));
            return;
        }

        session.Field.Broadcast(CubePacket.ExtendPlot());
        SetPlot(plotInfo);
        session.World.UpdateFieldPlot(new FieldPlotRequest {
            MapId = session.Field.MapId,
            PlotNumber = plotInfo.Number,
            IgnoreChannel = GameServer.GetChannel(),
            UpdatePlot = new FieldPlotRequest.Types.UpdatePlot(),
        });
    }

    private static bool TryRemoveCost(GameSession session, int itemId, int cost) {
        switch (itemId) {
            case >= 90000001 and <= 90000003:
                // Checked and taken in one step, as the merets below.
                return session.Currency.TryTakeMeso(cost);
            case 90000004 or 90000011 or 90000015 or 90000016:
                return session.Currency.TryTakeMeret(cost);
        }
        return false;
    }

    // Gives back a cost TryRemoveCost took. Chosen here: the meret refund is a plain Meret update with a positive
    // change, so the use_merat count the charge reported stays (the refund does not take it back).
    private static void RefundCost(GameSession session, int itemId, int cost) {
        switch (itemId) {
            case >= 90000001 and <= 90000003:
                session.Currency.ChangeMeso(cost);
                return;
            case 90000004 or 90000011 or 90000015 or 90000016:
                session.Currency.ChangeMeret(cost);
                return;
        }
    }

    public void Save(GameStorage.Request db) {
        db.SaveHome(Home);
        db.SavePlotInfo(Home.Indoor);
    }

    // The concept of a plot purchase (RequestCube 2): the model house dialog (uiugcmodelhousedialog.gfx, opened by
    // UIRealEstateDialog::SendRequestCubeMode2 for a player with no plot, uirealestatedialog.cpp:404-425) sends 0, 1 or
    // 2 for its three model houses (m_concept0..2) and -1 for its empty house (m_empty), through
    // UIUGCModelHouseDialog's OnHomeConcept (uiugcmodelhousedialog.cpp:76-91). Model house c is the exported UGC map
    // c + 1 (exportedugcmap/1.xml to 3.xml), as for the same dialog's RequestHome 3 (HomeHandler.HandleWarp).
    // Chosen here: any other value is taken as -1, the empty house.
    public static string? ConceptTemplate(int concept) {
        return concept is >= 0 and <= 2 ? (concept + 1).ToString() : null;
    }

    // A bought plot's concept builds the buyer's indoor home when it has no name yet (never made), the same as the
    // dialog's RequestHome 3 path; a home that already has a name is left as it is. Nothing is placed on the outdoor
    // plot. The template's cubes are free, as for a new home made by RequestHome 3.
    // Chosen here: an unnamed indoor home that already holds cubes (built through the plot's door) is left as it is
    // too, as building the model house would delete them (SaveCubes) without returning them to storage.
    public bool InitHomeForConcept(int concept) {
        if (!string.IsNullOrEmpty(Home.Indoor.Name)) {
            return false;
        }
        using (GameStorage.Request db = session.GameStorage.Context()) {
            if (db.HasCubes(Home.Indoor.Id)) {
                return false;
            }
        }

        ExportedUgcMapMetadata? template = null;
        string? name = ConceptTemplate(concept);
        if (name != null) {
            session.MapMetadata.TryGetExportedUgc(name, out template);
        }
        InitNewHome(session.Player.Value.Character.Name, template);
        return true;
    }

    public void InitNewHome(string characterName, ExportedUgcMapMetadata? template) {
        Home.NewHomeDefaults(characterName);
        // A new home is at decoration level 1: the first report of interior_level (see InteriorCheckIn). The trophy
        // keeps the highest level, so a home that is created again reports nothing new.
        session.ConditionUpdate(ConditionType.interior_level, counter: TrophyReportRules.InteriorLevelCount(Home.DecorationLevel));

        using GameStorage.Request db = session.GameStorage.Context();
        if (template is null) {
            Home.SetArea(10);
            Home.SetHeight(4);

            db.SaveHome(Home);
            db.SavePlotInfo(Home.Indoor);
            return;
        }

        Home.SetArea(template.IndoorSize[0]);
        Home.SetHeight(template.IndoorSize[2]);

        List<PlotCube> plotCubes = [];
        foreach (ExportedUgcMapMetadata.Cube cube in template.Cubes) {
            if (!session.ItemMetadata.TryGet(cube.ItemId, out ItemMetadata? itemMetadata) || itemMetadata.Install is null || itemMetadata.Housing is null) {
                logger.Error("Failed to get item metadata for cube {cubeId}.", cube.ItemId);
                continue;
            }

            long itemUid = session.Item.Furnishing.AddCube(cube.ItemId);
            if (itemUid == 0) {
                logger.Error("Failed to add cube {cubeId} to storage.", cube.ItemId);
                continue;
            }

            session.FunctionCubeMetadata.TryGet(itemMetadata.Install.ObjectCubeId, out FunctionCubeMetadata? functionCubeMetadata);
            long expiryTime = session.Item.Furnishing.GetCube(itemUid)?.ExpiryTime ?? 0;
            PlotCube? plotCube = CreateCube(Home.Indoor, itemMetadata, functionCubeMetadata, null, template.BaseCubePosition + cube.OffsetPosition, cube.Rotation, expiryTime);
            if (plotCube is null) {
                logger.Error("Failed to create cube {cubeId} at position {position}.", cube.ItemId, template.BaseCubePosition + cube.OffsetPosition);
                continue;
            }

            session.Item.Furnishing.TryAddCube(itemUid, plotCube);

            plotCubes.Add(plotCube);
        }

        db.SaveHome(Home);
        db.SavePlotInfo(Home.Indoor);
        // The cubes already in the home (an unnamed home built through the plot's door) are deleted by the save and
        // go back to furnishing storage in the same transaction, the ones storage does not take by mail
        // (FurnishingManager.PlanReturn). The client's model house dialog does not say they are lost
        // (UIUGCModelHouseDialog), and a home taken away returns its furnishings (system mail 50000001).
        var mailed = new List<(long CharacterId, long MailId)>();
        lock (session.Item) {
            CubeReturn? plan = null;
            List<long> deleted = [];
            ICollection<PlotCube>? saved = db.SaveCubes(Home.Indoor, plotCubes, session.CharacterId, mailed, cubes => {
                deleted = cubes.Select(cube => cube.Id).ToList();
                return plan = session.Item.Furnishing.PlanReturn(cubes, session.CharacterId);
            });
            if (saved == null) {
                // Nothing was written: the cubes in the home stay there and storage is as it was. The model house's
                // cubes, placed in memory only, leave the furnishing inventory.
                logger.Error("Failed to save the model house cubes of home {PlotId}", Home.Indoor.Id);
                session.Item.Furnishing.ForgetCubes(plotCubes.Select(cube => cube.Id));
                return;
            }
            if (plan != null) {
                session.Item.Furnishing.ApplyReturn(plan, deleted);
            }
        }
        FieldManager.NotifyMailed(session, mailed);
    }

    // An interior package (item function UGCMapPackage, UIInteriorBuildDialog::OnUseItem) builds its exported map in
    // the owner's home: the cubes there go back to storage, a home smaller than the package grows to its size, and
    // each cube of the package is put into storage and placed from there at the home's base cell plus its offset
    // (UgcMapPackageRules.Origin). A cube that cannot be placed stays in storage. A package without a size keeps the
    // home's size. The whole install holds the plot's edit lock, so a build helper's edit waits for it.
    // Refused (nothing used, nothing changed) when furnishing storage cannot take the home's cubes and the package's
    // (s_err_ugcmap_package_cant_use) or consume fails; otherwise null with the number placed.
    public StringCode? InstallPackage(Plot plot, ExportedUgcMapMetadata package, Func<bool> consume, out int placed) {
        placed = 0;
        if (session.Field is null) {
            return StringCode.s_err_ugcmap_package_cant_use;
        }
        lock (plot.EditLock) {
            List<PlotCube> old = plot.Cubes.Values.ToList();
            var incoming = new List<UgcMapPackageRules.Stack>();
            incoming.AddRange(old.Select(cube => new UgcMapPackageRules.Stack(cube.ItemId, cube.Template?.Url,
                cube.Metadata.Housing?.MaidId > 0, 1, cube.Metadata.Property.SlotMax, FurnishingManager.KeptExpiry(cube.Metadata, cube.ExpiryTime))));
            foreach (ExportedUgcMapMetadata.Cube cube in package.Cubes) {
                if (session.ItemMetadata.TryGet(cube.ItemId, out ItemMetadata? metadata) && metadata.Install != null && metadata.Housing != null) {
                    incoming.Add(new UgcMapPackageRules.Stack(cube.ItemId, null, metadata.Housing.MaidId > 0, 1, metadata.Property.SlotMax));
                }
            }
            if (!session.Item.Furnishing.StorageTakes(incoming)) {
                return StringCode.s_err_ugcmap_package_cant_use;
            }
            if (!consume()) {
                return StringCode.s_err_ugcmap_package_failed_to_consume_package_item;
            }
            placed = BuildPackage(plot, package, old);
        }
        return null;
    }

    private int BuildPackage(Plot plot, ExportedUgcMapMetadata package, List<PlotCube> old) {
        FieldManager field = session.Field!;
        foreach (PlotCube cube in old) {
            if (RemoveCube(plot, cube.Position, out _, null)) {
                field.Broadcast(CubePacket.RemoveCube(session.Player.ObjectId, cube.Position));
            }
        }

        if (UgcMapPackageRules.Size(package) is var (area, height) && (Home.Area < area || Home.Height < height)) {
            Home.SetArea(Math.Max(Home.Area, area));
            Home.SetHeight(Math.Max(Home.Height, height));
            field.Broadcast(CubePacket.UpdateHomeAreaAndHeight(Home.Area, Home.Height));
        }

        Vector3B origin = UgcMapPackageRules.Origin(Home.Area);
        int placed = 0;
        foreach (ExportedUgcMapMetadata.Cube cube in package.Cubes) {
            if (!session.ItemMetadata.TryGet(cube.ItemId, out ItemMetadata? itemMetadata) || itemMetadata.Install is null || itemMetadata.Housing is null) {
                logger.Error("Package {PackageId}: no install data for cube {CubeId}.", package.Id, cube.ItemId);
                continue;
            }
            long itemUid = session.Item.Furnishing.AddCube(cube.ItemId);
            if (itemUid == 0) {
                logger.Error("Package {PackageId}: cube {CubeId} not added to storage.", package.Id, cube.ItemId);
                continue;
            }
            Vector3B position = origin + cube.OffsetPosition;
            if (!PlaceCube(new PlotCube(itemMetadata, itemUid) { Type = PlotCube.CubeType.Default }, plot, itemMetadata, position, cube.Rotation, out PlotCube? plotCube, false, null)) {
                continue;
            }
            field.Broadcast(plotCube.Position.Z == 0
                ? CubePacket.ReplaceCube(session.Player.ObjectId, plotCube)
                : CubePacket.PlaceCube(session.Player.ObjectId, plot, plotCube));
            placed++;
        }

        Vector3 safe = Home.CalculateSafePosition(plot.Cubes.Values.ToList());
        foreach (FieldPlayer fieldPlayer in field.Players.Values) {
            fieldPlayer.Session.Send(PortalPacket.MoveByPortal(fieldPlayer, safe, default));
        }
        session.Item.Furnishing.SendStorageCount();
        SaveHome();
        session.Send(NoticePacket.Message(StringCode.s_ugcmap_package_automatic_creation_completed, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
        return placed;
    }

    // The decoration score of a plot as it stands: the points of the decoration goals its cubes meet.
    public static int DecorationScore(Plot plot) {
        return InteriorPointRules.Score(plot.Cubes.Values
            .Where(plotCube => plotCube.Metadata.Housing != null)
            .GroupBy(plotCube => plotCube.Metadata.Housing!.HousingCategory)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.Count()));
    }

    public void InteriorCheckIn(Plot plot) {
        if (session.Field is null) return;

        if (Home.DecorationRewardTimestamp != 0) {
            session.Send(CubePacket.InteriorCheckInError(InteriorRewardRules.CheckInError(true, false)));
            return;
        }

        int decorationScore = DecorationScore(plot);

        // The row the client shows and offers (InteriorRewardRules.RewardPoint); a score with no row gets 85, as a box
        // that cannot be made.
        UgcHousingPointRewardTable.Entry? reward = null;
        int? housingPoint = InteriorRewardRules.RewardPoint(tableMetadata.UgcHousingPointRewardTable.Entries.Keys, decorationScore);
        if (housingPoint is null || !tableMetadata.UgcHousingPointRewardTable.Entries.TryGetValue(housingPoint.Value, out reward) || reward.IndividualDropBoxId <= 0) {
            session.Send(CubePacket.InteriorCheckInError(InteriorRewardRules.CheckInError(false, false)));
            return;
        }

        ICollection<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, reward.IndividualDropBoxId);

        // The check-in (its time and the exp it adds) is stored before the box is given: the box went first and a
        // mailed item's row is written at once, so a crash before the next session save left the home not checked in
        // and the box could be taken again after a relog. A refused store gives nothing and leaves the check-in open.
        long levelBefore = Home.DecorationLevel;
        long expBefore = Home.DecorationExp;
        Home.GainExp(decorationScore, tableMetadata.MasteryUgcHousingTable.Entries);
        Home.DecorationRewardTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!session.TrySavePurchase(db => db.SaveHome(Home), () => {
                Home.DecorationLevel = levelBefore;
                Home.DecorationExp = expBefore;
                Home.DecorationRewardTimestamp = 0;
            })) {
            logger.Warning("Interior check-in not stored, nothing given AccountId={AccountId}", session.AccountId);
            session.Send(CubePacket.InteriorCheckInError(UgcMapError.s_ugcmap_take_interior_gift_fail_from_db));
            return;
        }
        foreach (Item item in items) {
            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }
        session.Send(CubePacket.DesignRankReward(Home));

        // interior_exp_offset (trophy 23500023) adds the decoration exp of the check-in; interior_level (trophies
        // 23500020-23500022) takes the home's decoration level and keeps the highest, so a home that is ahead of its
        // trophy catches up here. KMS2 sends them in this order after the check-in answer.
        bool gained = Home.DecorationLevel != levelBefore || Home.DecorationExp != expBefore;
        long expCounted = TrophyReportRules.InteriorExpCounted(decorationScore, gained);
        if (expCounted > 0) {
            session.ConditionUpdate(ConditionType.interior_exp_offset, counter: expCounted);
        }
        session.ConditionUpdate(ConditionType.interior_level, counter: TrophyReportRules.InteriorLevelCount(Home.DecorationLevel));
    }

    public void InteriorReward(byte rewardId) {
        tableMetadata.MasteryUgcHousingTable.Entries.TryGetValue(rewardId, out MasteryUgcHousingTable.Entry? reward);
        UgcMapError? error = InteriorRewardRules.GradeError(Home.InteriorRewardsClaimed, Home.DecorationLevel, rewardId, reward != null);
        if (error is null) {
            return;
        }
        Item? rewardItem = error == UgcMapError.s_ugcmap_ok ? session.Field?.ItemDrop.CreateItem(reward!.RewardJobItemId) : null;
        if (rewardItem == null) {
            session.Send(CubePacket.InteriorGradeRewardError(error == UgcMapError.s_ugcmap_ok ? UgcMapError.s_ugcmap_take_interior_gift_fail_from_db : error.Value));
            return;
        }

        // The claimed grade is stored before the item is given, as the check-in (InteriorCheckIn).
        Home.InteriorRewardsClaimed.Add(rewardId);
        if (!session.TrySavePurchase(db => db.SaveHome(Home), () => Home.InteriorRewardsClaimed.Remove(rewardId))) {
            logger.Warning("Interior grade {Grade} claim not stored, nothing given AccountId={AccountId}", rewardId, session.AccountId);
            session.Send(CubePacket.InteriorGradeRewardError(UgcMapError.s_ugcmap_take_interior_gift_fail_from_db));
            return;
        }
        if (!session.Item.Inventory.Add(rewardItem, true)) {
            session.Item.MailItem(rewardItem);
        }
        session.Send(CubePacket.DesignRankReward(Home));
    }

    // The row is made with the expiry of the storage item the cube is placed from (TryAddCube sets the same value), so
    // a placed cube keeps its end time even if the plot is never saved after it.
    // inTransaction: the request the row is written in (a player's place writes it with the storage row); null for
    // a row written at once.
    private PlotCube? CreateCube(PlotInfo plot, ItemMetadata itemMetadata, FunctionCubeMetadata? functionCubeMetadata, UgcItemLook? template, Vector3 position, float rotation, long expiryTime,
                                 GameStorage.Request? inTransaction = null) {
        var result = new PlotCube(itemMetadata, 0, template) {
            Type = PlotCube.CubeType.Construction,
            Position = position,
            Rotation = rotation,
            ExpiryTime = expiryTime,
        };

        using GameStorage.Request? own = inTransaction == null ? session.GameStorage.Context() : null;
        GameStorage.Request db = inTransaction ?? own!;

        if (functionCubeMetadata is not null) {
            result.Interact = new InteractCube(position, functionCubeMetadata);
            if (result.Interact.Nurturing is not null && result.Interact.Metadata.Nurturing is not null) {
                Nurturing? nurturing = db.GetNurturing(session.AccountId, result.Interact.Metadata.Id, result.Interact.Metadata.Nurturing);
                if (nurturing is null) {
                    nurturing = db.CreateNurturing(session.AccountId, result.Interact.Metadata.Nurturing, result.Interact.Metadata.Id);
                    if (nurturing is null) {
                        logger.Error("Failed to create Nurturing for {AccountId}, ItemId {ItemId}", session.AccountId, result.ItemId);
                        return null;
                    }
                }
            }
        }

        result = db.CreateCube(plot, result);
        if (result is null) {
            logger.Error("Failed to create cube for plot {PlotNumber} at position {Position}.", plot.Number, position);
            return null;
        }

        return result;
    }

    // Called under the plot's edit lock. A row that cannot be deleted is kept as a stray row of the plot: the
    // instance stays current (HomeFieldManager.Outdated) and its next save deletes the row.
    private void DeleteCube(Plot plot, PlotCube cube) {
        using GameStorage.Request db = session.GameStorage.Context();
        if (db.DeleteCube(cube)) return;
        logger.Error("Failed to delete cube {CubeId} at position {Position}.", cube.Id, cube.Position);
        if (cube.Id < Constant.FurnishingBaseId) {
            plot.StrayRows.Add(cube.Id);
        }
    }

    // Turns the cube at the position and writes its rotation to its row at once, as a place or a remove writes its
    // row: a field judged outdated saves nothing at the leave, and another instance's rotation is seen by Outdated.
    // When the row cannot be written the cube is turned back and the rotation refused, so the held cube keeps its
    // row's rotation (the field stays current).
    public bool TryRotateCube(Plot plot, in Vector3B position, bool clockwise, [NotNullWhen(true)] out PlotCube? cube) {
        lock (plot.EditLock) {
            if (!plot.Cubes.TryGetValue(position, out cube)) {
                return false;
            }
            float before = cube.Rotation;
            cube.Rotate(clockwise);
            if (plot.IsPlanner || plot.Id == 0 || cube.Id >= Constant.FurnishingBaseId) {
                return true;
            }
            using GameStorage.Request db = session.GameStorage.Context();
            if (!db.SetCubeRotation(cube.Id, cube.Rotation)) {
                logger.Error("Failed to save the rotation of cube {CubeId} at position {Position}.", cube.Id, cube.Position);
                cube.Rotation = before;
                return false;
            }
            return true;
        }
    }

    public bool TryPlaceCube(HeldCube cube, Plot plot, ItemMetadata itemMetadata, in Vector3B position, float rotation,
                             [NotNullWhen(true)] out PlotCube? result, bool isReplace = false, GameSession? helper = null, bool byPlayer = false) {
        try {
            lock (plot.EditLock) {
                return PlaceCube(cube, plot, itemMetadata, position, rotation, out result, isReplace, helper, byPlayer);
            }
        } finally {
            // Stored after the plot's lock is let go, so the store's account lock and commit do not hold other edits
            // of the plot. A layout apply places many cubes and stores their prices once, after the last.
            if (byPlayer) {
                StoreCubePrices();
            }
        }
    }

    // A cube bought in PlaceCube has its plot row (CreateCube) and its storage row (AddCube) written at once, but its
    // price was taken in memory only (PurchaseCube), stored by the next session save: a crash before it kept the cube
    // and gave the price back. The currency is stored once the place (or a layout apply's last place) is done
    // (SavePurchase: the currency alone); a failed store is left to the next save.
    private bool pricesTaken;

    private void StoreCubePrices() {
        if (!pricesTaken) {
            return;
        }
        pricesTaken = false;
        if (!session.SavePurchase()) {
            logger.Error("Cube prices not stored now, left to the next save CharacterId={CharacterId}", session.CharacterId);
        }
    }

    // The cube's shop row with the price the running SaleHousing events leave, the price the build palette shows:
    // the first event row with the item's housing filter and the row's money type (EventSaleRules.HousingPrice),
    // the events taken by id as the client keeps them (gamerule.cpp:1988).
    private FurnishingShopTable.Entry? SalePrice(FurnishingShopTable.Entry? shopEntry, ItemMetadata? itemMetadata) {
        if (shopEntry == null || itemMetadata?.Housing == null || shopEntry.Price <= 0) {
            return shopEntry;
        }
        SaleHousingCube[] cubes = session.FindEvent(GameEventType.SaleHousing).OrderBy(gameEvent => gameEvent.Id)
            .SelectMany(gameEvent => gameEvent.Metadata.Data is SaleHousing sale ? sale.Cubes : []).ToArray();
        if (cubes.Length == 0) {
            return shopEntry;
        }
        int price = EventSaleRules.HousingPrice(new SaleHousing(cubes), itemMetadata.Housing.Filter, shopEntry.FurnishingTokenType, shopEntry.Price);
        return price == shopEntry.Price ? shopEntry : shopEntry with { Price = price };
    }

    private bool PlaceCube(HeldCube cube, Plot plot, ItemMetadata itemMetadata, in Vector3B position, float rotation,
                           [NotNullWhen(true)] out PlotCube? result, bool isReplace, GameSession? helper, bool byPlayer = false) {
        result = null;
        // A build helper edits through the owner's manager; what the edit refuses goes to the helper.
        GameSession notify = helper ?? session;
        if (session.Field is null || itemMetadata.Install is null) return false;
        CubePacket.Edit edit = isReplace ? CubePacket.Edit.Replace : CubePacket.Edit.Place;
        void Refuse(UgcMapError error) => notify.Send(CubePacket.EditError(edit, error, notify.Player.ObjectId));

        // The client sends a whole angle in degrees, a multiple of 90 unless the cube is attached
        // (Unk_PCCubeCraft::ConfirmPlacement, cubecraft_2.cpp:991-995); a NaN or infinite angle was stored with the
        // cube and sent to everyone who enters the home. Chosen here: refused as a cube with no item info.
        if (!float.IsFinite(rotation)) {
            if (byPlayer) {
                Refuse(UgcMapError.s_ugcmap_null_cube_item_info);
            } else {
                logger.Warning("Cube {ItemId} at {Position} of plot {PlotId} not placed: its rotation {Rotation} is not finite",
                    cube.ItemId, position, plot.Number, rotation);
            }
            return false;
        }

        // The client checks these when a player confirms a placement (cubecraft_2.cpp:954-968); package installs and
        // layouts place their cubes without them. The trophies and the housing level are this manager's session's: the
        // builder's own, or for a helper the home owner's.
        tableMetadata.FurnishingShopTable.Entries.TryGetValue(cube.ItemId, out FurnishingShopTable.Entry? shopEntry);
        UgcMapError cubeLock = UgcMapError.s_ugcmap_ok;
        if (byPlayer) {
            bool hurdle = session.ServerTableMetadata.FeatureTable.IsOn(CubeEditRules.LevelHurdleFeature);
            int? categoryLevel = hurdle && helper == null && itemMetadata.Housing != null
                && tableMetadata.UgcCubeCategoryTable.RequireLevels.TryGetValue(itemMetadata.Housing.CategoryIndex, out int level) ? level : null;
            cubeLock = CubeEditRules.CubeLock(itemMetadata.Housing, shopEntry?.FurnishingTokenType, (id, grade) => session.Achievement.HasAchievement(id, grade),
                hurdle ? Home.DecorationLevel : null, categoryLevel);
        }
        if (cubeLock != UgcMapError.s_ugcmap_ok) {
            Refuse(cubeLock);
            return false;
        }
        UgcMapError emptyTemplate = byPlayer ? CubeEditRules.EmptyTemplate(itemMetadata.Mesh, cube.Template?.Id) : UgcMapError.s_ugcmap_ok;
        if (emptyTemplate != UgcMapError.s_ugcmap_ok) {
            Refuse(emptyTemplate);
            return false;
        }

        shopEntry = SalePrice(shopEntry, itemMetadata);

        switch (plot.PlotMode) {
            case PlotMode.BlueprintPlanner when itemMetadata.Housing!.IsNotAllowedInBlueprint:
                Refuse(UgcMapError.s_err_cannot_install_blueprint);
                return false;
            case PlotMode.BlueprintPlanner when itemMetadata.Housing!.HousingCategory is HousingCategory.Farming or HousingCategory.Ranching:
                Refuse(UgcMapError.s_err_cannot_install_nurturing_in_design_home);
                return false;
            case PlotMode.BlueprintPlanner when cube.ItemId is Constant.InteriorPortalCubeId:
                Refuse(UgcMapError.s_err_cannot_install_magic_portal);
                return false;
            case PlotMode.BlueprintPlanner when shopEntry is null:
                Refuse(UgcMapError.s_err_cannot_buy_limited_item_more);
                return false;
        }

        float groundHeight = 0;
        bool outdoor = plot.MapId is not Constant.DefaultHomeMapId;
        if (outdoor) {
            FieldSellableTile? groundTile = session.Field.AccelerationStructure?.FirstSellableTile(position, (x) => x.SellableGroup == plot.Number);
            if (groundTile is null) {
                Refuse(UgcMapError.s_ugcmap_cant_create_on_place);
                return false;
            }

            groundHeight = groundTile.Position.Z / Constant.BlockSize;
        }

        if (!outdoor && itemMetadata.Install.IndoorPortal) {
            Refuse(UgcMapError.s_ugcmap_cant_be_created);
            return false;
        }

        // can only place one indoor portal per plot
        if (outdoor && plot.Cubes.Any(x => x.Value.Metadata.Install is { IndoorPortal: true }) && itemMetadata.Install.IndoorPortal) {
            Refuse(UgcMapError.s_ugcmap_cant_create_on_place);
            return false;
        }

        bool isSolidCube = itemMetadata.Install!.IsSolidCube;
        bool isOnGround = Math.Abs(position.Z - groundHeight) < 0.1;
        bool allowWaterOnGround = itemMetadata.Install.MapAttribute is MapAttribute.water && Constant.AllowWaterOnGround;

        // If the cube is not a solid cube and it's replacing ground, it's not allowed.
        if ((!isSolidCube && isOnGround) && !allowWaterOnGround) {
            Refuse(UgcMapError.s_ugcmap_cant_create_on_place);
            return false;
        }

        // A place on a taken cell and a replace by the cube that is there are refused as KMS2 refuses them.
        UgcMapError taken = CubeEditRules.Taken(isReplace, plot.Cubes.TryGetValue(position, out PlotCube? there) ? there.ItemId : null, cube.ItemId);
        if (taken != UgcMapError.s_ugcmap_ok) {
            Refuse(taken);
            return false;
        }

        UgcMapError bounds = outdoor ? CubeEditRules.PlotHeight(position.Z - groundHeight, plot.Metadata.Limit.Height) : HomeBounds(position);
        if (bounds != UgcMapError.s_ugcmap_ok) {
            Refuse(bounds);
            return false;
        }

        // The group's count limits hold for a player's place only: the client counts them for its display alone
        // (CubeMapInformationService FUN_1410d27c0) and a layout or package apply checks none, so such an apply places
        // its cubes past them as the layout or package has them.
        Vector3B cell = position;
        session.FunctionCubeMetadata.TryGet(itemMetadata.Install.ObjectCubeId, out FunctionCubeMetadata? placedFunction);
        UgcMapError groupLimit = !byPlayer ? UgcMapError.s_ugcmap_ok : CubeEditRules.GroupLimit(plot.Metadata.Limit, itemMetadata, placedFunction,
            plot.Cubes.Values
                .Where(other => !(isReplace && other.Position == cell))
                .Select(other => (other.Metadata,
                    other.Metadata.Install is { } install && session.FunctionCubeMetadata.TryGet(install.ObjectCubeId, out FunctionCubeMetadata? f) ? f : null)));
        if (groupLimit != UgcMapError.s_ugcmap_ok) {
            Refuse(groupLimit);
            return false;
        }

        // The cube's storage stack, and the expiry the cube will take: the stack's, or for a cube bought here that of
        // the new item, which the purchase then keeps (AddCube joins only a stack of that expiry).
        Item? storedItem = null;
        long expiryTime = 0;
        if (!plot.IsPlanner) {
            // The client names both the storage uid and the item id: a stack of another item is not this cube's, so
            // the cube is bought as any cube not in storage (a cheap stack's uid must not place a dear item).
            storedItem = helper == null ? session.Item.Furnishing.GetCube(cube.Id) : null;
            if (storedItem != null && storedItem.Id != cube.ItemId) {
                storedItem = null;
            }
            // The design (template) of a UGC cube is the storage stack's, never the client's: a cube from storage must
            // name its stack's design and takes the stack's template, and a design is never bought. Before, the
            // client's template was placed and went back to storage on pick-up, so any design (another player's, or
            // one never paid for) could be made from a plain stack or a shop buy.
            if (!CubeEditRules.SameDesign(storedItem is { Amount: > 0 } ? storedItem : null, cube.Template)) {
                Refuse(UgcMapError.s_ugcmap_not_owned_item);
                return false;
            }
            // No field of the client's template is kept: the stack's (or none on a buy).
            cube.Template = storedItem is { Amount: > 0 } ? storedItem.Template?.Clone() : null;
            if (storedItem is { Amount: > 0 }) {
                expiryTime = storedItem.ExpiryTime;
            } else {
                expiryTime = session.Field.ItemDrop.CreateItem(cube.ItemId)?.ExpiryTime ?? 0;
                cube.ExpiryTime = expiryTime;
            }

            // Storage must take back the old cube of a replace (RemoveCube refuses it otherwise) and a cube bought here
            // (it goes to storage once paid). Asked before anything is charged or taken off the plot.
            bool buying = helper == null && storedItem is not { Amount: > 0 } && shopEntry != null;
            PlotCube? replaced = isReplace ? there : null;
            var incoming = new List<UgcMapPackageRules.Stack>();
            if (replaced != null) {
                incoming.Add(FurnishingManager.RetrievedStack(replaced));
            }
            if (buying) {
                incoming.Add(new UgcMapPackageRules.Stack(cube.ItemId, cube.Template?.Url, itemMetadata.Housing?.MaidId > 0, 1,
                    itemMetadata.Property.SlotMax, FurnishingManager.KeptExpiry(itemMetadata, cube.ExpiryTime)));
            }
            if (incoming.Count > 0 && !session.Item.Furnishing.StorageTakes(incoming)) {
                if (replaced != null && !session.Item.Furnishing.CanRetrieve(replaced)) {
                    Refuse(UgcMapError.s_ugcmap_cant_be_removed);
                } else {
                    // What is full is the furnishing storage, which is none of the client's 15 inventory tabs: a cube's
                    // own tab (FUN_14053e530) is the life tab, so the inventory notice named that tab. No
                    // furnishing-storage-full code was found in the client's ugcmap errors. Chosen here: the storage
                    // notice s_item_err_store_full, which takes no argument.
                    notify.Send(NoticePacket.Message(StringCode.s_item_err_store_full, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
                }
                return false;
            }

            // An owner's cube not in storage is bought below, after the old cube of a replace is taken off. Whether it
            // can be bought and paid for is asked here: a refusal after the old cube is off would leave the cell empty
            // here while the client, sent only the error, still shows the old cube.
            if (helper == null && storedItem is not { Amount: > 0 }) {
                if (shopEntry is null) {
                    Refuse(UgcMapError.s_err_cannot_buy_limited_item_more);
                    return false;
                }
                if (!shopEntry.Buyable) {
                    return false;
                }
                UgcMapError payError = session.Item.Furnishing.CanPay(shopEntry);
                if (payError != UgcMapError.s_ugcmap_ok) {
                    Refuse(payError);
                    return false;
                }
            }
        }

        // A helper's cube is paid from the budget. Asked before the old cube of a replace is taken off: a
        // refusal after that would leave the cell empty here while the clients still show the old cube.
        if (helper != null && !plot.IsPlanner) {
            if (shopEntry is not { Buyable: true }) {
                Refuse(UgcMapError.s_ugcmap_not_for_sale);
                return false;
            }
            UgcMapError budgetError;
            lock (Home) {
                budgetError = HomeBuildRules.CanCharge(Home, shopEntry.FurnishingTokenType == FurnishingCurrencyType.Meret, shopEntry.Price);
            }
            if (budgetError != UgcMapError.s_ugcmap_ok) {
                Refuse(budgetError);
                return false;
            }
        }

        if (isReplace && plot.Cubes.ContainsKey(position) && !RemoveCube(plot, position, out _, helper)) {
            return false;
        }

        session.FunctionCubeMetadata.TryGet(itemMetadata.Install.ObjectCubeId, out FunctionCubeMetadata? functionCubeMetadata);

        if (plot.IsPlanner) {
            if (shopEntry is null && cube.Id == 0) {
                Refuse(UgcMapError.s_ugcmap_cant_additionalbuy);
                return false;
            }
            Item? existingItem = session.Item.Furnishing.GetItem(cube.ItemId);
            int existingCubeCount = plot.Cubes.Count(x => x.Value.ItemId == cube.ItemId);
            if (shopEntry is null && (existingItem is null || existingCubeCount >= existingItem.Amount)) {
                Refuse(UgcMapError.s_err_cannot_buy_limited_item_more);
                return false;
            }

            result = new PlotCube(itemMetadata, FurnishingManager.NextCubeId(), cube.Template) {
                Position = position,
                Rotation = rotation,
                Type = PlotCube.CubeType.Construction,
            };

            if (functionCubeMetadata is not null) {
                result.Interact = new InteractCube(position, functionCubeMetadata);
            }

            plot.Cubes.Add(position, result);
            return true;
        }

        // The cube's source: a helper's buy from the budget, the owner's storage stack, or the owner's buy. The buy's
        // price is taken first, outside the transaction below (a meso take updates quests, which may store).
        bool fromStorage = helper == null && storedItem is { Amount: > 0 };
        bool buyHere = helper == null && !fromStorage;
        if (buyHere) {
            // A cube bought here goes to storage only once it is paid: a cube not for sale, or one the player cannot
            // pay for, leaves no storage item. Room was asked above, so the paid cube is not refused by storage.
            if (shopEntry is null) {
                Refuse(UgcMapError.s_err_cannot_buy_limited_item_more);
                return false;
            }
            if (!session.Item.Furnishing.PurchaseCube(shopEntry, out UgcMapError purchaseError)) {
                if (purchaseError != UgcMapError.s_ugcmap_ok) {
                    Refuse(purchaseError);
                }
                return false;
            }
        }

        // The cube's row and its storage stack's row (a bought cube's new stack too, and for a helper the home's row
        // with the budget it charged) are written in one transaction, and the bag follows. Before, the cube's row was
        // written at once and the stack (and budget) waited for the session's next save: a stop before it kept the cube
        // on the plot and in storage both (or a bought cube twice). A buy's price is still stored after the place
        // (StoreCubePrices).
        long storedUid = 0; // the storage item the cube was placed from, when it was in storage already
        bool bought = false;
        lock (session.Item) {
            using GameStorage.Request db = session.GameStorage.Context();
            db.BeginTransaction();
            Action? undo = buyHere ? () => session.Item.Furnishing.RefundCube(shopEntry!) : null;
            bool Fail(UgcMapError error) {
                undo?.Invoke();
                Refuse(error);
                return false;
            }

            result = CreateCube(plot, itemMetadata, functionCubeMetadata, cube.Template, position, rotation, expiryTime, db);
            if (result is null) {
                return Fail(UgcMapError.s_ugcmap_db);
            }
            PlotCube placed = result;

            if (helper != null) {
                if (!BuyFromBudget(helper, cube, shopEntry, placed, edit, db, out undo)) {
                    undo?.Invoke();
                    return false;
                }
            } else if (fromStorage) {
                long stackUid = cube.Id;
                if (!session.Item.Furnishing.TryAddCube(stackUid, placed)) {
                    return Fail(UgcMapError.s_ugcmap_not_owned_item);
                }
                undo = () => session.Item.Furnishing.UndoPlace(stackUid, placed.Id, created: false);
                if (!session.Item.Furnishing.SaveStack(db, stackUid)) {
                    return Fail(UgcMapError.s_ugcmap_db);
                }
                storedUid = stackUid;
            } else {
                long itemUid = session.Item.Furnishing.AddCube(cube, db, out bool created);
                if (itemUid == 0) {
                    logger.Error("Paid cube {ItemId} could not be added to storage, refunded", cube.ItemId);
                    return Fail(UgcMapError.s_ugcmap_db);
                }
                Action refund = undo!;
                undo = () => {
                    session.Item.Furnishing.UndoBuy(itemUid, created);
                    refund();
                };
                // KMS2 sends the bought notice between the storage add and the place.
                session.Send(CubePacket.PurchaseCube(session.Player.ObjectId));
                // Now that we have purchased the cube, it must be placeable.
                if (!session.Item.Furnishing.TryAddCube(itemUid, placed)) {
                    return Fail(UgcMapError.s_ugcmap_not_owned_item);
                }
                undo = () => {
                    session.Item.Furnishing.UndoPlace(itemUid, placed.Id, created: false);
                    session.Item.Furnishing.UndoBuy(itemUid, created);
                    refund();
                };
                if (!session.Item.Furnishing.SaveStack(db, itemUid)) {
                    return Fail(UgcMapError.s_ugcmap_db);
                }
                bought = true;
            }

            if (!db.Commit()) {
                logger.Error("Cube {ItemId} of plot {PlotId} not stored; nothing placed", cube.ItemId, plot.Number);
                return Fail(UgcMapError.s_ugcmap_db);
            }
        }

        if (bought) {
            pricesTaken = true;
            session.Item.Furnishing.ReportPurchase(shopEntry!);
        }
        if (helper != null) {
            helper.Send(CubePacket.PurchaseCube(helper.Player.ObjectId));
            if (shopEntry!.Price > 0) {
                SendBuildBudget();
            }
        }

        if (functionCubeMetadata is not null) {
            session.Field.AddFieldFunctionInteract(result, plot);
        }

        if (outdoor && itemMetadata.Install.IndoorPortal) {
            FieldPortal? fieldPortal = session.Field.SpawnFieldToHomePortal(result, plot.OwnerId);
            if (fieldPortal is null) {
                // The place is stored already: the cube goes back to storage as a pickup does (its row deleted and the
                // stack given back in one transaction). Before, its row was deleted and the stack's take kept, so the
                // cube was in neither. When that store fails too, the cube stays placed, without its portal.
                if (ReturnToStorage(result)) {
                    if (result.Interact is not null) {
                        session.Field.RemoveFieldFunctionInteract(result.Interact.Id);
                    }
                    Refuse(UgcMapError.s_ugcmap_not_owned_item);
                    return false;
                }
                plot.Cubes.Add(position, result);
                session.Field.AddMaid(result, plot, announce: MoveMaidToCube(storedUid, result, plot));
                return true;
            }
            session.Field.Broadcast(PortalPacket.Add(fieldPortal));
        }

        plot.Cubes.Add(position, result);
        session.Field.AddMaid(result, plot, announce: MoveMaidToCube(storedUid, result, plot));
        return true;
    }

    // A maid item placed from storage brings its maid: the maid row moves from the item uid to the cube uid, the uid
    // the client knows the placed furnishing by. True when a maid moved (the owner's list drops the item uid here
    // and gets the cube uid when the maid is placed).
    private bool MoveMaidToCube(long storedUid, PlotCube cube, Plot plot) {
        if (storedUid == 0 || MaidRules.MaidId(cube.Metadata, plot.IsPlanner) == 0) {
            return false;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        Maid? moved = db.MoveMaid(storedUid, cube.Id);
        if (moved == null) {
            return false;
        }
        session.Send(UserMaidPacket.Remove(storedUid));
        if (MaidRules.IsCrafting(moved)) {
            session.Send(MaidCraftItemPacket.Remove(storedUid)); // the craft comes back under the cube uid
        }
        return true;
    }

    // A maid cube picked up keeps its maid: the row moves from the cube uid to the storage item the cube went back
    // to, which the maid item tooltip and the owner's list look it up by.
    public bool TryRemoveCube(Plot plot, in Vector3B position, [NotNullWhen(true)] out PlotCube? cube, GameSession? helper = null) {
        lock (plot.EditLock) {
            return RemoveCube(plot, position, out cube, helper);
        }
    }

    private bool RemoveCube(Plot plot, in Vector3B position, [NotNullWhen(true)] out PlotCube? cube, GameSession? helper) {
        GameSession notify = helper ?? session;
        if (!plot.Cubes.TryGetValue(position, out cube)) {
            notify.Send(CubePacket.EditError(CubePacket.Edit.Remove, UgcMapError.s_ugcmap_no_cube_to_remove, notify.Player.ObjectId));
            return false;
        }
        // Storage must take the cube back before it leaves the plot (a cube of its own expiry may need a new slot).
        if (!plot.IsPlanner && !session.Item.Furnishing.CanRetrieve(cube)) {
            notify.Send(CubePacket.EditError(CubePacket.Edit.Remove, UgcMapError.s_ugcmap_cant_be_removed, notify.Player.ObjectId));
            cube = null;
            return false;
        }
        plot.Cubes.Remove(position);

        if (cube.Interact?.PortalSettings is not null) {
            session.Field?.RemovePortal(cube.Interact.PortalSettings.PortalObjectId);
        }

        if (cube.Interact is not null) {
            session.Field?.RemoveFieldFunctionInteract(cube.Interact.Id);
        }
        session.Field?.RemoveMaid(cube.Id);

        if (plot.MapId is not Constant.DefaultHomeMapId && cube.Metadata.Install is { IndoorPortal: true }) {
            FieldPortal? portal = session.Field?.GetPortals().FirstOrDefault(x => x.HomeId == plot.OwnerId);
            if (portal is not null) {
                session.Field?.RemovePortal(portal.ObjectId);
            }
        }

        if (plot.IsPlanner) {
            return true;
        }

        // A mannequin's items go back to the plot owner. Removal always runs on the owner's housing manager (a build
        // helper's removal goes through owner.Housing), so the owner check only guards that; the doll has already
        // left the field above, so no put on can add an item after the rows are read.
        if (cube.Interact?.Metadata.FittingDoll != null && plot.OwnerId == session.AccountId) {
            FittingDollHandler.ReturnItems(session, cube.Id);
        }

        if (!ReturnToStorage(cube)) {
            // The cube stays where it is: on the plot and in its row (and in storage nothing changed).
            plot.Cubes[position] = cube;
            notify.Send(CubePacket.EditError(CubePacket.Edit.Remove, UgcMapError.s_ugcmap_db, notify.Player.ObjectId));
            cube = null;
            return false;
        }
        return true;
    }

    // A placed cube goes back to the owner's storage: the storage stack it joins (or its new row), the maid it takes
    // along and the cube row's delete are stored in one transaction, and the bag follows. Before, the cube row went
    // at once and the stack waited for the session's next save, so a stop before it lost the cube. False when
    // nothing was stored (the cube is in the furnishing inventory and its row as before).
    private bool ReturnToStorage(PlotCube cube) {
        long itemUid;
        Maid? moved = null;
        lock (session.Item) {
            using GameStorage.Request db = session.GameStorage.Context();
            db.BeginTransaction();
            itemUid = session.Item.Furnishing.RetrieveCube(cube.Id, db, out bool created);
            if (itemUid == 0) {
                // RetrieveCube kept the cube in the furnishing inventory.
                logger.Error("Failed to deposit cube {CubeId} back into storage.", cube.Id);
                return false;
            }
            // Before the cube row goes, which would take a maid still keyed by it.
            bool stored = session.Item.Furnishing.SaveStack(db, itemUid);
            if (stored && cube.Metadata.Housing?.MaidId is > 0) {
                // A maid that cannot move goes with the cube row, as before.
                moved = db.MoveMaid(cube.Id, itemUid);
            }
            stored = stored && db.DeleteCube(cube) && db.Commit();
            if (!stored) {
                logger.Error("Return of cube {CubeId} to storage not stored; the cube stays placed", cube.Id);
                session.Item.Furnishing.UndoRetrieve(itemUid, cube, created);
                return false;
            }
        }
        if (moved != null) {
            session.Send(UserMaidPacket.Add(moved));
            if (MaidRules.IsCrafting(moved)) {
                session.Send(MaidCraftItemPacket.Add(moved));
            }
        }
        return true;
    }

    #region Build budget and helpers
    // The owner whose home this session was told it may build in (ResponseCube 0x35); 0 when none.
    public long BuildOwnerId { get; private set; }

    public bool IsBuilder(long accountId) {
        lock (Home.Builders) {
            return Home.Builders.Contains(accountId);
        }
    }

    // The plot a session may place, remove, rotate and replace cubes on: its own, or the plot of the home it
    // is in when that home's owner is in the field and lists the session's account as a build helper.
    // owner is the session whose home, storage and budget the edit uses.
    public Plot? GetBuildFieldPlot(out GameSession owner) {
        owner = session;
        Plot? plot = GetOwnFieldPlot();
        if (plot != null) {
            return plot;
        }

        GameSession? host = FindHost();
        plot = host?.Housing.GetOwnFieldPlot();
        if (host == null || plot == null || plot.IsPlanner) {
            return null;
        }
        lock (host.Housing.Home.Builders) {
            if (!HomeEditRules.MayBuild(plot.OwnerId, session.AccountId, host.Housing.Home.Builders)) {
                return null;
            }
        }

        owner = host;
        return plot;
    }

    // The owner of the home this session is in, when another account's and in the field.
    private GameSession? FindHost() {
        if (session.Field is not HomeFieldManager homeField || homeField.OwnerId == session.AccountId) {
            return null;
        }
        return homeField.Players.Values.Select(player => player.Session).FirstOrDefault(other => other.AccountId == homeField.OwnerId);
    }

    private IEnumerable<GameSession> HelpersInField() {
        if (session.Field is not HomeFieldManager homeField || homeField.OwnerId != session.AccountId) {
            return [];
        }
        return homeField.Players.Values.Select(player => player.Session).Where(other => other != session && IsBuilder(other.AccountId)).ToList();
    }

    private void GrantBuild(GameSession owner) {
        Home ownerHome = owner.Housing.Home;
        BuildOwnerId = owner.AccountId;
        session.Send(CubePacket.GrantBuildPermissions(session.AccountId, owner.AccountId, ownerHome.BuildMeso, ownerHome.BuildMerat));
        // KMS2 follows 0x35 with 0x37. Without it the helper's client holds interior level 0 for the owner.
        session.Send(CubePacket.BuildOwnerLocks((short) Math.Clamp(ownerHome.DecorationLevel, 0, short.MaxValue),
            HomeBuildRules.MissingTrophies(session.ItemMetadata.HousingTrophies(), owner.Achievement.HasAchievement)));
    }

    // The client keeps the owner 0x35 named until its next login, so the server takes it back.
    private void RevokeBuild() {
        if (BuildOwnerId == 0) {
            return;
        }
        BuildOwnerId = 0;
        session.Send(CubePacket.GrantBuildPermissions(session.AccountId));
    }

    // After the session entered a field. In its own home the owner gets the helper list (0x3b) and the budget
    // (0x38: 0x35 would put the owner's client in helper mode); helpers already there are told. In a home it
    // may build in, a helper gets 0x35.
    public void EnterBuildField() {
        if (session.Field is HomeFieldManager homeField && homeField.OwnerId == session.AccountId) {
            RevokeBuild();
            lock (Home.Builders) {
                session.Send(CubePacket.LoadBuildPermission(Home.Builders));
            }
            session.Send(CubePacket.UpdateBuildBudget(0, Home.BuildMeso, Home.BuildMerat));
            TellHelpers();
            return;
        }

        GameSession? host = FindHost();
        if (host != null && HomeBuildRules.MayGrant(host.Housing.Home.IsPlanner, host.Housing.IsBuilder(session.AccountId))) {
            GrantBuild(host);
            return;
        }
        RevokeBuild();
    }

    // The helpers in the owner's home get the owner and the budget (0x35), or lose them while the home is in
    // a planner mode, where a helper may not build.
    private void TellHelpers() {
        foreach (GameSession helper in HelpersInField()) {
            if (HomeBuildRules.MayGrant(Home.IsPlanner, true)) {
                helper.Housing.GrantBuild(session);
            } else {
                helper.Housing.RevokeBuild();
            }
        }
    }

    // Before the session leaves a field: the helpers in the owner's home cannot build without the owner.
    public void LeaveBuildField() {
        foreach (GameSession helper in HelpersInField()) {
            helper.Housing.RevokeBuild();
        }
    }

    private void SendBuildBudget() {
        session.Send(CubePacket.UpdateBuildBudget(0, Home.BuildMeso, Home.BuildMerat));
        TellHelpers();
    }

    // RequestCube 56.
    public void SetBuildBudget(long meso, long merat) {
        if (!Home.IsHomeSetup) {
            session.Send(CubePacket.UpdateBuildBudget((byte) UgcMapError.s_ugcmap_no_home));
            return;
        }

        // A raise is held from the wallet and a fall is given to it inside the store: the wallet and the home's row
        // with its budget are stored in one purchase save. Before, the home's row was stored at once and the wallet
        // left to the next session save, so a stop before it gave back the mesos (and merets) of a raise with the
        // budget kept, or took back those of a fall with the budget lowered.
        long mesoCost, meratCost, sends;
        lock (session.Item) {
            lock (Home) {
                mesoCost = meso - Home.BuildMeso;
                meratCost = merat - Home.BuildMerat;
                UgcMapError error = HomeBuildRules.CheckBudget(meso, merat, Home.BuildMeso, Home.BuildMerat,
                    session.Currency.Meso, session.Currency.Meret, Constant.MaxMeso, Constant.MaxMeret);
                if (error == UgcMapError.s_ugcmap_ok &&
                    (session.Currency.CanAddMeso(-mesoCost) != -mesoCost || session.Currency.CanAddMeret(-meratCost) != -meratCost)) {
                    error = HomeBuildRules.Refused;
                }
                // A raise of the merets is taken first, without a packet: it is the take another thread can come
                // before (a wedding hall paid by the partner); its packet goes after the meso one, as before.
                if (error == UgcMapError.s_ugcmap_ok && meratCost > 0 && !session.Currency.TryTakeMeret(meratCost, notify: false)) {
                    error = HomeBuildRules.Refused;
                }
                // The mesos can be taken on another thread too (a field's take) after the check: a raise takes them
                // in one step, and when they are gone the merets taken above go back.
                sends = session.Currency.MesoSends;
                if (error == UgcMapError.s_ugcmap_ok && mesoCost > 0 && !session.Currency.TryTakeMeso(mesoCost, notify: false)) {
                    if (meratCost > 0) {
                        session.Currency.ReturnMeret(meratCost);
                    }
                    error = HomeBuildRules.Refused;
                }
                if (error != UgcMapError.s_ugcmap_ok) {
                    session.Send(CubePacket.UpdateBuildBudget((byte) error));
                    return;
                }

                Home.BuildMeso = meso;
                Home.BuildMerat = merat;
            }
        }

        (long Meso, long Meret, long GameMeret) added = default;
        bool stored = session.TrySavePurchase(db => {
            added = session.Currency.AddUnsent(Math.Max(0, -mesoCost), Math.Max(0, -meratCost), 0);
            bool saved;
            lock (Home) {
                saved = db.SaveHome(Home);
            }
            return saved && (added == default || db.SaveCurrency(session.Player));
        }, () => session.Currency.TakeBackAdded(added));
        if (!stored) {
            // Put back by the difference, not to the old values: a helper's buy may have charged the budget since.
            // A rise gives back what is still in the budget, and the wallet gets that much; what a helper spent of it
            // meanwhile stays paid by the wallet. A fall gives its amount back to the budget (the wallet's gain was
            // taken back by the store's undo).
            long mesoBack, meratBack;
            lock (Home) {
                mesoBack = Math.Min(Math.Max(0, mesoCost), Home.BuildMeso);
                meratBack = Math.Min(Math.Max(0, meratCost), Home.BuildMerat);
                Home.BuildMeso += mesoCost > 0 ? -mesoBack : -mesoCost;
                Home.BuildMerat += meratCost > 0 ? -meratBack : -meratCost;
            }
            if (mesoBack < mesoCost || meratBack < meratCost) {
                logger.Warning("Build budget of home {AccountId} spent meanwhile: {Meso} mesos and {Merat} merets of the failed rise kept",
                    session.AccountId, mesoCost - mesoBack, Math.Max(0, meratCost) - meratBack);
            }
            if (mesoBack > 0) {
                session.Currency.ReturnHeldMeso(mesoBack, sends);
            }
            if (meratBack > 0) {
                session.Currency.ReturnMeret(meratBack);
            }
            // The part a helper spent stays taken, but the take was silent (notify false): the balance goes out, as the
            // client still shows the wallet before the rise. The merets spent are reported as the stored rise's are.
            if (mesoBack < mesoCost) {
                session.Currency.NotifyMeso();
            }
            if (meratBack < meratCost) {
                session.Currency.NotifyMeretTaken(meratCost - meratBack);
            }
            logger.Error("Failed to save the build budget of home {AccountId}.", session.AccountId);
            session.Send(CubePacket.UpdateBuildBudget((byte) HomeBuildRules.Refused));
            return;
        }

        if (mesoCost > 0) {
            session.Currency.NotifyMeso();
        }
        session.Currency.NotifyAdded(added);
        if (meratCost > 0) {
            session.Currency.NotifyMeretTaken(meratCost);
        }
        SendBuildBudget();
    }

    private long FindAccountId(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return 0;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(name);
        if (characterId == 0 || !session.PlayerInfo.GetOrFetch(characterId, out PlayerInfo? info)) {
            return 0;
        }
        return info.AccountId;
    }

    private GameSession? FindInField(long accountId) {
        return session.Field?.Players.Values.Select(player => player.Session).FirstOrDefault(other => other != session && other.AccountId == accountId);
    }

    // RequestCube 57: a character name; the list holds the character's account.
    public void AddBuilder(string name) {
        long targetId = Home.IsHomeSetup ? FindAccountId(name) : 0;
        lock (Home.Builders) {
            UgcMapError error = HomeBuildRules.CheckAdd(session.AccountId, targetId, Home.Builders);
            if (error != UgcMapError.s_ugcmap_ok) {
                session.Send(CubePacket.AddBuildPermission((byte) error));
                return;
            }
            Home.Builders.Add(targetId);
        }

        if (!SaveHome()) {
            logger.Error("Failed to save the build helpers of home {AccountId}.", session.AccountId);
        }
        session.Send(CubePacket.AddBuildPermission(0, targetId));
        if (session.Field is HomeFieldManager homeField && homeField.OwnerId == session.AccountId && HomeBuildRules.MayGrant(Home.IsPlanner, true)) {
            FindInField(targetId)?.Housing.GrantBuild(session);
        }
    }

    // RequestCube 58.
    public void RemoveBuilder(string name) {
        long targetId = FindAccountId(name);
        lock (Home.Builders) {
            UgcMapError error = HomeBuildRules.CheckRemove(targetId, Home.Builders);
            if (error != UgcMapError.s_ugcmap_ok) {
                session.Send(CubePacket.RemoveBuildPermission((byte) error));
                return;
            }
            Home.Builders.Remove(targetId);
        }

        if (!SaveHome()) {
            logger.Error("Failed to save the build helpers of home {AccountId}.", session.AccountId);
        }
        session.Send(CubePacket.RemoveBuildPermission(0, targetId));
        GameSession? target = FindInField(targetId);
        if (target != null && target.Housing.BuildOwnerId == session.AccountId) {
            target.Housing.RevokeBuild();
        }
    }

    // A helper cannot use the owner's stored cubes or its own (the client's palette lists none for it), so
    // each cube it places is bought, from the home's budget, into the owner's storage and placed from there.
    // It is bought in db's transaction: the budget (the home's row), the storage stack and the cube's row are stored
    // together (before, the storage item was written at once and the charge stored just before it, apart from the
    // cube's row and the stack's take). undo takes back the memory side when the transaction is not committed.
    private bool BuyFromBudget(GameSession helper, HeldCube cube, FurnishingShopTable.Entry? shopEntry, PlotCube result, CubePacket.Edit edit,
                               GameStorage.Request db, out Action? undo) {
        undo = null;
        if (shopEntry is not { Buyable: true }) {
            helper.Send(CubePacket.EditError(edit, UgcMapError.s_ugcmap_not_for_sale, helper.Player.ObjectId));
            return false;
        }

        bool merat = shopEntry.FurnishingTokenType == FurnishingCurrencyType.Meret;
        lock (Home) {
            UgcMapError error = HomeBuildRules.Charge(Home, merat, shopEntry.Price);
            if (error != UgcMapError.s_ugcmap_ok) {
                helper.Send(CubePacket.EditError(edit, error, helper.Player.ObjectId));
                return false;
            }
        }
        void RefundBudget() {
            lock (Home) {
                HomeBuildRules.Refund(Home, merat, shopEntry.Price);
            }
        }
        undo = RefundBudget;

        long itemUid = session.Item.Furnishing.AddCube(cube, db, out bool created);
        if (itemUid == 0) {
            helper.Send(CubePacket.EditError(edit, UgcMapError.s_ugcmap_not_for_sale, helper.Player.ObjectId));
            return false;
        }
        undo = () => {
            session.Item.Furnishing.UndoBuy(itemUid, created);
            RefundBudget();
        };

        // The item is bought into the owner's storage for the place: a place that fails gives all of it back.
        if (!session.Item.Furnishing.TryAddCube(itemUid, result)) {
            helper.Send(CubePacket.EditError(edit, UgcMapError.s_ugcmap_not_owned_item, helper.Player.ObjectId));
            return false;
        }
        undo = () => {
            session.Item.Furnishing.UndoPlace(itemUid, result.Id, created: false);
            session.Item.Furnishing.UndoBuy(itemUid, created);
            RefundBudget();
        };

        bool homeStored;
        lock (Home) {
            homeStored = shopEntry.Price <= 0 || db.SaveHome(Home);
        }
        if (!session.Item.Furnishing.SaveStack(db, itemUid) || !homeStored) {
            helper.Send(CubePacket.EditError(edit, UgcMapError.s_ugcmap_db, helper.Player.ObjectId));
            return false;
        }
        return true;
    }
    #endregion

    public bool IsCoordOutsideArea(Vector3B position) => HomeBounds(position) != UgcMapError.s_ugcmap_ok;

    private UgcMapError HomeBounds(Vector3B position) {
        return Home.IsPlanner
            ? CubeEditRules.HomeBounds(position.X, position.Y, position.Z, Home.PlannerArea, Home.PlannerHeight)
            : CubeEditRules.HomeBounds(position.X, position.Y, position.Z, Home.Area, Home.Height);
    }

    public bool RequestLayout(HomeLayout layout, out (Dictionary<FurnishingCurrencyType, long> cubeCosts, int cubeCount) result) {
        Dictionary<int, int> groupedCubes = layout.Cubes.GroupBy(plotCube => plotCube.ItemId)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.Count()); // Dictionary<item id, count>
        int cubeCount = 0;
        Dictionary<FurnishingCurrencyType, long> cubeCosts = new() {
            { FurnishingCurrencyType.Meso, 0 },
            { FurnishingCurrencyType.Meret, 0 },
        };

        foreach ((int id, int amount) in groupedCubes) {
            tableMetadata.FurnishingShopTable.Entries.TryGetValue(id, out FurnishingShopTable.Entry? shopEntry);
            session.ItemMetadata.TryGet(id, out ItemMetadata? saleItem);
            shopEntry = SalePrice(shopEntry, saleItem);

            Item? item = session.Item.Furnishing.GetItem(id);
            if (item is null) {
                if (shopEntry is null) {
                    continue;
                }
                cubeCosts[shopEntry.FurnishingTokenType] += (long) shopEntry.Price * amount;
                cubeCount += amount;
                continue;
            }

            if (item.Amount >= amount) {
                continue;
            }

            if (shopEntry is null) {
                continue;
            }

            int missingCubes = amount - item.Amount;
            cubeCosts[shopEntry.FurnishingTokenType] += (long) shopEntry.Price * missingCubes;
            cubeCount += missingCubes;
        }

        // Only the money types with a cost: the client's bill opens only for a bill with entries, and a bill of none
        // is applied at once (FUN_140ee8190 sends RequestCube 0x30, FUN_140ee83f0 0x46 when it is not a preview;
        // uiugccubebilldialog.cpp:248, :297). A layout whose cubes are all in storage showed a bill of 0 meso.
        result = (cubeCosts.Where(entry => entry.Value > 0).ToDictionary(entry => entry.Key, entry => entry.Value), cubeCount);
        return true;
    }

    public void ApplyLayout(Plot plot, HomeLayout layout, bool isBlueprint = false) {
        if (session.Field is null) return;
        if (plot.IsPlanner) {
            Home.SetPlannerArea(layout.Area);
            Home.SetPlannerHeight(layout.Height);
        } else {
            Home.SetArea(layout.Area);
            Home.SetHeight(layout.Height);
        }

        if (plot.IsPlanner) {
            session.Field.Broadcast(CubePacket.UpdateHomeAreaAndHeight(Home.PlannerArea, Home.PlannerHeight));
        } else {
            session.Field.Broadcast(CubePacket.UpdateHomeAreaAndHeight(Home.Area, Home.Height));
        }
        if (isBlueprint && plot.IsPlanner) {
            // If it's planner, only send packets don't save properties
            session.Field.Broadcast(CubePacket.SetBackground(layout.Background));
            session.Field.Broadcast(CubePacket.SetLighting(layout.Lighting));
            session.Field.Broadcast(CubePacket.SetCamera(layout.Camera));
        } else if (isBlueprint) {
            if (session.Player.Value.Home.SetBackground(layout.Background)) {
                session.Field.Broadcast(CubePacket.SetBackground(layout.Background));
            }

            if (session.Player.Value.Home.SetLighting(layout.Lighting)) {
                session.Field.Broadcast(CubePacket.SetLighting(layout.Lighting));
            }

            if (session.Player.Value.Home.SetCamera(layout.Camera)) {
                session.Field.Broadcast(CubePacket.SetCamera(layout.Camera));
            }
        }

        foreach (PlotCube cube in layout.Cubes) {
            Item? item = session.Item.Furnishing.GetItem(cube.ItemId, cube.Template?.Url);
            cube.Id = item?.Uid ?? 0;
            ItemMetadata? metadata = null;
            if (item is null) {
                if (session.ItemMetadata.TryGet(cube.ItemId, out ItemMetadata? newMetadata)) {
                    metadata = newMetadata;
                }
            } else {
                metadata = item.Metadata;
            }
            if (metadata is null) {
                logger.Error("Failed to get item metadata for cube {CubeId} when applying layout {LayoutId}.", cube.ItemId, layout.Id);
                continue;
            }
            if (!TryPlaceCube(cube, plot, metadata, cube.Position, cube.Rotation, out PlotCube? plotCube)) {
                continue;
            }

            ByteWriter sendPacket;
            if (plotCube.Position.Z == 0) {
                sendPacket = CubePacket.ReplaceCube(session.Player.ObjectId, plotCube);
            } else {
                sendPacket = CubePacket.PlaceCube(session.Player.ObjectId, plot, plotCube);
            }

            if (plotCube.Interact is not null) {
                if (cube.Interact?.PortalSettings is not null) {
                    plotCube.Interact.PortalSettings = cube.Interact.PortalSettings;
                }
            }

            session.Field.Broadcast(sendPacket);
        }

        StoreCubePrices();

        List<PlotCube> cubePortals = plot.Cubes.Values
            .Where(x => x.ItemId is Constant.InteriorPortalCubeId && x.Interact?.PortalSettings is not null)
            .ToList();

        foreach (PlotCube cubePortal in cubePortals) {
            FieldPortal? fieldPortal = session.Field.SpawnCubePortal(cubePortal);
            if (fieldPortal is null) {
                continue;
            }
            session.Field.Broadcast(PortalPacket.Add(fieldPortal));
        }

        Vector3 position = Home.CalculateSafePosition(plot.Cubes.Values.ToList());
        foreach (FieldPlayer fieldPlayer in session.Field.Players.Values) {
            fieldPlayer.Session.Send(PortalPacket.MoveByPortal(fieldPlayer, position, default));
        }

        session.Item.Furnishing.SendStorageCount();
        session.Housing.SaveHome();
        session.Field.Broadcast(NoticePacket.Message(StringCode.s_ugcmap_package_automatic_creation_completed, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
    }
}
