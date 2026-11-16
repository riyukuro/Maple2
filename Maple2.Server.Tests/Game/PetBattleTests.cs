using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Game;

// A summoned battle pet (pet data type 1; KR 71 pets, all with battleTraceDistance 5000) fights the monsters near its
// owner with the pet battle AI (ConstantsTable PetBattleAiPath, Pet/AI_DefaultPetBattle.xml) while the owner's pet
// attack flag (RequestPet 15, on at login) is on. The client credits the pet's hits to its owner
// (dpsmetersystem.cpp:1087-1090, battlerecordmanager.cpp:328), and so does the kill here. A wild pet keeps the taming
// AI and its rule: it targets a player once fed. The npc (a monster in Mobs) stands at 1000, the player at 0, the pet
// at 900.
public class PetBattleTests : NpcCastFixture {
    private FieldPet pet = null!;

    [TearDown]
    public void DisposePet() => pet?.Dispose();

    // The sight of every KR pet npc (e.g. 61000002): 3000 around, 300 up, 0 down.
    private static readonly NpcMetadataDistance PetSight = new(100, 3000, 300, 0, 3000, 250, 80);

    // The field's navmesh: the fixture's flat floor (map x and y -2000 to 2000, height 0), and with ledge also a ledge
    // one block up (height 150) over the floor's x 500 to 2000, with no link between the two.
    private bool ledge;

    protected override Navigation BuildNavigation() => ledge ? LedgeNavigation() : base.BuildNavigation();

    private static Navigation LedgeNavigation() {
        const ushort none = 0xffff;
        return TriggerHallGameTests.MeshNavigation(new DtNavMeshCreateParams {
            verts = [0, 0, 0, 0, 0, 400, 400, 0, 400, 400, 0, 0, 250, 15, 0, 250, 15, 400, 400, 15, 400, 400, 15, 0],
            vertCount = 8,
            polys = [0, 1, 2, 3, none, none, none, none, none, none, none, none, 4, 5, 6, 7, none, none, none, none, none, none, none, none],
            polyAreas = [0, 0],
            polyFlags = [1, 1],
            polyCount = 2,
            nvp = 6,
            walkableHeight = 2,
            walkableRadius = 0.5f,
            walkableClimb = 0.5f,
            bmin = new RcVec3f(-20, 0, -20),
            bmax = new RcVec3f(20, 3, 20),
            cs = 0.1f,
            ch = 0.1f,
            buildBvTree = true,
            offMeshConVerts = [],
            offMeshConRad = [],
            offMeshConDir = [],
            offMeshConAreas = [],
            offMeshConFlags = [],
            offMeshConUserID = [],
        });
    }

    private FieldPet Pet(bool owned, int type = 1, float battleTrace = 5000, NpcMetadataDistance? sight = null) {
        StartCaster(["Idle_A", "Run_A"]);
        Assert.That(Field.NpcMetadata.TryGet(CasterId, out NpcMetadata? metadata), Is.True);
        if (sight != null) {
            metadata = metadata! with { Distance = sight };
        }
        var petMetadata = (PetMetadata) RuntimeHelpers.GetUninitializedObject(typeof(PetMetadata)) with {
            Id = CasterId, NpcId = CasterId, Type = type, Distance = new PetMetadataDistance(3000, 300, battleTrace), Effect = [],
        };
        pet = new FieldPet(Field, 950, Field.Navigation.AddAgent(metadata!, new Vector3(900, 0, 0)), new Npc(metadata!, null),
            new Item(TestGameSession.Metadata(61100010, type: 11, slotMax: 1)), petMetadata, string.Empty, owned ? test.Session.Player : null) {
            Position = new Vector3(900, 0, 0), Origin = new Vector3(900, 0, 0),
        };
        Field.Pets[pet.ObjectId] = pet;
        return pet;
    }

    private long searchTick;

    // One battle update, past the last one's 500 ms search interval.
    private IActor? Search() {
        searchTick = searchTick == 0 ? Field.FieldTick : searchTick + 500;
        pet.BattleState.Update(searchTick);
        return pet.BattleState.Target;
    }

    [Test]
    public void ABattlePet_TakesTheMonster_NotThePlayer() {
        Pet(owned: true);
        Assert.That(pet.Battles, Is.True);
        Assert.That(Search(), Is.SameAs(npc));
    }

