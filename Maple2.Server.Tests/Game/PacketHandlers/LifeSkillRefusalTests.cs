using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Mastery 1 (grade reward) and 2 (craft), Fishing 0 in a house, and the end of an improvisation.
public class LifeSkillRefusalTests {
    private const int RewardItemId = 30000001;
    private const int InstrumentItemId = 34000001;

    private TestGameSession test = null!;

    // Mining: grade 1 at 0 without an item, grade 2 at 50 with one item.
    private GameSession StartMastery() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardItemId));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>> {
            [MasteryType.Mining] = new Dictionary<int, MasteryRewardTable.Entry> {
                [1] = new(0, 0, 0, 0),
                [2] = new(50, RewardItemId, 1, 3),
            },
        }));
        test.SetTable(new MasteryRecipeTable(new Dictionary<int, MasteryRecipeTable.Entry>()));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();
        return session;
    }

    private MasteryHandler Handler(GameSession session) {
        return new MasteryHandler {
            ItemMetadata = session.ItemMetadata,
            TableMetadata = test.TableMetadata,
        };
    }

    private void Send(GameSession session, byte command, int id) {
        Handler(session).Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteInt(id);
        }));
    }

    // Mastery 3: i16 error.
    private static void AssertMasteryError(List<byte[]> sent, MasteryError error) {
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(SendOp.Mastery));
        Assert.That(sent[0], Has.Length.EqualTo(5));
        Assert.That(sent[0][2], Is.EqualTo(3));
        Assert.That(sent[0][3] | sent[0][4] << 8, Is.EqualTo((int) error));
    }

    private static int MiningId(int grade) => (int) MasteryType.Mining * 1000 + grade;

    [Test]
    public void Reward_OfAGradeIsGivenOnce() {
        GameSession session = StartMastery();
        session.Player.Value.Character.Mastery.Mining = 50;

        Send(session, 1, MiningId(2));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Exists(packet => TestGameSession.Op(packet) == SendOp.Mastery && packet[2] == 1), Is.True);
        Assert.That(session.Player.Value.Unlock.MasteryRewardsClaimed.ContainsKey(MiningId(2)), Is.True);

        Send(session, 1, MiningId(2));
        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_unknown);
    }

    [Test]
    public void Reward_BelowTheGradeValueIsRefused() {
        GameSession session = StartMastery();
        session.Player.Value.Character.Mastery.Mining = 49;

        Send(session, 1, MiningId(2));

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_invalid_level);
        Assert.That(session.Player.Value.Unlock.MasteryRewardsClaimed, Is.Empty);
    }

    // The id is type * 1000 + grade: 3102 is grade 102 (not in the table), never grade 2 a second time.
    [TestCase(102)]
    [TestCase(1)] // a grade without an item
    [TestCase(3)] // no such grade
    public void Reward_OfAGradeWithoutItemIsRefused(int grade) {
        GameSession session = StartMastery();
        session.Player.Value.Character.Mastery.Mining = 5000;

        Send(session, 1, MiningId(grade));

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_unknown);
        Assert.That(session.Player.Value.Unlock.MasteryRewardsClaimed, Is.Empty);
    }

    [TestCase(-1)]
    [TestCase(2)] // a type that does not exist (0) with grade 2
    public void Reward_OfAnIdOutsideTheTableIsRefused(int id) {
        GameSession session = StartMastery();
        session.Player.Value.Character.Mastery.Mining = 5000;

        Send(session, 1, id);

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_unknown);
    }

    [Test]
    public void Craft_OfAnUnknownRecipeIsRefused() {
        GameSession session = StartMastery();

        Send(session, 2, 12345);

        AssertMasteryError(test.TakeSent(), MasteryError.s_mastery_error_unknown);
    }

    // Fishing 2: i16 error. The client refuses by itself in a house (error 8).
    [Test]
    public void Fishing_InAHouseIsRefused() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterHome();
        session.Fishing = new FishingManager(session, test.TableMetadata, test.ServerTableMetadata);
        test.TakeSent();

        new FishingHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(1);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(SendOp.Fishing));
        Assert.That(sent[0][2], Is.EqualTo(2));
        Assert.That(sent[0][3] | sent[0][4] << 8, Is.EqualTo((int) FishingError.s_fishing_error_ugcmap));
        Assert.That(session.GuideObject, Is.Null);
    }

    private static int FieldInstruments(GameSession session) {
        FieldInfo field = typeof(FieldManager).GetField("fieldInstruments", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((ICollection) field.GetValue(session.Field)!).Count;
    }

    // PlayInstrument 4 ends a score; an improvisation ends with 2, gives nothing and leaves no instrument behind.
    [Test]
    public void Improvisation_EndsWithItsOwnStopAndGivesNothing() {
        test = new TestGameSession();
        ItemMetadata instrument = TestGameSession.Metadata(InstrumentItemId,
            function: new ItemMetadataFunction(ItemFunction.OpenInstrument, "OpenInstrument", "6", false), type: 6);
        test.Register(instrument);
        test.SetTable(new InstrumentTable(new Dictionary<int, InstrumentMetadata> {
            [6] = new(6, 0, 3, 1, 0, 0),
        }));
        Item item = test.Store(instrument);
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        var handler = new InstrumentHandler {
            TableMetadata = test.TableMetadata,
            BanWordStorage = null!,
        };

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(item.Uid);
        }));
        Assert.That(session.Instrument, Is.Not.Null);
        Assert.That(session.Instrument!.Improvising, Is.True);
        Assert.That(FieldInstruments(session), Is.EqualTo(1));
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(4)));
        Assert.That(session.Instrument, Is.Not.Null);
        Assert.That(session.Player.Value.Character.Mastery.Instrument, Is.EqualTo(0));
        Assert.That(test.TakeSentOps(), Has.None.EqualTo(SendOp.ExpUp));

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(2)));
        Assert.That(session.Instrument, Is.Null);
        Assert.That(FieldInstruments(session), Is.EqualTo(0));
        Assert.That(session.Player.Value.Character.Mastery.Instrument, Is.EqualTo(0));
    }
}
