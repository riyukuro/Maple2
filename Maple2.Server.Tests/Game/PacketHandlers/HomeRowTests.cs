using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The home's permission rows where the client asks them (WorldService::FUN_141bdb660): a ride start or change
// (row 5, WorldService::FUN_141bcb210), the in-place revive (row 8, UIRevivalDialog::FUN_141abcb90 hides its
// button) and the skill rows (1 climb, 2 skill, 4 potion, 6 glide; CSkillSystem::FUN_14176a8c0). A disabled row
// stops no one, setting 0 everyone, 1 everyone but the owner, 2 everyone but the owner and the owner's party.
public class HomeRowTests {
    private TestGameSession test = null!;

    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
    }

    // The session stands in a home of its own (owner) or of another account, whose row is set (or left disabled).
    private void InAHome(bool owner, HomePermission row, HomePermissionSetting? setting) {
        session.Housing.InitNewHome("home", null);
        test.EnterHome();
        // A home lets one ride and summon pets.
        typeof(FieldManager).GetField(nameof(FieldManager.Metadata))!.SetValue(session.Field, session.Field!.Metadata with {
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true),
        });
        var home = new Home { AccountId = owner ? session.AccountId : session.AccountId + 1 };
        if (setting != null) {
            home.Permissions[row] = setting.Value;
        }
        typeof(HomeFieldManager).GetField("<Home>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Field, home);
        test.TakeSent();
    }

    [TestCase(HomePermission.GroundMount)]
    [TestCase(HomePermission.Pvp)]
    [TestCase(HomePermission.Revival)]
    [TestCase(HomePermission.Skill)]
    public void ARow_BlocksAsTheClientsCheck(HomePermission row) {
        InAHome(owner: false, row, null);
        Assert.That(HomeRowCheck.Blocks(session, row), Is.False);
        InAHome(owner: false, row, HomePermissionSetting.Self);
        Assert.That(HomeRowCheck.Blocks(session, row), Is.True);
        InAHome(owner: true, row, HomePermissionSetting.Self);
        Assert.That(HomeRowCheck.Blocks(session, row), Is.False);
        InAHome(owner: true, row, HomePermissionSetting.None);
        Assert.That(HomeRowCheck.Blocks(session, row), Is.True);
        // Another row does not count.
        InAHome(owner: false, HomePermission.Music, HomePermissionSetting.None);
        Assert.That(HomeRowCheck.Blocks(session, row), Is.False);
    }

    [Test]
    public void OutsideAHome_NoRowBlocks() {
        test.EnterMap(TestGameSession.Map(2000001));
        Assert.That(HomeRowCheck.Blocks(session, HomePermission.GroundMount), Is.False);
    }

    private static readonly byte[] CantRide = NoticePacket.MessageBox(StringCode.s_action_cant_ride).Buffer[..NoticePacket.MessageBox(StringCode.s_action_cant_ride).Length];

    private List<byte[]> Sent() => test.TakeSent();

    // RequestRide 0 (start) with a mount the inventory does not hold: the row's refusal is the client's own
    // s_action_cant_ride; past the row the missing item is what refuses.
    [TestCase(false, null, false)]
    [TestCase(false, HomePermissionSetting.Self, true)]
    [TestCase(true, HomePermissionSetting.Self, false)]
    [TestCase(true, HomePermissionSetting.None, true)]
    public void RideStart_FollowsTheGroundMountRow(bool owner, HomePermissionSetting? setting, bool refused) {
        InAHome(owner, HomePermission.GroundMount, setting);
        session.Ride = new Maple2.Server.Game.Manager.RideManager(session);

        new RideHandler { RideMetadata = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteByte((byte) RideOnType.Default);
            writer.WriteInt(1);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteInt(0);
            writer.WriteLong(12345);
        }));

        List<byte[]> sent = Sent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0].SequenceEqual(CantRide), Is.EqualTo(refused));
    }

    // RequestRide 2 (change) while riding nothing is refused alike; the row is asked first, so a blocked visitor
    // and a rider without a ride both get s_action_cant_ride. Riding, only the row refuses before the item.
    [Test]
    public void RideChange_RefusedByTheGroundMountRow() {
        InAHome(owner: false, HomePermission.GroundMount, HomePermissionSetting.Self);
        session.Ride = new Maple2.Server.Game.Manager.RideManager(session);
        var duck = new Maple2.Model.Metadata.RideMetadata(21000021, "",
            new Maple2.Model.Metadata.RideMetadataBasic(RideOnType.Default, 0, 0, 0, false, 0, 0),
            new Maple2.Model.Metadata.RideMetadataSpeed(0, 0, 0, 0), new Dictionary<BasicAttribute, long>());
        Item item = test.Store(TestGameSession.Metadata(50600001));
        session.Ride.Ride = new Ride(session.Player.ObjectId, duck,
            new Maple2.Model.Game.RideOnActionDefault(duck.Id, 1, item));
        void Change() => new RideHandler { RideMetadata = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(1);
            writer.WriteLong(12345);
        }));

        Change();
        Assert.That(Sent().Single().SequenceEqual(CantRide), Is.True);

        InAHome(owner: true, HomePermission.GroundMount, HomePermissionSetting.Self);
        Change();
        Assert.That(Sent().Single().SequenceEqual(CantRide), Is.False);
    }

    // RequestPet 0 (summon): the client asks the pvp row first (MyPC::FUN_14152aa90,
    // s_ugcmap_fun_pvp_ffa_forbiden_summon_pet). A test field has no spawn point, so an allowed summon brings
    // no pet either; the row's part is in ARow_BlocksAsTheClientsCheck.
    [Test]
    public void PetSummon_RefusedByThePvpRow() {
        Maple2.Model.Metadata.ItemMetadata petData = TestGameSession.Metadata(60000001, type: 11, slotMax: 1);
        petData = petData with { Property = petData.Property with { PetId = 60000001 } };
        Item pet = test.Store(petData);
        session.Player.Value.Character.Level = 10;
        InAHome(owner: false, HomePermission.Pvp, HomePermissionSetting.Self);

        new PetHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(pet.Uid);
        }));

        Assert.Multiple(() => {
            Assert.That(session.Pet, Is.Null);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    // Revival 2 (meso or voucher in place) and 1 (merets or coupon in place) come only from the dialog's in-place
    // button, which the row hides; the handler stops at the row before it reads any table.
    [TestCase((byte) 1)]
    [TestCase((byte) 2)]
    public void InPlaceRevive_RefusedByTheRevivalRow(byte mode) {
        InAHome(owner: false, HomePermission.Revival, HomePermissionSetting.Self);
        var handler = new RevivalHandler { ServerTableMetadata = null! };

        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            writer.WriteBool(false);
        })));
        Assert.That(Sent(), Is.Empty);
    }

    // The skill rows by the skill's main type (SkillData::FUN_1407e9bd0): 5 potion, 7 glide, 13 climb; the types
    // FUN_1407e9f30 leaves out are free; every other type is the skill row. A GM of level 50 glides anyway.
    [Test]
    public void SkillRows() {
        Assert.Multiple(() => {
            Assert.That(HomePermissionRules.SkillRow(5), Is.EqualTo(HomePermission.Potion));
            Assert.That(HomePermissionRules.SkillRow(7), Is.EqualTo(HomePermission.AirMount));
            Assert.That(HomePermissionRules.SkillRow(13), Is.EqualTo(HomePermission.Climb));
            Assert.That(HomePermissionRules.SkillRow(0), Is.EqualTo(HomePermission.Skill));
            Assert.That(HomePermissionRules.SkillRow(2), Is.EqualTo(HomePermission.Skill));
            foreach (int free in new[] { 1, 3, 4, 10, 11, 14, 15, 17, 18, 19 }) {
                Assert.That(HomePermissionRules.SkillRow(free), Is.Null, $"main type {free}");
            }
            Assert.That(HomePermissionRules.SkillRowApplies(HomePermission.AirMount, 49), Is.True);
            Assert.That(HomePermissionRules.SkillRowApplies(HomePermission.AirMount, 50), Is.False);
            Assert.That(HomePermissionRules.SkillRowApplies(HomePermission.Skill, 50), Is.True);
        });
    }
}
