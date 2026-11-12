using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;
// ReSharper disable RedundantTypeArgumentsOfMethod

namespace Maple2.Server.Game.Packets;

public static class CubePacket {
    private enum Command : byte {
        BuildMode = 0,
        HoldCube = 1,
        BuyPlot = 2,
        ConfirmBuyPlot = 4,
        ForfeitPlot = 5,
        ConfirmForfeitPlot = 7,
        ExtendPlot = 9,
        PlaceCube = 10, // Also PurchaseCube?
        RemoveCube = 12,
        RotateCube = 14,
        ReplaceCube = 15,
        LiftupObject = 17,
        LiftupDrop = 18,
        UpdateProfile = 20,
        SetHomeName = 21,
        UpdatePlot = 22, // Buy/Extend/Forfeit
        SetPasscode = 24,
        ConfirmVote = 25,
        KickOut = 26,
        LiftedCubes = 27,
        ArchitectScore = 28,
        SetHomeMessage = 29,
        // 32
        // 33
        ReturnMap = 34,
        RequestLayout = 36,
        IncreaseArea = 37,
        DecreaseArea = 38,
        DesignRankReward = 39,
        // 40, 41
        EnablePermission = 42,
        SetPermission = 43,
        IncreaseHeight = 44,
        DecreaseHeight = 45,
        SaveLayout = 46,
        // 50
        SetBackground = 51,
        SetLighting = 52,
        GrantBuildPermissions = 53,
        SetCamera = 54,
        BuildOwnerLocks = 55,
        UpdateBuildBudget = 56,
        AddBuildPermission = 57,
        RemoveBuildPermission = 58,
        LoadBuildPermission = 59,
        // Blueprint stuff:
        // 60, 61, 63, 64, 67, 69
        UpdateHomeAreaAndHeight = 62,
        CreateBlueprint = 63,
        SaveBlueprint = 64,
        CalculateEstimate = 67,
        PreviewBlueprintAgain = 69,
        FunctionCubeError = 71,
    }

