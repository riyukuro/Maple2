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

// The couple dance through the Microgame handler (modes 0xD to 0x19, CoupleDanceRules): banner, invite, request box
// ack, accept, decline, cancel, the answers of each round and the daily rewards. Ids are made up.
public class MicrogameCoupleDanceTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int EventId = 7302;
    private const int Prize = 20000041;
    private const int Bonus = 20000042;
    private const int Rounds = 2;

    private TestGameSession a = null!; // inviter
    private TestGameSession b = null!; // invitee
    private long aId => a.Session.CharacterId;
    private long bId => b.Session.CharacterId;

    private static CoupleDance Event(int goodEndCount = 1) => new(
        [[new RewardItem(Prize, 1, 1)], [new RewardItem(Prize, 1, 3)]], goodEndCount, [new RewardItem(Bonus, 1, 1)],
        ["d0", "d1", "d2"], Rounds, 63000062, []);

    [SetUp]
    public void SetUp() {
        a = new TestGameSession();
        a.AddEvent(EventId, GameEventType.CoupleDance, Event());
        a.Start();
        b = new TestGameSession(a.Options);
        b.AddEvent(EventId, GameEventType.CoupleDance, Event());
        b.Start();
        a.Register(TestGameSession.Metadata(Prize));
        a.Register(TestGameSession.Metadata(Bonus));
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

    private static void Send(TestGameSession sender, short mode, int eventId, Action<ByteWriter>? rest = null) =>
        new MicrogameHandler().Handle(sender.Session, TestGameSession.Packet(w => {
            w.WriteShort(mode);
            w.WriteInt(eventId);
            rest?.Invoke(w);
        }));

    private static byte[][] Games(TestGameSession test) =>
        test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.Microgame).ToArray();

    private void Invite() => Send(a, CoupleDanceRules.Invite, EventId, w => w.WriteLong(bId));
    private void Accept() => Send(b, CoupleDanceRules.Accept, EventId, w => w.WriteLong(aId));

    // Starts the dance; returns the stages both players were sent.
    private short[] Start() {
        Invite();
        Accept();
        byte[][] sent = Games(a);
        Games(b);
        byte[] start = sent.Last();
        var stages = new short[Rounds];
        for (int i = 0; i < Rounds; i++) {
            int at = start.Length - 2 * (Rounds - i);
            stages[i] = BitConverter.ToInt16(start, at);
        }
        return stages;
    }

    private static void Answer(TestGameSession who, int round, int dance) =>
        Send(who, CoupleDanceRules.Answer, EventId, w => {
            w.WriteByte((byte) round);
            w.WriteShort((short) dance);
        });

    private static string? State(TestGameSession test) =>
        test.Session.GameEvent.Get(GameEventUserValueType.CoupleDanceRewardState, EventId, Maple2.Database.Extensions.DateTimeExtensions.ToEpochSeconds(DateTime.Now.AddDays(1).Date)).String();

    [Test]
    public void Banner_OpensForTheEvent() {
        Send(a, CoupleDanceRules.OpenBanner, EventId);

        Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceBanner(EventId).ToArray() }));
    }

    [Test]
    public void Banner_UnknownEvent_IsIgnored() {
        Send(a, CoupleDanceRules.OpenBanner, EventId + 1);

        Assert.That(Games(a), Is.Empty);
    }

    [Test]
    public void Banner_NotWhileInvited() {
        Invite();
        a.TakeSent();

        Send(a, CoupleDanceRules.OpenBanner, EventId);

        Assert.That(Games(a), Is.Empty);
    }

    [Test]
    public void Invite_ReachesTheInvitee() {
        Invite();

        Assert.Multiple(() => {
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceInvite(EventId, aId).ToArray() }));
            Assert.That(Games(a), Is.Empty);
        });
    }

    [Test]
    public void Invite_Self_Fails() {
        Send(a, CoupleDanceRules.Invite, EventId, w => w.WriteLong(aId));

        Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, aId, CoupleDanceRules.ReasonPeerState).ToArray() }));
    }

    [Test]
    public void Invite_TooFar_Fails() {
        b.Session.Player.Position = new Vector3(RpsRules.MaxDistance + 1, 0, 0);

        Invite();

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, bId, CoupleDanceRules.ReasonPeerState).ToArray() }));
            Assert.That(Games(b), Is.Empty);
        });
    }

    [Test]
    public void Invite_UnknownEvent_Fails() {
        Send(a, CoupleDanceRules.Invite, EventId + 1, w => w.WriteLong(bId));

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId + 1, bId, CoupleDanceRules.ReasonPeerState).ToArray() }));
            Assert.That(Games(b), Is.Empty);
        });
    }

    [Test]
    public void Invite_InviteeAlreadyInvited_Fails() {
        Invite();
        a.TakeSent();
        b.TakeSent();

        Send(b, CoupleDanceRules.Invite, EventId, w => w.WriteLong(aId));

        Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, aId, CoupleDanceRules.ReasonPeerState).ToArray() }));
    }

    [Test]
    public void Invite_InviteeInARpsGame_Fails() {
        a.Session.Field!.Rps.Invite(bId, 999, a.Session.Field.FieldTick);

        Invite();

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, bId, CoupleDanceRules.ReasonPeerState).ToArray() }));
            Assert.That(Games(b), Is.Empty);
        });
    }

    [Test]
    public void InviteAck_TellsTheInviter() {
        Invite();
        a.TakeSent();
        b.TakeSent();

        Send(b, CoupleDanceRules.InviteAck, EventId, w => w.WriteLong(aId));

        Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceInviteSent(EventId, bId).ToArray() }));
    }

    [Test]
    public void InviteAck_WithoutInvite_IsIgnored() {
        Send(b, CoupleDanceRules.InviteAck, EventId, w => w.WriteLong(aId));

        Assert.That(Games(a), Is.Empty);
    }

    [Test]
    public void AutoReject_FailsTheInviter_AndDropsTheInvite() {
        Invite();
        a.TakeSent();
        b.TakeSent();

        Send(b, CoupleDanceRules.AutoReject, EventId, w => {
            w.WriteLong(aId);
            w.WriteByte(0);
        });
        byte[][] sent = Games(a);
        Accept();

        Assert.Multiple(() => {
            Assert.That(sent, Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, bId, CoupleDanceRules.ReasonPeerState).ToArray() }));
            Assert.That(Games(b), Is.Empty);
        });
    }

    [Test]
    public void Accept_SendsTheSameStagesToBoth() {
        Invite();
        b.TakeSent();
        Accept();
        byte[][] sentA = Games(a);
        byte[][] sentB = Games(b);

        Assert.Multiple(() => {
            Assert.That(sentA, Has.Length.EqualTo(2));
            Assert.That(sentB, Has.Length.EqualTo(1));
            Assert.That(sentA[1], Is.EqualTo(sentB[0]));
        });
        byte[] start = sentB[0];
        var stages = new short[Rounds];
        for (int i = 0; i < Rounds; i++) {
            stages[i] = BitConverter.ToInt16(start, start.Length - 2 * (Rounds - i));
        }
        Assert.Multiple(() => {
            Assert.That(sentA[0], Is.EqualTo(MicrogamePacket.CoupleDanceAccepted(EventId, bId).ToArray()));
            Assert.That(sentB[0], Is.EqualTo(MicrogamePacket.CoupleDanceStart(EventId, stages).ToArray()));
            Assert.That(stages, Has.All.InRange((short) 0, (short) 2));
        });
    }

    [Test]
    public void Accept_WithoutInvite_IsIgnored() {
        Accept();

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.Empty);
            Assert.That(Games(b), Is.Empty);
        });
    }

    [Test]
    public void Accept_InviterMovedAway_FailsBoth() {
        Invite();
        a.TakeSent();
        b.TakeSent();
        a.Session.Player.Position = new Vector3(-RpsRules.MaxDistance, 0, 0);

        Accept();

        Assert.Multiple(() => {
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, aId, CoupleDanceRules.ReasonAcceptPeerState).ToArray() }));
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, bId, CoupleDanceRules.ReasonPeerState).ToArray() }));
        });
    }

    [Test]
    public void Decline_TellsTheInviter() {
        Invite();
        a.TakeSent();
        b.TakeSent();

        Send(b, CoupleDanceRules.Decline, EventId, w => w.WriteLong(aId));

        Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceDeclined(EventId, bId).ToArray() }));
    }

    [Test]
    public void Decline_ThenAccept_Fails() {
        Invite();
        Send(b, CoupleDanceRules.Decline, EventId, w => w.WriteLong(aId));
        a.TakeSent();
        b.TakeSent();

        Accept();

        Assert.That(Games(a), Is.Empty);
    }

    [Test]
    public void Cancel_PendingInvite_TellsTheInvitee() {
        Invite();
        a.TakeSent();
        b.TakeSent();

        Send(a, CoupleDanceRules.Cancel, EventId, w => w.WriteLong(bId));

        Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceCancelled(EventId, aId).ToArray() }));
    }

    [Test]
    public void Cancel_RunningDance_FailsThePartner_AndEndsIt() {
        Start();

        Send(a, CoupleDanceRules.Cancel, EventId, w => w.WriteLong(bId));
        byte[][] sent = Games(b);
        Answer(b, 0, 0);

        Assert.Multiple(() => {
            Assert.That(sent, Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, aId, CoupleDanceRules.ReasonFailed).ToArray() }));
            Assert.That(Games(a), Is.Empty);
        });
    }

    [Test]
    public void Cancel_WrongPartner_IsIgnored() {
        Start();

        Send(a, CoupleDanceRules.Cancel, EventId, w => w.WriteLong(bId + 5));

        Assert.That(Games(b), Is.Empty);
    }

    [Test]
    public void Answer_NeedsBothBeforeAResult() {
        Start();

        Answer(a, 0, 1);

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.Empty);
            Assert.That(Games(b), Is.Empty);
        });
    }

    [Test]
    public void Answer_SameDance_IsGood_AndSwapsPerSide() {
        Start();

        Answer(a, 0, 2);
        Answer(b, 0, 2);

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceResult(EventId, 0, true, 2, 2).ToArray() }));
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceResult(EventId, 0, true, 2, 2).ToArray() }));
        });
    }

    [Test]
    public void Answer_DifferentDances_IsNotGood_EachSeesOwnFirst() {
        Start();

        Answer(b, 0, 0);
        Answer(a, 0, 2);

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceResult(EventId, 0, false, 2, 0).ToArray() }));
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceResult(EventId, 0, false, 0, 2).ToArray() }));
        });
    }

    [Test]
    public void Answer_SecondAnswerOfTheRound_IsRefused() {
        Start();

        Answer(a, 0, 1);
        Answer(a, 0, 2);
        Answer(b, 0, 1);

        // The second answer did not replace the first: the dances are equal.
        Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceResult(EventId, 0, true, 1, 1).ToArray() }));
    }

    [TestCase(1)]
    [TestCase(5)]
    public void Answer_WrongRound_IsIgnored(int round) {
        Start();

        Answer(a, round, 0);
        Answer(b, round, 0);

        Assert.That(Games(a), Is.Empty);
    }

    [TestCase(-1)]
    [TestCase(3)]
    public void Answer_DanceOutsideTheEvent_IsIgnored(int dance) {
        Start();

        Answer(a, 0, dance);
        Answer(b, 0, dance);

        Assert.That(Games(a), Is.Empty);
    }

    [Test]
    public void Answer_WithoutADance_IsIgnored() {
        Answer(a, 0, 0);

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.Empty);
            Assert.That(State(a), Is.Null.Or.Empty);
        });
    }

    [Test]
    public void Answer_OtherEventId_EndsTheDanceForBoth() {
        Start();

        Send(a, CoupleDanceRules.Answer, EventId + 1, w => {
            w.WriteByte(0);
            w.WriteShort(0);
        });

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, bId, CoupleDanceRules.ReasonFailed).ToArray() }));
            Assert.That(Games(b), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, aId, CoupleDanceRules.ReasonFailed).ToArray() }));
        });
    }

    [Test]
    public void Answer_LastRound_RecordsTheFirstDailyReward_ForBoth() {
        Start();

        Answer(a, 0, 0);
        Answer(b, 0, 1);
        Answer(a, 1, 1);
        Answer(b, 1, 2);

        // Score 0 of the needed 1: a normal end.
        Assert.Multiple(() => {
            Assert.That(State(a), Is.EqualTo("1,0"));
            Assert.That(State(b), Is.EqualTo("1,0"));
        });
    }

    [Test]
    public void Answer_LastRound_GoodEnd_RecordsTheBonusState() {
        Start();

        Answer(a, 0, 0);
        Answer(b, 0, 0);
        Answer(a, 1, 1);
        Answer(b, 1, 2);

        Assert.Multiple(() => {
            Assert.That(State(a), Is.EqualTo("2,0"));
            Assert.That(State(b), Is.EqualTo("2,0"));
        });
    }

    [Test]
    public void Answer_AfterTheLastRound_TheDanceIsOver() {
        Start();
        Answer(a, 0, 0);
        Answer(b, 0, 0);
        Answer(a, 1, 0);
        Answer(b, 1, 0);
        a.TakeSent();
        b.TakeSent();

        Answer(a, 2, 0);
        Answer(b, 2, 0);

        Assert.That(Games(a), Is.Empty);
    }

    [Test]
    public void Answer_SecondDanceOfTheDay_TakesTheNextReward() {
        Start();
        Answer(a, 0, 0);
        Answer(b, 0, 1);
        Answer(a, 1, 0);
        Answer(b, 1, 1);
        // The inviter's cooldown runs on the field tick, so the partner invites the second time.
        Send(b, CoupleDanceRules.Invite, EventId, w => w.WriteLong(aId));
        Send(a, CoupleDanceRules.Accept, EventId, w => w.WriteLong(bId));
        Answer(a, 0, 0);
        Answer(b, 0, 0);
        Answer(a, 1, 0);
        Answer(b, 1, 1);

        Assert.Multiple(() => {
            Assert.That(State(a), Is.EqualTo("1,2"));
            Assert.That(State(b), Is.EqualTo("1,2"));
        });
    }

    [Test]
    public void Invite_SameInviterAgainWithinTheCooldown_Fails() {
        Invite();
        Send(b, CoupleDanceRules.Decline, EventId, w => w.WriteLong(aId));
        a.TakeSent();
        b.TakeSent();

        Invite();

        Assert.Multiple(() => {
            Assert.That(Games(a), Is.EqualTo(new[] { MicrogamePacket.CoupleDanceFailed(EventId, bId, CoupleDanceRules.ReasonPeerState).ToArray() }));
            Assert.That(Games(b), Is.Empty);
        });
    }

    [Test]
    public void ClaimReward_GivesTheStepItemsOnce() {
        Start();
        Answer(a, 0, 0);
        Answer(b, 0, 1);
        Answer(a, 1, 0);
        Answer(b, 1, 1);
        a.TakeSent();

        Send(a, CoupleDanceRules.ClaimReward, EventId, w => w.WriteInt(0));
        Send(a, CoupleDanceRules.ClaimReward, EventId, w => w.WriteInt(0));

        Assert.Multiple(() => {
            Assert.That(a.Session.Item.Inventory.Find(Prize).Sum(i => i.Amount), Is.EqualTo(1));
            Assert.That(a.Session.Item.Inventory.Find(Bonus), Is.Empty);
            Assert.That(State(a), Is.EqualTo("3,0"));
        });
    }

    [Test]
    public void ClaimReward_GoodEnd_AddsTheBonusItems() {
        Start();
        Answer(a, 0, 0);
        Answer(b, 0, 0);
        Answer(a, 1, 0);
        Answer(b, 1, 0);
        a.TakeSent();

        Send(a, CoupleDanceRules.ClaimReward, EventId, w => w.WriteInt(0));

        Assert.Multiple(() => {
            Assert.That(a.Session.Item.Inventory.Find(Prize).Sum(i => i.Amount), Is.EqualTo(1));
            Assert.That(a.Session.Item.Inventory.Find(Bonus).Sum(i => i.Amount), Is.EqualTo(1));
            Assert.That(State(a), Is.EqualTo("4,0"));
        });
    }

    [Test]
    public void ClaimReward_StepNotCompleted_GivesNothing() {
        Start();
        Answer(a, 0, 0);
        Answer(b, 0, 1);
        Answer(a, 1, 0);
        Answer(b, 1, 1);

        Send(a, CoupleDanceRules.ClaimReward, EventId, w => w.WriteInt(1));

        Assert.Multiple(() => {
            Assert.That(a.Session.Item.Inventory.Find(Prize), Is.Empty);
            Assert.That(State(a), Is.EqualTo("1,0"));
        });
    }

    [TestCase(-1)]
    [TestCase(2)]
    public void ClaimReward_IndexOutOfRange_IsIgnored(int step) {
        Send(a, CoupleDanceRules.ClaimReward, EventId, w => w.WriteInt(step));

        Assert.That(a.Session.Item.Inventory.Find(Prize), Is.Empty);
    }

    [Test]
    public void ClaimReward_UnknownEvent_IsIgnored() {
        Start();
        Answer(a, 0, 0);
        Answer(b, 0, 1);
        Answer(a, 1, 0);
        Answer(b, 1, 1);

        Send(a, CoupleDanceRules.ClaimReward, EventId + 1, w => w.WriteInt(0));

        Assert.That(a.Session.Item.Inventory.Find(Prize), Is.Empty);
    }
}
