using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Maple2.Server.Tests.Game;

// The TreeWatering game event (client GameEventTreeWatering, TreeWateringContext): the reader of its three values,
// the replay's move of its steps and emotion days, the packet the client reads and the rules of a watering and an
// emotion. Made-up ids.
public class TreeWateringTests {
    private const string Value1 = "<ms2><tree_step><step step=\"1\" begin=\"2019-11-11-00-00\" end=\"2019-11-12-23-59-59\" />"
                                  + "<step step=\"2\" begin=\"2019-11-13-00-00\" end=\"2019-11-14-23-59-59\" />"
                                  + "<step step=\"2\" begin=\"2019-11-13-00-00\" end=\"2019-11-17-23-59-59\" />"
                                  + "<step step=\"3\" begin=\"bad\" end=\"2019-11-17-23-59-59\" /></tree_step>"
                                  + "<emotion_day><time begin=\"2019-11-17-00-00\" end=\"2019-11-17-23-59-59\" /></emotion_day></ms2>";
    private const string Value2 = "<ms2><watering_reward MaxRewardCountDaily=\"10\" itemID=\"30001432\" grade=\"1\" count=\"1\" />"
                                  + "<emotion_reward MaxRewardCountDaily=\"1\" itemID=\"20302918\" grade=\"2\" count=\"3\" /></ms2>";
    private const string Value3 = "<ms2><resource FieldID=\"2000064\" TreeNameTag=\"TreeWateringEvent201906\" WaterPotItemID=\"30001431\""
                                  + " WateringAnimation=\"Functobj_Feed_A01\" EmotionAnimation=\"Emotion_Clapping_A\" />"
                                  + "<tree_resource><step step=\"1\" nif_id=\"Interaction_A01\" /><step step=\"2\" nif_id=\"Interaction_A02\" />"
                                  + "</tree_resource></ms2>";

    private static readonly DateTime Day = new(2019, 11, 17);

    [Test]
    public void Reader_AllThreeValues() {
        TreeWatering? tree = EventXmlReader.TreeWatering(Value1, Value2, Value3);
        Assert.That(tree, Is.Not.Null);
        // One step per number, the last; a step with a time that is not one is left out.
        Assert.That(tree!.Steps, Is.EqualTo(new[] {
            new TreeWatering.Step(1, new DateTime(2019, 11, 11), new DateTime(2019, 11, 12, 23, 59, 59)),
            new TreeWatering.Step(2, new DateTime(2019, 11, 13), new DateTime(2019, 11, 17, 23, 59, 59)),
        }));
        Assert.That(tree.EmotionDays, Is.EqualTo(new[] { new TreeWatering.Period(Day, Day.AddDays(1).AddSeconds(-1)) }));
        Assert.That((tree.WaterLimit, tree.WaterReward), Is.EqualTo((10, new RewardItem(30001432, 1, 1))));
        Assert.That((tree.EmotionLimit, tree.EmotionReward), Is.EqualTo((1, new RewardItem(20302918, 2, 3))));
        Assert.That(tree.Resources, Is.EqualTo(new[] { new TreeWatering.Resource(1, "Interaction_A01"), new TreeWatering.Resource(2, "Interaction_A02") }));
        Assert.That((tree.FieldId, tree.TreeName, tree.WaterPotItemId), Is.EqualTo((2000064, "TreeWateringEvent201906", 30001431)));
        Assert.That((tree.WaterAnimation, tree.EmotionAnimation, tree.EmotionDayEffect),
            Is.EqualTo(("Functobj_Feed_A01", "Emotion_Clapping_A", "")));
    }

    // The client keeps no event when a value is not an <ms2> document.
    [TestCase(null, Value2, Value3)]
    [TestCase(Value1, "", Value3)]
    [TestCase(Value1, Value2, "<ms2>")]
    [TestCase(Value1, Value2, "<tree />")]
    public void Reader_AValueMissing(string? value1, string? value2, string? value3) {
        Assert.That(EventXmlReader.TreeWatering(value1, value2, value3), Is.Null);
    }

    // Empty documents: no steps, days or resources, limits 0, empty rewards and names.
    [Test]
    public void Reader_EmptyDocuments() {
        TreeWatering tree = EventXmlReader.TreeWatering("<ms2 />", "<ms2 />", "<ms2 />")!;
        Assert.That((tree.Steps.Length, tree.EmotionDays.Length, tree.Resources.Length), Is.EqualTo((0, 0, 0)));
        Assert.That((tree.WaterLimit, tree.WaterReward, tree.EmotionLimit, tree.EmotionReward),
            Is.EqualTo((0, default(RewardItem), 0, default(RewardItem))));
        Assert.That((tree.FieldId, tree.TreeName, tree.WaterPotItemId), Is.EqualTo((0, "", 0)));
    }

