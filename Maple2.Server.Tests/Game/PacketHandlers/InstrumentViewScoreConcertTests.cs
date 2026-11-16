using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// PlayInstrument 0x0074 as the client writes it: mode 10 (view a written score, FUN_140d09c80) is the score's i64
// uid; mode 12 (end the concert, UIMusicConcertDialog) has no body; mode 15 (the concert emote) is an i32 skill id.
// The view answers with mode 10 (i64 uid, the notes as a string); the end clears the stage the performer opened
// with mode 11. The ids are made up.
public class InstrumentViewScoreConcertTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ScoreItemId = 35100001;

    private TestGameSession test = null!;
    private GameSession Session => test.Session;

    private void Handle(byte mode, System.Action<IByteWriter>? body = null) {
        new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = null! }.Handle(Session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            body?.Invoke(writer);
        }));
    }

    private Item StartWithScore(ItemCustomMusicScore? music) {
        test = new TestGameSession();
        test.Store(TestGameSession.Metadata(ScoreItemId, type: 6));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        Item score = Session.Item.Inventory.Filter(item => item.Id == ScoreItemId, InventoryType.FishingMusic).Single();
        score.Music = music;
        test.TakeSent();
        return score;
    }

    private List<byte[]> Views() {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.PlayInstrument && packet[2] == 10).ToList();
    }

    // A written score is shown: its uid and its notes.
    [Test]
    public void ViewScore_AWrittenScore_SendsItsNotes() {
        Item score = StartWithScore(new ItemCustomMusicScore { Author = "writer", AuthorId = 5, Title = "song", Length = 2500, Mml = "cdefg" });

        Handle(10, writer => writer.WriteLong(score.Uid));

        List<byte[]> views = Views();
        Assert.That(views, Has.Count.EqualTo(1));
        var reader = new ByteReader(views[0], 3);
        Assert.Multiple(() => {
            Assert.That(reader.ReadLong(), Is.EqualTo(score.Uid));
            Assert.That(reader.ReadString(), Is.EqualTo("cdefg"));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    // A blank score (no author) and a uid the bag does not have are not answered.
    [Test]
    public void ViewScore_BlankOrUnknown_SendsNothing() {
        Item score = StartWithScore(new ItemCustomMusicScore { Title = "song", Mml = "cdefg" });

        Handle(10, writer => writer.WriteLong(score.Uid));
        Handle(10, writer => writer.WriteLong(score.Uid + 1000));

        Assert.That(Views(), Is.Empty);
    }

    private ConcurrentDictionary<FieldProperty, IFieldProperty> StartStage() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(Constant.PerformanceMapId));
        FieldManager field = Session.Field!;
        var properties = new ConcurrentDictionary<FieldProperty, IFieldProperty>();
        typeof(FieldManager).GetField("fieldProperties", Any)!.SetValue(field, properties);
        typeof(FieldManager).GetField(nameof(FieldManager.PerformanceStage), Any)!.SetValue(field, new PerformanceStageManager(field));
        test.TakeSent();
        return properties;
    }

    // The performer's end (mode 12 after mode 11) takes the concert property off the field and frees the stage.
    [Test]
    public void EndPerform_ByThePerformer_EndsTheConcert() {
        ConcurrentDictionary<FieldProperty, IFieldProperty> properties = StartStage();
        Handle(11);
        Assert.That(properties.ContainsKey(FieldProperty.MusicConcert), Is.True);

        Handle(12);

        Assert.That(properties.ContainsKey(FieldProperty.MusicConcert), Is.False);
        Assert.That(typeof(PerformanceStageManager).GetField("performanceOwner", Any)!.GetValue(Session.Field!.PerformanceStage), Is.Null);
    }

    // An end with no concert running changes nothing.
    [Test]
    public void EndPerform_WithNoConcert_ChangesNothing() {
        ConcurrentDictionary<FieldProperty, IFieldProperty> properties = StartStage();

        Handle(12);

        Assert.That(properties, Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // The concert emotes (applaud, glowstick) are read on the performance map and answered with nothing; the client
    // plays them itself.
    [TestCase(90210001)]
    [TestCase(90210002)]
    public void Emote_OnTheConcertMap_SendsNothing(int skillId) {
        StartStage();

        Handle(15, writer => writer.WriteInt(skillId));

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }
}
