using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Wedding 3 (propose) and 4 (the answer) between two friends on one field. As in the client
// (WeddingManager::CheckPropose asks for the proposer's ring, the answer box checks no item), only the proposer
// needs the ring, and the accept uses up that one.
public class WeddingProposalTests {
    private const byte Propose = 3;
    private const byte Reply = 4;
    private const short Accept = 1;

    private TestGameSession proposer = null!;
    private TestGameSession target = null!;
    private readonly WeddingHandler handler = new();

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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

        SendAccept();
        Assert.That(proposer.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Engaged));
        Assert.That(target.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Engaged));
        Assert.That(Sent(proposer), Is.EqualTo(new[] { Hex(WeddingPacket.Engaged(proposer.Session.Marriage.Marriage)) }));
        Assert.That(Sent(target), Is.EqualTo(new[] { Hex(WeddingPacket.Engaged(target.Session.Marriage.Marriage)) }));
        Assert.That(Rings(proposer), Is.EqualTo(0));
        Assert.That(Rings(target), Is.EqualTo(0));
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
}
