using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StoredMarriage = Maple2.Database.Model.Marriage;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Wedding 3 (propose) and 4 (the answer) between two friends on one field. As in the client
// (WeddingManager::CheckPropose asks for the proposer's ring, the answer box checks no item), only the proposer
// needs the ring, and the accept uses up that one.
public class WeddingProposalTests {
    private const byte Propose = 3;
    private const byte Reply = 4;
    private const short Accept = 1;

    private readonly MarriageFault fault = new();
    private TestGameSession proposer = null!;
    private TestGameSession target = null!;
    private readonly WeddingHandler handler = new();

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(fault)
            .Options;
        proposer = new TestGameSession(options, Gender.Male);
        target = new TestGameSession(options, Gender.Female);
        using (GameStorage.Request db = proposer.Storage.Context()) {
            Assume.That(db.CreateBuddy(proposer.CharacterId, target.CharacterId, BuddyType.Default), Is.Not.Null);
            Assume.That(db.CreateBuddy(target.CharacterId, proposer.CharacterId, BuddyType.Default), Is.Not.Null);
        }
        proposer.Store(TestGameSession.Metadata(Constant.ProposalItemId));
        target.Register(TestGameSession.Metadata(Constant.ProposalItemId));
        foreach (TestGameSession test in new[] { proposer, target }) {
            test.World.AddPlayer(proposer.AccountId, proposer.CharacterId, "proposer");
            test.World.AddPlayer(target.AccountId, target.CharacterId, "target");
            test.AddMap(TestGameSession.Map(2000023));
            test.Start();
            test.EnterMap(TestGameSession.Map(2000023));
        }
        // Both on the proposer's field, as the handler finds the other one there.
        FieldManager field = proposer.Session.Field!;
        field.Players[1001] = proposer.Session.Player;
        field.Players[1002] = target.Session.Player;
        target.Session.Field = field;
        Assume.That(proposer.Session.Buddy.IsBuddy(target.CharacterId), Is.True);
        proposer.TakeSent();
        target.TakeSent();
    }

    private static string[] Sent(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Wedding).Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static int Rings(TestGameSession test) =>
        test.Session.Item.Inventory.Find(Constant.ProposalItemId).Sum(item => item.Amount);

    private void SendPropose() {
        handler.Handle(proposer.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(Propose);
            writer.WriteUnicodeString(target.Session.PlayerName);
        }));
    }

    private void SendAccept() {
        handler.Handle(target.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(Reply);
            writer.WriteShort(Accept);
            writer.WriteLong(proposer.CharacterId);
        }));
    }

    // The target holds no ring: the proposal still goes out, and the accept engages both and takes the proposer's.
    [Test]
    public void Accept_UsesTheProposersRingOnly() {
        Assume.That(Rings(proposer), Is.EqualTo(1));
        Assume.That(Rings(target), Is.EqualTo(0));

        SendPropose();
        Assert.That(Sent(proposer), Is.EqualTo(new[] { Hex(WeddingPacket.Error(WeddingError.s_wedding_msg_box_propose_to)) }));
        Assert.That(Sent(target), Is.EqualTo(new[] { Hex(WeddingPacket.Proposal(proposer.Session.PlayerName, proposer.CharacterId)) }));

        Item held = proposer.Session.Item.Inventory.Find(Constant.ProposalItemId).Single();
        SendAccept();
        Assert.That(proposer.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Engaged));
        Assert.That(target.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Engaged));
        // The ring's row is taken in the marriage's own save, not left to the session's next save (a crash before it
        // kept the ring with the engagement stored).
        Assert.That(proposer.Row(held.Uid)!.OwnerId, Is.EqualTo(0));
        Assert.That(Sent(proposer), Is.EqualTo(new[] { Hex(WeddingPacket.Engaged(proposer.Session.Marriage.Marriage)) }));
        Assert.That(Sent(target), Is.EqualTo(new[] { Hex(WeddingPacket.Engaged(target.Session.Marriage.Marriage)) }));
        Assert.That(Rings(proposer), Is.EqualTo(0));
        Assert.That(Rings(target), Is.EqualTo(0));
    }

    // 23100423: <condition type="wedding_propose_decline" value="1" /> and 23100422: <condition
    // type="wedding_propose_declined" value="1" />: a declined proposal (answer 2) counts the decline for the target and
    // the declined for the proposer; an answer box that timed out (6) counts neither.
    [TestCase((short) 2, 1)]
    [TestCase((short) 6, 0)]
    public void Decline_23100423_23100422_CountsBoth(short answer, int counted) {
        Quest Watch(TestGameSession test) => test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [
                new ConditionMetadata(ConditionType.wedding_propose_decline, 1, null, null),
                new ConditionMetadata(ConditionType.wedding_propose_declined, 1, null, null),
            ],
        });
        Quest proposerQuest = Watch(proposer);
        Quest targetQuest = Watch(target);
        SendPropose();

        handler.Handle(target.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(Reply);
            writer.WriteShort(answer);
            writer.WriteLong(proposer.CharacterId);
        }));

        Assert.Multiple(() => {
            Assert.That(proposer.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Single));
            Assert.That((targetQuest.Conditions[0].Counter, targetQuest.Conditions[1].Counter), Is.EqualTo((counted, 0)));
            Assert.That((proposerQuest.Conditions[0].Counter, proposerQuest.Conditions[1].Counter), Is.EqualTo((0, counted)));
        });
    }

    // A proposer without a ring is told so (error 0x1f) and nothing is sent to the target.
    [Test]
    public void Propose_WithoutRing_IsRefused() {
        Item ring = proposer.Session.Item.Inventory.Find(Constant.ProposalItemId).Single();
        Assume.That(proposer.Session.Item.Inventory.Consume(ring.Uid, 1), Is.True);
        proposer.TakeSent();

        SendPropose();
        string expected = Hex(WeddingPacket.Error(WeddingError.s_wedding_result_err_not_find_propose_item));
        Assert.That(expected[4..], Is.EqualTo("02" + "1F"));
        Assert.That(Sent(proposer), Is.EqualTo(new[] { expected }));
        Assert.That(Sent(target), Is.Empty);
    }

    // The ring went before the answer: no engagement, the proposer gets the ring notice and the target the error.
    [Test]
    public void Accept_AfterTheRingIsGone_MakesNoMarriage() {
        SendPropose();
        proposer.TakeSent();
        target.TakeSent();
        Item ring = proposer.Session.Item.Inventory.Find(Constant.ProposalItemId).Single();
        Assume.That(proposer.Session.Item.Inventory.Consume(ring.Uid, 1), Is.True);
        proposer.TakeSent();

        SendAccept();
        string expected = Hex(WeddingPacket.Error(WeddingError.s_wedding_result_err_not_find_propose_item));
        Assert.That(Sent(proposer), Is.EqualTo(new[] { expected }));
        Assert.That(Sent(target), Is.EqualTo(new[] { expected }));
        Assert.That(proposer.Session.Marriage.Marriage.Status, Is.Not.EqualTo(MaritalStatus.Engaged));
        Assert.That(target.Session.Marriage.Marriage.Status, Is.Not.EqualTo(MaritalStatus.Engaged));
        using GameStorage.Request db = proposer.Storage.Context();
        Assert.That(db.GetMarriage(proposer.CharacterId), Is.Null);
    }

    // The marriage row is not stored: the proposer keeps the ring, nobody is engaged, the one who accepts gets the
    // system error and the proposer a no.
    [Test]
    public void Accept_WhenTheMarriageIsNotStored_KeepsTheRing() {
        SendPropose();
        proposer.TakeSent();
        target.TakeSent();
        fault.Fail = true;

        SendAccept();
        fault.Fail = false;
        Assert.That(Rings(proposer), Is.EqualTo(1));
        Assert.That(Sent(target), Is.EqualTo(new[] { Hex(WeddingPacket.Error(WeddingError.s_wedding_result_err_system)) }));
        Assert.That(Sent(proposer), Is.EqualTo(new[] { Hex(WeddingPacket.Error(WeddingError.s_wedding_result_decline_propose)) }));
        Assert.That(proposer.Session.Marriage.Marriage.Status, Is.Not.EqualTo(MaritalStatus.Engaged));
        Assert.That(target.Session.Marriage.Marriage.Status, Is.Not.EqualTo(MaritalStatus.Engaged));
        using GameStorage.Request db = proposer.Storage.Context();
        Assert.That(db.GetMarriage(proposer.CharacterId), Is.Null);
    }

    // The ring's stack changes while the marriage row is stored (a move or merge gives it a new uid): the accept
    // takes the ring the proposer holds then. Before, it used the uid found before the save and was refused.
    [Test]
    public void Accept_TakesTheRingHeldAfterTheSave() {
        SendPropose();
        proposer.TakeSent();
        target.TakeSent();
        fault.OnSave = () => {
            Item ring = proposer.Session.Item.Inventory.Find(Constant.ProposalItemId).Single();
            Assume.That(proposer.Session.Item.Inventory.Consume(ring.Uid, 1), Is.True);
            Item moved;
            using (GameStorage.Request db = proposer.Storage.Context()) {
                moved = db.CreateItem(proposer.CharacterId, new Item(ring.Metadata)) ?? throw new InvalidOperationException("item row not created");
            }
            Assume.That(moved.Uid, Is.Not.EqualTo(ring.Uid));
            Assume.That(proposer.Session.Item.Inventory.Add(moved), Is.True);
        };

        SendAccept();
        fault.OnSave = null;
        Assert.That(proposer.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Engaged));
        Assert.That(target.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Engaged));
        Assert.That(Rings(proposer), Is.EqualTo(0));
    }

    // A marriage row that cannot be read back (a partner without player info; here a character id with no row,
    // which the in-memory database takes as it has no foreign keys) is not kept: the create answers
    // null and stores nothing. Before, the row was committed and then answered null, so it stayed with no ring taken
    // and was the one found for the proposer from then on.
    [Test]
    public void CreateMarriage_ThatCannotBeRead_StoresNothing() {
        using GameStorage.Request db = proposer.Storage.Context();
        Assert.That(db.CreateMarriage(proposer.CharacterId, 987654321), Is.Null);
        Assert.That(db.CreateMarriage(proposer.CharacterId, target.CharacterId), Is.Not.Null);
        Marriage? found = db.GetMarriage(proposer.CharacterId);
        Assert.That(found, Is.Not.Null);
        Assert.That(new[] { found!.Partner1.CharacterId, found.Partner2.CharacterId }, Is.EquivalentTo(new[] { proposer.CharacterId, target.CharacterId }));
    }

    // Wedding 26 from a tombstoned player is refused with the dead notice (0x3a) before any hall is looked up.
    [Test]
    public void EnterCeremony_WhileDead_IsRefused() {
        typeof(FieldPlayer).GetField("isDead", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(proposer.Session.Player, true);
        Assume.That(proposer.Session.Player.IsDead, Is.True);
        proposer.TakeSent();

        handler.Handle(proposer.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(26);
            writer.WriteLong(1);
        }));
        string expected = Hex(WeddingPacket.Error(WeddingError.s_wedding_visit_failed_dead));
        Assert.That(expected[4..], Is.EqualTo("02" + "3A"));
        Assert.That(Sent(proposer), Is.EqualTo(new[] { expected }));
    }

    // Fails every save that adds a marriage row while set; OnSave runs at such a save.
    private sealed class MarriageFault : SaveChangesInterceptor {
        public bool Fail;
        public Action? OnSave;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            if (eventData.Context != null && eventData.Context.ChangeTracker.Entries<StoredMarriage>()
                    .Any(entry => entry.State == EntityState.Added)) {
                if (Fail) {
                    throw new DbUpdateException("The test made this save fail");
                }
                Action? onSave = OnSave;
                OnSave = null;
                onSave?.Invoke();
            }
            return result;
        }
    }
}