    // A lot of ops contain this logic, we are just reusing Command.BuyPlot
    public static ByteWriter Error(UgcMapError error) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.BuyPlot);
        pWriter.Write<UgcMapError>(error);

        return pWriter;
    }

    // Client: CSceneStateGame ResponseCube case 0 (scenestategame.cpp:9883): int player object id, int mode. For a
    // player in cube mode it calls Unk_PCCubeCraft::SetMode (cubecraft_2.cpp:693), which stores the mode and rebuilds
    // the guides. The client never sets the mode on its own: it sends request mode 0 (1 from the cube palette when no
    // cube is selected, 0 before it asks for a cube while in mode 1) and waits for this answer.
    public static ByteWriter BuildMode(int objectId, int mode) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.WriteByte((byte) Command.BuildMode);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(mode);

        return pWriter;
    }

    // Client: CSceneStateGame::OnResponseCube case 0x45 (scenestategame.cpp:10838): one long, the blueprint uid.
    // The client answers with RequestCube 68 for that uid, at once or when its pending cube set is empty
    // (CubeMapInformationService::SendRequestCubeMode68).
    public static ByteWriter PreviewBlueprintAgain(long blueprintUid) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.WriteByte((byte) Command.PreviewBlueprintAgain);
        pWriter.WriteLong(blueprintUid);

        return pWriter;
    }

    public static ByteWriter HoldCube(int objectId, HeldCube cube) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.HoldCube);
        pWriter.WriteInt(objectId);
        pWriter.WriteClass<HeldCube>(cube);

        return pWriter;
    }

    public enum Edit : byte {
        Place = Command.PlaceCube,
        Remove = Command.RemoveCube,
        Replace = Command.ReplaceCube,
    }

    // A refused place, remove or replace as KMS2 answers it: the edit's own command, the error, then the two
    // object ids of the answer that succeeds (10 bytes).
    public static ByteWriter EditError(Edit edit, UgcMapError error, int objectId) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.WriteByte((byte) edit);
        pWriter.WriteByte((byte) error);
        pWriter.WriteInt(objectId); // Owner
        pWriter.WriteInt(objectId); // Player
        return pWriter;
    }

    // A refused lift as KMS2 answers it: the lift command, the error and the player's object id (6 bytes).
    public static ByteWriter LiftupError(UgcMapError error, int objectId) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.LiftupObject);
        pWriter.WriteByte((byte) error);
        pWriter.WriteInt(objectId);
        return pWriter;
    }

    public static ByteWriter BuyPlot(PlotInfo plot) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.BuyPlot);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteInt(plot.Number);
        pWriter.WriteInt(plot.ApartmentNumber);
        pWriter.WriteUnicodeString(plot.Name);
        pWriter.WriteLong(plot.ExpiryTime);
        pWriter.WriteLong(plot.OwnerId);

        return pWriter;
    }

    public static ByteWriter ConfirmBuyPlot() {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.ConfirmBuyPlot);

        return pWriter;
    }

    public static ByteWriter ForfeitPlot(PlotInfo plot) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.ForfeitPlot);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteInt(plot.Number);
        pWriter.WriteInt(plot.ApartmentNumber);
        pWriter.WriteBool(false); // This might be state?

        return pWriter;
    }

    // The client shows its "already expired" notice for reason 0x75 and the forfeit notice for any other; KMS2 sends
    // 0x72 to an owner who gave the plot up.
    public const short ForfeitByOwner = 0x72;
    public const short ForfeitExpired = 0x75;

    // Client: CSceneStateGame::OnResponseCube case 7 (scenestategame.cpp:9959): short reason, byte flag, int map,
    // int plot number.
    public static ByteWriter ConfirmForfeitPlot(PlotInfo plot, short reason, bool flag = false) {
        return ConfirmForfeitPlot(plot.MapId, plot.Number, reason, flag);
    }

    public static ByteWriter ConfirmForfeitPlot(int mapId, int plotNumber, short reason, bool flag = false) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.ConfirmForfeitPlot);
        pWriter.WriteShort(reason);
        pWriter.WriteBool(flag);
        pWriter.WriteInt(mapId);
        pWriter.WriteInt(plotNumber);

        return pWriter;
    }

    // OnResponseCube case 9 (scenestategame.cpp:10310) reads only the error byte.
    public static ByteWriter ExtendPlot() {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.ExtendPlot);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);

        return pWriter;
    }

    public static ByteWriter PurchaseCube(int objectId) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.PlaceCube);
        pWriter.Write<UgcMapError>(UgcMapError.s_empty_string);
        pWriter.WriteInt(objectId); // Owner
        pWriter.WriteInt(objectId); // Player

        return pWriter;
    }

    public static ByteWriter PlaceCube(int objectId, PlotInfo plot, PlotCube cube) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.PlaceCube);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteInt(objectId); // Owner
        pWriter.WriteInt(objectId); // Player
        pWriter.WriteInt(plot.Number);
        pWriter.WriteInt(plot.ApartmentNumber);
        pWriter.Write<Vector3B>(cube.Position);
        pWriter.WriteLong(cube.Id);
        pWriter.WriteClass<HeldCube>(cube);
        pWriter.WriteBool(false); // Unknown
        pWriter.WriteFloat(cube.Rotation);
        pWriter.WriteInt(); // Unknown
        pWriter.WriteBool(false);

        return pWriter;
    }

    public static ByteWriter PlaceLiftable(int objectId, LiftableCube liftable, in Vector3 position, float rotation, bool isBound = false) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.PlaceCube);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteInt(objectId); // Owner
        pWriter.WriteInt(objectId); // Player
        pWriter.WriteInt(); // plot #
        pWriter.WriteInt();
        pWriter.Write<Vector3B>(position);
        pWriter.WriteLong(liftable.Id);
        pWriter.WriteClass<HeldCube>(liftable);
        pWriter.WriteBool(true); // Unknown
        pWriter.WriteFloat(rotation);
        pWriter.WriteInt(); // Unknown
        pWriter.WriteBool(isBound);
        if (isBound) {
            pWriter.WriteString();
            pWriter.WriteByte();
        }

        return pWriter;
    }

    public static ByteWriter RemoveCube(int objectId, in Vector3B position) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.RemoveCube);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteInt(objectId); // Owner
        pWriter.WriteInt(objectId); // Player
        pWriter.Write<Vector3B>(position);
        pWriter.WriteBool(false);

        return pWriter;
    }

    public static ByteWriter RotateCube(int objectId, PlotCube cube) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.RotateCube);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteInt(objectId); // Owner
        pWriter.WriteInt(objectId); // Player
        pWriter.Write<Vector3B>(cube.Position);
        pWriter.WriteFloat(cube.Rotation);

        return pWriter;
    }

    public static ByteWriter ReplaceCube(int objectId, PlotCube cube) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.ReplaceCube);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteInt(objectId); // Owner
        pWriter.WriteInt(objectId); // Player
        pWriter.Write<Vector3B>(cube.Position);
        pWriter.WriteLong(cube.Id);
        pWriter.WriteClass<HeldCube>(cube);
        pWriter.WriteBool(false); // Unknown
        pWriter.WriteFloat(cube.Rotation);
        pWriter.WriteInt(); // Unknown

        return pWriter;
    }

    public static ByteWriter LiftupObject(IActor<Player> player, LiftupWeapon liftupWeapon, int respawnTick) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.LiftupObject);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteInt(player.ObjectId);
        pWriter.Write<Vector3B>(liftupWeapon.Object.Position);
        pWriter.WriteInt(liftupWeapon.ItemId);
        pWriter.WriteInt(respawnTick);

        return pWriter;
    }

    // Client: ResponseCube case 0x1b (scenestategame.cpp:10195): int count, then per cube its cell key and the
    // tick it comes back at; the cube is hidden until then, as after sub-type 17. For a player who enters the
    // field while cubes are away.
    public static ByteWriter LiftedCubes(IReadOnlyCollection<(Vector3B Position, int RespawnTick)> cubes) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.LiftedCubes);
        pWriter.WriteInt(cubes.Count);
        foreach ((Vector3B position, int respawnTick) in cubes) {
            pWriter.Write<Vector3B>(position);
            pWriter.WriteInt(respawnTick);
        }

        return pWriter;
    }

    public static ByteWriter LiftupDrop(IActor<Player> player) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.LiftupDrop);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteInt(player.ObjectId);

        return pWriter;
    }

    public static ByteWriter SetHomeName(Home home) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.SetHomeName);
        pWriter.WriteBool(false);
        pWriter.WriteLong(home.AccountId);
        pWriter.WriteInt(home.PlotNumber);
        pWriter.WriteInt(home.ApartmentNumber);
        pWriter.WriteUnicodeString(home.Indoor.Name);

        return pWriter;
    }

    public static ByteWriter UpdateProfile(Player player, bool load = false) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.UpdateProfile);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteInt(player.Home.Indoor.MapId);
        pWriter.WriteInt(player.Home.PlotMapId);
        pWriter.WriteInt(player.Home.PlotNumber);
        pWriter.WriteUnicodeString(player.Home.Name);
        pWriter.WriteLong(player.Home.PlotExpiryTime);
        pWriter.WriteLong(player.Home.LastModified);
        pWriter.WriteBool(load);
        // UserUGCMapInfo +0x40: the open type the home option dialog shows (0 public, 1 password, 2 private).
        pWriter.WriteByte(HomeOpenRules.OpenType(player.Home.Passcode));

        return pWriter;
    }

    public static ByteWriter UpdatePlot(PlotInfo plot) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.UpdatePlot);
        pWriter.WriteInt(plot.Number);
        pWriter.WriteInt(plot.ApartmentNumber);
        pWriter.Write<PlotState>(plot.State);
        pWriter.WriteLong(plot.ExpiryTime);

        return pWriter;
    }
    public static ByteWriter SetPasscode() {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.SetPasscode);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);

        return pWriter;
    }

    // Client: CSceneStateGame::OnResponseCube case 0x19 (scenestategame.cpp:10320): byte error; when 0, the account id
    // (the id of the login result) and a value the client keeps when the id is its own.
    public static ByteWriter ConfirmVote(byte error = 0, long accountId = 0, long value = 0) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.ConfirmVote);
        pWriter.WriteByte(error);
        if (error == 0) {
            pWriter.WriteLong(accountId);
            pWriter.WriteLong(value);
        }

        return pWriter;
    }

    public static ByteWriter KickOut() {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.KickOut);

        return pWriter;
    }

    // Client: CSceneStateGame::OnResponseCube case 0x1c (scenestategame.cpp:10356): two ints, stored by FUN_1410c3410 / FUN_1410c3450.
    public static ByteWriter ArchitectScore(int value1, int value2) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.ArchitectScore);
        pWriter.WriteInt(value1);
        pWriter.WriteInt(value2);

        return pWriter;
    }

    public static ByteWriter SetHomeMessage(string message) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.SetHomeMessage);
        pWriter.WriteBool(false);
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    public static ByteWriter ReturnMap(int mapId) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.ReturnMap);
        pWriter.WriteInt(mapId);

        return pWriter;
    }

    public static ByteWriter BuyCubes(Dictionary<FurnishingCurrencyType, long> cubeCosts, int cubeCount) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.RequestLayout);
        pWriter.WriteByte((byte) cubeCosts.Keys.Count);
        pWriter.WriteInt(cubeCount);
        foreach ((FurnishingCurrencyType moneyType, long amount) in cubeCosts) {
            pWriter.Write<FurnishingCurrencyType>(moneyType);
            pWriter.WriteLong(amount);
        }

        return pWriter;
    }

    public static ByteWriter IncreaseArea(byte area) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.IncreaseArea);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteByte(area);

        return pWriter;
    }

    public static ByteWriter DecreaseArea(byte area) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.DecreaseArea);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteByte(area);

        return pWriter;
    }

    // Client: CSceneStateGame::OnResponseCube case 0x27 (scenestategame.cpp:10560): long account id (dropped unless it is
    // the id of the login result, set in UIServerListDialog::OnLoginResult), long reward time, long level (kept as a
    // short), long exp, int count, count x int claimed reward id.
    public static ByteWriter DesignRankReward(Home home) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.DesignRankReward);
        pWriter.WriteLong(home.AccountId);
        pWriter.WriteLong(home.DecorationRewardTimestamp);
        pWriter.WriteLong(home.DecorationLevel);
        pWriter.WriteLong(home.DecorationExp);
        pWriter.WriteInt(home.InteriorRewardsClaimed.Count);

        foreach (int rewardId in home.InteriorRewardsClaimed) {
            pWriter.WriteInt(rewardId);
        }

        return pWriter;
    }

    public static ByteWriter EnablePermission(HomePermission permission, bool enabled) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.EnablePermission);
        pWriter.Write<HomePermission>(permission);
        pWriter.WriteBool(enabled);

        return pWriter;
    }

    public static ByteWriter SetPermission(HomePermission permission, HomePermissionSetting setting) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.SetPermission);
        pWriter.Write<HomePermission>(permission);
        pWriter.Write<HomePermissionSetting>(setting);

        return pWriter;
    }

    public static ByteWriter IncreaseHeight(byte height) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.IncreaseHeight);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteByte(height);

        return pWriter;
    }

    public static ByteWriter DecreaseHeight(byte height) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.DecreaseHeight);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteByte(height);

        return pWriter;
    }

    public static ByteWriter SaveLayout(long accountId, HomeLayout layout) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.SaveLayout);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteLong(accountId);
        pWriter.WriteClass<HomeLayout>(layout);

        return pWriter;
    }

    public static ByteWriter SetBackground(HomeBackground background) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.SetBackground);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.Write<HomeBackground>(background);

        return pWriter;
    }

    public static ByteWriter SetLighting(HomeLighting lighting) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.SetLighting);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.Write<HomeLighting>(lighting);

        return pWriter;
    }

    // Client: CSceneStateGame::OnResponseCube case 0x35 (scenestategame.cpp:10592): the account id (the id of the login
    // result), the house owner's id, and the two build budget values of UpdateBuildBudget; the client ignores it for another account.
    public static ByteWriter GrantBuildPermissions(long accountId = 0, long ownerId = 0, long value1 = 0, long value2 = 0) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.GrantBuildPermissions);
        pWriter.WriteLong(accountId);
        pWriter.WriteLong(ownerId);
        pWriter.WriteLong(value1);
        pWriter.WriteLong(value2);

        return pWriter;
    }

    // The home owner's interior level and the trophy grades the owner lacks, for a build helper: its client
    // locks a cube by these instead of its own (MyPC::FUN_1415302e0, client/ms2/mypc.cpp:4437).
    public static ByteWriter BuildOwnerLocks(short interiorLevel, IReadOnlyCollection<(int TrophyId, int Grade)> missing) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.BuildOwnerLocks);
        pWriter.WriteShort(interiorLevel);
        pWriter.WriteInt(missing.Count);
        foreach ((int trophyId, int grade) in missing) {
            pWriter.WriteInt(trophyId);
            pWriter.WriteInt(grade);
        }

        return pWriter;
    }

    public static ByteWriter SetCamera(HomeCamera camera) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.SetCamera);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.Write<HomeCamera>(camera);

        return pWriter;
    }

    // Client: CSceneStateGame::OnResponseCube case 0x38 (scenestategame.cpp:10791): byte error; when 0, two longs, then the home dialog's UpdateBalance.
    public static ByteWriter UpdateBuildBudget(byte error = 0, long value1 = 0, long value2 = 0) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.UpdateBuildBudget);
        pWriter.WriteByte(error);
        if (error == 0) {
            pWriter.WriteLong(value1);
            pWriter.WriteLong(value2);
        }

        return pWriter;
    }

    // Client: CSceneStateGame::OnResponseCube case 0x39 (scenestategame.cpp:10761): byte error; when 0, the long id added (FUN_1410d57a0).
    public static ByteWriter AddBuildPermission(byte error = 0, long id = 0) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.AddBuildPermission);
        pWriter.WriteByte(error);
        if (error == 0) {
            pWriter.WriteLong(id);
        }

        return pWriter;
    }

    // Client: CSceneStateGame::OnResponseCube case 0x3a (scenestategame.cpp:10776): byte error; when 0, the long id removed (FUN_1410d5830).
    public static ByteWriter RemoveBuildPermission(byte error = 0, long id = 0) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.RemoveBuildPermission);
        pWriter.WriteByte(error);
        if (error == 0) {
            pWriter.WriteLong(id);
        }

        return pWriter;
    }

    // Client: CSceneStateGame::OnResponseCube case 0x3b (scenestategame.cpp:10808): int count, count x long id, each added as in case 0x39.
    public static ByteWriter LoadBuildPermission(ICollection<long> ids) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.LoadBuildPermission);
        pWriter.WriteInt(ids.Count);
        foreach (long id in ids) {
            pWriter.WriteLong(id);
        }

        return pWriter;
    }

    public static ByteWriter UpdateHomeAreaAndHeight(byte area, byte height) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.UpdateHomeAreaAndHeight);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteByte(area);
        pWriter.WriteByte(height);

        return pWriter;
    }

    public static ByteWriter CreateBlueprint(long itemUid, ItemBlueprint blueprint) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.CreateBlueprint);
        pWriter.Write<UgcMapError>(UgcMapError.s_empty_string);
        pWriter.WriteLong(itemUid);
        pWriter.WriteClass<ItemBlueprint>(blueprint);

        return pWriter;
    }

    public static ByteWriter SaveBlueprint(long accountId, HomeLayout layout) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.SaveBlueprint);
        pWriter.Write<UgcMapError>(UgcMapError.s_ugcmap_ok);
        pWriter.WriteLong(accountId);
        pWriter.WriteClass<HomeLayout>(layout);

        return pWriter;
    }

    // Client: FUN_140ee83f0 (uiugccubebilldialog.cpp:283): byte count, int, count x (byte pay type, long amount),
    // bool preview. With preview set the bill is only shown; without it the client confirms with RequestCube 70,
    // at once when the bill is empty.
    public static ByteWriter CalculateEstimate(Dictionary<FurnishingCurrencyType, long> cubeCosts, int cubeCount, bool preview = true) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.CalculateEstimate);
        pWriter.WriteByte((byte) cubeCosts.Keys.Count);
        pWriter.WriteInt(cubeCount);
        foreach ((FurnishingCurrencyType moneyType, long amount) in cubeCosts) {
            pWriter.Write<FurnishingCurrencyType>(moneyType);
            pWriter.WriteLong(amount);
        }
        pWriter.WriteBool(preview);

        return pWriter;
    }

    public static ByteWriter FunctionCubeError(FunctionCubeError error) {
        var pWriter = Packet.Of(SendOp.ResponseCube);
        pWriter.Write<Command>(Command.FunctionCubeError);
        pWriter.Write<FunctionCubeError>(error);

        return pWriter;
    }
}
