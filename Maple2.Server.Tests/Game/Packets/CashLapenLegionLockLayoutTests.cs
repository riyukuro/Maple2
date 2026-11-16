using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Cash (0xAD) against CCashMarketMan::OnCash (cashinventorybag.cpp:304): u8 mode; 0: int count (products); 1: int code,
// int arg (code 1 sets the balance); 5: int code, code 1: int page, int count (products); 9: u8 enabled.
// Lapenshard (0x11B) against CSceneStateGame::OnLapenshard (scenestategame.cpp:8743; entries
// Unk_LapenEquipped::Read and Unk_LapenUnequip::Read, itemlapensharddata.cpp:482-497): u8 mode; 0: int count x (int
// slot, int item); 1: int slot, int item; 2: int slot. Modes 4 and 5 are read by the system window's page
// (unit_140c5d230.h, vfunc_9 body not in the source): no reader to compare.
// LegionBattle (0xF4) against CSceneStateGame::OnLegionBattle (scenestategame.cpp:8480): u8 mode; 0: u16 count x (int
// key, int npc, long time); 1: int key, int npc, long time; 2: int key.
// ItemLock (0xDB) against CItemLockManager::OnItemLockPacket (unit_1406125d0_itemlockmanager.cpp:60): u8 mode; 1: long
// uid, u16 slot; 2: long uid; 3: u8 count x (long uid, the item stream); 4: int result.
public class CashLapenLegionLockLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void Cash() {
        ByteReader reader = Read(CashPacket.Products(), SendOp.Cash);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(CashPacket.Balance(1234), SendOp.Cash);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1234));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(CashPacket.Inventory(2), SendOp.Cash);
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(CashPacket.Enabled(true), SendOp.Cash);
        Assert.That(reader.ReadByte(), Is.EqualTo(9));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Lapenshard() {
        ByteReader reader = Read(LapenshardPacket.Load(new Dictionary<LapenshardSlot, int> { [LapenshardSlot.Blue1] = 70000001 }), SendOp.Lapenshard);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) LapenshardSlot.Blue1));
        Assert.That(reader.ReadInt(), Is.EqualTo(70000001));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(LapenshardPacket.Equip(LapenshardSlot.Red2, 70000002), SendOp.Lapenshard);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) LapenshardSlot.Red2));
        Assert.That(reader.ReadInt(), Is.EqualTo(70000002));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(LapenshardPacket.Unequip(LapenshardSlot.Green1), SendOp.Lapenshard);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) LapenshardSlot.Green1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void LegionBattle() {
        ByteReader reader = Read(LegionBattlePacket.Load(new Dictionary<int, WorldBossMetadata>()), SendOp.LegionBattle);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(LegionBattlePacket.Update(3, 21000001, 1700000000), SendOp.LegionBattle);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(21000001));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(LegionBattlePacket.Remove(3), SendOp.LegionBattle);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void ItemLock() {
        ByteReader reader = Read(ItemLockPacket.Stage(9001, 4), SendOp.ItemLock);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(9001));
        Assert.That(reader.ReadShort(), Is.EqualTo(4));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemLockPacket.Unstage(9001), SendOp.ItemLock);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(9001));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemLockPacket.Commit(new List<Item>()), SendOp.ItemLock);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemLockPacket.Error(3), SendOp.ItemLock);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
