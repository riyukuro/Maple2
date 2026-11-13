using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The Achieve packet against the client's reader, CAchieveManager::OnPacket (achievemanager.cpp:499) and
// CAchieve::Decode (commonfiles/achievecondition.cpp:166): byte state, int, int current grade, int start grade, byte
// bookmark, long counter, int count, count x (int grade, long time).
public class AchievementLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    private static Achievement Trophy() {
        var grades = new Dictionary<int, AchievementMetadataGrade>();
        foreach ((int grade, long value) in new[] { (1, 10L), (2, 50L) }) {
            grades[grade] = new AchievementMetadataGrade(grade, new ConditionMetadata(ConditionType.playtime, value, null, null), null);
        }
        return new Achievement(new AchievementMetadata(0x01020304, null, false, AchievementCategory.Life, [], grades)) {
            CurrentGrade = 2,
            RewardGrade = 1,
            Favorite = true,
            Counter = 0x10,
            Grades = new Dictionary<int, long> { [1] = 0x20 },
        };
    }

    private static readonly byte[] Decoded = [
        0x02, // state: in progress (one of two grades)
        0x00, 0, 0, 0, // completed
        0x02, 0, 0, 0, // current grade
        0x01, 0, 0, 0, // start (reward) grade
        0x01, // bookmark
        0x10, 0, 0, 0, 0, 0, 0, 0, // counter
        0x01, 0, 0, 0, 0x01, 0, 0, 0, 0x20, 0, 0, 0, 0, 0, 0, 0, // grades
    ];

    // Mode 1 (:504): int count; per trophy int id, int grade, then Decode, which the client runs whether or not it
    // knows the trophy, so no entry desyncs the next.
    [Test]
    public void Load() {
        byte[] expected = [0x01, 0x01, 0, 0, 0, 0x04, 0x03, 0x02, 0x01, 0x02, 0, 0, 0, .. Decoded];
        Assert.That(Body(AchievementPacket.Load([Trophy()])), Is.EqualTo(expected));
    }

    // Mode 2 (:525): int id, then Decode.
    [Test]
    public void Update() {
        byte[] expected = [0x02, 0x04, 0x03, 0x02, 0x01, .. Decoded];
        Assert.That(Body(AchievementPacket.Update(Trophy())), Is.EqualTo(expected));
    }

    // Mode 0 (:502): nothing read; the client reloads its list.
    [Test]
    public void Initialize() {
        Assert.That(Body(AchievementPacket.Initialize()), Is.EqualTo(new byte[] { 0x00 }));
    }
}
