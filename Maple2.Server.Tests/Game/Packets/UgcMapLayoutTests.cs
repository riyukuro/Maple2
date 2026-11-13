using System;
using System.Collections.Generic;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// UserEnv against CSceneStateGame::OnUserEnv (scenestategame.cpp), LoadCubes against OnLoadCubes (:10857), LoadUgcMap
// against OnLoadUgcMap (:2172), GlobalPortal against UIGlobalPortalDialog::OnGlobalPortalPacket
// (uiglobalportaldialog.cpp:213) and RecallUser against OnRecallUser (:4733). EnterUgcMap is in EnterUgcMapPacketTests.
public class UgcMapLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    // 0: i32; 1: i32, i32; 2: i32 n x i32; 3: n x (i32, u8); 4: n x i32; 8: n x (i32, i32); 9: n x (i32, u8);
    // 10: n x (i16, i32); 11: i16, i32; 12: n x (i32, i64); 13: i32, i64.
    [Test]
    public void UserEnv() {
        var expected = new ByteWriter();
        expected.WriteByte(0);
        expected.WriteInt(0x11);
        Assert.That(Body(UserEnvPacket.AddTitle(0x11)), Is.EqualTo(expected.ToArray()));

        expected = new ByteWriter();
        expected.WriteByte(1);
        expected.WriteInt(0x12);
        expected.WriteInt(0x13);
        Assert.That(Body(UserEnvPacket.UpdateTitle(0x12, 0x13)), Is.EqualTo(expected.ToArray()));

        expected = new ByteWriter();
        expected.WriteByte(2);
        expected.WriteInt(1);
        expected.WriteInt(0x14);
        Assert.That(Body(UserEnvPacket.LoadTitles(new HashSet<int> { 0x14 })), Is.EqualTo(expected.ToArray()));

        expected = new ByteWriter();
        expected.WriteByte(3);
        expected.WriteInt(1);
        expected.WriteInt(0x15);
        expected.WriteByte(2);
        Assert.That(Body(UserEnvPacket.ItemCollects(new Dictionary<int, byte> { [0x15] = 2 })), Is.EqualTo(expected.ToArray()));

        expected = new ByteWriter();
        expected.WriteByte(4);
        expected.WriteInt(1);
        expected.WriteInt(0x16);
        Assert.That(Body(UserEnvPacket.InteractedObjects(new HashSet<int> { 0x16 })), Is.EqualTo(expected.ToArray()));

        expected = new ByteWriter();
        expected.WriteByte(8);
        expected.WriteInt(1);
        expected.WriteInt(0x17);
        expected.WriteInt(0x18);
        Assert.That(Body(UserEnvPacket.GatheringCounts(new Dictionary<int, int> { [0x17] = 0x18 })), Is.EqualTo(expected.ToArray()));

        expected = new ByteWriter();
        expected.WriteByte(9);
        expected.WriteInt(1);
        expected.WriteInt(0x19);
        expected.WriteBool(true);
        Assert.That(Body(UserEnvPacket.LoadClaimedRewards(new Dictionary<int, bool> { [0x19] = true })), Is.EqualTo(expected.ToArray()));

        expected = new ByteWriter();
        expected.WriteByte(10);
        expected.WriteInt(1);
        expected.WriteShort((short) AllianceType.GreenHoods);
        expected.WriteInt(0x1a);
        Assert.That(Body(UserEnvPacket.FamePoints(new Dictionary<AllianceType, int> { [AllianceType.GreenHoods] = 0x1a })),
            Is.EqualTo(expected.ToArray()));

        expected = new ByteWriter();
        expected.WriteByte(11);
        expected.WriteShort((short) AllianceType.GreenHoods);
        expected.WriteInt(0x1b);
        Assert.That(Body(UserEnvPacket.FamePoint(AllianceType.GreenHoods, 0x1b)), Is.EqualTo(expected.ToArray()));

        expected = new ByteWriter();
        expected.WriteByte(12);
        expected.WriteInt(1);
        expected.WriteInt(0x1c);
        expected.WriteLong(0x1d);
        Assert.That(Body(UserEnvPacket.FameLogs(new Dictionary<int, long> { [0x1c] = 0x1d })), Is.EqualTo(expected.ToArray()));

        expected = new ByteWriter();
        expected.WriteByte(13);
        expected.WriteInt(0x1e);
        expected.WriteLong(0x1f);
        Assert.That(Body(UserEnvPacket.FameLog(0x1e, 0x1f)), Is.EqualTo(expected.ToArray()));
    }

    // 0: u8, i32 n, n x (i32 packed position, i64 uid, CubeItemInfo (ugcmapoptions.cpp:182: i32 item, i64 uid,
    // i64, u8 has look), i32 plot, i32, u8, f32 rotation, i32, u8 has notice). The notice flag is always 0: the
    // notice text comes in HomeAction 12 / 0 after the field enter.
    [Test]
    public void LoadCubes() {
        var cube = new PlotCube(TestGameSession.Metadata(50200001), 0x0102030405060708) {
            Position = new Vector3B(1, 2, 3), Type = PlotCube.CubeType.Liftable, PlotId = 0x21, Rotation = 90,
        };
        var load = new ByteWriter();
        load.WriteByte(0);
        load.WriteBool(false);
        load.WriteInt(1);
        load.WriteByte(1);
        load.WriteByte(2);
        load.WriteByte(3);
        load.WriteByte(0);
        load.WriteLong(0x0102030405060708);
        load.WriteInt(50200001);
        load.WriteLong(0x0102030405060708);
        load.WriteLong(0);
        load.WriteBool(false);
        load.WriteInt(0x21);
        load.WriteInt(0);
        load.WriteBool(true);
        load.WriteFloat(90);
        load.WriteInt(0);
        load.WriteBool(false);
        Assert.That(Body(LoadCubesPacket.Load([cube])), Is.EqualTo(load.ToArray()));

        var plot = new Plot(null!) { Number = 0x31, ApartmentNumber = 0, OwnerId = 0x32, Name = "plot", ExpiryTime = 0x33 };

        // 1: n x (i32 plot, u8 taken).
        var state = new ByteWriter();
        state.WriteByte(1);
        state.WriteInt(1);
        state.WriteInt(0x31);
        state.WriteBool(true);
        Assert.That(Body(LoadCubesPacket.PlotState([plot])), Is.EqualTo(state.ToArray()));

        // 2: n x (i32 plot, i32 apartment, unicode name, i64 owner).
        var owners = new ByteWriter();
        owners.WriteByte(2);
        owners.WriteInt(1);
        owners.WriteInt(0x31);
        owners.WriteInt(0);
        owners.WriteUnicodeString("plot");
        owners.WriteLong(0x32);
        Assert.That(Body(LoadCubesPacket.PlotOwners([plot])), Is.EqualTo(owners.ToArray()));

        // 3: n x (i32 plot, i32 apartment, u8 state, i64 expiry).
        var expiry = new ByteWriter();
        expiry.WriteByte(3);
        expiry.WriteInt(1);
        expiry.WriteInt(0x31);
        expiry.WriteInt(0);
        expiry.WriteByte((byte) PlotState.Taken);
        expiry.WriteLong(0x33);
        Assert.That(Body(LoadCubesPacket.PlotExpiry([plot])), Is.EqualTo(expiry.ToArray()));
    }

    // i32 mode (cube count), i32, u8 has info; info: i64 owner, unicode x 2, u8, i32 x 4, u8 x 3, 3 x u8 (bg, light,
    // camera), u8 n, n x (u8 set, u8 value when set == 1); the designs and blueprints (u8 n, n x (i32, unicode,
    // i64)) only when the owner is the client's own account.
    [Test]
    public void LoadUgcMap() {
        var plain = new ByteWriter();
        plain.WriteInt(5);
        plain.WriteInt(0);
        plain.WriteBool(false);
        Assert.That(Body(LoadUgcMapPacket.Load(5)), Is.EqualTo(plain.ToArray()));

        var home = new Home {
            AccountId = 0x41, CurrentArchitectScore = 0x42, ArchitectScore = 0x43, Message = "msg",
            Indoor = new PlotInfo(null!) { Name = "home", PlotMode = PlotMode.Normal },
        };
        home.Layouts.Add(new HomeLayout(0x44, "layout", 4, 5, DateTimeOffset.FromUnixTimeSeconds(0x45), []));

        var visitor = new ByteWriter();
        visitor.WriteInt(5);
        visitor.WriteInt(0);
        visitor.WriteBool(true);
        visitor.WriteLong(0x41);
        visitor.WriteUnicodeString("home");
        visitor.WriteUnicodeString("msg");
        visitor.WriteByte(0);
        visitor.WriteInt(0x42);
        visitor.WriteInt(0x43);
        visitor.WriteInt(0);
        visitor.WriteInt(0);
        visitor.WriteByte((byte) PlotMode.Normal);
        visitor.WriteByte(home.Area);
        visitor.WriteByte(home.Height);
        visitor.WriteByte((byte) home.Background);
        visitor.WriteByte((byte) home.Lighting);
        visitor.WriteByte((byte) home.Camera);
        visitor.WriteByte(9);
        for (int i = 0; i < 9; i++) {
            visitor.WriteBool(false);
        }
        Assert.That(Body(LoadUgcMapPacket.LoadHome(5, home, 0x51)), Is.EqualTo(visitor.ToArray()));

        var owner = new ByteWriter();
        owner.WriteBytes(visitor.ToArray());
        owner.WriteByte(1);
        owner.WriteInt(0x44);
        owner.WriteUnicodeString("layout");
        owner.WriteLong(0x45);
        owner.WriteByte(0);
        Assert.That(Body(LoadUgcMapPacket.LoadHome(5, home, 0x41)), Is.EqualTo(owner.ToArray()));
    }

    // 0: i32 event, i32 param, unicode text, unicode sound, three unicode captions; 1 and 3: nothing.
    [Test]
    public void GlobalPortal() {
        var portal = new GlobalPortalMetadata(0x61, 0, default, default, default, default, default, "popup", "sound",
            [new GlobalPortalMetadata.Field("first", 0, 0), new GlobalPortalMetadata.Field("second", 0, 0)]);
        var announce = new ByteWriter();
        announce.WriteByte(0);
        announce.WriteInt(0x61);
        announce.WriteInt(0x62);
        announce.WriteUnicodeString("popup");
        announce.WriteUnicodeString("sound");
        announce.WriteUnicodeString("first");
        announce.WriteUnicodeString("second");
        announce.WriteUnicodeString("");
        Assert.That(Body(GlobalPortalPacket.Announce(portal, 0x62)), Is.EqualTo(announce.ToArray()));
        Assert.That(Body(GlobalPortalPacket.Close()), Is.EqualTo(new byte[] { 1 }));
        Assert.That(Body(GlobalPortalPacket.CannotEnterDead()), Is.EqualTo(new byte[] { 3 }));
    }

    // 4: u8, unicode, i32; 5: unicode, u8; 6: unicode; 7: unicode, u8.
    [Test]
    public void RecallUser() {
        var invite = new ByteWriter();
        invite.WriteByte(4);
        invite.WriteByte(2);
        invite.WriteUnicodeString("caller");
        invite.WriteInt(0x71);
        Assert.That(Body(RecallUserPacket.Invite("caller", 0x71, 2)), Is.EqualTo(invite.ToArray()));

        var requested = new ByteWriter();
        requested.WriteByte(5);
        requested.WriteUnicodeString("name");
        requested.WriteByte(RecallUserPacket.REQUEST_CONFIRM);
        Assert.That(Body(RecallUserPacket.Requested("name", RecallUserPacket.REQUEST_CONFIRM)), Is.EqualTo(requested.ToArray()));

        var recalled = new ByteWriter();
        recalled.WriteByte(6);
        recalled.WriteUnicodeString("name");
        Assert.That(Body(RecallUserPacket.Recalled("name")), Is.EqualTo(recalled.ToArray()));

        var fail = new ByteWriter();
        fail.WriteByte(7);
        fail.WriteUnicodeString("name");
        fail.WriteByte(RecallUserPacket.FAIL_NOT_FOUND);
        Assert.That(Body(RecallUserPacket.Fail("name", RecallUserPacket.FAIL_NOT_FOUND)), Is.EqualTo(fail.ToArray()));
    }
}