    [Test]
    public void ABattlePetWithTheAttackFlagOff_TakesNothing_AndLeavesItsFight() {
        Pet(owned: true);
        test.Session.PetAttackOff = true;
        Assert.That(Search(), Is.Null);

        test.Session.PetAttackOff = false;
        Assert.That(Search(), Is.SameAs(npc));
        test.Session.PetAttackOff = true;
        Assert.That(Search(), Is.Null, "the flag turned off: the pet drops its target");
    }

    // The monster stands 1000 from the owner: within a battleTraceDistance of 1500, not of 500.
    [TestCase(1500, true)]
    [TestCase(500, false)]
    public void ABattlePet_TakesOnlyAMonsterWithinItsBattleTraceOfTheOwner(float battleTrace, bool takes) {
        Pet(owned: true, battleTrace: battleTrace);
        Assert.That(Search(), takes ? Is.SameAs(npc) : Is.Null);
    }

    [Test]
    public void ASummonedBagPet_DoesNotFight() {
        Pet(owned: true, type: 0);
        Assert.That(pet.Battles, Is.False);
        Assert.That(Search(), Is.Null);
    }

    // The taming AI's rule stays: a fed wild pet takes the player, and never the monster.
    [Test]
    public void AFedWildPet_StillTakesThePlayer() {
        Pet(owned: false);
        pet.TamingPoint = 100;
        Assert.That(pet.Battles, Is.False);
        Assert.That(Search(), Is.SameAs(test.Session.Player));
    }

    [Test]
    public void TheBattleAi_IsASummonedBattlePets() {
        const string battle = "Pet/AI_DefaultPetBattle.xml";
        Assert.Multiple(() => {
            Assert.That(FieldPet.AiPath(true, 1, battle), Is.EqualTo(battle));
            Assert.That(FieldPet.AiPath(true, 0, battle), Is.EqualTo(Constant.PetFieldAiPath), "a bag pet");
            Assert.That(FieldPet.AiPath(false, 1, battle), Is.EqualTo(Constant.PetFieldAiPath), "a wild pet");
            Assert.That(FieldPet.AiPath(true, 1, string.Empty), Is.EqualTo(Constant.PetFieldAiPath), "no constant");
        });
    }

    // A hostile area skill around both (500 from each) hits the monster for the battle pet; for a wild pet, the player.
    [TestCase(true)]
    [TestCase(false)]
    public void APetsHostileSkill_HitsItsSidesEnemies(bool owned) {
        Pet(owned);
        SkillMetadataAttack attack = Attack("Attack_01_A");
        IActor[] targets = Field.GetTargets(pet, [new Prism(new Circle(new Vector2(500, 0), 600), -100, 300)], attack.Range, 10).ToArray();
        Assert.That(targets, Is.EqualTo(owned ? new IActor[] { npc } : new IActor[] { test.Session.Player }));
    }

