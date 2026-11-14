using System.Numerics;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class RequestCubeHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestCube;

    private enum Command : byte {
        BuildState = 0,
        HoldCube = 1,
        BuyPlot = 2,
        OwnPlotSign = 5,
        ForfeitPlot = 6,
        ExtendPlot = 9,
        PlaceCube = 10,
        PlaceAttachedCube = 11,
        RemoveCube = 12,
        RemoveCube13 = 13,
        RotateCube = 14,
        ReplaceCube = 15,
        ReplaceAttachedCube = 16,
        LiftupObject = 17,
        LiftupDrop = 18,
        GodModeIndex = 19,
        SetHomeName = 21,
        SetPasscode = 24,
        VoteHome = 25,
        SetHomeMessage = 29,
        HomeSummon = 30,
        ClearCubes = 31,
        RequestLayout = 35,
        IncreaseArea = 37,
        DecreaseArea = 38,
        InteriorDesignCheckIn = 40,
        InteriorDesignReward = 41,
        EnablePermission = 42,
        SetPermission = 43,
        IncreaseHeight = 44,
        DecreaseHeight = 45,
        SaveLayout = 46,
        DecorPlannerLoadLayout = 47,
        LoadLayout = 48,
        KickOut = 49,
        SetBackground = 51,
        SetLighting = 52,
        SetCamera = 54,
        SetBuildBudget = 56,
        AddBuildPermission = 57,
        RemoveBuildPermission = 58,
        CreateBlueprint = 63,
        SaveBlueprint = 64,
        LoadBlueprint = 65,
        CalculateEstimate = 66,
        PreviewBlueprint = 68,
        ApplyBlueprint = 70,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.HoldCube:
                HandleHoldCube(session, packet);
                return;
            case Command.BuyPlot:
                HandleBuyPlot(session, packet);
                return;
            case Command.ForfeitPlot:
                HandleForfeitPlot(session);
                break;
            case Command.ExtendPlot:
                HandleExtendPlot(session);
                break;
            case Command.PlaceCube:
            case Command.PlaceAttachedCube:
                HandlePlaceCube(session, packet, command == Command.PlaceAttachedCube);
                break;
            case Command.RemoveCube:
                HandleRemoveCube(session, packet);
                break;
            case Command.RotateCube:
                HandleRotateCube(session, packet);
                break;
            case Command.ReplaceCube:
            case Command.ReplaceAttachedCube:
                HandleReplaceCube(session, packet, command == Command.ReplaceAttachedCube);
                break;
            case Command.LiftupObject:
                HandleLiftupObject(session, packet);
                break;
            case Command.LiftupDrop:
                HandleLiftupDrop(session);
                break;
            case Command.SetHomeName:
                HandleSetHomeName(session, packet);
                break;
            case Command.SetPasscode:
                HandleSetPasscode(session, packet);
                break;
            case Command.VoteHome:
                HandleVoteHome(session);
                break;
            case Command.SetHomeMessage:
                HandleSetHomeMessage(session, packet);
                break;
            case Command.ClearCubes:
                HandleClearCubes(session);
                break;
            case Command.RequestLayout:
                HandleRequestLayout(session, packet);
                break;
            case Command.IncreaseArea:
                HandleIncreaseArea(session);
                break;
            case Command.DecreaseArea:
                HandleDecreaseArea(session);
                break;
            case Command.InteriorDesignCheckIn:
                HandleInteriorDesignCheckIn(session);
                break;
            case Command.InteriorDesignReward:
                HandleInteriorDesignReward(session, packet);
                break;
            case Command.EnablePermission:
                HandleEnablePermission(session, packet);
                break;
            case Command.SetPermission:
                HandleSetPermission(session, packet);
                break;
            case Command.IncreaseHeight:
                HandleIncreaseHeight(session);
                break;
            case Command.DecreaseHeight:
                HandleDecreaseHeight(session);
                break;
            case Command.SaveLayout:
                HandleSaveLayout(session, packet);
                break;
            case Command.DecorPlannerLoadLayout:
            case Command.LoadLayout:
                HandleLoadLayout(session, packet);
                break;
            case Command.KickOut:
                HandleKickOut(session);
                break;
            case Command.SetBackground:
                HandleSetBackground(session, packet);
                break;
            case Command.SetLighting:
                HandleSetLighting(session, packet);
                break;
            case Command.SetCamera:
                HandleSetCamera(session, packet);
                break;
            case Command.CreateBlueprint:
                HandleCreateBlueprint(session);
                break;
            case Command.SaveBlueprint:
                HandleSaveBlueprint(session, packet);
                break;
            case Command.LoadBlueprint:
                HandleLoadBlueprint(session, packet);
                break;
            case Command.CalculateEstimate:
                HandleCalculateEstimate(session, packet);
                break;
            case Command.PreviewBlueprint:
                HandlePreviewBlueprint(session, packet);
                break;
            // Sub-types the KMS 2525 client sends that have no logic here yet: read and log only.
            case Command.BuildState: { // sent with 0 before HoldCube in build mode, with 1 from the cube palette
                // The client changes its cube mode only on the answer (ResponseCube 0), so the field gets it back.
                int mode = packet.ReadInt();
                if (mode is 0 or 1) {
                    session.Field?.Broadcast(CubePacket.BuildMode(session.Player.ObjectId, mode));
                }
                break;
            }
            case Command.RemoveCube13: // cube position
                Logger.Debug("RequestCube {Command} from {CharacterId}: {Value}", command, session.CharacterId, packet.ReadInt());
                break;
            // CGodModeInfo::SendRequestCube (godmodeinfo.cpp:121): a debug index stepped with two keys, sent only
            // while a client debug switch is on. Nothing in the client turns that switch on; it is only cleared
            // (mypc.cpp:4531). GM accounts only, then logged.
            case Command.GodModeIndex:
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }

                Logger.Debug("RequestCube {Command} from {CharacterId}: {Value}", command, session.CharacterId, packet.ReadInt());
                break;
            // CUgcSignCinematicComponent::vfunc_2 (cinematiccomponent.cpp:1034): the action key in the talk scene of
            // a plot's sign. The account that owns the plot sends 5 (plot number, 0); any other account sends the
            // purchase, 2, from the same place. The only answer the client reads under sub-type 5
            // (scenestategame.cpp:9945) takes the owner off the plot, which is what a sale (6) already sends.
            // Whether 5 is a second way to sell is not settled, and a guess could remove a home: read and logged.
            case Command.OwnPlotSign:
                Logger.Debug("RequestCube {Command} from {CharacterId}: plot {Plot}, {Value}", command, session.CharacterId, packet.ReadInt(), packet.ReadInt());
                break;
            // UIHomeSummonDialog::Summon (unit_141299650_uihomesummondialog.cpp:137): the dialog of a function cube
            // of sub-kind 7 (CFunctionCubeObject::OpenSubKindDialog) sends the cube's id string and a typed text of
            // two or more letters. The client reads no answer under sub-type 30 and does not show which packet the
            // named player gets: read and logged. The dialog is for a cube with UseUiFunction "Summon"
            // (commonfiles/objectdata.cpp:170), and no function cube in the data has it.
            case Command.HomeSummon:
                Logger.Debug("RequestCube {Command} from {CharacterId}: {First}, {Second}", command, session.CharacterId, packet.ReadUnicodeString(), packet.ReadUnicodeString());
                break;
            // UIHomeInfoDialog::FUN_1412c7ff0 (uihomeinfodialog.cpp:776) and the user command SetUgcMapBalance
            // (usercommandcli.cpp:1145): meso, merat. The budget of the sender's own home, which pays for the
            // cubes its build helpers place; the answer is ResponseCube 0x38.
            case Command.SetBuildBudget: {
                long meso = packet.ReadLong();
                long merat = packet.ReadLong();
                session.Housing.SetBuildBudget(meso, merat);
                break;
            }
            // UIPopUpMenu::SendRequestCube (uipartydialog.cpp:3428, a menu entry that needs client feature 0x100)
            // and the user command AddUgcMapPriv (usercommandcli.cpp:1151): character name. Adds a build helper
            // to the sender's own home; the answer is ResponseCube 0x39.
            case Command.AddBuildPermission:
                session.Housing.AddBuilder(packet.ReadUnicodeString());
                break;
            // UIHomeInfoDialog::SendRequestCubeMode58 (uihomeinfodialog.cpp:674, the remove button of the helper
            // list) and the user command RemoveUgcMapPriv (usercommandcli.cpp:1157): character name. The answer
            // is ResponseCube 0x3a.
            case Command.RemoveBuildPermission:
                session.Housing.RemoveBuilder(packet.ReadUnicodeString());
                break;
            case Command.ApplyBlueprint:
                HandleApplyBlueprint(session, packet);
                break;
        }
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    private void HandleHoldCube(GameSession session, IByteReader packet) {
        var cubeItem = packet.ReadClass<PlotCube>();

        if (session.GuideObject == null || session.GuideObject.Value.Type != GuideObjectType.Construction) {
            return;
        }

        session.HeldCube = cubeItem;
        session.Field?.Broadcast(CubePacket.HoldCube(session.Player.ObjectId, session.HeldCube));
    }

    private void HandleBuyPlot(GameSession session, IByteReader packet) {
        int plotNumber = packet.ReadInt();
        int concept = packet.ReadInt(); // the model house of the first purchase, -1 = empty (HousingManager.ConceptTemplate)

        if (session.Housing.BuyPlot(plotNumber)) {
            session.Send(CubePacket.ConfirmBuyPlot());
            session.Housing.InitHomeForConcept(concept);
        }
    }

    private void HandleForfeitPlot(GameSession session) {
        PlotInfo? plot = session.Housing.ForfeitPlot();
        if (plot != null) {
            session.Send(CubePacket.ConfirmForfeitPlot(plot, CubePacket.ForfeitByOwner));
        }
    }

    private void HandleExtendPlot(GameSession session) {
        session.Housing.ExtendPlot();
    }

    private void HandlePlaceCube(GameSession session, IByteReader packet, bool attached = false) {
        var position = packet.Read<Vector3B>();
        var cubeItem = packet.ReadClass<HeldCube>();
        float rotation = packet.ReadFloat();
        if (attached) {
            packet.ReadByte(); // the side the cube is attached to; the rotation above is the angle of that side
        }

        session.Field?.PlaceCube(session, cubeItem, position, rotation);
    }

    private void HandleRemoveCube(GameSession session, IByteReader packet) {
        if (session.Field == null) return;
        var position = packet.Read<Vector3B>();

        // No plot to build on here: the client's own answer for a cube not the player's (FUN_1410c6980 mode 4 failing,
        // cubecraft_2.cpp:1146), 40 s_ugcmap_cant_remove_default_cube.
        Plot? plot = session.Housing.GetBuildFieldPlot(out GameSession owner);
        if (plot == null) {
            session.Send(CubePacket.EditError(CubePacket.Edit.Remove, UgcMapError.s_ugcmap_cant_remove_default_cube, session.Player.ObjectId));
            return;
        }

        if (!owner.Housing.TryRemoveCube(plot, position, out PlotCube? cube, owner == session ? null : session)) {
            return;
        }

        session.Field.Broadcast(CubePacket.RemoveCube(session.Player.ObjectId, position));
        if (plot.MapId is not Constant.DefaultHomeMapId) {
            session.World.UpdateFieldPlot(new FieldPlotRequest {
                IgnoreChannel = GameServer.GetChannel(),
                MapId = session.Field.MapId,
                PlotNumber = plot.Number,
                UpdateBlock = new FieldPlotRequest.Types.UpdateBlock {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                    Remove = new FieldPlotRequest.Types.UpdateBlock.Types.Remove(),
                },
            });
        }
        if (plot.IsPlanner) {
            return;
        }

        session.ConditionUpdate(ConditionType.uninstall_item, codeLong: cube.ItemId);
    }

    private void HandleRotateCube(GameSession session, IByteReader packet) {
        if (session.Field == null) return;
        var position = packet.Read<Vector3B>();
        bool clockwise = packet.ReadBool();

        // No plot to build on here: the client's own answer for a cube not the player's (FUN_1410c6980 mode 3 failing,
        // cubecraft_2.cpp:1091), 27 s_ugcmap_cant_rotate_default_cube.
        Plot? plot = session.Housing.GetBuildFieldPlot(out GameSession owner);
        if (plot == null) {
            session.Send(CubePacket.EditError(CubePacket.Edit.Rotate, UgcMapError.s_ugcmap_cant_rotate_default_cube, session.Player.ObjectId));
            return;
        }
        // A build helper turns the owner's cube through the owner's manager, as a place or a remove.
        if (!owner.Housing.TryRotateCube(plot, position, clockwise, out PlotCube? cube)) {
            session.Send(CubePacket.EditError(CubePacket.Edit.Rotate, UgcMapError.s_ugcmap_no_cube_to_rotate, session.Player.ObjectId));
            return;
        }

        session.Field.Broadcast(CubePacket.RotateCube(session.Player.ObjectId, cube));
        if (plot.MapId is not Constant.DefaultHomeMapId) {
            session.World.UpdateFieldPlot(new FieldPlotRequest {
                IgnoreChannel = GameServer.GetChannel(),
                MapId = session.Field.MapId,
                PlotNumber = plot.Number,
                UpdateBlock = new FieldPlotRequest.Types.UpdateBlock {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                    Rotate = new FieldPlotRequest.Types.UpdateBlock.Types.Rotate {
                        Clockwise = clockwise,
                    },
                },
            });
        }

        if (plot.IsPlanner) {
            return;
        }

        session.ConditionUpdate(ConditionType.rotate_cube, codeLong: cube.ItemId);
    }

    private void HandleReplaceCube(GameSession session, IByteReader packet, bool attached = false) {
        if (session.Field == null) return;

        var position = packet.Read<Vector3B>();
        var cubeItem = packet.ReadClass<HeldCube>();
        float rotation = packet.ReadFloat();
        if (attached) {
            packet.ReadByte(); // the side the cube is attached to; the rotation above is the angle of that side
        }

        Plot? plot = session.Housing.GetBuildFieldPlot(out GameSession owner);
        if (plot == null) {
            return;
        }

        if (!session.ItemMetadata.TryGet(cubeItem.ItemId, out ItemMetadata? metadata)) {
            return;
        }

        // The client replaces a cell with the cube it holds, as for a place (Unk_PCCubeCraft::ConfirmPlacement,
        // cubecraft_2.cpp:1007-1033); a replace with nothing held, or of another item than the one held, replaced
        // any cube with any furnishing. Refused with the place's answer, s_ugcmap_null_cube_item_info.
        if (session.HeldCube is not PlotCube held || held.ItemId != cubeItem.ItemId) {
            session.Send(CubePacket.EditError(CubePacket.Edit.Replace, UgcMapError.s_ugcmap_null_cube_item_info, session.Player.ObjectId));
            return;
        }

        if (!owner.Housing.TryPlaceCube(cubeItem, plot, metadata, position, rotation, out PlotCube? placedCube, isReplace: true, helper: owner == session ? null : session, byPlayer: true)) {
            return;
        }

        // A replace is the client's place onto a taken cell (the same confirm, cubecraft_2.cpp:1007-1033), so it counts
        // as a place: the owner's decoration score trophy and the placer's install_item. It counted for neither.
        if (!plot.IsPlanner) {
            owner.ConditionUpdate(ConditionType.interior_point, counter: HousingManager.DecorationScore(plot));
        }
        session.Field.Broadcast(CubePacket.ReplaceCube(session.Player.ObjectId, placedCube));

        if (plot.MapId is not Constant.DefaultHomeMapId) {
            session.World.UpdateFieldPlot(new FieldPlotRequest {
                IgnoreChannel = GameServer.GetChannel(),
                MapId = session.Field.MapId,
                PlotNumber = plot.Number,
                UpdateBlock = new FieldPlotRequest.Types.UpdateBlock {
                    BlockUid = placedCube.Id,
                    Replace = new FieldPlotRequest.Types.UpdateBlock.Types.Replace { },
                },
            });
        }
        session.ConditionUpdate(ConditionType.install_item, codeLong: cubeItem.ItemId);
    }

    private void HandleLiftupObject(GameSession session, IByteReader packet) {
        var position = packet.Read<Vector3B>();

        if (session.Field == null || session.HeldLiftup != null) {
            session.Send(CubePacket.LiftupError(UgcMapError.s_ugcmap_not_allowed_item, session.Player.ObjectId));
            return;
        }

        if (!session.Field.LiftupCube(position, out LiftupWeapon? weapon, out int respawnTick)) {
            session.Send(CubePacket.LiftupError(UgcMapError.s_ugcmap_no_cube_to_lift, session.Player.ObjectId));
            return;
        }

        session.HeldLiftup = weapon;
        session.Field.Broadcast(CubePacket.LiftupObject(session.Player, session.HeldLiftup, respawnTick));
        session.Player.InBattle = true;
    }

    private void HandleLiftupDrop(GameSession session) {
        if (session.Field == null || session.HeldLiftup == null) {
            return;
        }

        session.HeldLiftup = null;
        session.Field.Broadcast(CubePacket.LiftupDrop(session.Player));
    }

    private void HandleSetHomeName(GameSession session, IByteReader packet) {
        string name = packet.ReadUnicodeString();
        session.Housing.SetName(name);
    }

    private void HandleSetPasscode(GameSession session, IByteReader packet) {
        // MyPC::SendHouseSetting: the kind (0 public, 1 password, 2 private), the password text, then a flag byte.
        byte kind = packet.ReadByte();
        string text = packet.ReadUnicodeString();
        if (!HomeOpenRules.TryGetPasscode(kind, text, out string passcode)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_center));
            return;
        }

        session.Housing.SetPasscode(passcode);
    }

    // CommendHome: the account commends the home it stands in, once a day (HomeCommendRules). The voter gets the time
    // of its commendation (ResponseCube 0x19, which turns its recommend button off), the field the home's new scores
    // (ResponseCube 0x1c for the home info dialog, HomeCommand 1 for the owner, in whichever field and channel it is).
    private void HandleVoteHome(GameSession session) {
        if (session.Field is not HomeFieldManager home) {
            session.Send(CubePacket.ConfirmVote((byte) UgcMapError.s_ugcmap_no_owner_to_commend));
            return;
        }
        long ownerId = home.OwnerId;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using GameStorage.Request db = session.GameStorage.Context();
        long last = db.GetLastHomeCommend(session.AccountId);
        if (HomeCommendRules.Check(ownerId, session.AccountId, last, now, TimeZoneInfo.Local) is { } error) {
            session.Send(CubePacket.ConfirmVote((byte) error));
            return;
        }
        if (!db.AddHomeCommend(ownerId, session.AccountId, now)) {
            session.Send(CubePacket.ConfirmVote((byte) UgcMapError.s_ugcmap_add_commend_home_fail_from_db));
            return;
        }

        (long monthBegin, long monthEnd) = HomeCommendRules.MonthSpan(now, TimeZoneInfo.Local);
        (int current, int total) = db.GetHomeCommendScore(ownerId, monthBegin, monthEnd);

        session.Send(CubePacket.ConfirmVote(0, session.AccountId, now));
        // This channel's copies at once (the owner may be in any field here), the other channels through the World.
        HomeArchitectScore.Apply(home, session.FindSessionByAccountId(ownerId), current, total);
        try {
            session.World.UpdateFieldPlot(HomeArchitectScore.Request(home.MapId, ownerId, current, total, GameServer.GetChannel()));
        } catch (RpcException ex) {
            Logger.Warning(ex, "Architect score of home {OwnerId} not sent to the other channels", ownerId);
        }
        session.ConditionUpdate(ConditionType.commend_home);
    }

    private void HandleSetHomeMessage(GameSession session, IByteReader packet) {
        string message = packet.ReadUnicodeString();
        session.Housing.SetHomeMessage(message);
    }

    private void HandleClearCubes(GameSession session) {
        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot == null) {
            return;
        }

        foreach (PlotCube cube in plot.Cubes.Values) {
            if (session.Housing.TryRemoveCube(plot, cube.Position, out _)) {
                session.Field?.Broadcast(CubePacket.RemoveCube(session.Player.ObjectId, cube.Position));
            }
        }

        session.Send(NoticePacket.Message(StringCode.s_ugcmap_package_automatic_removal_completed, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
    }

    private void HandleRequestLayout(GameSession session, IByteReader packet) {
        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot == null) {
            return;
        }

        if (plot.Cubes.Count != 0) {
            session.Send(NoticePacket.Message(StringCode.s_err_ugcmap_package_clear_indoor_first, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
            return;
        }

        int slot = packet.ReadInt();

        // A slot the server has no layout in is answered with 0x02 s_ugcmap_db (13), as a layout load is (RequestCube
        // 48): the client shows it with ShowBuildNotice (scenestategame.cpp:9913). Before, it got no answer at all.
        HomeLayout? layout = session.Player.Value.Home.Layouts.FirstOrDefault(homeLayout => homeLayout.Id == slot);
        if (layout == null) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        if (session.Housing.RequestLayout(layout, out (Dictionary<FurnishingCurrencyType, long> cubeCosts, int cubeCount) result)) {
            session.Send(CubePacket.BuyCubes(result.cubeCosts, result.cubeCount));
        }
    }

    private void HandleIncreaseArea(GameSession session) {
        if (session.Field is null || !InOwnHome(session)) return;
        if (session.Player.Value.Home.IsPlanner) {
            int decorArea = session.Player.Value.Home.PlannerArea + 1;
            if (session.Player.Value.Home.SetPlannerArea(decorArea)) {
                session.Field.Broadcast(CubePacket.IncreaseArea((byte) decorArea));
            }
            return;
        }

        int area = session.Player.Value.Home.Area + 1;
        if (session.Player.Value.Home.SetArea(area) && session.Housing.SaveHome()) {
            session.Field.Broadcast(CubePacket.IncreaseArea((byte) area));
        }
    }

    private void HandleDecreaseArea(GameSession session) {
        if (session.Field is null || !InOwnHome(session)) return;
        // The client sends this only when its area table has a smaller step (uihomeresizedialog.cpp, OnShrinkArea);
        // a decrease that does not change the area (at the minimum, or a save that failed) removes no cube.
        int newArea;
        if (session.Player.Value.Home.IsPlanner) {
            newArea = session.Player.Value.Home.PlannerArea - 1;
            if (!session.Player.Value.Home.SetPlannerArea(newArea)) {
                return;
            }
        } else {
            newArea = session.Player.Value.Home.Area - 1;
            if (!session.Player.Value.Home.SetArea(newArea)) {
                return;
            }
            if (!session.Housing.SaveHome()) {
                session.Player.Value.Home.SetArea(newArea + 1);
                return;
            }
        }
        session.Field.Broadcast(CubePacket.DecreaseArea((byte) newArea));

        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot == null) {
            return;
        }

        // Remove cubes that are now outside the new area
        List<PlotCube> cubesToRemove = plot.Cubes.Values.Where(cube => session.Housing.IsCoordOutsideArea(cube.Position)).ToList();
        foreach (PlotCube cube in cubesToRemove) {
            if (session.Housing.TryRemoveCube(plot, cube.Position, out _)) {
                session.Field.Broadcast(CubePacket.RemoveCube(session.Player.ObjectId, cube.Position));
            }
        }

        Vector3 safeCoord = session.Player.Value.Home.CalculateSafePosition(plot.Cubes.Values.ToList());

        foreach (FieldPlayer fieldPlayer in session.Field.Players.Values) {
            fieldPlayer.MoveToPosition(safeCoord, default);
        }
    }

    private void HandleIncreaseHeight(GameSession session) {
        if (session.Field is null || !InOwnHome(session)) return;
        if (session.Player.Value.Home.IsPlanner) {
            int decorHeight = session.Player.Value.Home.PlannerHeight + 1;
            if (session.Player.Value.Home.SetPlannerHeight(decorHeight)) {
                session.Field.Broadcast(CubePacket.IncreaseHeight((byte) decorHeight));
            }
            return;
        }

        int height = session.Player.Value.Home.Height + 1;
        if (session.Player.Value.Home.SetHeight(height) && session.Housing.SaveHome()) {
            session.Field.Broadcast(CubePacket.IncreaseHeight((byte) height));
        }
    }

    private void HandleDecreaseHeight(GameSession session) {
        if (session.Field is null || !InOwnHome(session)) return;
        // As the area: at the minimum height (3) SetHeight keeps the height, and the cubes of the top layer stayed in
        // bounds but were taken off the plot.
        int newHeight;
        if (session.Player.Value.Home.IsPlanner) {
            newHeight = session.Player.Value.Home.PlannerHeight - 1;
            if (!session.Player.Value.Home.SetPlannerHeight(newHeight)) {
                return;
            }
        } else {
            newHeight = session.Player.Value.Home.Height - 1;
            if (!session.Player.Value.Home.SetHeight(newHeight)) {
                return;
            }
            if (!session.Housing.SaveHome()) {
                session.Player.Value.Home.SetHeight(newHeight + 1);
                return;
            }
        }
        session.Field.Broadcast(CubePacket.DecreaseHeight((byte) newHeight));

        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot == null) {
            return;
        }

        // Remove cubes that are now outside the new height
        List<PlotCube> cubesToRemove = plot.Cubes.Values.Where(cube => cube.Position.Z > newHeight).ToList();
        foreach (PlotCube cube in cubesToRemove) {
            if (session.Housing.TryRemoveCube(plot, cube.Position, out _)) {
                session.Field.Broadcast(CubePacket.RemoveCube(session.Player.ObjectId, cube.Position));
            }
        }

        Vector3 safeCoord = session.Player.Value.Home.CalculateSafePosition(plot.Cubes.Values.ToList());
        foreach (FieldPlayer fieldPlayer in session.Field.Players.Values) {
            fieldPlayer.MoveToPosition(safeCoord, default);
        }
    }

    private void HandleInteriorDesignCheckIn(GameSession session) {
        Plot? plot = session.Housing.GetFieldPlot();
        if (plot == null) {
            return;
        }

        // Another player's home: the check-in's own answer (0x28) with the error the generic one carried.
        if (plot.OwnerId != session.AccountId) {
            session.Send(CubePacket.InteriorCheckInError(UgcMapError.s_ugcmap_dont_have_ownership));
            return;
        }

        session.Housing.InteriorCheckIn(plot);
    }

    private void HandleInteriorDesignReward(GameSession session, IByteReader packet) {
        byte rewardId = packet.ReadByte();
        session.Housing.InteriorReward(rewardId);
    }

    // The options are the owner's (HomePermissionRules.MayChange). They are saved at once: a player who enters
    // the home later gets them from the database (LoadUgcMapHandler), not from the owner's session.
    private static bool InOwnHome(GameSession session) {
        return session.Field is HomeFieldManager home && HomePermissionRules.MayChange(home.OwnerId, session.AccountId);
    }

    private void HandleEnablePermission(GameSession session, IByteReader packet) {
        var permission = packet.Read<HomePermission>();
        bool enabled = packet.ReadBool();
        if (session.Field is null || !InOwnHome(session)) return;

        if (HomePermissionRules.Enable(session.Player.Value.Home.Permissions, permission, enabled) && session.Housing.SaveHome()) {
            session.Field.Broadcast(CubePacket.EnablePermission(permission, enabled));
        }
    }

    private void HandleSetPermission(GameSession session, IByteReader packet) {
        var permission = packet.Read<HomePermission>();
        var setting = packet.Read<HomePermissionSetting>();
        if (session.Field is null || !InOwnHome(session)) return;

        if (HomePermissionRules.Set(session.Player.Value.Home.Permissions, permission, setting) is { } applied && session.Housing.SaveHome()) {
            session.Field.Broadcast(CubePacket.SetPermission(permission, applied));
        }
    }

    private void HandleSaveLayout(GameSession session, IByteReader packet) {
        int slot = packet.ReadInt();
        string name = packet.ReadUnicodeString();

        Home home = session.Player.Value.Home;
        if (slot is > Constant.HomeMaxLayoutSlots or < 0) {
            return;
        }

        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot == null) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();

        HomeLayout? layout = home.Layouts.FirstOrDefault(homeLayout => homeLayout.Id == slot);
        if (layout is not null) {
            home.Layouts.Remove(layout);
            db.RemoveHomeLayout(layout);
        }

        byte area = home.IsPlanner ? home.PlannerArea : home.Area;
        byte height = home.IsPlanner ? home.PlannerHeight : home.Height;
        layout = db.SaveHomeLayout(new HomeLayout(slot, name, area, height, DateTimeOffset.Now, plot.Cubes.Values.ToList()));
        if (layout is null) {
            Logger.Error("Failed to save layout for {AccountId}", session.AccountId);
            session.Send(CubePacket.SaveLayoutError(UgcMapError.s_ugcmap_db));
            return;
        }
        home.Layouts.Add(layout);

        session.Housing.SaveHome();

        session.Send(CubePacket.SaveLayout(session.AccountId, layout));
    }

    private void HandleLoadLayout(GameSession session, IByteReader packet) {
        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot is null) {
            return;
        }

        if (plot.Cubes.Count != 0) {
            session.Send(NoticePacket.Message(StringCode.s_err_ugcmap_package_clear_indoor_first, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
            return;
        }

        int slot = packet.ReadInt();

        Home home = session.Player.Value.Home;

        // The client sends 48 only from the bill of a saved layout (UIUGCCubeBillDialog::SendRequestCube kind 1,
        // uiugccubebilldialog.cpp:216, :256) with the layout's id, slot 0 included; a blueprint is confirmed with 70
        // and its item uid (kind 3). Slot 0 applied the staged blueprint, unchecked against the item, instead of the
        // layout saved in slot 0.
        HomeLayout? layout = home.Layouts.FirstOrDefault(homeLayout => homeLayout.Id == slot);
        if (layout is null) {
            Logger.Error("Failed to find layout {Slot} for {AccountId}", slot, session.AccountId);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        session.Housing.StagedItemBlueprint = null;
        session.Housing.StagedBlueprintItemUid = 0;
        session.Housing.ApplyLayout(plot, layout);
    }

    // The client confirms the bill of a blueprint item (UIUGCCubeBillDialog::SendRequestCube and FUN_140ee83f0,
    // uiugccubebilldialog.cpp:222, :302): long uid of the item whose use opened the bill.
    private void HandleApplyBlueprint(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();

        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot is null) {
            return;
        }

        if (plot.Cubes.Count != 0) {
            session.Send(NoticePacket.Message(StringCode.s_err_ugcmap_package_clear_indoor_first, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
            return;
        }

        if (!BlueprintBillRule.Confirms(session.Housing.StagedItemBlueprint != null, session.Housing.StagedBlueprintItemUid, itemUid)) {
            return;
        }

        ApplyStagedBlueprint(session, plot);
    }

    private void ApplyStagedBlueprint(GameSession session, Plot plot) {
        if (session.Housing.StagedItemBlueprint is null) {
            return;
        }

        HomeLayout? layout;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            layout = db.GetHomeLayout(session.Housing.StagedItemBlueprint.BlueprintUid);
        }
        if (layout is null) {
            Logger.Error("Failed to load layout for {AccountId}", session.AccountId);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        session.Housing.StagedItemBlueprint = null;
        session.Housing.StagedBlueprintItemUid = 0;
        session.Housing.ApplyLayout(plot, layout, isBlueprint: true);
    }


    // UIHomeOptionDialog::SendRequestCubeMode49 (uihomeoptiondialog.cpp:278, the "send everyone out" button of the
    // home options): no fields. The owner's client shows its own notice. Every visitor of another account gets
    // ResponseCube 0x1a (leave within 10 seconds, or Exit now) and, if that same field entry is still in the home
    // after the delay, goes back to the return map.
    private void HandleKickOut(GameSession session) {
        if (session.Field is not HomeFieldManager home) return;
        var told = new Dictionary<int, FieldPlayer>();
        foreach ((int objectId, FieldPlayer visitor) in home.Players) {
            if (HomeVisitorRules.SentOutWithAll(home.OwnerId, session.AccountId, visitor.Value.Account.Id)) {
                visitor.Session.Send(CubePacket.KickOut());
                told[objectId] = visitor;
            }
        }
        if (told.Count == 0) return;

        home.Scheduler.Schedule(() => SendOutStillThere(home, told), HomeVisitorRules.SendOutDelay);
    }

    // The send-everyone-out timer: only the field entries told to leave that are still in the home go.
    internal static void SendOutStillThere(FieldManager home, IReadOnlyDictionary<int, FieldPlayer> told) {
        foreach (FieldPlayer visitor in HomeVisitorRules.StillToSendOut(told, home.Players)) {
            visitor.Session.ReturnField();
        }
    }

    private void HandleSetBackground(GameSession session, IByteReader packet) {
        if (session.Field is null || !InOwnHome(session)) return;
        var background = packet.Read<HomeBackground>();
        if (session.Player.Value.Home.SetBackground(background) && session.Housing.SaveHome()) {
            session.Field.Broadcast(CubePacket.SetBackground(background));
        }
    }

    private void HandleSetLighting(GameSession session, IByteReader packet) {
        if (session.Field is null || !InOwnHome(session)) return;
        var lighting = packet.Read<HomeLighting>();
        if (session.Player.Value.Home.SetLighting(lighting) && session.Housing.SaveHome()) {
            session.Field.Broadcast(CubePacket.SetLighting(lighting));
        }
    }

    private void HandleSetCamera(GameSession session, IByteReader packet) {
        if (session.Field is null || !InOwnHome(session)) return;
        var camera = packet.Read<HomeCamera>();
        if (session.Player.Value.Home.SetCamera(camera) && session.Housing.SaveHome()) {
            session.Field.Broadcast(CubePacket.SetCamera(camera));
        }
    }

    // Every refusal is answered with 0x3f result 0 as well (CreateBlueprintFailed): the client closes its upload dialog
    // and shows its own notice only on that answer (ugcmanager_4.cpp:3934-3964); before, a refusal left it open.
    private void HandleCreateBlueprint(GameSession session) {
        if (!CreateBlueprint(session)) {
            session.Send(CubePacket.CreateBlueprintFailed());
        }
    }

    private bool CreateBlueprint(GameSession session) {
        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot is null) {
            return false;
        }

        if (!TableMetadata.UgcDesignTable.Entries.TryGetValue(Constant.BlueprintId, out UgcDesignTable.Entry? design)) {
            Logger.Error("Failed to find design {BlueprintId}", Constant.BlueprintId);
            return false;
        }

        long blueprintCost = design.CreatePrice;

        // Nothing is paid or written before the bag is known to take the blueprint: the merets were taken first and
        // a full bag lost them, with the layout row saved for no item.
        lock (session.Item) {
            Item? item = session.Field?.ItemDrop.CreateItem(Constant.BlueprintId);
            if (item is null) {
                return false;
            }
            if (!session.Item.Inventory.CanAdd(item)) {
                session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
                return false;
            }

            // Taken here without a packet (another thread can take merets too: a wedding hall paid by the partner),
            // given back when a row cannot be made, and shown where the take was before.
            if (!session.Currency.TryTakeMeret(blueprintCost, notify: false)) {
                session.Send(CubePacket.Error(UgcMapError.s_err_ugcmap_not_enough_meso_balance));
                return false;
            }

            Home home = session.Player.Value.Home;
            byte area = home.PlannerArea;
            byte height = home.PlannerHeight;
            using GameStorage.Request db = session.GameStorage.Context();

            var homeLayout = new HomeLayout(0, "Blueprint", area, height, DateTimeOffset.Now, plot.Cubes.Values.ToList()) {
                Background = home.Background,
                Lighting = home.Lighting,
                Camera = home.Camera,
            };
            HomeLayout? layout = db.SaveHomeLayout(homeLayout);
            if (layout is null) {
                session.Currency.ReturnMeret(blueprintCost);
                Logger.Error("Failed to save layout for {AccountId}", session.AccountId);
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
                return false;
            }

            item.Blueprint = new ItemBlueprint {
                BlueprintUid = layout.Uid,
                Width = home.PlannerArea,
                Length = home.PlannerArea,
                Height = home.PlannerHeight,
                CreationTime = DateTimeOffset.Now,
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                CharacterName = session.PlayerName,
            };

            // The answer carries the bag item's uid and the client looks the item up by it (CUgcManager::FUN_14188ff10,
            // ugcmanager_4.cpp:3939-3942); the add of a new item puts a copy with the new row's uid in the bag, so the
            // answer sent uid 0 and the staged item was not the bag's. The row is made here and that item added.
            Item? created = db.CreateItem(session.CharacterId, item);
            if (created is null) {
                db.RemoveHomeLayout(layout);
                session.Currency.ReturnMeret(blueprintCost);
                Logger.Error("Failed to create blueprint item for {CharacterId}", session.CharacterId);
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
                return false;
            }

            session.Currency.NotifyMeretTaken(blueprintCost);
            // The room was checked under the same lock, so the add takes it; should it not, the paid item goes by mail,
            // and the request is answered with a failure so the client's upload dialog does not wait on it (before, no
            // answer was sent). Chosen here: no extra notice says the blueprint was mailed.
            if (!session.Item.Inventory.Add(created, notifyNew: true, commit: true)) {
                if (!session.Item.MailHeldItem(created)) {
                    Logger.Error("Blueprint {Uid} of {CharacterId} was neither added nor mailed", created.Uid, session.CharacterId);
                }
                return false;
            }
            // create_blueprint (23500025 "Create a blueprint once").
            session.ConditionUpdate(ConditionType.create_blueprint);

            session.StagedUgcItem = created;
            session.Send(CubePacket.CreateBlueprint(created.Uid, created.Blueprint!));
            return true;
        }
    }

    private void HandleSaveBlueprint(GameSession session, IByteReader packet) {
        int slot = packet.ReadInt();
        string name = packet.ReadUnicodeString();

        Home home = session.Player.Value.Home;
        if (slot is > Constant.HomeMaxLayoutSlots or < 0) {
            return;
        }

        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot == null) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();

        HomeLayout? layout = home.Blueprints.FirstOrDefault(homeLayout => homeLayout.Id == slot);
        if (layout is not null) {
            home.Blueprints.Remove(layout);
            db.RemoveHomeLayout(layout);
        }

        byte area = home.PlannerArea;
        byte height = home.PlannerHeight;
        var homeLayout = new HomeLayout(slot, name, area, height, DateTimeOffset.Now, plot.Cubes.Values.ToList()) {
            Background = home.Background,
            Lighting = home.Lighting,
            Camera = home.Camera,
        };
        layout = db.SaveHomeLayout(homeLayout);
        if (layout is null) {
            Logger.Error("Failed to save layout for {AccountId}", session.AccountId);
            session.Send(CubePacket.SaveLayoutError(UgcMapError.s_ugcmap_db, blueprint: true));
            return;
        }
        home.Blueprints.Add(layout);

        session.Housing.SaveHome();

        session.Send(CubePacket.SaveBlueprint(session.AccountId, layout));
    }

    private void HandleLoadBlueprint(GameSession session, IByteReader packet) {
        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot is null) {
            return;
        }

        if (plot.Cubes.Count != 0) {
            session.Send(NoticePacket.Message(StringCode.s_err_ugcmap_package_clear_indoor_first, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
            return;
        }

        int slot = packet.ReadInt();

        Home home = session.Player.Value.Home;
        // A missing slot gets 0x02 s_ugcmap_db, as a layout load and a layout bill do; before, no answer.
        HomeLayout? layout = home.Blueprints.FirstOrDefault(homeLayout => homeLayout.Id == slot);
        if (layout is null) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        session.Housing.ApplyLayout(plot, layout, isBlueprint: true);
    }

    private void HandleCalculateEstimate(GameSession session, IByteReader packet) {
        long blueprintId = packet.ReadLong();

        using GameStorage.Request db = session.GameStorage.Context();
        // As for a preview: only a blueprint in the own bag or listed on the market, not any stored layout by uid.
        bool inOwnBag = session.Item.Inventory.Filter(item => item.Blueprint != null && item.Blueprint.BlueprintUid == blueprintId).Count > 0;
        bool listed = !inOwnBag && db.IsUgcMarketBlueprintListed(blueprintId);
        if (!BlueprintPreviewRule.BlueprintAllows(listed, inOwnBag)) {
            return;
        }

        HomeLayout? layout = db.GetHomeLayout(blueprintId);
        if (layout is null) {
            Logger.Error("Failed to load layout for {AccountId}", session.AccountId);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        if (session.Housing.RequestLayout(layout, out (Dictionary<FurnishingCurrencyType, long> cubeCosts, int cubeCount) result)) {
            session.Send(CubePacket.CalculateEstimate(result.cubeCosts, result.cubeCount));
        }
    }

    private void HandlePreviewBlueprint(GameSession session, IByteReader packet) {
        long blueprintId = packet.ReadLong();

        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot is null) {
            return;
        }

        // A preview builds only in the model home, never in a real home or another planner.
        if (!BlueprintPreviewRule.PlotAllows(plot.PlotMode)) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        bool inOwnBag = session.Item.Inventory.Filter(item => item.Blueprint != null && item.Blueprint.BlueprintUid == blueprintId).Count > 0;
        bool listed = !inOwnBag && db.IsUgcMarketBlueprintListed(blueprintId);
        if (!BlueprintPreviewRule.BlueprintAllows(listed, inOwnBag)) {
            return;
        }

        HomeLayout? layout = db.GetHomeLayout(blueprintId);
        if (layout is null) {
            Logger.Error("Failed to load layout for {AccountId}", session.AccountId);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        if (BlueprintPreviewRule.ClearsFirst(plot.Cubes.Count)) {
            // The model home still shows an earlier preview: take it down and ask the client to send the request
            // again. The client holds that second request until its own cube changes are done.
            foreach (PlotCube cube in plot.Cubes.Values.ToList()) {
                if (session.Housing.TryRemoveCube(plot, cube.Position, out _)) {
                    session.Field?.Broadcast(CubePacket.RemoveCube(session.Player.ObjectId, cube.Position));
                }
            }
            if (!BlueprintPreviewRule.ClearsFirst(plot.Cubes.Count)) {
                session.Send(CubePacket.PreviewBlueprintAgain(blueprintId));
            }
            return;
        }

        session.Housing.ApplyLayout(plot, layout, isBlueprint: true);
    }
}
