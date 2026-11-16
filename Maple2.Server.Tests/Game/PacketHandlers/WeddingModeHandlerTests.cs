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

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Wedding 5, 7, 9, 0x1b, 0x1c, 0x1e, 0x22 and 0x25 sent as the client's WeddingManager and wedding dialogs write
// them: 5 the target's character id (WeddingManager::SendCancelPropose), 7 nothing (UIWeddingDialog::SendWedding),
// 9 one short (OnDivorceAnswer), 0x1b the typed name (the guest dialog's stranger page), 0x1c a count and that many
// guests of a short, two longs and a name (UIWeddingInvitationDialog::SendInvitation, WeddingGuest::Write), 0x1e the
// box type and a bool (OnActingAnswer), 0x22 the message (UIWeddingPage) and 0x25 the skill id (UIWeddingPageSkill).
public class WeddingModeHandlerTests {
    private TestGameSession proposer = null!;
    private TestGameSession target = null!;
    private readonly WeddingHandler handler = new();

    [SetUp]
    public void SetUp() {
        proposer = new TestGameSession(gender: Gender.Male);
        target = new TestGameSession(proposer.Options, Gender.Female);
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
    }

    private static string[] Sent(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Wedding).Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static string Error(WeddingError error) => Hex(WeddingPacket.Error(error));

    private void Send(TestGameSession test, Action<Maple2.PacketLib.Tools.ByteWriter> write) =>
        handler.Handle(test.Session, TestGameSession.Packet(write));

    private void Propose() {
        Send(proposer, writer => {
            writer.WriteByte(3);
            writer.WriteUnicodeString(target.Session.PlayerName);
        });
    }

    private void Accept() {
        Send(target, writer => {
            writer.WriteByte(4);
            writer.WriteShort(1);
            writer.WriteLong(proposer.CharacterId);
        });
    }

