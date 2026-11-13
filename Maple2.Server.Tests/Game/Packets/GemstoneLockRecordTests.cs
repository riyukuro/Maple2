using System;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// A socketed gem's lock record as the client builds it (itemsocket.cpp:184-205): state 1 = locked, state 0 =
// unlocking until the expire time, no record when neither. The server sent state 0 for a locked gem, whose
// UnlockTime is the time it was locked, so the client read it as unlocked, and an unlocking gem sent no record.
public class GemstoneLockRecordTests {
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // Returns (has record, state, expire) as the client reads TSocketGemStoneInfo (itemsocket.cpp:228).
    private static (bool, byte, long) ReadLock(ByteReader reader) {
        Assert.That(reader.ReadInt(), Is.EqualTo(40200001));
        Assert.That(reader.ReadBool(), Is.False); // no binding
        if (!reader.ReadBool()) return (false, 0, 0);
        return (true, reader.ReadByte(), reader.ReadLong());
    }

    private static (bool, byte, long) EquipPacket(bool isLocked, long unlockTime) {
        ByteWriter writer = ItemSocketPacket.EquipGemstone(1, 0, new ItemGemstone(40200001, null, new ItemStats(), isLocked, unlockTime));
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        reader.ReadShort(); // opcode
        reader.ReadByte(); // command
        reader.ReadLong();
        reader.ReadByte();
        Assert.That(reader.ReadBool(), Is.True);
        return ReadLock(reader);
    }

    private static (bool, byte, long) ModelWrite(bool isLocked, long unlockTime) {
        var writer = new ByteWriter();
        writer.WriteClass<ItemGemstone>(new ItemGemstone(40200001, null, new ItemStats(), isLocked, unlockTime));
        return ReadLock(new ByteReader(writer.Buffer[..writer.Length], 0));
    }

    [Test]
    public void LockedGem_SendsStateOne() {
        Assert.Multiple(() => {
            Assert.That(EquipPacket(true, Now - 100), Is.EqualTo((true, (byte) 1, Now - 100)));
            Assert.That(ModelWrite(true, Now - 100), Is.EqualTo((true, (byte) 1, Now - 100)));
        });
    }

    [Test]
    public void UnlockingGem_SendsStateZeroWithItsExpireTime() {
        Assert.Multiple(() => {
            Assert.That(EquipPacket(false, Now + 3600), Is.EqualTo((true, (byte) 0, Now + 3600)));
            Assert.That(ModelWrite(false, Now + 3600), Is.EqualTo((true, (byte) 0, Now + 3600)));
        });
    }

    [Test]
    public void UnlockedGem_SendsNoRecord() {
        Assert.Multiple(() => {
            Assert.That(EquipPacket(false, Now - 100), Is.EqualTo((false, (byte) 0, 0L)));
            Assert.That(ModelWrite(false, 0), Is.EqualTo((false, (byte) 0, 0L)));
        });
    }

    // The model reads back what it wrote: state 1 is the lock, state 0 the unlock time.
    [Test]
    public void ModelRead_KeepsLockAndUnlockTime() {
        foreach ((bool locked, long time) in new[] { (true, Now - 100), (false, Now + 3600) }) {
            var writer = new ByteWriter();
            writer.WriteClass<ItemGemstone>(new ItemGemstone(40200001, null, new ItemStats(), locked, time));
            ItemGemstone read = new ByteReader(writer.Buffer[..writer.Length], 0).ReadClass<ItemGemstone>();
            Assert.That((read.IsLocked, read.UnlockTime), Is.EqualTo((locked, time)));
        }
    }
}
