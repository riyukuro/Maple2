using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// An emote use (Emote 2) carries the emote skill's own emotion name (its <kinds emotion>; stateskill.cpp:1906-1909),
// which the emotion condition counts. KR 90200001 has emotion="angry"; a use of it naming "hello" is dropped.
public class EmoteKeyTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Angry = 90200001;

    private static SkillMetadataStorage Skills(string emotion) {
        var storage = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty)) with { Emotion = emotion };
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(storage)!;
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(Angry, (short) 1), new SkillMetadata(Angry, 1, null, property, null!, null!)]);
        return storage;
    }

    private static long Count(params string[] keys) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        typeof(GameSession).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(session, Skills("angry"));
        session.Player.Value.Unlock.LearnEmote(Angry, 0);
        Quest quest = test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [new ConditionMetadata(ConditionType.emotion, 10, new[] { "angry", "hello" }.ConvertCodes(), null)],
        });
        foreach (string key in keys) {
            new EmoteHandler().Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(2);
                writer.WriteInt(Angry);
                writer.WriteUnicodeString(key);
            }));
        }
        return quest.Conditions[0].Counter;
    }

    [Test]
    public void TheEmotesOwnEmotion_Counts() {
        Assert.That(Count("angry"), Is.EqualTo(1));
    }

    [Test]
    public void AnotherEmotion_IsDropped() {
        Assert.That(Count("hello", "angry"), Is.EqualTo(1));
    }

    [Test]
    public void KeyMatches_IsTheRowsName() {
        Assert.Multiple(() => {
            Assert.That(EmoteHandler.KeyMatches("angry", "angry"), Is.True);
            Assert.That(EmoteHandler.KeyMatches("angry", "hello"), Is.False);
            Assert.That(EmoteHandler.KeyMatches(null, ""), Is.True);
        });
    }
}
