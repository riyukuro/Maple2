using System;
using System.Linq;
using System.Threading.Tasks;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The pet fusion stones (item function AddPetEXP, 30000915 "3000"): the client sends a plain item use with an empty
// text. The server gives the stone's exp to the summoned pet as a fusion does (ResponsePet 10, and 11 on a level up).
public class AddPetExpTests {
    private const int PetId = 60000001, StoneId = 30000915, MaxLevel = 50;

    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item stone = null!;
    private Item pet = null!;

    private GameSession Start(string parameters = "3000", int petType = 1, bool summon = true) {
        test = new TestGameSession();
        test.RegisterPet(PetId, petType);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { PetMaxLevel = MaxLevel });
        // 1000 exp a level for every rarity.
        test.SetTable(new PetExpTable(Enumerable.Range(1, 50).ToDictionary(level => level, _ => Enumerable.Repeat(1000L, PetExpTable.Columns).ToArray())));
        ItemMetadata petItem = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
        long petUid = test.Store(petItem with { Property = petItem.Property with { PetId = PetId } }).Uid;
        stone = test.Store(TestGameSession.Metadata(StoneId,
            function: new ItemMetadataFunction(ItemFunction.AddPetEXP, "AddPetEXP", parameters, false), type: 15), amount: 2);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        pet = session.Item.Inventory.Get(petUid)!;
        if (summon) {
            Summon(session);
        }
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteLong(stone.Uid);
        writer.WriteUnicodeString("");
    }));

    private int Stones(GameSession session) => session.Item.Inventory.Get(stone.Uid)?.Amount ?? 0;

    // A pet manager for the pet taken out of the bag (as PetTrainingTests: the field pet needs data a test session
    // has none of).
    private void Summon(GameSession session) {
        Assert.That(session.Item.Inventory.Remove(pet.Uid, out Item? taken), Is.True);
        pet = taken!;
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var fieldPet = (FieldPet) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), any)!.SetValue(fieldPet, pet);
        var manager = (PetManager) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
        typeof(PetManager).GetField("session", any)!.SetValue(manager, session);
        typeof(PetManager).GetField("pet", any)!.SetValue(manager, fieldPet);
        session.Pet = manager;
    }

    [Test]
    public void Use_GivesTheSummonedPetTheExp() {
        GameSession session = Start();
        pet.Pet!.Level = 3;
        pet.Pet.Exp = 700;

        Use(session);

        // 700 + 3000 = 3700: three levels (1000 each), 700 left.
        Assert.That((pet.Pet.Level, pet.Pet.Exp), Is.EqualTo(((short) 6, 700L)));
        Assert.That(Stones(session), Is.EqualTo(1));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ResponsePet).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Convert.ToHexString(PetPacket.Fusion(session.Player.ObjectId, pet).ToArray()),
            Convert.ToHexString(PetPacket.LevelUp(session.Player.ObjectId, pet).ToArray()),
        }));
    }

    [Test]
    public void Use_WithoutLevelUp() {
        GameSession session = Start("300");

        Use(session);

        Assert.That((pet.Pet!.Level, pet.Pet.Exp), Is.EqualTo(((short) 1, 300L)));
        Assert.That(test.TakeSentOps().Count(op => op == SendOp.ResponsePet), Is.EqualTo(1));
    }

    // No summoned pet, a summoned pet at the top level or of type 0, or a stone without exp: the stone stays.
    [TestCase("none")]
    [TestCase("max")]
    [TestCase("type")]
    [TestCase("stone")]
    public void Use_Refused(string why) {
        GameSession session = Start(why == "stone" ? "" : "3000", petType: why == "type" ? 0 : 1, summon: why != "none");
        if (why == "max") {
            pet.Pet!.Level = MaxLevel;
        }
        short level = pet.Pet!.Level;

        Use(session);

        Assert.That(Stones(session), Is.EqualTo(2));
        Assert.That((pet.Pet.Level, pet.Pet.Exp), Is.EqualTo((level, 0L)));
        Assert.That(test.TakeSentOps(), Does.Not.Contain(SendOp.ResponsePet));
    }

    // The client's UseItem (@0x140d1bd60) does not ask the item lock: a locked item is used as any other.
    // CItemLockManager::CanUseItem guards only the trade, the black market, the shop sell, the drop/destroy and the
    // dismantle.
    [Test]
    public void Use_LockedStone_IsUsed() {
        GameSession session = Start();
        stone.IsLocked = true;
        stone.UnlockTime = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();

        Assert.That(ItemUseHandler.UseAllowed(session, stone), Is.True);
        Use(session);

        Assert.That(Stones(session), Is.EqualTo(1));
        Assert.That((pet.Pet!.Level, pet.Pet.Exp), Is.EqualTo(((short) 4, 0L)));
    }

    // A kill's exp runs on the field thread: it waits for the item lock a stone use, fusion or training holds.
    [Test]
    public void KillExp_WaitsForTheItemLock() {
        GameSession session = Start();
        Assert.That(session.ItemMetadata.TryGetPet(PetId, out PetMetadata? petMetadata), Is.True);
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        object fieldPet = typeof(PetManager).GetField("pet", any)!.GetValue(session.Pet)!;
        typeof(FieldPet).GetField(nameof(FieldPet.Metadata), any)!.SetValue(fieldPet, petMetadata);

        Task kill;
        lock (session.Item) {
            kill = Task.Run(() => session.Pet!.AddKillExp(100, 3));
            Assert.That(kill.Wait(300), Is.False);
            Assert.That(pet.Pet!.Exp, Is.EqualTo(0));
        }
        Assert.That(kill.Wait(TimeSpan.FromSeconds(10)), Is.True);

        // 100 * 48 / 100 = 48.
        Assert.That(pet.Pet.Exp, Is.EqualTo(PetFusionRules.KillExp(100, 3)).And.EqualTo(48));
    }
}