    private static GameEventMetadata Event(int id, GameEventData data, DateTime start, DateTime end) {
        return new GameEventMetadata(id, GameEventType.TreeWatering, start, end, TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", "");
    }

    private static TreeWatering Tree(TreeWatering.Period[]? days = null, int waterLimit = 2, int emotionLimit = 1) {
        return new TreeWatering([new TreeWatering.Step(1, Day.AddDays(-6), Day.AddDays(-4))], days ?? [new TreeWatering.Period(Day, Day.AddDays(1))],
            waterLimit, new RewardItem(30001432, 1, 1), emotionLimit, new RewardItem(20302918, 1, 1),
            [new TreeWatering.Resource(1, "Interaction_A01")], 2000064, "TreeWateringEvent201906", 30001431, "Functobj_Feed_A01", "Emotion_Clapping_A", "");
    }

    // The replay moves the steps and the emotion days by as much as the event.
    [Test]
    public void Replay_MovesStepsAndDays() {
        GameEventMetadata ended = Event(900119, Tree(), Day.AddDays(-6), Day.AddDays(1).AddSeconds(-1));
        Assert.That(AttendGiftReplay.Types, Does.Contain(GameEventType.TreeWatering));
        GameEventMetadata moved = AttendGiftReplay.Redate(ended, new DateTime(2026, 11, 12, 15, 0, 0));
        Assert.That(moved.StartTime, Is.EqualTo(new DateTime(2026, 11, 12)));
        TimeSpan shift = moved.StartTime - ended.StartTime;
        var tree = (TreeWatering) moved.Data;
        Assert.That(tree.Steps.Single(), Is.EqualTo(new TreeWatering.Step(1, Day.AddDays(-6) + shift, Day.AddDays(-4) + shift)));
        Assert.That(tree.EmotionDays.Single(), Is.EqualTo(new TreeWatering.Period(Day + shift, Day.AddDays(1) + shift)));
        Assert.That(tree.Resources, Is.EqualTo(Tree().Resources));
    }

    // The pot and both rewards must be items the client has; an empty reward is none.
    [Test]
    public void Replay_ClientRewards() {
        GameEventMetadata gameEvent = Event(900119, Tree(), Day, Day.AddDays(1));
        Assert.That(AttendGiftReplay.ClientHasRewards(gameEvent, _ => 1), Is.True);
        Assert.That(AttendGiftReplay.ClientHasRewards(gameEvent, id => id == 30001431 ? 0 : 1), Is.False);
        Assert.That(AttendGiftReplay.ClientHasRewards(gameEvent, id => id == 20302918 ? 0 : 1), Is.False);
        GameEventMetadata noEmotion = gameEvent with { Data = Tree() with { EmotionReward = default } };
        Assert.That(AttendGiftReplay.ClientHasRewards(noEmotion, id => id == 0 ? 0 : 1), Is.True);
    }

    // GameEvent::vfunc_4 reads the id, then TreeWateringContext::Read the id again and the rest, strings one-byte.
    [Test]
    public void Packet_ClientOrder() {
        GameEventMetadata metadata = Event(900119, Tree(), Day.AddDays(-6), Day.AddDays(1));
        var writer = new ByteWriter();
        new GameEvent(metadata).WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("TreeWatering"));
        Assert.That((reader.ReadInt(), reader.ReadInt()), Is.EqualTo((900119, 900119)));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(GameEvent.EpochSeconds(Day.AddDays(-6))));
        Assert.That(reader.ReadLong(), Is.EqualTo(GameEvent.EpochSeconds(Day.AddDays(-4))));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(GameEvent.EpochSeconds(Day)));
        Assert.That(reader.ReadLong(), Is.EqualTo(GameEvent.EpochSeconds(Day.AddDays(1))));
        Assert.That((reader.ReadInt(), reader.ReadInt(), reader.ReadByte(), reader.ReadInt()), Is.EqualTo((2, 30001432, (byte) 1, 1)));
        Assert.That((reader.ReadInt(), reader.ReadInt(), reader.ReadByte(), reader.ReadInt()), Is.EqualTo((1, 20302918, (byte) 1, 1)));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That((reader.ReadInt(), reader.ReadString()), Is.EqualTo((1, "Interaction_A01")));
        Assert.That(reader.ReadInt(), Is.EqualTo(2000064));
        Assert.That(reader.ReadString(), Is.EqualTo("TreeWateringEvent201906"));
        Assert.That(reader.ReadInt(), Is.EqualTo(30001431));
        Assert.That((reader.ReadString(), reader.ReadString(), reader.ReadString()), Is.EqualTo(("Functobj_Feed_A01", "Emotion_Clapping_A", "")));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
        Assert.That(GameEvent.EpochSeconds(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)), Is.EqualTo(0));
    }

    [TestCase(true, 0, TreeWateringEvent.Rewarded)]
    [TestCase(true, 1, TreeWateringEvent.Rewarded)]
    [TestCase(true, 2, TreeWateringEvent.WaterLimit)]
    [TestCase(false, 0, TreeWateringEvent.NoWaterPot)]
    public void Water_PotAndLimit(bool holdsPot, int waterCount, byte expected) {
        Assert.That(TreeWateringEvent.Water(Tree(), holdsPot, waterCount), Is.EqualTo(expected));
    }

    // Only on an emotion day (strictly inside it) and after the day's waterings.
    [Test]
    public void Emotion_DayAndWaterings() {
        TreeWatering tree = Tree();
        DateTime noon = Day.AddHours(12);
        Assert.That(TreeWateringEvent.Emotion(tree, noon, 2, 0), Is.EqualTo(TreeWateringEvent.Rewarded));
        Assert.That(TreeWateringEvent.Emotion(tree, noon, 2, 1), Is.EqualTo(TreeWateringEvent.EmotionLimit));
        Assert.That(TreeWateringEvent.Emotion(tree, noon, 1, 0), Is.Null);
        Assert.That(TreeWateringEvent.Emotion(tree, Day, 2, 0), Is.Null);
        Assert.That(TreeWateringEvent.Emotion(tree, Day.AddDays(1), 2, 0), Is.Null);
        Assert.That(TreeWateringEvent.Emotion(Tree(days: []), noon, 2, 0), Is.Null);
    }

    [Test]
    public void Find_LowestId() {
        var high = new GameEvent(Event(900120, Tree(), Day, Day.AddDays(1)));
        var low = new GameEvent(Event(900119, Tree(), Day, Day.AddDays(1)));
        Assert.That(TreeWateringEvent.Find([high, low]), Is.SameAs(low));
        Assert.That(TreeWateringEvent.Find([]), Is.Null);
    }
}

