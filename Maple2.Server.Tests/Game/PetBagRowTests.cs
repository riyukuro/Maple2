using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The rows of a pet bag move (RequestPetInventory 0 and 1): what the database holds after a deposit or a withdraw,
// both once the session and the unsummon have saved and at once (a stop before the unsummon). A part deposited was
// left at owner 0 until the unsummon, so a stop before it lost that part.
public class PetBagRowTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int PetId = 60000001;
    private const int Potion = 2000001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private PetManager manager = null!;
    private ItemCollection bag = null!;
    private Item pet = null!;
    private string dump = "";

    private void Setup(int bagAmount, int heldAmount, out Item held, out Item? inBag) {
        test = new TestGameSession();
        ItemMetadata petData = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
        petData = petData with { Property = petData.Property with { PetId = PetId } };
        // The in-memory store numbers items and characters alike: a filler keeps the pet's uid off the character id.
        test.Store(TestGameSession.Metadata(2000002));
        pet = test.Store(petData);
        ItemMetadata potion = TestGameSession.Metadata(Potion);
        held = test.Store(potion, heldAmount);
        inBag = null;
        if (bagAmount > 0) {
            using GameStorage.Request db = test.Storage.Context();
            inBag = db.CreateItem(pet.Uid, new Item(potion, 1, bagAmount))!;
        }
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        var fieldPet = (FieldPet) RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), Any)!.SetValue(fieldPet, session.Item.Inventory.Get(pet.Uid)!);
        manager = (PetManager) RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
        bag = new ItemCollection(6);
        if (inBag != null) {
            inBag.Slot = 0;
            bag.Add(inBag);
        }
        typeof(PetManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(PetManager).GetField("pet", Any)!.SetValue(manager, fieldPet);
        typeof(PetManager).GetField("items", Any)!.SetValue(manager, bag);
        typeof(PetManager).GetField("config", Any)!.SetValue(manager, new PetConfig());
        session.Pet = manager;
        test.TakeSent();
    }

    // What a later load finds: the rows of the character's bag and the pet's bag (the session's and the unsummon's saves).
    private (int Held, int InBag) SavedAndLoaded() {
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Item.Inventory.Save(db);
            Assert.That(db.SaveItems(pet.Uid, bag.ToArray()), Is.True);
        }
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        int Sum(long owner) => context.Item.Where(row => row.OwnerId == owner && row.ItemId == Potion).Sum(row => row.Amount);
        dump = (string.Join(" ", context.Item.Where(row => row.ItemId == Potion).Select(row => $"{row.Id}:{row.OwnerId}:{row.Amount}")) + $" char={test.CharacterId} pet={pet.Uid}");
        return (Sum(test.CharacterId), Sum(pet.Uid));
    }

    [Test]
    public void ADepositMergedIntoABagStack_IsNotHeldTwice() {
        Setup(5, 3, out Item held, out _);
        manager.Add(held.Uid, 1, 3);
        Assert.That(SavedAndLoaded(), Is.EqualTo((0, 8)), () => dump);
    }

    [Test]
    public void AWithdrawMergedIntoAHeldStack_IsNotHeldTwice() {
        Setup(5, 3, out _, out Item? inBag);
        manager.Remove(inBag!.Uid, -1, 5);
        Assert.That(SavedAndLoaded(), Is.EqualTo((8, 0)), () => dump);
    }

    // Only the session's own writes, as at a stop before the unsummon saves the pet's bag.
    private (int Held, int InBag) StoredNow() {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        int Sum(long owner) => context.Item.Where(row => row.OwnerId == owner && row.ItemId == Potion).Sum(row => row.Amount);
        dump = string.Join(" ", context.Item.Where(row => row.ItemId == Potion).Select(row => $"{row.Id}:{row.OwnerId}:{row.Amount}"));
        return (Sum(test.CharacterId), Sum(pet.Uid));
    }

    [Test]
    public void APartWithdrawn_IsStoredOutOfTheBagAtOnce() {
        Setup(5, 0, out _, out Item? inBag);
        manager.Remove(inBag!.Uid, -1, 2);
        Assert.That(StoredNow(), Is.EqualTo((2, 3)), () => dump);
    }

    [Test]
    public void AWholeStackWithdrawn_IsStoredOutOfTheBagAtOnce() {
        Setup(5, 3, out _, out Item? inBag);
        manager.Remove(inBag!.Uid, -1, 5);
        Assert.That(StoredNow(), Is.EqualTo((8, 0)), () => dump);
    }

    [Test]
    public void APartDeposited_IsStoredInTheBagAtOnce() {
        Setup(0, 5, out Item held, out _);
        manager.Add(held.Uid, 0, 2);
        Assert.That(StoredNow(), Is.EqualTo((3, 2)), () => dump);
    }
}
