using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
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

    private FieldPet Pet(bool owned, int type = 1, float battleTrace = 5000) {
        StartCaster(["Idle_A"]);
        Assert.That(Field.NpcMetadata.TryGet(CasterId, out NpcMetadata? metadata), Is.True);
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
}
