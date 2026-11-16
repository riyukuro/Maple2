using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// SurvivalSkin (KR 20900009, 20910009, 20920006): a medal item (group 2, type 9) becomes a survival medal when it
// is added (InventoryManager.Add, SurvivalManager.AddMedal) and never reaches the bag, so the client has no use to
// send. A second medal of the same id renews the one held. A medal row that is in the bag all the same (stored before
// that, placed by a load) is used with the plain use (ItemUseHandler, SurvivalManager.UseMedal).
public class SurvivalSkinTests {
    private const int TailId = 20900001;
    private const int RidingId = 20910001;
    private TestGameSession test = null!;

    private Item bagMedal = null!;

    // bagParameters: a medal row stored in the bag (owner the character, as a row stored before Add learned medals).
    private GameSession Start(string? bagParameters = null) {
        test = new TestGameSession();
        if (bagParameters != null) {
            bagMedal = test.Store(TestGameSession.Metadata(20900009,
                function: new ItemMetadataFunction(ItemFunction.SurvivalSkin, "SurvivalSkin", bagParameters, false), type: 10));
        }
        test.SetTable(new SurvivalSkinInfoTable(new Dictionary<int, MedalType> {
            [TailId] = MedalType.Tail,
            [RidingId] = MedalType.Riding,
        }));
        // No pass period: Load enters the open period before it sends the stats, and there is none.
        test.SetTable(new MapleSurvivalTable(new Dictionary<int, MapleSurvivalTable.Period>(), new Dictionary<int, long>(),
            new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>()));
        GameSession session = test.Start();
        session.Survival = new SurvivalManager(session);
        test.TakeSent();
        return session;
    }

    private static Item Medal(int itemId, string parameters) {
        return new Item(TestGameSession.Metadata(itemId,
            function: new ItemMetadataFunction(ItemFunction.SurvivalSkin, "SurvivalSkin", parameters, false)));
    }

    private static void WithRestoredClient(string? value, Action action) {
        string? before = Environment.GetEnvironmentVariable("MS2_SURVIVAL_CLIENT");
        Environment.SetEnvironmentVariable("MS2_SURVIVAL_CLIENT", value);
        try {
            action();
        } finally {
            Environment.SetEnvironmentVariable("MS2_SURVIVAL_CLIENT", before);
        }
    }