// The 0x00AA handler on a TestGameSession: the watering and the emotion, their replies, counts and rewards.
public class TreeWateringSessionTests {
    private const int EventId = 900119;
    private const int FieldId = 2000064;
    private const int PotId = 30001431;
    private const int WaterRewardId = 30001432;
    private const int EmotionRewardId = 20302918;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Start(bool holdsPot = true, bool emotionDay = true, int fieldId = FieldId, params AchievementMetadata[] trophies) {
        test = new TestGameSession();
        test.AddAchievement(trophies);
        test.Register(TestGameSession.Metadata(WaterRewardId), TestGameSession.Metadata(EmotionRewardId));
        if (holdsPot) {
            test.Store(TestGameSession.Metadata(PotId));
        }
        TreeWatering.Period[] days = emotionDay ? [new TreeWatering.Period(DateTime.Now.AddHours(-1), DateTime.Now.AddHours(1))] : [];
        test.AddEvent(EventId, GameEventType.TreeWatering, new TreeWatering([], days, 2, new RewardItem(WaterRewardId, 1, 1), 1,
            new RewardItem(EmotionRewardId, 1, 3), [], FieldId, "TreeWateringEvent201906", PotId, "Functobj_Feed_A01", "Emotion_Clapping_A", ""));
        test.Start();
        test.EnterMap(TestGameSession.Map(fieldId));
        test.TakeSent();
    }

