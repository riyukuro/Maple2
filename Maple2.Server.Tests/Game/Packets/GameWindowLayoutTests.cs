using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// BonusGame (0x7C) against UIRouletteDialog::OnRoulettePacket (uirewardnotify.cpp): u8 mode; 1: u8, int count x (int
// item id, u8 rank, int count), int; 2: int count x (int slot, int item id, int count, short rank).
// CardReverseGame (0xDA) against UICardReverseGameDialog::vfunc_79 (uicardreversegamedialog.cpp:176): u8 mode; 0: int
// cost item, int cost count, int count x (int item id, u8 rank, int count); 1: none; 2: int index; 3: u8 result.
// Liftable against CSceneStateGame::OnLiftable (scenestategame.cpp:11941) and WorldService::FUN_141bc5420
// (worldservice_1.cpp:4429): u8 mode; 0: int count x (string name, u8 enabled, int state, u8 kind, 4 x unicode string,
// bool); 2: string name, u8 enabled, int state, u8 kind; 3: string name, u32 kind, 4 x unicode string, bool; 4: string
// name.
public class GameWindowLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void BonusGameLoad() {
        var items = new List<ItemComponent> { new(20000001, 3, 5, ItemTag.None), new(20000002, 1, 2, ItemTag.None) };
        ByteReader reader = Read(BonusGamePacket.Load(items), SendOp.BonusGame);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000002));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void CardReverseGame() {
        var game = new BonusGameTable.Game(1, 5, new ItemComponent(30000001, 1, 3, ItemTag.None), []);
        var drop = new BonusGameTable.Drop(1, [new BonusGameTable.Drop.Item(new ItemComponent(20000001, 2, 7, ItemTag.None), 1, false)]);
        ByteReader reader = Read(CardReverseGamePacket.Deck(game, drop), SendOp.CardReverseGame);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(30000001));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(CardReverseGamePacket.Shuffle(), SendOp.CardReverseGame);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(CardReverseGamePacket.Pick(4), SendOp.CardReverseGame);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(CardReverseGamePacket.LackCost(), SendOp.CardReverseGame);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    private static FieldLiftable Liftable() {
        var value = new Liftable(1, 4, 0, 0, 0, "10", "1", "20", "2", true, default, default);
        var liftable = new FieldLiftable(null!, 5, "abc", value) { State = LiftableState.Disabled };
        return liftable;
    }

    private static void ReadFlags(ByteReader reader) {
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("10"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("1"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("20"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("2"));
        Assert.That(reader.ReadBool(), Is.True);
    }

    [Test]
    public void Liftable_Batch() {
        ByteReader reader = Read(LiftablePacket.Update([Liftable()]), SendOp.Liftable);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadString(), Is.EqualTo("abc"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) LiftableState.Disabled));
        ReadFlags(reader);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Liftable_UpdateAddRemove() {
        ByteReader reader = Read(LiftablePacket.Update(Liftable()), SendOp.Liftable);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadString(), Is.EqualTo("abc"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) LiftableState.Disabled));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(LiftablePacket.Add(Liftable()), SendOp.Liftable);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadString(), Is.EqualTo("abc"));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        ReadFlags(reader);
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(LiftablePacket.Remove("abc"), SendOp.Liftable);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadString(), Is.EqualTo("abc"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
