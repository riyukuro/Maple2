using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Game;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// HomeAction 10 (the home OX quiz event) as the host's create dialog drives it: open (2), settings (0), the confirm's
// create (3), and the commands a player outside a stadium may not use. The client's keys name each refusal.
public class HomeActionUgcOxTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private readonly HomeActionHandler handler = new();

    // The ticket is stored before the start: the session loads its inventory then.
    private void Begin(bool ticket) {
        test = new TestGameSession();
        if (ticket) {
            test.Store(TestGameSession.Metadata(UgcOxRules.TicketItemId, type: 10));
        }
        test.Start();
        var factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        typeof(FieldManager.Factory).GetField("ugcOxEvents", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(factory, new ConcurrentDictionary<long, UgcOxEvent>());
        typeof(GameSession).GetField("<FieldFactory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, factory);
        test.EnterHome();
        session.Currency.Meso = 50000;
        test.TakeSent();
    }

    private void Quiz(byte sub, Action<Maple2.PacketLib.Tools.ByteWriter>? body = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteByte(sub);
            body?.Invoke(writer);
        }));
    }

    private void Settings(long fee, int max, int share) => Quiz(0, writer => {
        writer.WriteLong(fee);
        writer.WriteInt(max);
        writer.WriteInt(share);
    });

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Open_WithTheTicket_AnswersTheFees() {
        Begin(ticket: true);

        Quiz(2);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(HomeActionPacket.UgcOxFees(UgcOxRules.Fees)) }));
    }

    [Test]
    public void Open_WithoutTheTicket_RefusesWithTheItemKey() {
        Begin(ticket: false);
        Quiz(2);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(NoticePacket.Message("s_ugcox_create_fail_item_lack")) }));
    }

    [Test]
    public void Settings_Valid_AsksForTheConfirm() {
        Begin(ticket: true);

        Settings(2000, 20, 50);

        Assert.Multiple(() => {
            Assert.That(session.UgcOxPending, Is.EqualTo(new UgcOxRules.Settings(2000, 20, 50)));
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(HomeActionPacket.UgcOxConfirm(session.PlayerName, 2000, 20, 50)) }));
        });
    }

    [TestCase(1500L, 20, 50, TestName = "Settings_FeeNotInTheDialog_Ignored")]
    [TestCase(2000L, 25, 50, TestName = "Settings_MaxNotInTheDialog_Ignored")]
    [TestCase(2000L, 20, 55, TestName = "Settings_ShareNotInTheDialog_Ignored")]
    public void Settings_Forged_Ignored(long fee, int max, int share) {
        Begin(ticket: true);

        Settings(fee, max, share);

        Assert.Multiple(() => {
            Assert.That(session.UgcOxPending, Is.Null);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void Settings_FeeBeyondTheMeso_RefusesWithTheFeeKey() {
        Begin(ticket: true);
        session.Currency.Meso = 1999;
        test.TakeSent();

        Settings(2000, 20, 50);

        Assert.Multiple(() => {
            Assert.That(session.UgcOxPending, Is.Null);
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(NoticePacket.Message("s_ugcox_create_fail_fee_lack")) }));
        });
    }

    [Test]
    public void Create_WithoutTheConfirmedSettings_Silent() {
        Begin(ticket: true);

        Quiz(3);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Create_AfterTheTicketIsGone_RefusesAndClearsThePending() {
        Begin(ticket: false);
        session.UgcOxPending = new UgcOxRules.Settings(2000, 20, 50);

        Quiz(3);

        Assert.Multiple(() => {
            Assert.That(session.UgcOxPending, Is.Null);
            Assert.That(session.Currency.Meso, Is.EqualTo(50000));
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(NoticePacket.Message("s_ugcox_create_fail_item_lack")) }));
        });
    }

    [Test]
    public void Enter_And_Promote_WithoutAnEvent_Silent() {
        Begin(ticket: false);
        Quiz(5, writer => {
            writer.WriteInt(0);
            writer.WriteInt(0);
        });
        Quiz(1);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The host's stadium commands (start, finish, question, answer, wall, resurrect) are the host's, inside its stadium.
    [TestCase((byte) 7)]
    [TestCase((byte) 8)]
    [TestCase((byte) 10)]
    [TestCase((byte) 11)]
    [TestCase((byte) 17)]
    [TestCase((byte) 18)]
    public void StadiumCommands_OutsideAStadium_Ignored(byte sub) {
        Begin(ticket: false);
        Quiz(sub, writer => writer.WriteUnicodeString("q"));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The test field stands in for the stadium: the event is set on it by hand, as the factory does.
    private UgcOxEvent Host(long hostCharacterId = -1) {
        var ox = new UgcOxEvent(session.AccountId, hostCharacterId == -1 ? session.CharacterId : hostCharacterId, "host", 2000001,
            new UgcOxRules.Settings(1000, 10, 50));
        session.Field!.SetUgcOx(ox);
        return ox;
    }

    [Test]
    public void Start_WithTooFewEntrants_CancelsTheEvent() {
        Begin(ticket: false);
        UgcOxEvent ox = Host();
        Quiz(7);
        Assert.Multiple(() => {
            Assert.That(ox.Canceled, Is.True);
            Assert.That(ox.Phase, Is.EqualTo(UgcOxPhase.Welcome));
        });
    }

    [Test]
    public void Start_WithEntrants_RunsTheGame() {
        Begin(ticket: false);
        UgcOxEvent ox = Host();
        ox.Enter(11, _ => true);
        ox.Enter(12, _ => true);
        Quiz(7);
        Assert.That(ox.Phase, Is.EqualTo(UgcOxPhase.Running));
    }

    [Test]
    public void Finish_AtTheWelcome_Cancels() {
        Begin(ticket: false);
        UgcOxEvent ox = Host();
        Quiz(8);
        Assert.Multiple(() => {
            Assert.That(ox.Canceled, Is.True);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void Finish_BeforeTheSecondRound_IsRefusedWithTheClientKey() {
        Begin(ticket: false);
        UgcOxEvent ox = Host();
        ox.Enter(11, _ => true);
        ox.Enter(12, _ => true);
        ox.Start();
        ox.NextRound();
        Quiz(8);
        Assert.Multiple(() => {
            Assert.That(ox.Finished, Is.False);
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(NoticePacket.Message("s_ugcox_finish_fail_by_round")) }));
        });
    }

    [Test]
    public void Finish_FromTheSecondRound_EndsAfterTheRound() {
        Begin(ticket: false);
        UgcOxEvent ox = Host();
        ox.Enter(11, _ => true);
        ox.Enter(12, _ => true);
        ox.Start();
        ox.NextRound();
        ox.NextRound();
        Quiz(8);
        Assert.That(ox.Finished, Is.True);
    }

    [Test]
    public void Question_WhileAsked_IsKeptTrimmed() {
        Begin(ticket: false);
        UgcOxEvent ox = Host();
        ox.Enter(11, _ => true);
        ox.Enter(12, _ => true);
        ox.Start();
        ox.SetHostStep(UgcOxHostStep.InputQuiz);
        Quiz(10, writer => writer.WriteUnicodeString("  Is it?  "));
        Assert.That(ox.Question, Is.EqualTo("Is it?"));
    }

    [Test]
    public void Question_WhenNotAsked_Ignored() {
        Begin(ticket: false);
        UgcOxEvent ox = Host();
        Quiz(10, writer => writer.WriteUnicodeString("Is it?"));
        Assert.That(ox.Question, Is.Null);
    }

    [TestCase((byte) 1, true)]
    [TestCase((byte) 0, false)]
    public void Answer_WhileAsked_IsKept(byte value, bool expected) {
        Begin(ticket: false);
        UgcOxEvent ox = Host();
        ox.Enter(11, _ => true);
        ox.Enter(12, _ => true);
        ox.Start();
        ox.SetHostStep(UgcOxHostStep.SelectAnswer);
        Quiz(11, writer => writer.WriteByte(value));
        Assert.That(ox.Answer, Is.EqualTo(expected));
    }

    [Test]
    public void RemoveWall_WhileAnswerAsked_SetsTheFlag() {
        Begin(ticket: false);
        UgcOxEvent ox = Host();
        ox.Enter(11, _ => true);
        ox.Enter(12, _ => true);
        ox.Start();
        ox.SetHostStep(UgcOxHostStep.SelectAnswer);
        Quiz(17);
        Assert.That(ox.WallRemoved, Is.True);
    }

    [TestCase((byte) 9)] // leave: a pause only
    [TestCase((byte) 16)] // judge: the script judges
    [TestCase((byte) 99)] // unknown
    public void HostCommandsWithNoEffect_ChangeNothing(byte sub) {
        Begin(ticket: false);
        UgcOxEvent ox = Host();
        Quiz(sub);
        Assert.Multiple(() => {
            Assert.That(ox.Canceled, Is.False);
            Assert.That(ox.Phase, Is.EqualTo(UgcOxPhase.Welcome));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void StadiumCommands_FromAnotherCharacter_Ignored() {
        Begin(ticket: false);
        UgcOxEvent ox = Host(session.CharacterId + 1);
        Quiz(8);
        Quiz(7);
        Assert.That(ox.Canceled, Is.False);
    }
}
