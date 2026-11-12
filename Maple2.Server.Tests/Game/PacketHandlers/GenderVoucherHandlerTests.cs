using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The gender voucher through ItemUseHandler.Handle, on a session with real managers over an in-memory storage.
public class GenderVoucherHandlerTests {
    private const int VoucherId = 20300001;
    private static readonly ItemMetadata Voucher = TestGameSession.Metadata(VoucherId,
        function: new ItemMetadataFunction(ItemFunction.ChangeGender, "ChangeGender", "", false), type: 2);

    private static readonly ItemMetadata MaleHair = TestGameSession.Metadata(10200001, EquipSlot.HR, Gender.Male, type: 1);
    private static readonly ItemMetadata FemaleHair = TestGameSession.Metadata(10200006, EquipSlot.HR, Gender.Female, type: 1);
    private static readonly ItemMetadata MaleFace = TestGameSession.Metadata(10300001, EquipSlot.FA, Gender.Male, type: 1);
    private static readonly ItemMetadata FemaleFace = TestGameSession.Metadata(10300002, EquipSlot.FA, Gender.Female, type: 1);
    private static readonly ItemMetadata Decoration = TestGameSession.Metadata(10400000, EquipSlot.FD, type: 1);
    private static readonly ItemMetadata WornDecoration = TestGameSession.Metadata(10400001, EquipSlot.FD, type: 1);
    private static readonly ItemMetadata Ear = TestGameSession.Metadata(10500001, EquipSlot.ER, type: 1);
    private static readonly ItemMetadata WornEar = TestGameSession.Metadata(10500002, EquipSlot.ER, type: 1);
    private static readonly ItemMetadata Clothes = TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1);
    private static readonly ItemMetadata FemaleClothes = TestGameSession.Metadata(11400002, EquipSlot.CL, Gender.Female, type: 1);

    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item voucher = null!;
    private long[] oldLook = null!;
    private long clothes;

    // A male character wearing hair, face, face decoration, ear and clothes, with vouchers in the bag.
    private GameSession Login(int vouchers = 1, DbContextOptions? options = null) {
        test = new TestGameSession(options);
        test.Register(MaleHair, FemaleHair, MaleFace, FemaleFace, Decoration, Ear);
        test.SetTable(new DefaultItemsTable(new Dictionary<EquipSlot, int[]> {
            [EquipSlot.HR] = [MaleHair.Id, FemaleHair.Id], [EquipSlot.FA] = [MaleFace.Id, FemaleFace.Id],
            [EquipSlot.FD] = [Decoration.Id], [EquipSlot.ER] = [Ear.Id],
        }, new Dictionary<JobCode, IReadOnlyDictionary<EquipSlot, int[]>>()));
        oldLook = [
            test.Store(MaleHair, group: ItemGroup.Outfit, slot: EquipSlot.HR).Uid,
            test.Store(MaleFace, group: ItemGroup.Outfit, slot: EquipSlot.FA).Uid,
            test.Store(WornDecoration, group: ItemGroup.Outfit, slot: EquipSlot.FD).Uid,
            test.Store(WornEar, group: ItemGroup.Outfit, slot: EquipSlot.ER).Uid,
        ];
        clothes = test.Store(Clothes, group: ItemGroup.Outfit, slot: EquipSlot.CL).Uid;
        voucher = test.Store(Voucher, amount: vouchers);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        return test.Start();
    }

    private void Use(GameSession session) => handler.Handle(session, TestGameSession.Packet(writer => writer.WriteLong(voucher.Uid)));

    private static string Hex(byte[] packet) => Convert.ToHexString(packet);
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // The session's state after one change, and the same state after a new login from the stored rows.
    private void AssertChanged(GameSession session) {
        Assert.That(session.Player.Value.Character.Gender, Is.EqualTo(Gender.Female));
        Assert.That(session.Item.Inventory.Get(voucher.Uid), Is.Null);
        var worn = session.Item.Equips.Outfit;
        Assert.That(worn.Keys, Is.EquivalentTo(new[] { EquipSlot.HR, EquipSlot.FA, EquipSlot.FD, EquipSlot.ER, EquipSlot.CL }));
        Assert.That(worn[EquipSlot.HR].Id, Is.EqualTo(FemaleHair.Id));
        Assert.That(worn[EquipSlot.FA].Id, Is.EqualTo(FemaleFace.Id));
        Assert.That(worn[EquipSlot.FD].Id, Is.EqualTo(Decoration.Id));
        Assert.That(worn[EquipSlot.ER].Id, Is.EqualTo(Ear.Id));
        Assert.That(worn[EquipSlot.CL].Uid, Is.EqualTo(clothes));
        Assert.That(worn.Values.Select(item => item.Uid).Intersect(oldLook), Is.Empty);
    }

    private void AssertStoredAsWorn(GameSession session) {
        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.StoredCharacter().Gender, Is.EqualTo(Gender.Female));
        Assert.That(test.Row(voucher.Uid)?.OwnerId, Is.EqualTo(0));
        foreach (long uid in oldLook) {
            Assert.That(test.Row(uid)?.OwnerId, Is.EqualTo(0), "the old look's row is discarded (owner 0)");
        }
        // Every row the character still owns is a worn outfit item, the same ones the session wears.
        var rows = test.Rows();
        Assert.That(rows.Select(row => row.Id), Is.EquivalentTo(session.Item.Equips.Outfit.Values.Select(item => item.Uid)));
        foreach (Maple2.Database.Model.Item row in rows) {
            Item worn = session.Item.Equips.Outfit[(EquipSlot) row.Slot];
            Assert.That(row.Id, Is.EqualTo(worn.Uid));
            Assert.That(row.ItemId, Is.EqualTo(worn.Id));
            Assert.That(row.Group, Is.EqualTo(ItemGroup.Outfit));
        }
    }

    // KMS2 order: the voucher leaves the bag, the four look items come off, then the notice that closes the scene.
    [Test]
    public void Use_SendsKms2OrderAndSavesAtOnce() {
        GameSession session = Login();
        test.TakeSent();

        Use(session);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] {
            SendOp.ItemInventory, SendOp.ItemPutOff, SendOp.ItemPutOff, SendOp.ItemPutOff, SendOp.ItemPutOff, SendOp.Notice,
        }));
        Assert.That(sent[0][2], Is.EqualTo(1), "ItemInventory Remove");
        Assert.That(BitConverter.ToInt64(sent[0], 3), Is.EqualTo(voucher.Uid));
        for (int i = 0; i < 4; i++) {
            Assert.That(BitConverter.ToInt32(sent[1 + i], 2), Is.EqualTo(session.Player.ObjectId));
            Assert.That(BitConverter.ToInt64(sent[1 + i], 6), Is.EqualTo(oldLook[i]), "hair, face, face decoration, ear");
        }
        // Notice 5, message box flag, the done text: the client's string 756, not the refusal 755.
        Assert.That(Hex(sent[5]), Is.EqualTo(Hex(NoticePacket.Disconnect(StringCode.s_change_gender_result_success))));
        Assert.That(BitConverter.ToInt32(sent[5], sent[5].Length - 8), Is.EqualTo(756));

        AssertChanged(session);
        AssertStoredAsWorn(session);
        Assert.That(test.World.Acquired, Is.EqualTo(1));
        Assert.That(test.World.Released, Is.EqualTo(1));

        // The next login reads what was stored.
        AssertChanged(test.Start());
    }

    [TestCase(MaritalStatus.Engaged)]
    [TestCase(MaritalStatus.Married)]
    [TestCase(MaritalStatus.DivorceFiledByGroom)]
    public void Use_RefusedByTheWeddingState(MaritalStatus status) {
        GameSession session = Login();
        session.Marriage.Marriage.Status = status;
        test.TakeSent();

        Use(session);

        AssertRefused(session, StringCode.s_change_gender_result_failed);
    }

    [Test]
    public void Use_RefusedWhileGenderLimitedClothesAreWorn() {
        GameSession session = Login();
        // Worn after the login, as an equip would put it.
        Item limited = new(FemaleClothes) { Uid = 999, Group = ItemGroup.Outfit, Slot = (short) EquipSlot.PA };
        session.Item.Equips.Outfit[EquipSlot.PA] = limited;
        test.TakeSent();

        Use(session);

        session.Item.Equips.Outfit.TryRemove(EquipSlot.PA, out _);
        AssertRefused(session, StringCode.s_change_gender_err_equip_items);
    }

    private void AssertRefused(GameSession session, StringCode code) {
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(Hex), Is.EqualTo(new[] { Hex(NoticePacket.MessageBox(code)) }));
        Assert.That(session.Player.Value.Character.Gender, Is.EqualTo(Gender.Male));
        Assert.That(session.Item.Inventory.Get(voucher.Uid)?.Amount, Is.EqualTo(1));
        Assert.That(session.Item.Equips.Outfit.Values.Select(item => item.Uid), Is.EquivalentTo(oldLook.Append(clothes)));
        Assert.That(test.Rows(), Has.Count.EqualTo(6), "no row added or deleted");
        Assert.That(test.StoredCharacter().Gender, Is.EqualTo(Gender.Male));
        Assert.That(test.World.Acquired, Is.EqualTo(0), "nothing saved");
    }

    // The same request again finds no voucher: nothing is sent and nothing changes back.
    [Test]
    public void SecondRequestForTheSameVoucherDoesNothing() {
        GameSession session = Login();
        Use(session);
        test.TakeSent();

        Use(session);

        Assert.That(test.TakeSent(), Is.Empty);
        AssertChanged(session);
        AssertStoredAsWorn(session);
        Assert.That(test.World.Acquired, Is.EqualTo(1));
    }

    // A stack of two used back to back: female, then male again, each with its own new look rows.
    [Test]
    public void TwoVouchersBackToBack() {
        GameSession session = Login(vouchers: 2);
        test.TakeSent();

        Use(session);
        List<byte[]> first = test.TakeSent();
        Assert.That(first[0][2], Is.EqualTo(2), "ItemInventory UpdateAmount");
        Assert.That(session.Item.Inventory.Get(voucher.Uid)?.Amount, Is.EqualTo(1));
        Assert.That(session.Player.Value.Character.Gender, Is.EqualTo(Gender.Female));
        long[] femaleLook = [.. new[] { EquipSlot.HR, EquipSlot.FA, EquipSlot.FD, EquipSlot.ER }.Select(slot => session.Item.Equips.Outfit[slot].Uid)];
        Assert.That(test.Row(voucher.Uid)?.Amount, Is.EqualTo(1), "the smaller stack is stored at once");

        Use(session);
        List<byte[]> second = test.TakeSent();
        Assert.That(second.Select(TestGameSession.Op), Is.EqualTo(first.Select(TestGameSession.Op)));
        Assert.That(second[0][2], Is.EqualTo(1), "ItemInventory Remove");
        for (int i = 0; i < 4; i++) {
            Assert.That(BitConverter.ToInt64(second[1 + i], 6), Is.EqualTo(femaleLook[i]));
            Assert.That(test.Row(femaleLook[i])?.OwnerId, Is.EqualTo(0));
        }

        Assert.That(session.Player.Value.Character.Gender, Is.EqualTo(Gender.Male));
        Assert.That(test.StoredCharacter().Gender, Is.EqualTo(Gender.Male));
        Assert.That(session.Item.Equips.Outfit[EquipSlot.HR].Id, Is.EqualTo(MaleHair.Id));
        Assert.That(session.Item.Equips.Outfit[EquipSlot.FA].Id, Is.EqualTo(MaleFace.Id));
        Assert.That(test.Row(voucher.Uid)?.OwnerId, Is.EqualTo(0));
        Assert.That(test.Rows().Select(row => row.Id), Is.EquivalentTo(session.Item.Equips.Outfit.Values.Select(item => item.Uid)));
        Assert.That(test.World.Acquired, Is.EqualTo(2));
        Assert.That(test.World.Released, Is.EqualTo(2));
    }

    // The change runs under the session's item lock: while another thread holds it, nothing is sent or changed.
    [Test]
    public void Use_WaitsForTheItemLock() {
        GameSession session = Login();
        test.TakeSent();
        Task use;
        lock (session.Item) {
            use = Task.Run(() => Use(session));
            Assert.That(use.Wait(TimeSpan.FromMilliseconds(300)), Is.False, "the handler waits");
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(session.Player.Value.Character.Gender, Is.EqualTo(Gender.Male));
            Assert.That(session.Item.Inventory.Get(voucher.Uid), Is.Not.Null);
        }
        Assert.That(use.Wait(TimeSpan.FromSeconds(30)), Is.True);
        Assert.That(test.TakeSent(), Has.Count.EqualTo(6));
        AssertChanged(session);
    }

    // The immediate save (SavePlayer, Equips.Save, Inventory.Save) against a MySQL database: set
    // MAPLE2_TEST_MYSQL to the connection string of a scratch database (it is created if missing).
    [Test]
    public void Use_SavesToMySql() {
        string? connection = Environment.GetEnvironmentVariable("MAPLE2_TEST_MYSQL");
        if (string.IsNullOrEmpty(connection)) {
            Assert.Ignore("MAPLE2_TEST_MYSQL is not set");
        }
        DbContextOptions options = new DbContextOptionsBuilder().UseMySql(connection, ServerVersion.AutoDetect(connection)).Options;
        using (var context = new Maple2.Database.Context.Ms2Context(options)) {
            context.Database.EnsureCreated();
        }
        GameSession session = Login(vouchers: 2, options: options);
        session.Currency.Meso = 1234;

        Use(session);

        Assert.That(test.TakeSentOps().Last(), Is.EqualTo(SendOp.Notice));
        // Written: the rows. Reloaded: a new login. Compared: both with the session that made the change.
        Assert.That(test.StoredCharacter().Gender, Is.EqualTo(Gender.Female));
        Assert.That(test.Row(voucher.Uid)?.Amount, Is.EqualTo(1));
        foreach (long uid in oldLook) {
            Assert.That(test.Row(uid)?.OwnerId, Is.EqualTo(0));
        }
        GameSession again = test.Start();
        Assert.That(again.Player.Value.Character.Gender, Is.EqualTo(Gender.Female));
        Assert.That(again.Currency.Meso, Is.EqualTo(1234), "SavePurchase stores the currency in the same transaction");
        Assert.That(again.Item.Inventory.Get(voucher.Uid)?.Amount, Is.EqualTo(1));
        Assert.That(again.Item.Equips.Outfit.ToDictionary(entry => entry.Key, entry => (entry.Value.Uid, entry.Value.Id)),
            Is.EquivalentTo(session.Item.Equips.Outfit.ToDictionary(entry => entry.Key, entry => (entry.Value.Uid, entry.Value.Id))));
        Assert.That(again.Item.Equips.Outfit[EquipSlot.HR].Id, Is.EqualTo(FemaleHair.Id));
        Assert.That(again.Item.Equips.Outfit[EquipSlot.CL].Uid, Is.EqualTo(clothes));
        Assert.That(test.Rows(), Has.Count.EqualTo(6));
    }
}
