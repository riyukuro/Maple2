using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Validators;

public class RideSyncTests {
    // One base record in the client's order (commonfiles/statesyncobject.cpp:113-145), no optional field.
    private static void Record(ByteWriter writer, ActorState state, short z) {
        writer.WriteByte((byte) state); // :115
        writer.WriteByte(0); // :116 sub state
        writer.WriteShort(10); // :117 position
        writer.WriteShort(20);
        writer.WriteShort(z);
        writer.WriteShort(900); // :118 direction / 10
        writer.WriteByte(9); // :119 packed direction, no floats
        writer.WriteShort(0); // :124 vector
        writer.WriteShort(0);
        writer.WriteShort(0);
        writer.WriteByte(2); // :125
        writer.WriteShort(0); // :126
        writer.WriteShort(0); // :127
        writer.WriteByte(0); // :128 optional bits
        writer.WriteInt(z); // :145
    }

    // RideSync 0x0041 (client/ms2/state.cpp:546-553): u8, i32 server tick, i32 elapsed, u8 count, then per record
    // the virtual record writer (statesyncobject.cpp:186-222) and its i32 tick. The couple emotion record
    // (statesyncobject.cpp:294-301) adds i32, i32, u8 and two float vectors after the base record.
    [Test]
    public void ReadsACoupleEmotionRecordToTheEnd() {
        var writer = new ByteWriter();
        writer.WriteByte(0); // state.cpp:547
        writer.WriteInt(1000); // :548 server tick
        writer.WriteInt(2000); // :549 elapsed
        writer.WriteByte(2); // :550 count
        Record(writer, ActorState.CoupleEmotion, 31);
        writer.WriteInt(7); // statesyncobject.cpp:297
        writer.WriteInt(8); // :298
        writer.WriteByte(1); // :299
        writer.WriteFloat(1f); // :300
        writer.WriteFloat(2f);
        writer.WriteFloat(3f);
        writer.WriteFloat(4f); // :301
        writer.WriteFloat(5f);
        writer.WriteFloat(6f);
        writer.WriteInt(111); // statesyncobject.cpp:222, the record's tick
        Record(writer, ActorState.Walk, 32);
        writer.WriteInt(222);
        var packet = new ByteReader(writer.ToArray(), 0);

        StateSync[]? records = RideSyncHandler.ReadRecords(packet);

        Assert.That(records, Is.Not.Null);
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(records!.Length, Is.EqualTo(2));
        Assert.That(records[0], Is.InstanceOf<StateSyncCoupleEmotion>());
        var emotion = (StateSyncCoupleEmotion) records[0];
        Assert.That(emotion.UnknownCoupleEmotionInt1, Is.EqualTo(7));
        Assert.That(emotion.UnknownCoupleEmotionVector2.Z, Is.EqualTo(6f));
        Assert.That(records[1].SyncNumber, Is.EqualTo(32));
        // A walking record of the ridee is relayed as riding.
        Assert.That(records[1].State, Is.EqualTo(ActorState.Ride));
    }
}
