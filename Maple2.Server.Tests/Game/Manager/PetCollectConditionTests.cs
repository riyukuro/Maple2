using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The pet album's quest conditions as a summon adds a pet (PetManager), from real KR rows.
public class PetCollectConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    // 61100100: a pet item whose pet id (param1) is its own id.
    private const int PetId = 61100100, OtherPetId = 61100110;

    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.RegisterPet(PetId);
        test.RegisterPet(OtherPetId);
    }

    // The manager of a summon of the pet item of this rarity (the field pet needs data a test session has none of:
    // only its item and its npc's id are set).
    private void Summon(GameSession session, int petId, int rarity) {
        ItemMetadata metadata = TestGameSession.Metadata(petId, type: 11, slotMax: 1);
        var item = new Maple2.Model.Game.Item(metadata with { Property = metadata.Property with { PetId = petId } }, rarity) { Pet = new ItemPet() };
        var fieldPet = (FieldPet) RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), Any)!.SetValue(fieldPet, item);
        var npcMetadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Id>k__BackingField", Any)!.SetValue(npcMetadata, petId);
        var npc = new Npc(npcMetadata, null);
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(fieldPet, npc);
        Assert.That(npc.Id, Is.EqualTo(petId));
        // Not disposed: its dispose saves the pet and takes it off the field, which this test has no row or field for.
        session.Pet = new PetManager(session, fieldPet);
    }

    // 89010192: <condition type="pet_collect" target="1" value="1" />, a pet new to the album of rarity 1 or above
    // counts; 23100377: <condition type="pet_first_collect" code="61100100" />, the album's first pet 61100100. A
    // second summon of the pet, even of a better rarity, adds no pet and counts nothing; another pet counts for
    // pet_collect only.
    [Test]
    public void PetCollect_89010192_23100377_APetNewToTheAlbum() {
        GameSession session = test.Start();
        Quest quest = test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [
                new ConditionMetadata(ConditionType.pet_collect, 5, null, new ConditionMetadata.Parameters(Integers: [1])),
                new ConditionMetadata(ConditionType.pet_first_collect, 1, new ConditionMetadata.Parameters(Integers: [PetId]), null),
            ],
        });

        Summon(session, PetId, 1);
        Assert.That((quest.Conditions[0].Counter, quest.Conditions[1].Counter), Is.EqualTo((1, 1)));
        Summon(session, PetId, 3);
        Assert.That((quest.Conditions[0].Counter, quest.Conditions[1].Counter), Is.EqualTo((1, 1)));
        Summon(session, OtherPetId, 2);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.Player.Value.Unlock.Pets[PetId], Is.EqualTo(3));
        Assert.That((quest.Conditions[0].Counter, quest.Conditions[1].Counter), Is.EqualTo((2, 1)));
    }
}
