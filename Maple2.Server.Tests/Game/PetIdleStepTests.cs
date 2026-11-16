using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// A summoned pet's idle step (FieldPet.NextFollowTask) with its owner at 0 (trace 300, warp 3000): it stays near the
// owner, walks to one past its trace, and is put next to one past its warp with the summon sound (ResponsePet 2) sent to
// the owner. A wild pet stands. A trap that tames (forced, or with the gauge full) catches the pet; one short of a full
// gauge does not. The ids are made up.
public class PetIdleStepTests : NpcCastFixture {
    private FieldPet pet = null!;

    [TearDown]
    public void DisposePet() => pet?.Dispose();

    private FieldPet Pet(bool owned, Vector3 at) {
        StartCaster(["Idle_A"]);
        Assert.That(Field.NpcMetadata.TryGet(CasterId, out NpcMetadata? metadata), Is.True);
        var petMetadata = (PetMetadata) RuntimeHelpers.GetUninitializedObject(typeof(PetMetadata)) with {
            Id = CasterId, NpcId = CasterId, Type = 0, Distance = new PetMetadataDistance(3000, 300, 5000), Effect = [],
        };
        pet = new FieldPet(Field, 950, Field.Navigation.AddAgent(metadata!, at), new Npc(metadata!, null),
            new Item(TestGameSession.Metadata(61100010, type: 11, slotMax: 1)), petMetadata, string.Empty, owned ? test.Session.Player : null) {
            Position = at, Origin = at,
        };
        Field.Pets[pet.ObjectId] = pet;
        test.Session.Player.Position = Vector3.Zero;
        test.TakeSent();
        return pet;
    }

    private bool WarpSoundSent() => test.TakeSent().Any(packet => TestGameSession.Op(packet) == SendOp.ResponsePet && packet[2] == PetPacket.WarpSound(0).ToArray()[2]);

    [Test]
    public void APetNearItsOwner_Stays() {
        Pet(owned: true, new Vector3(200, 0, 0));
        var task = pet.NextFollowTask();
        Assert.Multiple(() => {
            Assert.That(task, Is.Not.Null);
            Assert.That(pet.Position, Is.EqualTo(new Vector3(200, 0, 0)));
            Assert.That(WarpSoundSent(), Is.False);
        });
    }

    [Test]
    public void APetPastItsTrace_WalksToItsOwner() {
        Pet(owned: true, new Vector3(900, 0, 0));
        var task = pet.NextFollowTask();
        Assert.Multiple(() => {
            Assert.That(task, Is.Not.Null);
            Assert.That(pet.Position, Is.EqualTo(new Vector3(900, 0, 0)), "it walks, it is not put there");
            Assert.That(WarpSoundSent(), Is.False);
        });
    }

    [Test]
    public void APetPastItsWarp_IsPutNextToItsOwner() {
        Pet(owned: true, new Vector3(4000, 0, 0));
        pet.NextFollowTask();
        Assert.Multiple(() => {
            Assert.That(Vector2.Distance(new Vector2(pet.Position.X, pet.Position.Y), Vector2.Zero), Is.LessThan(300));
            Assert.That(WarpSoundSent(), Is.True);
        });
    }

    [Test]
    public void AWildPet_Stands() {
        Pet(owned: false, new Vector3(4000, 0, 0));
        Assert.That(pet.NextFollowTask(), Is.Not.Null);
        Assert.That(pet.Position, Is.EqualTo(new Vector3(4000, 0, 0)));
    }

    // A trap (trap level 1) catches a wild pet when it is forced or fills the gauge; a trap short of a full gauge does
    // not. The caught pet dies on the spot.
    [TestCase(true, 1000, true)]
    [TestCase(false, 20000, true)]
    [TestCase(false, 1000, false)]
    public void ATrap_CatchesWhenForcedOrTheGaugeIsFull(bool forced, int points, bool caught) {
        Pet(owned: false, new Vector3(900, 0, 0));
        test.Register(TestGameSession.Metadata(61100010, type: 11, slotMax: 1));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { TamingPetMaxPoint = 10000 });
        test.SetTable(new CommonExpTable(new System.Collections.Generic.Dictionary<ExpType, CommonExpTable.Entry>()));
        test.SetTable(new MasteryRewardTable(new System.Collections.Generic.Dictionary<MasteryType, System.Collections.Generic.IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        var skill = new SkillMetadata(1, 1, null, (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty)), null!, null!);
        var trap = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Pet = new SkillMetadataPet(TamingGroup: 0, TrapLevel: 1, TamingPoint: points, ForcedTaming: forced),
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: false, Value: 0,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
        test.TakeSent();
        pet.ApplyDamage(test.Session.Player, new DamageRecord(skill, trap), trap);
        Assert.Multiple(() => {
            Assert.That(pet.IsDead, Is.EqualTo(caught));
            Assert.That(test.TakeSent().Any(packet => TestGameSession.Op(packet) == SendOp.FieldAddItem), Is.EqualTo(caught), "the caught pet dropped to its catcher");
        });
    }
}
