using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The rock-paper-scissors game through the Microgame handler (microgamemanagercli.cpp): invite, request box ack,
// accept, decline, cancel, the card choice and the play-count rewards. Ids are made up.
public class MicrogameRpsTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int EventId = 7301;
    private const int Ticket = 20000031;
    private const int Prize = 20000032;

    private TestGameSession a = null!; // inviter
    private TestGameSession b = null!; // invitee
    private long aId => a.Session.CharacterId;
    private long bId => b.Session.CharacterId;

    private static Rps Event() => new(Ticket, [new Rps.RewardData(1, [new RewardItem(Prize, 1, 2)])], string.Empty);

    [SetUp]
    public void SetUp() {
        a = new TestGameSession();
        a.AddEvent(EventId, GameEventType.RPS, Event());
        a.Store(TestGameSession.Metadata(Ticket));
        a.Start();
        b = new TestGameSession(a.Options);
        b.AddEvent(EventId, GameEventType.RPS, Event());
        b.Store(TestGameSession.Metadata(Ticket));
        b.Start();
        a.Register(TestGameSession.Metadata(Prize));
        FieldManager field = a.Session.Field!;
        // The test field is built without its initializers.
        typeof(FieldManager).GetField("<Rps>k__BackingField", Any)!.SetValue(field, new RpsMatchTable());
        typeof(FieldManager).GetField("<CoupleDance>k__BackingField", Any)!.SetValue(field, new CoupleDanceMatchTable());
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(b.Session.Player, 2);
        field.Players[2] = b.Session.Player;
        b.Session.Field = field;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(b.Session.Player, field);
        a.Session.Player.Position = Vector3.Zero;
        b.Session.Player.Position = new Vector3(100, 0, 0);
        a.TakeSent();
        b.TakeSent();
    }

    private static void Send(TestGameSession sender, short mode, Action<ByteWriter>? rest = null) =>
        new MicrogameHandler().Handle(sender.Session, TestGameSession.Packet(w => {
            w.WriteShort(mode);
            rest?.Invoke(w);
        }));

    private static byte[][] Games(TestGameSession test) =>
        test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.Microgame).ToArray();

    private void Invite() => Send(a, RpsRules.Invite, w => w.WriteLong(bId));
    private void Accept() => Send(b, RpsRules.Accept, w => w.WriteLong(aId));
    private void Start() {
        Invite();
        Accept();
        a.TakeSent();
        b.TakeSent();
    }
    private static void Play(TestGameSession who, int card) => Send(who, RpsRules.PlayCard, w => w.WriteInt(card));

    [Test]
    public void Banner_OpensOnlyWithATicket() {
        Send(a, RpsRules.OpenBanner);
        Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Banner().ToArray() }));

        a.Session.Item.Inventory.Consume(a.Session.Item.Inventory.Find(Ticket).Single().Uid, 1);
        a.TakeSent();
        Send(a, RpsRules.OpenBanner);
        Assert.That(Games(a), Is.Empty);
    }

    [Test]
    public void Banner_NotWhileInAGame() {
        Invite();
        a.TakeSent();

        Send(a, RpsRules.OpenBanner);

        Assert.That(Games(a), Is.Empty);
    }

    [Test]
    public void Invite_ReachesTheInvitee() {
        Invite();

        Assert.Multiple(() => {
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.Invite(aId).ToArray() }));
            Assert.That(Games(a), Is.Empty);
        });
    }

    [Test]
    public void Invite_Self_Fails() {
        Send(a, RpsRules.Invite, w => w.WriteLong(aId));

        Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Failed(aId, RpsRules.ReasonPeerState).ToArray() }));
    }

    [Test]
    public void Invite_TooFar_Fails() {
        b.Session.Player.Position = new Vector3(RpsRules.MaxDistance + 1, 0, 0);

        Invite();

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Failed(bId, RpsRules.ReasonPeerState).ToArray() }));
            Assert.That(Games(b), Is.Empty);
        });
    }

    [Test]
    public void Invite_AtTheLimit_Works() {
        b.Session.Player.Position = new Vector3(RpsRules.MaxDistance, 0, 0);

        Invite();

        Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.Invite(aId).ToArray() }));
    }

    [Test]
    public void Invite_InviteeWithoutATicket_Fails() {
        b.Session.Item.Inventory.Consume(b.Session.Item.Inventory.Find(Ticket).Single().Uid, 1);
        b.TakeSent();

        Invite();

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Failed(bId, RpsRules.ReasonPeerState).ToArray() }));
            Assert.That(Games(b), Is.Empty);
        });
    }

    [Test]
    public void Invite_AlreadyInvited_Fails() {
        Invite();
        a.TakeSent();
        b.TakeSent();

        Invite();

        Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Failed(bId, RpsRules.ReasonPeerState).ToArray() }));
    }

    [Test]
    public void InviteAck_TellsTheInviterToWait_OnlyForARealInvite() {
        Send(b, RpsRules.InviteAck, w => w.WriteLong(aId));
        Assert.That(Games(a), Is.Empty);

        Invite();
        a.TakeSent();
        Send(b, RpsRules.InviteAck, w => w.WriteLong(aId));

        Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.InviteSent(bId).ToArray() }));
    }

    [Test]
    public void AutoReject_FailsTheInviter() {
        Invite();
        a.TakeSent();
        b.TakeSent();

        Send(b, RpsRules.AutoReject, w => {
            w.WriteLong(aId);
            w.WriteByte(0);
        });

        Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Failed(bId, RpsRules.ReasonPeerState).ToArray() }));
        // The invite is gone: a new one goes through (the five second invite cooldown aside, the table is free).
        Assert.That(a.Session.Field!.Rps.IsBusy(aId), Is.False);
    }

    [Test]
    public void Accept_StartsARoundForBoth() {
        Invite();
        a.TakeSent();
        b.TakeSent();

        Accept();

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Accepted(bId).ToArray(), MicrogamePacket.Round().ToArray() }));
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.Round().ToArray() }));
        });
    }

    [Test]
    public void Accept_WithoutAnInvite_IsIgnored() {
        Accept();

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.Empty);
            Assert.That(Games(b), Is.Empty);
        });
    }

    // The invitee spent the ticket after the invite: the accept fails for both and the invite is gone.
    [Test]
    public void Accept_InviteeLostTheTicket_FailsBoth() {
        Invite();
        b.Session.Item.Inventory.Consume(b.Session.Item.Inventory.Find(Ticket).Single().Uid, 1);
        a.TakeSent();
        b.TakeSent();

        Accept();

        Assert.Multiple(() => {
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.Failed(aId, RpsRules.ReasonAcceptPeerState).ToArray() }));
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Failed(bId, RpsRules.ReasonPeerState).ToArray() }));
            Assert.That(a.Session.Field!.Rps.IsBusy(aId), Is.False);
        });
    }

    [Test]
    public void Decline_TellsTheInviter() {
        Invite();
        a.TakeSent();
        b.TakeSent();

        Send(b, RpsRules.Decline, w => w.WriteLong(aId));

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Declined(bId).ToArray() }));
            Assert.That(a.Session.Field!.Rps.IsBusy(aId), Is.False);
        });
    }

    [Test]
    public void Decline_WithoutAnInvite_IsIgnored() {
        Send(b, RpsRules.Decline, w => w.WriteLong(aId));

        Assert.That(Games(a), Is.Empty);
    }

    [Test]
    public void Cancel_PendingInvite_TellsTheInvitee() {
        Invite();
        a.TakeSent();
        b.TakeSent();

        Send(a, RpsRules.Cancel, w => w.WriteLong(bId));

        Assert.Multiple(() => {
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.Cancelled(aId).ToArray() }));
            Assert.That(a.Session.Field!.Rps.IsBusy(bId), Is.False);
        });
    }

    [Test]
    public void Cancel_RunningGame_FailsThePartner() {
        Start();

        Send(a, RpsRules.Cancel, w => w.WriteLong(bId));

        Assert.Multiple(() => {
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.Failed(aId, RpsRules.ReasonFailed).ToArray() }));
            Assert.That(a.Session.Field!.Rps.IsBusy(aId), Is.False);
        });
    }

    [Test]
    public void Cancel_NamingSomeoneElse_KeepsTheGame() {
        Start();

        Send(a, RpsRules.Cancel, w => w.WriteLong(bId + 99));

        Assert.Multiple(() => {
            Assert.That(Games(b), Is.Empty);
            Assert.That(a.Session.Field!.Rps.IsBusy(aId), Is.True);
        });
    }

    [Test]
    public void PlayCard_WaitsForTheSecondCard() {
        Start();

        Play(a, RpsRules.Rock);

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.Empty);
            Assert.That(Games(b), Is.Empty);
        });
    }

    [Test]
    public void PlayCard_NotInAGame_IsIgnored() {
        Play(a, RpsRules.Rock);

        Assert.That(Games(a), Is.Empty);
    }

    [TestCase(RpsRules.Rock, RpsRules.Scissors, RpsRules.Win, RpsRules.Lose)]
    [TestCase(RpsRules.Paper, RpsRules.Paper, RpsRules.Draw, RpsRules.Draw)]
    [TestCase(RpsRules.Scissors, RpsRules.Rock, RpsRules.Lose, RpsRules.Win)]
    public void PlayCard_BothIn_EachGetsTheirResultAndPaysATicket(int cardA, int cardB, byte resultA, byte resultB) {
        Start();

        Play(a, cardA);
        Play(b, cardB);

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Result(resultA, cardA, cardB).ToArray() }));
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.Result(resultB, cardB, cardA).ToArray() }));
            Assert.That(a.Session.Item.Inventory.Find(Ticket), Is.Empty);
            Assert.That(b.Session.Item.Inventory.Find(Ticket), Is.Empty);
            Assert.That(a.Session.GameEvent.Peek(GameEventUserValueType.RPSDailyMatches, EventId), Is.EqualTo("1"));
            Assert.That(b.Session.GameEvent.Peek(GameEventUserValueType.RPSDailyMatches, EventId), Is.EqualTo("1"));
        });
    }

    [Test]
    public void PlayCard_UnknownCard_IsIgnored() {
        Start();

        Play(a, 3);
        Play(b, RpsRules.Rock);

        Assert.That(Games(a), Is.Empty);
    }

    [Test]
    public void PlayCard_PlayerSpentTheTicketMidGame_FailsBoth() {
        Start();
        b.Session.Item.Inventory.Consume(b.Session.Item.Inventory.Find(Ticket).Single().Uid, 1);
        a.TakeSent();
        b.TakeSent();

        Play(a, RpsRules.Rock);
        Play(b, RpsRules.Paper);

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.Failed(bId, RpsRules.ReasonFailed).ToArray() }));
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.Failed(aId, RpsRules.ReasonFailed).ToArray() }));
            Assert.That(a.Session.Item.Inventory.Find(Ticket).Sum(i => i.Amount), Is.EqualTo(1));
        });
    }

    private static void SetMatches(TestGameSession test, int count) {
        test.Session.GameEvent.Get(GameEventUserValueType.RPSDailyMatches, EventId, Maple2.Database.Extensions.DateTimeExtensions.ToEpochSeconds(DateTime.Now.AddDays(1).Date));
        test.Session.GameEvent.Set(EventId, GameEventUserValueType.RPSDailyMatches, count);
    }

    [Test]
    public void ClaimReward_BelowThePlayCount_GivesNothing() {
        Send(a, RpsRules.ClaimReward, w => w.WriteInt(0));

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.Empty);
            Assert.That(a.Session.Item.Inventory.Find(Prize), Is.Empty);
        });
    }

    [Test]
    public void ClaimReward_AtThePlayCount_GivesTheItemsOnce() {
        SetMatches(a, 1);
        a.TakeSent();

        Send(a, RpsRules.ClaimReward, w => w.WriteInt(0));
        byte[][] first = Games(a);
        Send(a, RpsRules.ClaimReward, w => w.WriteInt(0));

        Assert.Multiple(() => {
            Assert.That(first, Has.Length.EqualTo(1));
            Assert.That(Games(a), Is.Empty);
            Assert.That(a.Session.Item.Inventory.Find(Prize).Sum(i => i.Amount), Is.EqualTo(2));
        });
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void ClaimReward_IndexOutOfRange_IsIgnored(int index) {
        SetMatches(a, 5);

        Send(a, RpsRules.ClaimReward, w => w.WriteInt(index));

        Assert.That(a.Session.Item.Inventory.Find(Prize), Is.Empty);
    }

    [Test]
    public void UnknownMode_IsIgnored() {
        Send(a, 0x30);

        Assert.That(Games(a), Is.Empty);
    }
}
