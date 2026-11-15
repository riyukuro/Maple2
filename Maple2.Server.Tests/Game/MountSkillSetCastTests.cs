using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.File.Ingest.Utils;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// A mount's skill set is held from the get-on (MyPC::vfunc_120, mypc.cpp:3299) to the ride-off (vfunc_121, :3351),
// and JobSystemCli::vfunc_2 passes a skill that needs learning only while a held set has it. A skill only mount sets
// give (RideSkillSets) is cast on such a mount and refused elsewhere. The ids are KR ones: battle mount skill set
// 99910030 (skills 99910031, 99910032) and the sets 99910340 and 99910360 that share skills 99910341-99910344.
public class MountSkillSetCastTests : TriggerTestBase {
    private const int MountSkill = 99910031;
    private const int MountSet = 99910030;

    private void Enter(bool rideOnly = true) {
        Start();
        MapMetadata map = TestGameSession.Map(2000001);
        test.EnterMap(map with {
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true),
        });
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(MountSkill);
        BeginCondition always = skill.Data.Condition with { Probability = 1f, Gender = Gender.All, PvpZone = PvpZoneApply.Everywhere, AllowOnBattleMount = true };
        skill = skill with { Property = skill.Property with { MainType = 2, RideSkillSets = rideOnly ? [MountSet] : null }, Data = skill.Data with { Condition = always } };
        QuestConditionWiringTests.SetSkills(test, skill);
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        test.TakeSent();
    }

    private void Mount(int skillSetId) {
        test.Session.Ride = new RideManager(test.Session);
        var metadata = new RideMetadata(99910030, "", new RideMetadataBasic(RideOnType.Battle, skillSetId, 0, 0, false, 0, 0),
            new RideMetadataSpeed(1, 1, 1, 1), new Dictionary<BasicAttribute, long>());
        test.Session.Ride.Ride = new Ride(test.Session.Player.ObjectId, metadata, new RideOnActionBattle(metadata.Id, 1, 0, 1));
    }

    private void Cast() {
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(1);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(MountSkill);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.Write(test.Session.Player.Position);
            writer.Write(Vector3.UnitY);
            writer.Write(Vector3.Zero);
            writer.WriteFloat(0);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteLong(0);
            writer.WriteLong(0);
        }));
    }

    private bool Ran() => test.Session.Player.ActiveSkills.Get(1) != null;

    [Test]
    public void OnFoot_IsRefused() {
        Enter();
        Cast();
        Assert.That(Ran(), Is.False);
    }

    // The same skill without the ingest's list (metadata from before it) runs on foot: the refusal is the set check's.
    [Test]
    public void OnFoot_WithoutTheList_Runs() {
        Enter(rideOnly: false);
        Cast();
        Assert.That(Ran(), Is.True);
    }

    [Test]
    public void OnTheMountOfItsSet_Runs() {
        Enter();
        Mount(MountSet);
        Cast();
        Assert.That(Ran(), Is.True);
    }

    [Test]
    public void OnAnotherMount_IsRefused() {
        Enter();
        Mount(99910040);
        Cast();
        Assert.That(Ran(), Is.False);
    }

    [Test]
    public void Rule() {
        Assert.Multiple(() => {
            Assert.That(SkillCastRules.RideSetRefuses(false, false, [MountSet], null), Is.True);
            Assert.That(SkillCastRules.RideSetRefuses(false, false, [MountSet], MountSet), Is.False);
            Assert.That(SkillCastRules.RideSetRefuses(false, false, [MountSet], 99910040), Is.True);
            // A later motion (the ride ended during the cast), a tree skill and a skill of no mount set are not refused.
            Assert.That(SkillCastRules.RideSetRefuses(true, false, [MountSet], null), Is.False);
            Assert.That(SkillCastRules.RideSetRefuses(false, true, [MountSet], null), Is.False);
            Assert.That(SkillCastRules.RideSetRefuses(false, false, null, null), Is.False);
            Assert.That(SkillCastRules.RideSetRefuses(false, false, [], null), Is.False);
        });
    }

    // The ingest's list: a skill only mount sets give, with each such set; one another source gives (a job tree, a
    // main type that needs no learning) is not listed; one a mount skill's cast turns into is listed with its set.
    [Test]
    public void RideOnlySkills_ListsOnlyWhatTheMountSetsAloneGive() {
        var mainTypes = new Dictionary<int, int> {
            [99910031] = 2, [99910032] = 2, [99910341] = 2, [99910342] = 2, [10900001] = 2, [99910099] = 2, [99910001] = 1,
        };
        var edges = new Dictionary<int, HashSet<int>> { [99910032] = [99910099] };
        var rideSets = new Dictionary<int, int[]> {
            [99910030] = [99910031, 99910032],
            [99910340] = [99910341, 99910342],
            [99910360] = [99910341, 99910342, 10900001],
            [99910001] = [99910001],
        };
        Dictionary<int, int[]> rideOnly = PlayerSkillSources.RideOnlySkills(mainTypes, edges, [10900001], rideSets);
        Assert.Multiple(() => {
            Assert.That(rideOnly[99910031], Is.EqualTo(new[] { 99910030 }));
            Assert.That(rideOnly[99910099], Is.EqualTo(new[] { 99910030 }));
            Assert.That(rideOnly[99910341], Is.EqualTo(new[] { 99910340, 99910360 }));
            Assert.That(rideOnly.ContainsKey(10900001), Is.False);
            Assert.That(rideOnly.ContainsKey(99910001), Is.False);
        });
    }
}
