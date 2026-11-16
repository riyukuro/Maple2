using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// FittingDoll modes that carry items against CSceneStateGame::OnFittingDoll (client/ms2/scenestategame.cpp:12128):
// 0 with items: i32 kind, i64 instance, i32, two vector3, u8 has items, i32 count x (i32 item id, i64 uid, u8 slot, i32
// rank, the item stream); 6: i64 owner uid, i32 item id, i64 item uid, u8 slot, i32 rank, the item stream. The stream is
// read back against CItem::ReadFromStream in ItemLayoutTests, so it is compared as the shared Item writer's bytes.
public class FittingDollItemsLayoutTests {
    private static FieldFittingDoll Doll() {
        var cube = new PlotCube(TestGameSession.Metadata(50200001), 0x0102030405060708) {
            Position = new Vector3B(1, 2, 3), Rotation = 90, Type = PlotCube.CubeType.Default,
        };
        return new FieldFittingDoll(9000, cube, null!, new FunctionCubeMetadata.FittingDollData(Gender.Male, 0));
    }

    private static Item Outfit() => new(TestGameSession.Metadata(11300001), 4, 1) { Uid = 8001, Slot = 3 };

    private static byte[] Stream() {
        var writer = new ByteWriter();
        writer.WriteClass<Item>(Outfit());
        return writer.ToArray();
    }

    [Test]
    public void PutOn_AndLoadWithItems() {
        var reader = new ByteReader(FittingDollPacket.PutOn(Doll(), Outfit()).ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.FittingDoll));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.ReadInt(), Is.EqualTo(11300001));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadBytes(reader.Available), Is.EqualTo(Stream()));

        reader = new ByteReader(FittingDollPacket.Load(Doll(), new List<Item> { Outfit() }).ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.FittingDoll));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(9000));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.ReadInt(), Is.EqualTo(50200001));
        reader.ReadBytes(24);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(11300001));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadBytes(reader.Available), Is.EqualTo(Stream()));
    }
}