    // The reply (mode, result and, on a reward, the count and limit) and the user value update before it.
    private (byte Mode, byte Result, int[] Values)? Send(TreeWateringEvent.Action action) {
        new TreeWateringHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteByte((byte) action)));
        List<byte[]> sent = test.TakeSent();
        byte[]? reply = sent.LastOrDefault(packet => TestGameSession.Op(packet) == SendOp.TreeWatering);
        if (reply == null) {
            return null;
        }
        var reader = new ByteReader(reply, 0);
        reader.Read<SendOp>();
        byte mode = reader.ReadByte();
        byte result = reader.ReadByte();
        var values = new List<int>();
        while (reader.Available >= 4) {
            values.Add(reader.ReadInt());
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return (mode, result, values.ToArray());
    }

    private int Held(int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    // Two waterings a day give the reward and count; the third is at the limit. The pot is kept.
    [Test]
    public void Water_UpToTheLimit() {
        Start();
        Assert.That(Send(TreeWateringEvent.Action.Water), Is.EqualTo(((byte) 2, (byte) 0, new[] { 1, 2 })));
        Assert.That(Send(TreeWateringEvent.Action.Water), Is.EqualTo(((byte) 2, (byte) 0, new[] { 2, 2 })));
        Assert.That(Send(TreeWateringEvent.Action.Water), Is.EqualTo(((byte) 2, (byte) 2, Array.Empty<int>())));
        Assert.That((Held(WaterRewardId), Held(PotId)), Is.EqualTo((2, 1)));
        Assert.That(TreeWateringEvent.Count(session, session.FindEvent(EventId)!, TreeWateringEvent.Action.Water), Is.EqualTo(2));
        // Stored with the reward, before any logout.
        Assert.That(test.StoredEventValues(EventId).GetValueOrDefault(GameEventUserValueType.TreeWateringCount), Is.EqualTo("2"));
        Assert.That(test.Errors, Is.Empty);
    }

    // splash_plant counts each rewarded watering; its code is the event's water pot (92000149: 30001431).
    [TestCase(PotId, 1L)]
    [TestCase(PotId + 1, null)]
    public void Water_CountsTheSplashPlantTrophyOfThePot(int code, long? expected) {
        const int trophy = 92000149;
        Start(trophies: TestGameSession.Trophy(trophy, ConditionType.splash_plant, [100], codes: [code]));
        Send(TreeWateringEvent.Action.Water);
        Assert.That(test.TrophyCounter(trophy), Is.EqualTo(expected));
    }

    [Test]
    public void Water_WithoutThePot() {
        Start(holdsPot: false);
        Assert.That(Send(TreeWateringEvent.Action.Water), Is.EqualTo(((byte) 2, (byte) 1, Array.Empty<int>())));
        Assert.That(Held(WaterRewardId), Is.EqualTo(0));
    }

    // Another field than the tree's: nothing.
    [Test]
    public void Water_OtherField() {
        Start(fieldId: FieldId + 1);
        Assert.That(Send(TreeWateringEvent.Action.Water), Is.Null);
        Assert.That(Held(WaterRewardId), Is.EqualTo(0));
    }

    // A reward neither the bag nor the mail can write is logged with its item; the watering still counts. Before,
    // the result was not read and the reward was lost with no trace.
    [Test]
    public void Water_UnwrittenRewardIsLogged() {
        Start();
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added
                                                                               && entry.Entity.GetType().Name == "Item");
        var errors = new List<string>();
        ILogger before = Log.Logger;
        (byte Mode, byte Result, int[] Values)? reply;
        try {
            Log.Logger = new LoggerConfiguration().WriteTo.Sink(new ErrorLines(errors)).CreateLogger();
            reply = Send(TreeWateringEvent.Action.Water);
        } finally {
            Log.Logger = before;
            test.FailSave = null;
        }
        Assert.Multiple(() => {
            Assert.That(reply, Is.EqualTo(((byte) 2, (byte) 0, new[] { 1, 2 })));
            Assert.That(Held(WaterRewardId), Is.Zero);
            Assert.That(errors.Count(line => line.Contains("reward " + WaterRewardId)), Is.EqualTo(1));
        });
    }

    private sealed class ErrorLines(List<string> errors) : Serilog.Core.ILogEventSink {
        public void Emit(Serilog.Events.LogEvent logEvent) {
            if (logEvent.Level >= Serilog.Events.LogEventLevel.Error) {
                errors.Add(logEvent.RenderMessage());
            }
        }
    }

    // The emotion after the day's waterings, once a day; before them nothing.
    [Test]
    public void Emotion_AfterTheWaterings() {
        Start();
        Assert.That(Send(TreeWateringEvent.Action.Emotion), Is.Null);
        Send(TreeWateringEvent.Action.Water);
        Send(TreeWateringEvent.Action.Water);
        Assert.That(Send(TreeWateringEvent.Action.Emotion), Is.EqualTo(((byte) 3, (byte) 0, new[] { 1, 1 })));
        Assert.That(Send(TreeWateringEvent.Action.Emotion), Is.EqualTo(((byte) 3, (byte) 3, Array.Empty<int>())));
        Assert.That(Held(EmotionRewardId), Is.EqualTo(3));
        Assert.That(test.StoredEventValues(EventId).GetValueOrDefault(GameEventUserValueType.TreeWateringEmotionCount), Is.EqualTo("1"));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Emotion_NotAnEmotionDay() {
        Start(emotionDay: false);
        Send(TreeWateringEvent.Action.Water);
        Send(TreeWateringEvent.Action.Water);
        Assert.That(Send(TreeWateringEvent.Action.Emotion), Is.Null);
        Assert.That(Held(EmotionRewardId), Is.EqualTo(0));
    }
}