    private void Engage() {
        Propose();
        Accept();
        Assume.That(proposer.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Engaged));
        proposer.TakeSent();
        target.TakeSent();
    }

    // 5: the proposer withdraws; both get the result text and a later yes engages nobody.
    [Test]
    public void CancelProposal_EndsTheWaitingProposal() {
        Propose();
        proposer.TakeSent();
        target.TakeSent();

        Send(proposer, writer => {
            writer.WriteByte(5);
            writer.WriteLong(target.CharacterId);
        });
        Assert.That(Sent(proposer), Is.EqualTo(new[] { Error(WeddingError.s_wedding_result_cancel_propose_to) }));
        Assert.That(Sent(target), Is.EqualTo(new[] { Error(WeddingError.s_wedding_result_cancel_propose_from) }));

        Accept();
        Assert.Multiple(() => {
            Assert.That(proposer.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Single));
            Assert.That(target.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Single));
            Assert.That(proposer.Session.Item.Inventory.Find(Constant.ProposalItemId).Sum(item => item.Amount), Is.EqualTo(1));
        });
    }

    // 5 without a proposal of this player's to that target: nothing is sent.
    [Test]
    public void CancelProposal_NothingWaiting_SendsNothing() {
        Send(target, writer => {
            writer.WriteByte(5);
            writer.WriteLong(proposer.CharacterId);
        });
        Assert.That(Sent(target), Is.Empty);
        Assert.That(Sent(proposer), Is.Empty);
    }

    // 7 from an engaged player who has no filed divorce: the state error, no move.
    [Test]
    public void DivorceVisit_NotFiled_IsTheStateError() {
        Engage();
        int mapId = target.Session.Field!.MapId;
        Send(target, writer => writer.WriteByte(7));
        Assert.That(Sent(target), Is.EqualTo(new[] { Error(WeddingError.s_wedding_result_invaild_state_divorce) }));
        Assert.That(target.Session.Field!.MapId, Is.EqualTo(mapId));
    }

    // 9 when the partner asked nothing: the state error, and the engagement stays.
    [TestCase((short) 13)]
    [TestCase((short) 14)]
    public void DivorceReply_NothingAsked_IsTheStateError(short answer) {
        Engage();
        Send(target, writer => {
            writer.WriteByte(9);
            writer.WriteShort(answer);
        });
        Assert.That(Sent(target), Is.EqualTo(new[] { Error(WeddingError.s_wedding_result_invaild_state_divorce) }));
        Assert.That(target.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Engaged));
        using GameStorage.Request db = target.Storage.Context();
        Assert.That(db.GetMarriage(target.CharacterId)?.Status, Is.EqualTo(MaritalStatus.Engaged));
    }

    private void HoldHall() {
        proposer.Session.Marriage.WeddingHall = new WeddingHall {
            Id = 7,
            MarriageId = 1,
            PackageId = 1,
            PackageHallId = 1,
            CeremonyTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400,
            ReserverAccountId = proposer.AccountId,
            ReserverCharacterId = proposer.CharacterId,
            ReserverName = proposer.Session.PlayerName,
            PartnerName = target.Session.PlayerName,
        };
    }

    private void FindUser(string name) {
        Send(proposer, writer => {
            writer.WriteByte(0x1b);
            writer.WriteUnicodeString(name);
        });
    }

    // 0x1b without a hall: the no-hall text, no lookup.
    [Test]
    public void InvitationFindUser_NoHall_Refused() {
        FindUser(target.Session.PlayerName);
        Assert.That(Sent(proposer), Is.EqualTo(new[] { Error(WeddingError.s_wedding_result_not_find_weddinghall) }));
    }

    // 0x1b with a hall: the found character's account and character id with its stored name, or ids 0 and the typed
    // name when there is no such character.
    [Test]
    public void InvitationFindUser_AnswersTheIdsOrZero() {
        HoldHall();
        FindUser(target.Session.PlayerName.ToUpperInvariant());
        Assert.That(Sent(proposer), Is.EqualTo(new[] {
            Hex(WeddingPacket.InvitationFound(target.AccountId, target.CharacterId, target.Session.PlayerName)),
        }));

        FindUser("nobody");
        Assert.That(Sent(proposer), Is.EqualTo(new[] { Hex(WeddingPacket.InvitationFound(0, 0, "nobody")) }));
    }

    private void SendInvitation(int count, params long[] guests) {
        Send(proposer, writer => {
            writer.WriteByte(0x1c);
            writer.WriteInt(count);
            foreach (long guest in guests) {
                writer.WriteShort(1);
                writer.WriteLong(guest == target.CharacterId ? target.AccountId : 0);
                writer.WriteLong(guest);
                writer.WriteUnicodeString(guest == target.CharacterId ? target.Session.PlayerName : "x");
            }
        });
    }

    // 0x1c with more guests than the invitation limit (70): the count text, read no further.
    [Test]
    public void SendInvitation_OverTheLimit_Refused() {
        HoldHall();
        SendInvitation(71);
        Assert.That(Sent(proposer), Is.EqualTo(new[] { Error(WeddingError.s_wedding_result_wedding_invitation_count) }));
    }

    // 0x1c with no hall: the no-hall text and no meso taken.
    [Test]
    public void SendInvitation_NoHall_Refused() {
        proposer.Session.Currency.Meso = 5000;
        SendInvitation(1, target.CharacterId);
        Assert.That(Sent(proposer), Is.EqualTo(new[] { Error(WeddingError.s_wedding_result_not_find_weddinghall) }));
        Assert.That(proposer.Session.Currency.Meso, Is.EqualTo(5000));
    }

    // 0x1c whose guests the store does not take (here the hall has no row): the price (1000 a guest) taken for the
    // check goes back, no mail and no ok.
    [Test]
    public void SendInvitation_NotStored_GivesThePriceBack() {
        HoldHall();
        proposer.Session.Currency.Meso = 5000;
        SendInvitation(1, target.CharacterId);
        Assert.That(Sent(proposer), Is.EqualTo(new[] { Error(WeddingError.s_wedding_result_not_find_weddinghall) }));
        Assert.That(proposer.Session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(proposer.Session.Marriage.WeddingHall.GuestList, Is.Empty);
    }

    private void AgreeAnswer(TestGameSession test, WeddingAgreeType type, bool accept) {
        Send(test, writer => {
            writer.WriteByte(0x1e);
            writer.WriteByte((byte) type);
            writer.WriteBool(accept);
        });
    }

    // 0x1e: the couple's answers to the open question decide its result; a question not asked keeps no answer.
    [Test]
    public void MutualAgreeAnswer_BothYes_Agree() {
        var ceremony = new WeddingCeremony(1, proposer.CharacterId, target.CharacterId);
        proposer.Session.Field!.Wedding = ceremony;
        Assume.That(ceremony.Ask(WeddingAgreeType.StartActing), Is.True);

        AgreeAnswer(proposer, WeddingAgreeType.EndActing, false);
        AgreeAnswer(proposer, WeddingAgreeType.StartActing, true);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.StartActing, true), Is.False, "one answer");
        AgreeAnswer(target, WeddingAgreeType.StartActing, true);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.StartActing, true), Is.True);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.EndActing, false), Is.False, "never asked");
    }

    // 0x1e from a player who is not of the couple is not kept.
    [Test]
    public void MutualAgreeAnswer_NotOfTheCouple_Ignored() {
        var ceremony = new WeddingCeremony(1, proposer.CharacterId, 999);
        proposer.Session.Field!.Wedding = ceremony;
        ceremony.Ask(WeddingAgreeType.EndActing);

        AgreeAnswer(target, WeddingAgreeType.EndActing, false);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.EndActing, false), Is.False);
    }

    // 0x22 of an engaged partner: stored in that partner's column of the marriage.
    [Test]
    public void SetCoupleMessage_StoresThePartnersLine() {
        Engage();
        Send(target, writer => {
            writer.WriteByte(0x22);
            writer.WriteUnicodeString("hello");
        });
        Marriage marriage = target.Session.Marriage.Marriage;
        MarriagePartner own = marriage.Partner1.CharacterId == target.CharacterId ? marriage.Partner1 : marriage.Partner2;
        Assert.That(own.Message, Is.EqualTo("hello"));
        using GameStorage.Request db = target.Storage.Context();
        Marriage? stored = db.GetMarriage(target.CharacterId);
        Assert.That(stored, Is.Not.Null);
        MarriagePartner storedOwn = stored!.Partner1.CharacterId == target.CharacterId ? stored.Partner1 : stored.Partner2;
        MarriagePartner storedOther = stored.Partner1.CharacterId == target.CharacterId ? stored.Partner2 : stored.Partner1;
        Assert.That((storedOwn.Message, storedOther.Message), Is.EqualTo(("hello", string.Empty)));
    }

    // 0x22 of a single player: nothing stored, nothing sent.
    [Test]
    public void SetCoupleMessage_Single_Ignored() {
        Send(proposer, writer => {
            writer.WriteByte(0x22);
            writer.WriteUnicodeString("hello");
        });
        Assert.That(Sent(proposer), Is.Empty);
        using GameStorage.Request db = proposer.Storage.Context();
        Assert.That(db.GetMarriage(proposer.CharacterId), Is.Null);
    }

    // 0x25 for a skill the wedding table does not have (and from a single player): no item and nothing sent.
    [Test]
    public void ReceiveSkillItem_UnknownSkill_GivesNothing() {
        Send(proposer, writer => {
            writer.WriteByte(0x25);
            writer.WriteInt(1);
        });
        Assert.That(proposer.TakeSent(), Is.Empty);
    }
}
