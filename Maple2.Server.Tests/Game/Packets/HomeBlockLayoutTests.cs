using Maple2.Model.Enum;
using Maple2.Model;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// HomeBlock read as HomeBlockManager::OnPacket (homeblockmanager.cpp:58) reads it: 4 (list) an int count and a
// HomeBlockInfo each; 2 (add) one HomeBlockInfo (commonstruct_wa.cpp:826: int64 id, int64 character, int64 account,
// name, short level, int job code, int job, picture, memo); 3 (remove) an int64 id; 5 (memo) an int64 id and a unicode
// memo; 6 (result) an int code.
public class HomeBlockLayoutTests {
    private static HomeBlockEntry Entry() => new() { Id = 7, CharacterId = 8, AccountId = 9, Name = "Nm", Level = 42, Job = Job.Knight, Picture = "pic", Memo = "memo" };

    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.HomeBlock));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    private static void ReadEntry(ByteReader reader) {
        Assert.That((reader.ReadLong(), reader.ReadLong(), reader.ReadLong()), Is.EqualTo((7L, 8L, 9L)));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Nm"));
        Assert.That(reader.ReadShort(), Is.EqualTo(42));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) Job.Knight.Code()));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) Job.Knight));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("pic"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("memo"));
    }

    [Test]
    public void ListAddRemoveMemoAndResult() {
        ByteReader list = Open(HomeBlockPacket.Load([Entry(), Entry()]), 4);
        Assert.That(list.ReadInt(), Is.EqualTo(2));
        ReadEntry(list);
        ReadEntry(list);
        Assert.That(list.Available, Is.Zero);

        ByteReader add = Open(HomeBlockPacket.Add(Entry()), 2);
        ReadEntry(add);
        Assert.That(add.Available, Is.Zero);

        ByteReader remove = Open(HomeBlockPacket.Remove(7), 3);
        Assert.That(remove.ReadLong(), Is.EqualTo(7));
        Assert.That(remove.Available, Is.Zero);

        ByteReader memo = Open(HomeBlockPacket.UpdateMemo(7, "new"), 5);
        Assert.That(memo.ReadLong(), Is.EqualTo(7));
        Assert.That(memo.ReadUnicodeString(), Is.EqualTo("new"));
        Assert.That(memo.Available, Is.Zero);

        ByteReader result = Open(HomeBlockPacket.Result(HomeBlockPacket.Error.MaxBlock), 6);
        Assert.That(result.ReadInt(), Is.EqualTo(4));
        Assert.That(result.Available, Is.Zero);
    }
}
