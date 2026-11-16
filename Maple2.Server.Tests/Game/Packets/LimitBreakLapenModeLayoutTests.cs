using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// LimitBreak mode 1 (OnLimitBreak, scenestategame.cpp:6859, then UIUnlimitedEnchantDialog::OnEnchantResult): an int64 uid
// and the item. Lapenshard mode 4 (UILapenshardUpgradeTab::vfunc_9, Ghidra 140c68870, FUN_1405b6360): an int; mode 5
// (FUN_1405b6220 and FUN_1405b5f30): an int64 uid, an int id, an int slot and a byte success flag.
public class LimitBreakLapenModeLayoutTests {
    [Test]
    public void LimitBreakResultIsUidAndItem() {
        var item = new Item(TestGameSession.Metadata(11300001), 1) { Uid = 0x0102030405 };
        var stream = new ByteWriter();
        stream.WriteClass<Item>(item);
        byte[] body = stream.ToArray();

        var reader = new ByteReader(LimitBreakPacket.LimitBreak(item).ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.LimitBreak));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(reader.ReadBytes(body.Length), Is.EqualTo(body));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void LapenshardPreviewAndUpgrade() {
        var preview = new ByteReader(LapenshardPacket.Preview().ToArray(), 0);
        Assert.That(preview.Read<SendOp>(), Is.EqualTo(SendOp.Lapenshard));
        Assert.That(preview.ReadByte(), Is.EqualTo(4));
        Assert.That(preview.ReadInt(), Is.EqualTo(10000));
        Assert.That(preview.Available, Is.Zero);

        var upgrade = new ByteReader(LapenshardPacket.Upgrade(0x0102030405, 70000001, LapenshardSlot.Red2, false).ToArray(), 0);
        Assert.That(upgrade.Read<SendOp>(), Is.EqualTo(SendOp.Lapenshard));
        Assert.That(upgrade.ReadByte(), Is.EqualTo(5));
        Assert.That(upgrade.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(upgrade.ReadInt(), Is.EqualTo(70000001));
        Assert.That(upgrade.ReadInt(), Is.EqualTo((int) LapenshardSlot.Red2));
        Assert.That(upgrade.ReadBool(), Is.False);
        Assert.That(upgrade.Available, Is.Zero);
    }
}
