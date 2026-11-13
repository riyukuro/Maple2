using System;
using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// ResponseCube against CSceneStateGame::OnResponseCube (client/ms2/scenestategame.cpp:9876), CubeItemInfo's reader
// (commonfiles/ugcmapoptions.cpp:182), UserUGCMapInfo::FUN_1408b4220 (ugcmapoptions.cpp:291), the bill readers
// FUN_140ee8190 / FUN_140ee83f0 (uiugccubebilldialog.cpp:233, :283), CUgcManager::FUN_14188ff10
// (ugcmanager_4.cpp:3931) and CBlueprintItemData::Read (commonfiles/ugcdef.cpp:1385).
public class ResponseCubeLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    private static PlotCube Cube() => new(TestGameSession.Metadata(50200001), 0x0102030405060708) {
        Position = new Vector3B(1, 2, 3), Type = PlotCube.CubeType.Default, Rotation = 90,
    };

    private static void Position(ByteWriter writer) {
        writer.WriteByte(1);
        writer.WriteByte(2);
        writer.WriteByte(3);
        writer.WriteByte(0);
    }

    // CubeItemInfo: i32 item, i64 uid, i64, u8 has look.
    private static void Item(ByteWriter writer) {
        writer.WriteInt(50200001);
        writer.WriteLong(0x0102030405060708);
        writer.WriteLong(0);
        writer.WriteBool(false);
    }

    private static PlotInfo Plot() => new(null!) {
        Number = 0x11, ApartmentNumber = 0x12, Name = "plot", ExpiryTime = 0x13, OwnerId = 0x14, MapId = 0x15,
    };

    // 0: i32, i32. 1: i32, CubeItemInfo. 0x45: i64.
    [Test]
    public void ModeAndHold() {
        var mode = new ByteWriter();
        mode.WriteByte(0);
        mode.WriteInt(0x21);
        mode.WriteInt(1);
        Assert.That(Body(CubePacket.BuildMode(0x21, 1)), Is.EqualTo(mode.ToArray()));

        var hold = new ByteWriter();
        hold.WriteByte(1);
        hold.WriteInt(0x21);
        Item(hold);
        Assert.That(Body(CubePacket.HoldCube(0x21, Cube())), Is.EqualTo(hold.ToArray()));

        var again = new ByteWriter();
        again.WriteByte(0x45);
        again.WriteLong(0x22);
        Assert.That(Body(CubePacket.PreviewBlueprintAgain(0x22)), Is.EqualTo(again.ToArray()));
    }

    // 2: u8 err; 0: i32, i32, wstr, i64, i64. 4: nothing. 5: u8 err; 0: i32, i32, u8. 7: u16, u8, i32, i32.
    // 9: u8. 0x16: i32, i32, u8, i64.
    [Test]
    public void Plots() {
        var buy = new ByteWriter();
        buy.WriteByte(2);
        buy.WriteByte(0);
        buy.WriteInt(0x11);
        buy.WriteInt(0x12);
        buy.WriteUnicodeString("plot");
        buy.WriteLong(0x13);
        buy.WriteLong(0x14);
        Assert.That(Body(CubePacket.BuyPlot(Plot())), Is.EqualTo(buy.ToArray()));
        Assert.That(Body(CubePacket.Error(UgcMapError.s_err_ugcmap_not_enough_meso_balance)),
            Is.EqualTo(new byte[] { 2, (byte) UgcMapError.s_err_ugcmap_not_enough_meso_balance }));
        Assert.That(Body(CubePacket.ConfirmBuyPlot()), Is.EqualTo(new byte[] { 4 }));

        var forfeit = new ByteWriter();
        forfeit.WriteByte(5);
        forfeit.WriteByte(0);
        forfeit.WriteInt(0x11);
        forfeit.WriteInt(0x12);
        forfeit.WriteBool(false);
        Assert.That(Body(CubePacket.ForfeitPlot(Plot())), Is.EqualTo(forfeit.ToArray()));

        var confirm = new ByteWriter();
        confirm.WriteByte(7);
        confirm.WriteShort(3);
        confirm.WriteBool(true);
        confirm.WriteInt(0x15);
        confirm.WriteInt(0x11);
        Assert.That(Body(CubePacket.ConfirmForfeitPlot(0x15, 0x11, 3, true)), Is.EqualTo(confirm.ToArray()));
        Assert.That(Body(CubePacket.ExtendPlot()), Is.EqualTo(new byte[] { 9, 0 }));

        var update = new ByteWriter();
        update.WriteByte(0x16);
        update.WriteInt(0x11);
        update.WriteInt(0x12);
        update.WriteByte((byte) PlotState.Taken);
        update.WriteLong(0x13);
        Assert.That(Body(CubePacket.UpdatePlot(Plot())), Is.EqualTo(update.ToArray()));
    }

    // 0xa: u8 err; 0: i32, i32, i32, i32, i32 packed, i64, CubeItemInfo, u8, f32, i32, u8 notice; else i32, i32.
    // 0xc: u8 err; 0: i32, i32, i32 packed, u8. 0xe: ... packed, f32. 0xf: ... packed, i64, CubeItemInfo, u8, f32, i32.
    [Test]
    public void Edits() {
        var place = new ByteWriter();
        place.WriteByte(10);
        place.WriteByte(0);
        place.WriteInt(0x21);
        place.WriteInt(0x21);
        place.WriteInt(0x11);
        place.WriteInt(0x12);
        Position(place);
        place.WriteLong(0x0102030405060708);
        Item(place);
        place.WriteBool(false);
        place.WriteFloat(90);
        place.WriteInt(0);
        place.WriteBool(false);
        Assert.That(Body(CubePacket.PlaceCube(0x21, Plot(), Cube())), Is.EqualTo(place.ToArray()));

        var purchase = new ByteWriter();
        purchase.WriteByte(10);
        purchase.WriteByte((byte) UgcMapError.s_empty_string);
        purchase.WriteInt(0x21);
        purchase.WriteInt(0x21);
        Assert.That(Body(CubePacket.PurchaseCube(0x21)), Is.EqualTo(purchase.ToArray()));

        var refuse = new ByteWriter();
        refuse.WriteByte((byte) CubePacket.Edit.Replace);
        refuse.WriteByte((byte) UgcMapError.s_ugcmap_not_for_sale);
        refuse.WriteInt(0x21);
        refuse.WriteInt(0x21);
        Assert.That(Body(CubePacket.EditError(CubePacket.Edit.Replace, UgcMapError.s_ugcmap_not_for_sale, 0x21)), Is.EqualTo(refuse.ToArray()));

        var remove = new ByteWriter();
        remove.WriteByte(12);
        remove.WriteByte(0);
        remove.WriteInt(0x21);
        remove.WriteInt(0x21);
        Position(remove);
        remove.WriteBool(false);
        Assert.That(Body(CubePacket.RemoveCube(0x21, new Vector3B(1, 2, 3))), Is.EqualTo(remove.ToArray()));

        var rotate = new ByteWriter();
        rotate.WriteByte(14);
        rotate.WriteByte(0);
        rotate.WriteInt(0x21);
        rotate.WriteInt(0x21);
        Position(rotate);
        rotate.WriteFloat(90);
        Assert.That(Body(CubePacket.RotateCube(0x21, Cube())), Is.EqualTo(rotate.ToArray()));

        var replace = new ByteWriter();
        replace.WriteByte(15);
        replace.WriteByte(0);
        replace.WriteInt(0x21);
        replace.WriteInt(0x21);
        Position(replace);
        replace.WriteLong(0x0102030405060708);
        Item(replace);
        replace.WriteBool(false);
        replace.WriteFloat(90);
        replace.WriteInt(0);
        Assert.That(Body(CubePacket.ReplaceCube(0x21, Cube())), Is.EqualTo(replace.ToArray()));
    }

    // 0x11: u8 err; 0: i32, i32 packed, i32, i32; else i32. 0x12: u8 err, i32. 0x1b: i32 n, n x (i32 packed, i32).
    [Test]
    public void Liftup() {
        var drop = new ByteWriter();
        drop.WriteByte(0x11);
        drop.WriteByte((byte) UgcMapError.s_ugcmap_no_cube_to_lift);
        drop.WriteInt(0x21);
        Assert.That(Body(CubePacket.LiftupError(UgcMapError.s_ugcmap_no_cube_to_lift, 0x21)), Is.EqualTo(drop.ToArray()));

        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        var player = test.Session.Player;
        var weapon = new LiftupWeapon(new ObjectWeapon([0x23], 0, 0, new Vector3(150, 300, 450), Vector3.Zero), 0x23, 0, 1);
        var lift = new ByteWriter();
        lift.WriteByte(0x11);
        lift.WriteByte(0);
        lift.WriteInt(player.ObjectId);
        Position(lift);
        lift.WriteInt(0x23);
        lift.WriteInt(0x24);
        Assert.That(Body(CubePacket.LiftupObject(player, weapon, 0x24)), Is.EqualTo(lift.ToArray()));

        var down = new ByteWriter();
        down.WriteByte(0x12);
        down.WriteByte(0);
        down.WriteInt(player.ObjectId);
        Assert.That(Body(CubePacket.LiftupDrop(player)), Is.EqualTo(down.ToArray()));

        var lifted = new ByteWriter();
        lifted.WriteByte(0x1b);
        lifted.WriteInt(1);
        Position(lifted);
        lifted.WriteInt(0x22);
        Assert.That(Body(CubePacket.LiftedCubes(new List<(Vector3B, int)> { (new Vector3B(1, 2, 3), 0x22) })), Is.EqualTo(lifted.ToArray()));
    }

    // 0x14: UserUGCMapInfo: i32 x4, wstr, i64, i64, u8, u8. 0x15: u8 err; 0: i64, i32, i32, wstr.
    // 0x27: i64 x4, i32 n, n x i32.
    [Test]
    public void Home() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        var player = test.Session.Player.Value;
        Home home = player.Home;
        home.InteriorRewardsClaimed = [0x31];

        var profile = new ByteWriter();
        profile.WriteByte(0x14);
        profile.WriteInt(test.Session.Player.ObjectId);
        profile.WriteInt(home.Indoor.MapId);
        profile.WriteInt(home.PlotMapId);
        profile.WriteInt(home.PlotNumber);
        profile.WriteUnicodeString(home.Name);
        profile.WriteLong(home.PlotExpiryTime);
        profile.WriteLong(home.LastModified);
        profile.WriteBool(true);
        profile.WriteByte(HomeOpenRules.OpenType(home.Passcode));
        Assert.That(Body(CubePacket.UpdateProfile(test.Session.Player, true)), Is.EqualTo(profile.ToArray()));

        var name = new ByteWriter();
        name.WriteByte(0x15);
        name.WriteByte(0);
        name.WriteLong(home.AccountId);
        name.WriteInt(home.PlotNumber);
        name.WriteInt(home.ApartmentNumber);
        name.WriteUnicodeString(home.Indoor.Name);
        Assert.That(Body(CubePacket.SetHomeName(home)), Is.EqualTo(name.ToArray()));

        var reward = new ByteWriter();
        reward.WriteByte(0x27);
        reward.WriteLong(home.AccountId);
        reward.WriteLong(home.DecorationRewardTimestamp);
        reward.WriteLong(home.DecorationLevel);
        reward.WriteLong(home.DecorationExp);
        reward.WriteInt(1);
        reward.WriteInt(0x31);
        Assert.That(Body(CubePacket.DesignRankReward(home)), Is.EqualTo(reward.ToArray()));
    }

    // 0x18: u8. 0x19: u8 err; 0: i64, i64. 0x1a: nothing. 0x1c: i32, i32. 0x1d: u8 err; 0: wstr. 0x22: i32.
    [Test]
    public void Visits() {
        Assert.That(Body(CubePacket.SetPasscode()), Is.EqualTo(new byte[] { 0x18, 0 }));
        var vote = new ByteWriter();
        vote.WriteByte(0x19);
        vote.WriteByte(0);
        vote.WriteLong(0x41);
        vote.WriteLong(0x42);
        Assert.That(Body(CubePacket.ConfirmVote(0, 0x41, 0x42)), Is.EqualTo(vote.ToArray()));
        Assert.That(Body(CubePacket.ConfirmVote(5)), Is.EqualTo(new byte[] { 0x19, 5 }));
        Assert.That(Body(CubePacket.KickOut()), Is.EqualTo(new byte[] { 0x1a }));

        var score = new ByteWriter();
        score.WriteByte(0x1c);
        score.WriteInt(0x43);
        score.WriteInt(0x44);
        Assert.That(Body(CubePacket.ArchitectScore(0x43, 0x44)), Is.EqualTo(score.ToArray()));

        var message = new ByteWriter();
        message.WriteByte(0x1d);
        message.WriteByte(0);
        message.WriteUnicodeString("hello");
        Assert.That(Body(CubePacket.SetHomeMessage("hello")), Is.EqualTo(message.ToArray()));

        var map = new ByteWriter();
        map.WriteByte(0x22);
        map.WriteInt(0x45);
        Assert.That(Body(CubePacket.ReturnMap(0x45)), Is.EqualTo(map.ToArray()));
    }

    // 0x24: u8 n, i32, n x (u8, i64). 0x43: the same, then bool preview.
    [Test]
    public void Bills() {
        var costs = new Dictionary<FurnishingCurrencyType, long> { [FurnishingCurrencyType.Meso] = 0x51 };
        var buy = new ByteWriter();
        buy.WriteByte(0x24);
        buy.WriteByte(1);
        buy.WriteInt(2);
        buy.WriteByte((byte) FurnishingCurrencyType.Meso);
        buy.WriteLong(0x51);
        Assert.That(Body(CubePacket.BuyCubes(costs, 2)), Is.EqualTo(buy.ToArray()));

        var estimate = new ByteWriter();
        estimate.WriteByte(0x43);
        estimate.WriteByte(1);
        estimate.WriteInt(2);
        estimate.WriteByte((byte) FurnishingCurrencyType.Meso);
        estimate.WriteLong(0x51);
        estimate.WriteBool(false);
        Assert.That(Body(CubePacket.CalculateEstimate(costs, 2, false)), Is.EqualTo(estimate.ToArray()));
    }

    // 0x25, 0x26, 0x2c, 0x2d, 0x33, 0x34, 0x36: u8 err; 0: u8. 0x3e: u8 err; 0: u8, u8. 0x28, 0x29: u8.
    // 0x2a: u8, u8 bool. 0x2b: u8, u8.
    [Test]
    public void Settings() {
        Assert.That(Body(CubePacket.IncreaseArea(5)), Is.EqualTo(new byte[] { 0x25, 0, 5 }));
        Assert.That(Body(CubePacket.DecreaseArea(5)), Is.EqualTo(new byte[] { 0x26, 0, 5 }));
        Assert.That(Body(CubePacket.IncreaseHeight(6)), Is.EqualTo(new byte[] { 0x2c, 0, 6 }));
        Assert.That(Body(CubePacket.DecreaseHeight(6)), Is.EqualTo(new byte[] { 0x2d, 0, 6 }));
        Assert.That(Body(CubePacket.SetBackground((HomeBackground) 2)), Is.EqualTo(new byte[] { 0x33, 0, 2 }));
        Assert.That(Body(CubePacket.SetLighting((HomeLighting) 3)), Is.EqualTo(new byte[] { 0x34, 0, 3 }));
        Assert.That(Body(CubePacket.SetCamera((HomeCamera) 1)), Is.EqualTo(new byte[] { 0x36, 0, 1 }));
        Assert.That(Body(CubePacket.UpdateHomeAreaAndHeight(7, 8)), Is.EqualTo(new byte[] { 0x3e, 0, 7, 8 }));
        Assert.That(Body(CubePacket.InteriorCheckInError((UgcMapError) 9)), Is.EqualTo(new byte[] { 0x28, 9 }));
        Assert.That(Body(CubePacket.InteriorGradeRewardError((UgcMapError) 9)), Is.EqualTo(new byte[] { 0x29, 9 }));
        Assert.That(Body(CubePacket.EnablePermission((HomePermission) 3, true)), Is.EqualTo(new byte[] { 0x2a, 3, 1 }));
        Assert.That(Body(CubePacket.SetPermission((HomePermission) 3, (HomePermissionSetting) 2)), Is.EqualTo(new byte[] { 0x2b, 3, 2 }));
    }

    // 0x35: i64 x4. 0x37: i16, i32 n, n x (i32, i32). 0x38: u8 err; 0: i64, i64. 0x39, 0x3a: u8 err; 0: i64.
    // 0x3b: i32 n, n x i64.
    [Test]
    public void Builders() {
        var grant = new ByteWriter();
        grant.WriteByte(0x35);
        grant.WriteLong(1);
        grant.WriteLong(2);
        grant.WriteLong(3);
        grant.WriteLong(4);
        Assert.That(Body(CubePacket.GrantBuildPermissions(1, 2, 3, 4)), Is.EqualTo(grant.ToArray()));

        var locks = new ByteWriter();
        locks.WriteByte(0x37);
        locks.WriteShort(5);
        locks.WriteInt(1);
        locks.WriteInt(0x61);
        locks.WriteInt(0x62);
        Assert.That(Body(CubePacket.BuildOwnerLocks(5, new List<(int, int)> { (0x61, 0x62) })), Is.EqualTo(locks.ToArray()));

        var budget = new ByteWriter();
        budget.WriteByte(0x38);
        budget.WriteByte(0);
        budget.WriteLong(0x63);
        budget.WriteLong(0x64);
        Assert.That(Body(CubePacket.UpdateBuildBudget(0, 0x63, 0x64)), Is.EqualTo(budget.ToArray()));
        Assert.That(Body(CubePacket.UpdateBuildBudget(4)), Is.EqualTo(new byte[] { 0x38, 4 }));

        var add = new ByteWriter();
        add.WriteByte(0x39);
        add.WriteByte(0);
        add.WriteLong(0x65);
        Assert.That(Body(CubePacket.AddBuildPermission(0, 0x65)), Is.EqualTo(add.ToArray()));
        Assert.That(Body(CubePacket.AddBuildPermission(4)), Is.EqualTo(new byte[] { 0x39, 4 }));
        var remove = new ByteWriter();
        remove.WriteByte(0x3a);
        remove.WriteByte(0);
        remove.WriteLong(0x65);
        Assert.That(Body(CubePacket.RemoveBuildPermission(0, 0x65)), Is.EqualTo(remove.ToArray()));
        Assert.That(Body(CubePacket.RemoveBuildPermission(4)), Is.EqualTo(new byte[] { 0x3a, 4 }));

        var load = new ByteWriter();
        load.WriteByte(0x3b);
        load.WriteInt(2);
        load.WriteLong(0x66);
        load.WriteLong(0x67);
        Assert.That(Body(CubePacket.LoadBuildPermission(new List<long> { 0x66, 0x67 })), Is.EqualTo(load.ToArray()));
    }

    // 0x2e, 0x40: u8 err; 0: i64, i32, wstr, i64. 0x3f: u8 ok (not 0), i64 item uid, CBlueprintItemData: i64, i32 x3,
    // i64, i32, i64, i64, wstr. 0x47: i32.
    [Test]
    public void Layouts() {
        var layout = new HomeLayout(0x71, "slot", 1, 2, DateTimeOffset.FromUnixTimeSeconds(0x72), []);
        foreach ((byte command, ByteWriter packet) in new[] { ((byte) 0x2e, CubePacket.SaveLayout(0x73, layout)), ((byte) 0x40, CubePacket.SaveBlueprint(0x73, layout)) }) {
            var expected = new ByteWriter();
            expected.WriteByte(command);
            expected.WriteByte(0);
            expected.WriteLong(0x73);
            expected.WriteInt(0x71);
            expected.WriteUnicodeString("slot");
            expected.WriteLong(0x72);
            Assert.That(Body(packet), Is.EqualTo(expected.ToArray()));
        }

        var blueprint = new ItemBlueprint {
            BlueprintUid = 0x74, Length = 1, Width = 2, Height = 3, CreationTime = DateTimeOffset.FromUnixTimeSeconds(0x75),
            Type = (BlueprintType) 1, AccountId = 0x76, CharacterId = 0x77, CharacterName = "maker",
        };
        var create = new ByteWriter();
        create.WriteByte(0x3f);
        create.WriteByte(1);
        create.WriteLong(0x78);
        create.WriteLong(0x74);
        create.WriteInt(1);
        create.WriteInt(2);
        create.WriteInt(3);
        create.WriteLong(0x75);
        create.WriteInt(1);
        create.WriteLong(0x76);
        create.WriteLong(0x77);
        create.WriteUnicodeString("maker");
        Assert.That(Body(CubePacket.CreateBlueprint(0x78, blueprint)), Is.EqualTo(create.ToArray()));

        var function = new ByteWriter();
        function.WriteByte(0x47);
        function.WriteInt(3);
        Assert.That(Body(CubePacket.FunctionCubeError((FunctionCubeError) 3)), Is.EqualTo(function.ToArray()));
    }
}