    // The restored client gets the new skin (Survival 0x1f) and the learned line (0x21). Before, the whole medal list
    // (0x1e) was sent again and the line never showed.
    [Test]
    public void AddMedal_New_SendsTheSkinAndTheLearnedLine() {
        WithRestoredClient("1", () => {
            GameSession session = Start();
            session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
            long end = session.Survival.Find(MedalType.Riding, RidingId)!.ExpiryTime;
            Assert.That(end, Is.LessThan(long.MaxValue / 2));
            Assert.That(test.TakeSent(), Is.EqualTo(new[] {
                MapleSurvivalPacket.SkinAdded(1, RidingId, end).ToArray(),
                MapleSurvivalPacket.SkinLearned(1, RidingId).ToArray(),
            }));
            // A renewal changes no list entry: the list again.
            session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
            List<byte[]> sent = test.TakeSent();
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0][2], Is.EqualTo(0x1e));
        });
    }

    // The KR client keeps the medal list.
    [Test]
    public void AddMedal_New_KrClient_SendsTheList() {
        WithRestoredClient("0", () => {
            GameSession session = Start();
            session.Survival.AddMedal(Medal(20900009, $"<v id=\"{TailId}\" type=\"effectTail\"/>"));
            List<byte[]> sent = test.TakeSent();
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0][2], Is.EqualTo(0x1e));
        });
    }

    // Before, the held medal's expiry had the new one's epoch added to it, so a second medal never expired.
    [Test]
    public void AddMedal_SecondEndDate_KeepsTheLaterEnd() {
        GameSession session = Start();
        session.Survival.AddMedal(Medal(20900009, $"<v id=\"{TailId}\" type=\"effectTail\" endDate=\"2030-01-01-00-00-00\"/>"));
        session.Survival.AddMedal(Medal(20900009, $"<v id=\"{TailId}\" type=\"effectTail\" endDate=\"2031-01-01-00-00-00\"/>"));
        Assert.That(session.Survival.Find(MedalType.Tail, TailId)?.ExpiryTime, Is.EqualTo(new DateTime(2031, 1, 1).ToEpochSeconds()));
        session.Survival.AddMedal(Medal(20900009, $"<v id=\"{TailId}\" type=\"effectTail\" endDate=\"2030-06-01-00-00-00\"/>"));
        Assert.That(session.Survival.Find(MedalType.Tail, TailId)?.ExpiryTime, Is.EqualTo(new DateTime(2031, 1, 1).ToEpochSeconds()));
    }

    // A durationSec medal adds its time to what is left of the held one.
    [Test]
    public void AddMedal_SecondDuration_AddsItsTime() {
        GameSession session = Start();
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
        session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
        long after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(session.Survival.Find(MedalType.Riding, RidingId)?.ExpiryTime, Is.InRange(before + 2 * 10080, after + 2 * 10080));
    }

    // The medal list sends each medal's stored end (the restored client hides an ended skin); before, long.MaxValue.
    [Test]
    public void Load_SendsEachMedalsEnd() {
        GameSession session = Start();
        session.Survival.AddMedal(Medal(20900009, $"<v id=\"{TailId}\" type=\"effectTail\" endDate=\"2030-01-01-00-00-00\"/>"));
        test.TakeSent();
        session.Survival.Load();
        byte[] list = test.TakeSent().Single(packet => packet[2] == 0x1e);
        long end = new DateTime(2030, 1, 1).ToEpochSeconds();
        Assert.That(Contains(list, BitConverter.GetBytes(TailId).Concat(BitConverter.GetBytes(end)).ToArray()), Is.True);
    }

    // A tester's state: one lasting medal of each type learned by the GM item (rows Slot -1, the far end), loaded at
    // login. The list has the three slots in the client's order (tail, riding, gliding), none worn, each with its own
    // medal and the stored end (9999-12-31, never before the client's server time).
    [Test]
    public void Load_LastingMedalOfEachType_OneInEachSlot() {
        const int glidingId = 20920001;
        GameSession session = Start();
        test.SetTable(new SurvivalSkinInfoTable(new Dictionary<int, MedalType> {
            [TailId] = MedalType.Tail,
            [RidingId] = MedalType.Riding,
            [glidingId] = MedalType.Gliding,
        }));
        DateTime far = (DateTime.MaxValue.ToEpochSeconds() - 1).FromEpochSeconds();
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            foreach (int id in new[] { glidingId, TailId, RidingId }) {
                context.Medal.Add(new Maple2.Database.Model.Medal { Id = id, OwnerId = test.CharacterId, Slot = -1, ExpiryTime = far });
            }
            context.SaveChanges();
        }
        session.Survival = new SurvivalManager(session);
        test.TakeSent();
        session.Survival.Load();

        byte[] list = test.TakeSent().Single(packet => packet[2] == 0x1e);
        var expected = new List<byte> { 0x19, 0x01, 0x1e, 3 };
        foreach (int id in new[] { TailId, RidingId, glidingId }) {
            expected.AddRange(BitConverter.GetBytes(0));
            expected.AddRange(BitConverter.GetBytes(1));
            expected.AddRange(BitConverter.GetBytes(id));
            expected.AddRange(BitConverter.GetBytes(far.ToEpochSeconds()));
        }
        Assert.That(list, Is.EqualTo(expected.ToArray()));
        Assert.That(far.ToEpochSeconds(), Is.GreaterThan(new DateTime(9999, 1, 1).ToEpochSeconds()));
    }

    // An ended medal is refused, as the restored client refuses it (s_item_err_puton_expired); a live one is worn.
    [Test]
    public void Equip_EndedMedal_IsRefused() {
        GameSession session = Start();
        session.Survival.AddMedal(Medal(20900009, $"<v id=\"{TailId}\" type=\"effectTail\" endDate=\"2020-01-01-00-00-00\"/>"));
        session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
        Assert.That(session.Survival.Equip(MedalType.Tail, TailId), Is.False);
        Assert.That(session.Survival.Find(MedalType.Tail, TailId)?.Slot, Is.EqualTo(-1));
        Assert.That(session.Survival.Equip(MedalType.Riding, RidingId), Is.True);
    }

    // A worn medal whose end has passed comes off at the next load and stays in the list.
    [Test]
    public void Load_WornEndedMedal_ComesOff() {
        GameSession session = Start();
        session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
        Assert.That(session.Survival.Equip(MedalType.Riding, RidingId), Is.True);
        Medal medal = session.Survival.Find(MedalType.Riding, RidingId)!;
        medal.ExpiryTime = DateTime.UtcNow.ToEpochSeconds() - 1;
        session.Survival.Load();
        Assert.That(medal.Slot, Is.EqualTo(-1));
        Assert.That(session.Survival.Find(MedalType.Riding, RidingId), Is.Not.Null);
    }

    private static void EquipSkin(GameSession session, byte slot, int skin) =>
        new MapleSurvivalHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteByte(slot);
            writer.WriteInt(skin);
        }));

    // The skin window's put on and take off (UIMapleSurvivalSkinDialog, CMapleSurvivalManager::EquipSkin: MapleSurvival
    // 8, the slot byte and the skin, 0 to take it off). The worn skin is answered with the slot list (0x1e) and the
    // slot's skin (0x20); a skin not held, one past the client's three slots, or one already worn gets no answer.
    [Test]
    public void EquipSkin_PutOnAndTakeOff() {
        GameSession session = Start();
        session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
        test.TakeSent();

        EquipSkin(session, 1, RidingId);
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(Convert.ToHexString), Has.Some.EqualTo(Convert.ToHexString(MapleSurvivalPacket.SkinEquipped(1, RidingId).ToArray())));
        Assert.That(session.Survival.Find(MedalType.Riding, RidingId)?.Slot, Is.EqualTo((short) MedalType.Riding));

        EquipSkin(session, 1, RidingId);
        Assert.That(test.TakeSent(), Is.Empty);

        EquipSkin(session, 1, 0);
        sent = test.TakeSent();
        Assert.That(sent.Select(Convert.ToHexString), Has.Some.EqualTo(Convert.ToHexString(MapleSurvivalPacket.SkinEquipped(1, 0).ToArray())));
        Assert.That(session.Survival.Find(MedalType.Riding, RidingId)?.Slot, Is.EqualTo(-1));
    }

    [TestCase((byte) 0, RidingId)] // a riding skin in the tail slot: not held there
    [TestCase((byte) 1, 20910002)] // not held
    [TestCase((byte) 3, RidingId)] // past the client's slots
    public void EquipSkin_NotHeldInTheSlot_NoAnswer(byte slot, int skin) {
        GameSession session = Start();
        session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
        test.TakeSent();

        EquipSkin(session, slot, skin);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Survival.Find(MedalType.Riding, RidingId)?.Slot, Is.EqualTo(-1));
    }

    // A medal item given to the bag (a reward, a mail) is learned as the skin and is not put in the bag.
    [Test]
    public void InventoryAdd_Medal_LearnedNotKept() {
        GameSession session = Start();

        Assert.That(session.Item.Inventory.Add(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>")), Is.True);

        Assert.That(session.Survival.Find(MedalType.Riding, RidingId), Is.Not.Null);
        Assert.That(session.Item.Inventory.Find(20910009), Is.Empty);
    }

    private void Use(GameSession session) {
        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(bagMedal.Uid);
            writer.WriteUnicodeString(string.Empty);
        }));
    }

    private Maple2.Database.Model.Medal? StoredMedal(int id) {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.Medal.AsNoTracking().SingleOrDefault(medal => medal.OwnerId == test.CharacterId && medal.Id == id);
    }

    private bool BagRowStored() {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.Item.Any(item => item.Id == bagMedal.Uid && item.OwnerId == test.CharacterId);
    }

    // Before, the server had no SurvivalSkin use: a medal row in the bag stayed there and was never learned. Now the
    // medal and the used item are stored together, then the skin is sent as AddMedal sends it.
    [Test]
    public void Use_BagMedal_LearnsItWithTheItemUsed() {
        WithRestoredClient("1", () => {
            GameSession session = Start($"<v id=\"{TailId}\" type=\"effectTail\" endDate=\"2030-01-01-00-00-00\"/>");
            Use(session);
            long end = new DateTime(2030, 1, 1).ToEpochSeconds();
            Assert.That(session.Survival.Find(MedalType.Tail, TailId)?.ExpiryTime, Is.EqualTo(end));
            Assert.That(session.Item.Inventory.Get(bagMedal.Uid), Is.Null);
            Assert.That(StoredMedal(TailId)?.ExpiryTime, Is.EqualTo(new DateTime(2030, 1, 1)));
            Assert.That(BagRowStored(), Is.False);
            // The item's removal from the bag, then the skin and the learned line.
            Assert.That(test.TakeSent().Skip(1), Is.EqualTo(new[] {
                MapleSurvivalPacket.SkinAdded(0, TailId, end).ToArray(),
                MapleSurvivalPacket.SkinLearned(0, TailId).ToArray(),
            }));
        });
    }

    // A held medal is renewed as AddMedal renews it, the renewed end stored with the used item; the list is sent.
    [Test]
    public void Use_HeldMedal_RenewsIt() {
        GameSession session = Start($"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>");
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
        test.TakeSent();
        Use(session);
        long after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long end = session.Survival.Find(MedalType.Riding, RidingId)!.ExpiryTime;
        Assert.That(end, Is.InRange(before + 2 * 10080, after + 2 * 10080));
        Assert.That(StoredMedal(RidingId)!.ExpiryTime.ToEpochSeconds(), Is.EqualTo(end));
        Assert.That((session.Item.Inventory.Get(bagMedal.Uid), BagRowStored()), Is.EqualTo(((Item?) null, false)));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Count(packet => packet[2] == 0x1e), Is.EqualTo(1));
    }

    // When the used item cannot be stored, no medal is learned and the item is kept.
    [Test]
    public void Use_ItemNotStored_NothingLearned() {
        GameSession session = Start($"<v id=\"{TailId}\" type=\"effectTail\"/>");
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Item>().Any(entry => entry.State == EntityState.Modified);
        Use(session);
        test.FailSave = null;
        Assert.That(session.Survival.Find(MedalType.Tail, TailId), Is.Null);
        Assert.That((session.Item.Inventory.Get(bagMedal.Uid) != null, BagRowStored()), Is.EqualTo((true, true)));
        Assert.That(test.TakeSent().Any(packet => packet[2] is 0x1e or 0x1f or 0x21), Is.False);
    }

    // When the medal cannot be stored, the item is kept.
    [Test]
    public void Use_MedalNotStored_ItemKept() {
        GameSession session = Start($"<v id=\"{TailId}\" type=\"effectTail\"/>");
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Medal>().Any(entry => entry.State == EntityState.Added);
        Use(session);
        test.FailSave = null;
        Assert.That(session.Survival.Find(MedalType.Tail, TailId), Is.Null);
        Assert.That((session.Item.Inventory.Get(bagMedal.Uid) != null, BagRowStored()), Is.EqualTo((true, true)));
    }

    // A skin the skin table lacks (or of another type there) is not learned; the item is kept.
    [Test]
    public void Use_UnknownSkin_Kept() {
        GameSession session = Start("<v id=\"20900099\" type=\"effectTail\"/>");
        Use(session);
        Assert.That((session.Item.Inventory.Get(bagMedal.Uid) != null, StoredMedal(20900099)), Is.EqualTo((true, (Maple2.Database.Model.Medal?) null)));

        session = Start($"<v id=\"{TailId}\" type=\"riding\"/>");
        Use(session);
        Assert.That((session.Item.Inventory.Get(bagMedal.Uid) != null, session.Survival.Find(MedalType.Riding, TailId)), Is.EqualTo((true, (Medal?) null)));
    }

    private static bool Contains(byte[] haystack, byte[] needle) {
        for (int i = 0; i + needle.Length <= haystack.Length; i++) {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return true;
        }
        return false;
    }
}
