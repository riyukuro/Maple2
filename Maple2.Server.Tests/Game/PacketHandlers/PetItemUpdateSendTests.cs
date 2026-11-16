using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ResponsePet 21 (the summoned pet's item again, CPetManager::FUN_140c8afa0) after an evolve, a rename, a kept option
// re-roll and a kill's level up of the summoned pet; a pet in the bag gets none (the reader takes no uid and reads
// into the summoned pet's copy). The stone and kit level ups are in AddPetExpTests and PetTrainingTests.
public class PetItemUpdateSendTests {
    private const int PetId = 60000001;
    private const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    private TestGameSession test = null!;
    private Item pet = null!;
    private Item bagPet = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.RegisterPet(PetId);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { PetMaxLevel = 50 });
        test.SetTable(new PetEvolutionTable(new Dictionary<int, PetEvolutionTable.Entry> { [0] = new(0, [10, 10, 10, 10]) }));
        // 1000 exp a level for every rarity.
        test.SetTable(new PetExpTable(Enumerable.Range(1, 50).ToDictionary(level => level, _ => Enumerable.Repeat(1000L, PetExpTable.Columns).ToArray())));
        ItemMetadata metadata = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
        metadata = metadata with { Property = metadata.Property with { PetId = PetId } };
        long petUid = test.Store(metadata).Uid;
        long bagUid = test.Store(metadata).Uid;
        GameSession session = test.Start();
        bagPet = session.Item.Inventory.Get(bagUid)!;
        // The summoned pet is out of the bag (as AddPetExpTests: the field pet needs data a test session has none of).
        Assert.That(session.Item.Inventory.Remove(petUid, out Item? taken), Is.True);
        pet = taken!;
        var fieldPet = (FieldPet) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), Any)!.SetValue(fieldPet, pet);
        Assert.That(session.ItemMetadata.TryGetPet(PetId, out PetMetadata? petMetadata), Is.True);
        typeof(FieldPet).GetField(nameof(FieldPet.Metadata), Any)!.SetValue(fieldPet, petMetadata);
        var manager = (PetManager) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
        typeof(PetManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(PetManager).GetField("pet", Any)!.SetValue(manager, fieldPet);
        session.Pet = manager;
        test.TakeSent();
        return session;
    }

    private string Update(GameSession session) => Convert.ToHexString(PetPacket.ItemUpdate(session.Player.ObjectId, pet).ToArray());

    // The mode-21 packets sent, as hex.
    private List<string> Updates() => test.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == SendOp.ResponsePet && packet[2] == 21)
        .Select(Convert.ToHexString).ToList();

    private static void Evolve(GameSession session, long uid) => new PetHandler { BanWordStorage = null! }.Handle(session,
        TestGameSession.Packet(writer => {
            writer.WriteByte(17);
            writer.WriteLong(uid);
        }));

    [Test]
    public void Evolve_TheSummonedPet_SendsItsItem() {
        GameSession session = Start();
        pet.Pet!.Level = 50;
        pet.Pet.EvolvePoints = 10;
        int rarity = pet.Rarity;

        Evolve(session, pet.Uid);

        Assert.That(pet.Rarity, Is.EqualTo(rarity + 1));
        Assert.That(Updates(), Is.EqualTo(new[] { Update(session) }));
    }

    [Test]
    public void Evolve_APetInTheBag_SendsNone() {
        GameSession session = Start();
        bagPet.Pet!.Level = 50;
        bagPet.Pet.EvolvePoints = 10;
        int rarity = bagPet.Rarity;

        Evolve(session, bagPet.Uid);

        Assert.That(bagPet.Rarity, Is.EqualTo(rarity + 1));
        Assert.That(Updates(), Is.Empty);
    }

    [Test]
    public void Rename_SendsTheItemWithTheNewName() {
        GameSession session = Start();
        pet.Pet!.RenameRemaining = 1;

        session.Pet!.Rename("Mochi");

        Assert.That(pet.Pet.Name, Is.EqualTo("Mochi"));
        Assert.That(Updates(), Is.EqualTo(new[] { Update(session) }));
    }

    // A paid rename (no free rename left) stores the merets and the new name together at once. Before, both waited for
    // the session's next save.
    [Test]
    public void APaidRename_IsStoredAtOnce() {
        GameSession session = Start();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { PetChangeNameMerat = 100 });
        session.Currency.Meret = 150;
        pet.Pet!.RenameRemaining = 0;

        session.Pet!.Rename("Mochi");

        Assert.That((pet.Pet.Name, session.Currency.Meret), Is.EqualTo(("Mochi", 50L)));
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        Assert.That(context.Account.Single(row => row.Id == test.AccountId).Currency.Meret, Is.EqualTo(50));
        using Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetItem(pet.Uid)!.Pet!.Name, Is.EqualTo("Mochi"));
    }

    // A paid rename whose store fails changes nothing: the merets are given back and the pet keeps its name, in memory
    // and in its row.
    [Test]
    public void APaidRename_NotStored_KeepsMeretsAndName() {
        GameSession session = Start();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { PetChangeNameMerat = 100 });
        session.Currency.Meret = 150;
        pet.Pet!.RenameRemaining = 0;
        string name = pet.Pet.Name;
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Item>().Any();

        session.Pet!.Rename("Mochi");
        test.FailSave = null;

        Assert.That((pet.Pet.Name, pet.Pet.RenameRemaining, session.Currency.Meret), Is.EqualTo((name, 0, 150L)));
        using Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetItem(pet.Uid)!.Pet!.Name, Is.EqualTo(name));
    }

    // The pet window's rename box (PetInfoBasic::SendRequestPet, tabcontentspetinfo.cpp:652-664) sends RequestPet 4 with
    // the pet identity (Unk_PetIdentity::FUN_1401fee50): the typed name, then an id, a value and three shorts, all left
    // at 0. The server takes the name for the summoned pet.
    private void RequestRename(GameSession session, string name) {
        BanWordStorage words = BanWordStorage.WithDynamicWords(
            new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options), () => ["badword"]);
        new PetHandler { BanWordStorage = words }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteUnicodeString(name);
            writer.WriteLong(0);
            writer.WriteInt(0);
            writer.WriteShort(0);
            writer.WriteShort(0);
            writer.WriteShort(0);
        }));
    }

    [Test]
    public void RequestRename_NamesTheSummonedPet() {
        GameSession session = Start();
        pet.Pet!.RenameRemaining = 1;

        RequestRename(session, "Mochi");

        Assert.That((pet.Pet.Name, pet.Pet.RenameRemaining), Is.EqualTo(("Mochi", (short) 0)));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(Convert.ToHexString(PetPacket.Rename(session.Player.ObjectId, pet.Pet).ToArray())));
    }

    // A name the server's ban-word list holds gets the filter's message box; a blank name, or a rename with no pet out,
    // changes nothing and sends nothing.
    [Test]
    public void RequestRename_BannedName_GetsTheFilterBox() {
        GameSession session = Start();
        pet.Pet!.RenameRemaining = 1;
        string name = pet.Pet.Name;

        RequestRename(session, "a badword b");

        Assert.That((pet.Pet.Name, pet.Pet.RenameRemaining), Is.EqualTo((name, (short) 1)));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(NoticePacket.MessageBox(StringCode.s_ban_check_err_any).ToArray()) }));
    }

    [TestCase("   ", true)]
    [TestCase("Mochi", false)]
    public void RequestRename_BlankOrWithNoPetOut_ChangesNothing(string newName, bool summoned) {
        GameSession session = Start();
        pet.Pet!.RenameRemaining = 1;
        string name = pet.Pet.Name;
        if (!summoned) {
            session.Pet = null;
        }

        RequestRename(session, newName);

        Assert.That((pet.Pet.Name, pet.Pet.RenameRemaining), Is.EqualTo((name, (short) 1)));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The re-roll keeps (ChangeAttributes 2 and the scroll's Select) send it for the summoned pet only.
    [Test]
    public void OptionKeep_OnlyTheSummonedPet() {
        GameSession session = Start();

        PetManager.SendItemUpdate(session, bagPet);
        Assert.That(Updates(), Is.Empty);
        PetManager.SendItemUpdate(session, pet);
        Assert.That(Updates(), Is.EqualTo(new[] { Update(session) }));
    }

    // A kill's exp sends it on a level up only.
    [Test]
    public void KillExp_OnALevelUp() {
        GameSession session = Start();
        pet.Pet!.Exp = 990;

        session.Pet!.AddKillExp(10, 0); // 10 * 12 / 100 = 1
        Assert.That(pet.Pet.Level, Is.EqualTo(1), "a kill's exp below the level");
        Assert.That(Updates(), Is.Empty);

        pet.Pet.Exp = 999;
        session.Pet.AddKillExp(100, 3);
        Assert.That(pet.Pet.Level, Is.EqualTo(2));
        Assert.That(Updates(), Is.EqualTo(new[] { Update(session) }));
    }
}
