using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
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
            float discount = float.Parse(saleEvent.Metadata.Value1) / 10000f;
            costAmount = (int) Math.Ceiling(costAmount * (1 - discount));
        }

        // The check and the charge are one step: the merets can also be taken on another thread (a partner's paid
        // wedding hall), so a charge after the save could fail with the plot already saved.
        if (!TryRemoveCost(session, contract.ItemId, costAmount)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_enough_money));
            return false;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        plotInfo = db.BuyPlot(session.PlayerName, session.AccountId, plot, TimeSpan.FromDays(contract.Days));
        if (plotInfo == null) {
            // A failed buy (owned on another channel, or a failed save) costs nothing: the cost is given back, and
            // the home's outdoor plot is left as it was.
            RefundCost(session, contract.ItemId, costAmount);
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

        if (Home.Outdoor.ExpiryTime.FromEpochSeconds() - DateTime.UtcNow > TimeSpan.FromDays(30)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_extension_date));
            return;
        }

        UgcMapGroup.Cost extension = Home.Outdoor.Metadata.ExtensionCost;

        int costAmount = extension.Amount;
        GameEvent? saleEvent = session.FindEvent(GameEventType.UGCMapExtensionSale).FirstOrDefault();
        if (saleEvent != null) {
            float discount = float.Parse(saleEvent.Metadata.Value1) / 10000f;
            costAmount = (int) Math.Ceiling(costAmount * (1 - discount));
        }

        if (!TryRemoveCost(session, extension.ItemId, costAmount)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_need_extension_pay));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        PlotInfo? plotInfo = db.ExtendPlot(Home.Outdoor, TimeSpan.FromDays(extension.Days));
        if (plotInfo == null) {
            // A failed extension costs nothing: the cost is given back.
            RefundCost(session, extension.ItemId, costAmount);
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
                if (session.Currency.Meso < cost) {
                    return false;
                }
                session.Currency.Meso -= cost;
                return true;
            case 90000004 or 90000011 or 90000015 or 90000016:
                if (session.Currency.Meret < cost) {
                    return false;
                }
                session.Currency.Meret -= cost;
                return true;
        }
        return false;
    }

    // Gives back a cost TryRemoveCost took. Chosen here: the meret refund is a plain Meret update with a positive
    // change, so the use_merat count the charge reported stays (the refund does not take it back).
    private static void RefundCost(GameSession session, int itemId, int cost) {
        switch (itemId) {
            case >= 90000001 and <= 90000003:
                session.Currency.Meso += cost;
                return;
            case 90000004 or 90000011 or 90000015 or 90000016:
                session.Currency.Meret += cost;
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
            PlotCube? plotCube = CreateCube(Home.Indoor, itemMetadata, functionCubeMetadata, null, template.BaseCubePosition + cube.OffsetPosition, cube.Rotation);
            if (plotCube is null) {
                logger.Error("Failed to create cube {cubeId} at position {position}.", cube.ItemId, template.BaseCubePosition + cube.OffsetPosition);
                continue;
            }

            session.Item.Furnishing.TryAddCube(itemUid, plotCube);

            plotCubes.Add(plotCube);
        }

        db.SaveHome(Home);
        db.SavePlotInfo(Home.Indoor);
        var mailed = new List<(long CharacterId, long MailId)>();
        db.SaveCubes(Home.Indoor, plotCubes, session.CharacterId, mailed);
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
                cube.Metadata.Housing?.MaidId > 0, 1, cube.Metadata.Property.SlotMax)));
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

        int housingPoint = decorationScore switch {
            < 300 => 100,
            < 500 => 300,
            < 700 => 500,
            < 1100 => 700,
            _ => 1100,
        };

        tableMetadata.UgcHousingPointRewardTable.Entries.TryGetValue(housingPoint, out UgcHousingPointRewardTable.Entry? reward);
        if (reward is null) {
            logger.Error("Failed to get reward for decoration score {DecorationScore}.", housingPoint);
            session.Send(CubePacket.InteriorCheckInError(InteriorRewardRules.CheckInError(false, false)));
            return;
        }

        ICollection<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, decorationScore, reward.IndividualDropBoxId);
        foreach (Item item in items) {
            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }

        long levelBefore = Home.DecorationLevel;
        long expBefore = Home.DecorationExp;
        Home.GainExp(decorationScore, tableMetadata.MasteryUgcHousingTable.Entries);
        Home.DecorationRewardTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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

        if (!session.Item.Inventory.Add(rewardItem, true)) {
            session.Item.MailItem(rewardItem);
        }

        Home.InteriorRewardsClaimed.Add(rewardId);
        session.Send(CubePacket.DesignRankReward(Home));
    }

    private PlotCube? CreateCube(PlotInfo plot, ItemMetadata itemMetadata, FunctionCubeMetadata? functionCubeMetadata, UgcItemLook? template, Vector3 position, float rotation) {
        var result = new PlotCube(itemMetadata, 0, template) {
            Type = PlotCube.CubeType.Construction,
            Position = position,
            Rotation = rotation,
        };

        using GameStorage.Request db = session.GameStorage.Context();

        if (functionCubeMetadata is not null) {
            result.Interact = new InteractCube(position, functionCubeMetadata);
            if (result.Interact.Nurturing is not null && result.Interact.Metadata.Nurturing is not null) {
                Nurturing? nurturing = db.GetNurturing(session.AccountId, result.ItemId, result.Interact.Metadata.Nurturing);
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

    private void DeleteCube(PlotCube cube) {
        using GameStorage.Request db = session.GameStorage.Context();
        if (db.DeleteCube(cube)) return;
        logger.Error("Failed to delete cube {CubeId} at position {Position}.", cube.Id, cube.Position);
    }

    public bool TryPlaceCube(HeldCube cube, Plot plot, ItemMetadata itemMetadata, in Vector3B position, float rotation,
                             [NotNullWhen(true)] out PlotCube? result, bool isReplace = false, GameSession? helper = null) {
        lock (plot.EditLock) {
            return PlaceCube(cube, plot, itemMetadata, position, rotation, out result, isReplace, helper);
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
                           [NotNullWhen(true)] out PlotCube? result, bool isReplace, GameSession? helper) {
        result = null;
        // A build helper edits through the owner's manager; what the edit refuses goes to the helper.
        GameSession notify = helper ?? session;
        if (session.Field is null || itemMetadata.Install is null) return false;
        CubePacket.Edit edit = isReplace ? CubePacket.Edit.Replace : CubePacket.Edit.Place;
        void Refuse(UgcMapError error) => notify.Send(CubePacket.EditError(edit, error, notify.Player.ObjectId));

        tableMetadata.FurnishingShopTable.Entries.TryGetValue(cube.ItemId, out FurnishingShopTable.Entry? shopEntry);
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

        if (plot.MapId is Constant.DefaultHomeMapId && IsCoordOutsideArea(position)) {
            Refuse(UgcMapError.s_ugcmap_area_limit);
            return false;
        }

        // Only check height on outside plots since we already check if it's inside the area when getting the ground height
        if (outdoor && position.Z - groundHeight > plot.Metadata.Limit.Height) {
            Refuse(UgcMapError.s_ugcmap_area_limit);
            return false;
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

        if (isReplace && plot.Cubes.ContainsKey(position)) {
            RemoveCube(plot, position, out _, helper);
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

        result = CreateCube(plot, itemMetadata, functionCubeMetadata, cube.Template, position, rotation);
        if (result is null) {
            Refuse(UgcMapError.s_ugcmap_db);
            return false;
        }

        long storedUid = 0; // the storage item the cube was placed from, when it was in storage already
        if (helper != null) {
            if (!TryBuyFromBudget(helper, cube, shopEntry, result, edit)) {
                DeleteCube(result);
                return false;
            }
        } else if (session.Item.Furnishing.TryAddCube(cube.Id, result)) {
            storedUid = cube.Id;
        } else {
            long itemUid = session.Item.Furnishing.AddCube(cube);
            if (itemUid == 0) {
                DeleteCube(result);
                Refuse(UgcMapError.s_ugcmap_not_for_sale);
                return false;
            }

            if (shopEntry is null) {
                DeleteCube(result);
                Refuse(UgcMapError.s_err_cannot_buy_limited_item_more);
                return false;
            }

            if (!session.Item.Furnishing.PurchaseCube(shopEntry, out UgcMapError purchaseError)) {
                DeleteCube(result);
                if (purchaseError != UgcMapError.s_ugcmap_ok) {
                    Refuse(purchaseError);
                }
                return false;
            }

            session.Send(CubePacket.PurchaseCube(session.Player.ObjectId));
            // Now that we have purchased the cube, it must be placeable.
            if (!session.Item.Furnishing.TryAddCube(itemUid, result)) {
                DeleteCube(result);
                Refuse(UgcMapError.s_ugcmap_not_owned_item);
                return false;
            }
        }

        if (functionCubeMetadata is not null) {
            session.Field.AddFieldFunctionInteract(result, plot);
        }

        if (outdoor && itemMetadata.Install.IndoorPortal) {
            FieldPortal? fieldPortal = session.Field.SpawnFieldToHomePortal(result, plot.OwnerId);
            if (fieldPortal is null) {
                DeleteCube(result);
                Refuse(UgcMapError.s_ugcmap_not_owned_item);
                return false;
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
    private void MoveMaidToItem(PlotCube cube, long itemUid) {
        if (cube.Metadata.Housing?.MaidId is not > 0) {
            return;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        Maid? moved = db.MoveMaid(cube.Id, itemUid);
        if (moved != null) {
            session.Send(UserMaidPacket.Add(moved));
            if (MaidRules.IsCrafting(moved)) {
                session.Send(MaidCraftItemPacket.Add(moved));
            }
        }
    }

    public bool TryRemoveCube(Plot plot, in Vector3B position, [NotNullWhen(true)] out PlotCube? cube, GameSession? helper = null) {
        lock (plot.EditLock) {
            return RemoveCube(plot, position, out cube, helper);
        }
    }

    private bool RemoveCube(Plot plot, in Vector3B position, [NotNullWhen(true)] out PlotCube? cube, GameSession? helper) {
        GameSession notify = helper ?? session;
        if (!plot.Cubes.Remove(position, out cube)) {
            notify.Send(CubePacket.EditError(CubePacket.Edit.Remove, UgcMapError.s_ugcmap_no_cube_to_remove, notify.Player.ObjectId));
            return false;
        }

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

        long itemUid = session.Item.Furnishing.RetrieveCube(cube.Id);
        if (itemUid == 0) {
            logger.Error("Failed to deposit cube {CubeId} back into storage.", cube.Id);
            return false;
        }

        // Before the cube row goes, which would take a maid still keyed by it.
        MoveMaidToItem(cube, itemUid);
        DeleteCube(cube);

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

        lock (session.Item) {
            lock (Home) {
                long mesoCost = meso - Home.BuildMeso;
                long meratCost = merat - Home.BuildMerat;
                UgcMapError error = HomeBuildRules.CheckBudget(meso, merat, Home.BuildMeso, Home.BuildMerat,
                    session.Currency.Meso, session.Currency.Meret, Constant.MaxMeso, Constant.MaxMeret);
                if (error == UgcMapError.s_ugcmap_ok &&
                    (session.Currency.CanAddMeso(-mesoCost) != -mesoCost || session.Currency.CanAddMeret(-meratCost) != -meratCost)) {
                    error = HomeBuildRules.Refused;
                }
                if (error != UgcMapError.s_ugcmap_ok) {
                    session.Send(CubePacket.UpdateBuildBudget((byte) error));
                    return;
                }

                if (mesoCost != 0) {
                    session.Currency.Meso -= mesoCost;
                }
                if (meratCost != 0) {
                    session.Currency.Meret -= meratCost;
                }
                Home.BuildMeso = meso;
                Home.BuildMerat = merat;
            }
        }

        if (!SaveHome()) {
            logger.Error("Failed to save the build budget of home {AccountId}.", session.AccountId);
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
    private bool TryBuyFromBudget(GameSession helper, HeldCube cube, FurnishingShopTable.Entry? shopEntry, PlotCube result, CubePacket.Edit edit) {
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

        long itemUid = session.Item.Furnishing.AddCube(cube);
        if (itemUid == 0) {
            lock (Home) {
                HomeBuildRules.Refund(Home, merat, shopEntry.Price);
            }
            helper.Send(CubePacket.EditError(edit, UgcMapError.s_ugcmap_not_for_sale, helper.Player.ObjectId));
            return false;
        }

        // The item was bought into the owner's storage and stays there, so the charge stays too.
        if (!session.Item.Furnishing.TryAddCube(itemUid, result)) {
            if (shopEntry.Price > 0) {
                SendBuildBudget();
            }
            helper.Send(CubePacket.EditError(edit, UgcMapError.s_ugcmap_not_owned_item, helper.Player.ObjectId));
            return false;
        }

        helper.Send(CubePacket.PurchaseCube(helper.Player.ObjectId));
        if (shopEntry.Price > 0) {
            SendBuildBudget();
        }
        return true;
    }
    #endregion

    public bool IsCoordOutsideArea(Vector3B position) {
        int height = Home.IsPlanner ? Home.PlannerHeight : Home.Height;
        int area = Home.IsPlanner ? Home.PlannerArea : Home.Area;

        // Check if the position is outside the planar area bounds
        if (position.X > 0 || position.Y > 0 || position.Z < 0) {
            return true;
        }

        area *= -1;
        if (position.X <= area || position.Y <= area) {
            return true;
        }

        // Check if the position is outside the height bounds
        return position.Z > height;
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
                cubeCosts[shopEntry.FurnishingTokenType] += shopEntry.Price * amount;
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
            cubeCosts[shopEntry.FurnishingTokenType] += shopEntry.Price * missingCubes;
            cubeCount += missingCubes;
        }

        result = (cubeCosts, cubeCount);
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
            Item? item = session.Item.Furnishing.GetItem(cube.ItemId);
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