    private static SkillMetadataAttack HitAttack() =>
        (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: 10,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null, ConstDamageByTargetHp: 0, ConstDamageByTargetSp: 0),
        };

    private void Hit(IActor caster) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        SkillMetadataAttack attack = HitAttack();
        npc.ApplyDamage(caster, new DamageRecord(new SkillMetadata(61100001, 1, null, property, null!, null!), attack) { SkillId = 61100001 }, attack);
    }

    // The pet's hit counts as its owner's: the last hitter and the kill's players are the owner, once with both
    // hitting. A wild pet's hit credits no one.
    [Test]
    public void APetsHit_IsCreditedToItsOwner() {
        Pet(owned: true);
        Hit(pet);
        Assert.That(npc.GetLastAttacker(), Is.SameAs(test.Session.Player));
        Assert.That(npc.CreditedPlayers(), Is.EqualTo(new[] { test.Session.Player }));

        Hit(test.Session.Player);
        Hit(pet);
        Assert.That(npc.CreditedPlayers(), Is.EqualTo(new[] { test.Session.Player }), "the owner and its pet count once");
    }

    [Test]
    public void AWildPetsHit_IsCreditedToNoOne() {
        Pet(owned: false);
        Hit(pet);
        Assert.That(npc.CreditedPlayer(pet.ObjectId), Is.Null);
        Assert.That(npc.CreditedPlayers(), Is.Empty);
    }

    [Test]
    public void APetsHitWithItsOwnerGone_IsCreditedToNoOne() {
        Pet(owned: true);
        Hit(pet);
        Assert.That(Field.Players.TryRemove(test.Session.Player.ObjectId, out _), Is.True);
        Assert.That(npc.CreditedPlayer(pet.ObjectId), Is.Null);
        Assert.That(npc.CreditedPlayers(), Is.Empty);
        Field.Players[test.Session.Player.ObjectId] = test.Session.Player;
    }

    // The owner is taken at the hit: the pet put away (or summoned again, under a new object id) before the kill
    // still credits its owner, once with the owner's own hits, and its damage adds to the owner's share (the rank
    // node's RankedTarget ranks by it).
    [Test]
    public void APetsHitBeforeItIsPutAway_StillCountsForItsOwner() {
        Pet(owned: true);
        Hit(pet);
        Assert.That(Field.Pets.TryRemove(pet.ObjectId, out _), Is.True);
        Assert.That(npc.CreditedPlayers(), Is.EqualTo(new[] { test.Session.Player }), "the pet's hit alone");
        Hit(test.Session.Player);
        Assert.Multiple(() => {
            Assert.That(npc.CreditedPlayers(), Is.EqualTo(new[] { test.Session.Player }));
            Assert.That(npc.CreditedDamage(), Is.EqualTo(new[] { (test.Session.Player.ObjectId, 20L) }), "10 by the pet, 10 by the owner");
            Assert.That(npc.DamageByDealer.Count(), Is.EqualTo(2));
        });
    }

    // The kill itself (the dealers' part of the death, FieldNpc.HandleDamageDealers): a monster the pet and its owner
    // both hit counts once for the owner's npc kill condition; one only the pet hit counts too.
    [TestCase(true)]
    [TestCase(false)]
    public void AMonsterThePetHit_CountsOnceForTheOwnersKillCondition(bool ownerHitsToo) {
        Pet(owned: true);
        var condition = new ConditionMetadata(ConditionType.npc, 5, new[] { CasterId.ToString() }.ConvertCodes(), System.Array.Empty<string>().ConvertCodes());
        Quest quest = test.StartQuest(TestGameSession.QuestData(91000001) with { Conditions = [condition] });
        Hit(pet);
        if (ownerHitsToo) {
            Hit(test.Session.Player);
        }
        typeof(FieldNpc).GetMethod("HandleDamageDealers", Any)!.Invoke(npc, []);
        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }

    [Test]
    public void ThePetAttackButton_SetsTheFlag() {
        Pet(owned: true);
        var handler = new PetHandler { BanWordStorage = null! };
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(15);
            writer.WriteBool(false);
        }));
        Assert.That(test.Session.PetAttackOff, Is.True);
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(15);
            writer.WriteBool(true);
        }));
        Assert.That(test.Session.PetAttackOff, Is.False);
        Assert.That(test.Errors, Is.Empty);
    }

    // Off the navmesh (map x 5000, beyond the fixture's floor) a pet sees nothing below its own height
    // (sightHeightDown 0). It trails its owner, and on a slope it stands above the monster by its owner: it takes the
    // monster within its height window from its owner's height.
    [TestCase(0f, true)]
    [TestCase(150f, false)]
    public void ABattlePetAboveTheMonsterOffTheNavmesh_TakesItFromItsOwnersHeight(float ownerHeight, bool takes) {
        Pet(owned: true, sight: PetSight);
        npc.Position = new Vector3(6000, 0, 0);
        pet.Position = new Vector3(5900, 0, 150);
        test.Session.Player.Position = new Vector3(5000, 0, ownerHeight);
        Assert.That(Search(), takes ? Is.SameAs(npc) : Is.Null);
    }

    [Test]
    public void AWildPetAboveTheMonster_DoesNotSeeItFromAPlayersHeight() {
        Pet(owned: false, sight: PetSight);
        pet.Position = new Vector3(900, 0, 150);
        Assert.That(pet.BattleState.ShouldTargetActor(npc, 3000 * 3000, 300, 0), Is.False);
    }

    // A field entry's summon comes at the arrival spot, above the ground the client lands the owner on (Ellin Grove's
    // taxi spot 2875: the owner falls 24); the pet goes to the owner's first spot on the ground below it.
    [Test]
    public void AFieldEntrysPet_GoesToItsOwnersFirstSpotOnTheGround() {
        Pet(owned: true);
        var arrival = new Vector3(450, -2850, 2875);
        var ground = arrival with { Z = 2851 };
        test.Session.Player.Position = ground;
        pet.AwaitOwnerLanding(arrival, 0);
        pet.OwnerLanded(arrival, 10);
        pet.Update(Field.FieldTick);
        Assert.That(pet.Position.X, Is.EqualTo(900), "a sync at the arrival spot's height is no landing");

        pet.OwnerLanded(ground, 200);
        pet.Update(Field.FieldTick + 100);
        Assert.That(pet.Position, Is.EqualTo(ground));
        Assert.That(pet.Origin, Is.EqualTo(ground));

        pet.OwnerLanded(ground with { X = 550, Z = 2800 }, 300);
        pet.Update(Field.FieldTick + 200);
        Assert.That(pet.Position, Is.EqualTo(ground), "only the first landing moves the pet");
    }

    [Test]
    public void AFieldEntrysPet_StopsWaitingAfterTheLandingWait() {
        Pet(owned: true);
        pet.AwaitOwnerLanding(new Vector3(450, -2850, 2875), 0);
        pet.OwnerLanded(new Vector3(450, -2850, 2851), FieldPet.LandingWaitMs);
        pet.Update(Field.FieldTick);
        Assert.That(pet.Position.X, Is.EqualTo(900));
    }

    [Test]
    public void APetSummonedByHand_StaysWhenItsOwnerLands() {
        Pet(owned: true);
        pet.OwnerLanded(new Vector3(450, -2850, 2851), 0);
        pet.Update(Field.FieldTick);
        Assert.That(pet.Position.X, Is.EqualTo(900));
    }

    // Off the navmesh, below its owner (another floor) the pet keeps its own window: the owner's would show it
    // monsters out of reach.
    [Test]
    public void ABattlePetBelowItsOwnerOffTheNavmesh_DoesNotSeeFromItsOwnersHeight() {
        Pet(owned: true, sight: PetSight);
        npc.Position = new Vector3(6000, 0, 0);
        pet.Position = new Vector3(5900, 0, -400);
        test.Session.Player.Position = new Vector3(5000, 0, 0);
        Assert.That(Search(), Is.Null);
    }

    // On the navmesh a battle pet takes what it can walk to from its own floor or its owner's: on a ledge with no way
    // down, it takes the monster a block below when its owner stands down there by the monster.
    [Test]
    public void ABattlePetOnALedge_TakesTheMonsterABlockBelowByItsOwner() {
        ledge = true;
        Pet(owned: true, sight: PetSight);
        pet.Position = new Vector3(900, 0, 150);
        test.Session.Player.Position = new Vector3(0, 0, 0);
        Assert.That(Search(), Is.SameAs(npc));
    }

    // With its owner beside it on the ledge, no walk leads down to the monster: the pet does not drop (no height a pet
    // may drop is known), so the monster is out of its reach.
    [Test]
    public void ABattlePetOnALedge_DoesNotTakeAMonsterBelowItCannotWalkTo() {
        ledge = true;
        Pet(owned: true, sight: PetSight);
        pet.Position = new Vector3(900, 0, 150);
        test.Session.Player.Position = new Vector3(600, 0, 150);
        Assert.That(Search(), Is.Null);
    }

    // A monster on a ledge a block up that no floor links to is out of the pet's reach, though its height window
    // (300 up) holds it.
    [Test]
    public void ABattlePetOnTheFloor_DoesNotTakeAMonsterOnALedgeItCannotClimb() {
        ledge = true;
        Pet(owned: true, sight: PetSight);
        pet.Position = new Vector3(300, 0, 0);
        npc.Position = new Vector3(1000, 0, 150);
        Assert.That(Search(), Is.Null);
    }

    // The pet battle AI (Pet/AI_DefaultPetBattle.xml) has no target node: nothing in it changes the target, so a pet
    // keeps the monster it fights while it can still take it, though its owner hits another one in its sight.
    [Test]
    public void ABattlePet_KeepsItsFight_WhenItsOwnerHitsAnotherMonster() {
        Pet(owned: true, sight: PetSight);
        Assert.That(Field.NpcMetadata.TryGet(CasterId, out NpcMetadata? metadata), Is.True);
        FieldNpc other = Field.SpawnNpc(metadata!, new Vector3(1800, 0, 0), Vector3.Zero)!;
        pet.BattleState.ForceTarget(npc);
        other.RecordDamage(test.Session.Player, 10);
        Assert.That(Search(), Is.SameAs(npc));
    }
}
