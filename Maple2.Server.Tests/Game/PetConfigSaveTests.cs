using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The save before a move with a summoned pet whose config is stored (GameStorage.SavePetConfig). The stored row was
// tracked by Find and a second instance with the same ItemUid given to Update threw: from the pet's second save on,
// every save of the session failed, a move's save after its second try too, so a channel change (or any move that
// migrates) was refused and the session ended. Each move here goes through MigrationSave's SaveRows, whose tries each
// open their own context.
public class PetConfigSaveTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int PetId = 60000001;
    private const int Target = 2000062;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private PetManager manager = null!;
    private Item pet = null!;

    private static readonly PetLootConfig Changed = new(false, true, false, true, false, true, false, true, 3, false);

    // A live session (its saves run) with the pet summoned; `stored`: its config row was written before this session.
    private void Setup(bool stored) {
        test = new TestGameSession();
        ItemMetadata petData = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
        petData = petData with { Property = petData.Property with { PetId = PetId } };
        // The in-memory store numbers items and characters alike: a filler keeps the pet's uid off the character id.
        test.Store(TestGameSession.Metadata(2000002));
        pet = test.Store(petData);
        if (stored) {
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.SavePetConfig(pet.Uid, new PetConfig()), Is.True);
        }
        test.Start();
        test.SavingConfig(session);
        typeof(GameSession).GetField("preMigrationSaved", Any)!.SetValue(session, false);
        session.State = SessionState.Connected;

        var fieldPet = (FieldPet) RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), Any)!.SetValue(fieldPet, session.Item.Inventory.Get(pet.Uid)!);
        manager = (PetManager) RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
        typeof(PetManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(PetManager).GetField("pet", Any)!.SetValue(manager, fieldPet);
        typeof(PetManager).GetField("items", Any)!.SetValue(manager, new ItemCollection(6));
        PetConfig config;
        using (GameStorage.Request db = test.Storage.Context()) {
            config = db.GetPetConfig(pet.Uid);
        }
        typeof(PetManager).GetField("config", Any)!.SetValue(manager, config);
        session.Pet = manager;
        test.TakeSent();
    }

    // The loot config a later load finds.
    private PetLootConfig StoredLoot() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetPetConfig(pet.Uid).LootConfig;
    }

    private void ChangeConfig(PetLootConfig loot) {
        manager.UpdateLootConfig(loot);
        test.TakeSent();
    }

    private void ChangeChannel() {
        new ChannelHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => writer.WriteShort(1)));
    }

    private void AssertMoved(int moves = 1) {
        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Has.Count.EqualTo(moves));
            Assert.That(session.MigrationSaved, Is.True);
            Assert.That(StoredLoot(), Is.EqualTo(Changed));
        });
    }

    // r108r: a channel change with the pet summoned and its config changed, the config row stored before.
    [Test]
    public void ChannelChange_PetConfigChanged_RowStored_Moves() {
        Setup(stored: true);
        ChangeConfig(Changed);

        ChangeChannel();

        AssertMoved();
    }

    // The row written by an earlier save of the same session (its first write adds it; the later ones failed).
    [Test]
    public void ChannelChange_PetConfigChanged_AfterAnEarlierSave_Moves() {
        Setup(stored: false);
        ChangeConfig(new PetLootConfig(true, false, true, false, true, false, true, false, 2, true));
        Assert.That(session.SessionSave(), Is.True, "the first save adds the row");
        ChangeConfig(Changed);

        ChangeChannel();

        AssertMoved();
    }

    // A dungeon's move (Migrate), with the pet's row stored.
    [Test]
    public void Migrate_PetConfigChanged_RowStored_Moves() {
        Setup(stored: true);
        ChangeConfig(Changed);

        session.Migrate(Target);

        AssertMoved();
    }

    // The second try of the move's save, after a first one that failed: it starts from its own context, so the row
    // the failed try tracked does not stand in its way.
    [Test]
    public void MigrationSave_FirstTryFails_SecondTryStoresThePetConfig() {
        Setup(stored: true);
        ChangeConfig(Changed);
        int failed = 0;
        test.FailSave = _ => failed++ < 1;

        Assert.That(session.MigrationSave(), Is.True);
        Assert.Multiple(() => {
            Assert.That(failed, Is.GreaterThan(1), "the first try failed and a second one ran");
            Assert.That(session.MigrationSaved, Is.True);
            Assert.That(StoredLoot(), Is.EqualTo(Changed));
        });
    }

    // The timed and session saves too: before, each one after the first failed while the pet was out.
    [Test]
    public void SessionSave_Twice_PetConfigChanged_Stores() {
        Setup(stored: false);
        Assert.That(session.SessionSave(), Is.True);
        ChangeConfig(Changed);

        Assert.That(session.SessionSave(), Is.True);
        Assert.That(StoredLoot(), Is.EqualTo(Changed));
    }
}
