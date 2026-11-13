using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A proposal (Wedding 0x03) to a character of the same gender is refused with 0x15 where the client's WeddingBlockSex
// feature is on (KR) and sent where it is off (NA); the server used to send it everywhere.
public class WeddingProposeGenderTests {
    private readonly WeddingHandler handler = new();

    private (TestGameSession, TestGameSession) Couple(Gender gender) {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var proposer = new TestGameSession(options, gender);
        var target = new TestGameSession(options, gender);
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
        FieldManager field = proposer.Session.Field!;
        field.Players[1001] = proposer.Session.Player;
        field.Players[1002] = target.Session.Player;
        target.Session.Field = field;
        proposer.TakeSent();
        target.TakeSent();
        return (proposer, target);
    }

    private static List<byte[]> Wedding(TestGameSession session) {
        return session.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Wedding).Select(packet => packet[2..]).ToList();
    }

    private void Propose(TestGameSession proposer, TestGameSession target) {
        handler.Handle(proposer.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteUnicodeString(target.Session.PlayerName);
        }));
    }

    [Test]
    public void SameGender_FollowsTheLocalesFeature() {
        (TestGameSession proposer, TestGameSession target) = Couple(Gender.Female);
        Propose(proposer, target);
        if (WeddingProposeRules.BlocksSameGender(Maple2.Tools.Locale.Read())) {
            Assert.Multiple(() => {
                Assert.That(Wedding(proposer), Is.EqualTo(new List<byte[]> { new byte[] { 2, 0x15 } }));
                Assert.That(Wedding(target), Is.Empty);
                Assert.That(proposer.Session.Item.Inventory.Find(Constant.ProposalItemId).Count(), Is.EqualTo(1));
            });
        } else {
            Assert.That(Wedding(target).Select(packet => packet[0]), Is.EqualTo(new[] { (byte) 3 }));
        }
    }

    [Test]
    public void OtherGender_IsSent() {
        (TestGameSession proposer, TestGameSession target) = Couple(Gender.Male);
        target.Session.Player.Value.Character.Gender = Gender.Female;
        Propose(proposer, target);
        Assert.That(Wedding(target).Select(packet => packet[0]), Is.EqualTo(new[] { (byte) 3 }));
    }

    [TestCase("KR", true)]
    [TestCase("CN", true)]
    [TestCase("TW", true)]
    [TestCase("NA", false)]
    [TestCase("JP", false)]
    [TestCase("TH", false)]
    public void BlockSex_Locales(string locale, bool blocked) {
        Assert.Multiple(() => {
            Assert.That(WeddingProposeRules.BlocksSameGender(locale), Is.EqualTo(blocked));
            Assert.That(WeddingProposeRules.GenderPairAllowed(Gender.Male, Gender.Male, locale), Is.EqualTo(!blocked));
            Assert.That(WeddingProposeRules.GenderPairAllowed(Gender.Male, Gender.Female, locale), Is.True);
        });
    }
}
