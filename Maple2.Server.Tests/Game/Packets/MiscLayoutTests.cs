using System;
using System.Linq;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Misc s2c packets as the KR client reads them: CSceneStateGame::OnFittingDoll, FUN_14173b5a0 (PlayerHost),
// CKeyTable::OnKeyTable, OnNewsNotification, OnTreeWatering, OnLimitBreak (2: u16 error), OnCurrencyToken,
// CGloabalFactor::ReadRates and CNetworkService::OnCharMaxCount. Writers with tests elsewhere are left out.
public class MiscLayoutTests {
    private const string Uid = "0807060504030201";
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string Hex(float value) => Convert.ToHexString(BitConverter.GetBytes(value));
    private static string Hex(Vector3 value) => Hex(value.X) + Hex(value.Y) + Hex(value.Z);

    private static FieldFittingDoll Doll() {
        var cube = new PlotCube(TestGameSession.Metadata(50200001), 0x0102030405060708) {
            Position = new Vector3B(1, 2, 3),
            Rotation = 90,
            Type = PlotCube.CubeType.Default,
        };
        return new FieldFittingDoll(9000, cube, null!, new FunctionCubeMetadata.FittingDollData(Gender.Male, 0));
    }

    // 0: i32 obj, i64 uid, i32 item, two Vector3, u8; the item list only when the byte is set. 1: i32.
    // 7: i64, u8 slot. 8: i64, i32 result. 0xb: i32.
    [Test]
    public void FittingDoll() {
        FieldFittingDoll doll = Doll();
        Assert.That(Hex(FittingDollPacket.Load(doll, Array.Empty<Item>())),
            Is.EqualTo("7B00" + "00" + "28230000" + Uid + "C1FDFD02" + Hex(doll.Position) + Hex(doll.Rotation) + "00"));
        Assert.That(Hex(FittingDollPacket.Remove(doll)), Is.EqualTo("7B00" + "01" + "28230000"));
        Assert.That(Hex(FittingDollPacket.PutOff(doll, EquipSlot.CL)), Is.EqualTo("7B00" + "07" + Uid + ((byte) EquipSlot.CL).ToString("X2")));
        Assert.That(Hex(FittingDollPacket.Transform(doll, FittingDollRules.TransformResult.Partial)),
            Is.EqualTo("7B00" + "08" + Uid + "18000000"));
        Assert.That(Hex(FittingDollPacket.Error(FittingDollRules.Error.InvalidOwner)), Is.EqualTo("7B00" + "0B" + "02000000"));
    }

    // 3: wstr host, i32 map. 4, 5: two ints. 7 (UIItemBillBoardDialog::OnBillboardPacket): i32 result.
    [Test]
    public void PlayerHost() {
        Assert.That(Hex(PlayerHostPacket.StartMiniGame("ab", 2000062)), Is.EqualTo("0501" + "03" + "0200" + "61006200" + "BE841E00"));
        Assert.That(Hex(PlayerHostPacket.MiniGameRewardNotice(5, 10)), Is.EqualTo("0501" + "04" + "05000000" + "0A000000"));
        Assert.That(Hex(PlayerHostPacket.BillboardInstallResult(BillboardInstallResult.MaxPerField)), Is.EqualTo("0501" + "07" + "03000000"));
    }

    // 0: u8 default; 1 ends it. Else i32 n, n x (i32, i32, i64, i32, u8), u16 page, u16 pages, per page i32 n and
    // n x (i32 slot, i32 skill, i32 item, i64 uid). 7: the pages alone.
    [Test]
    public void KeyTable() {
        Assert.That(Hex(KeyTablePacket.LoadDefault()), Is.EqualTo("6F00" + "00" + "01"));
        var bar = new HotBar([new QuickSlot(10200001, 0, 0), new QuickSlot(0, 20000001, 0x0102030405060708)]);
        string slots = "19000000" + "00000000" + "C1A39B00" + "00000000" + "0000000000000000"
            + "01000000" + "00000000" + "012D3101" + Uid
            + string.Concat(Enumerable.Range(2, 23).Select(i => i.ToString("X2") + "000000" + new string('0', 32)));
        Assert.That(Hex(KeyTablePacket.Load([new KeyBind(0x41, 2, 0x0102030405060708, 3, 1)], 1, [bar])),
            Is.EqualTo("6F00" + "00" + "00" + "01000000" + "41000000" + "02000000" + Uid + "03000000" + "01"
                + "0100" + "0100" + slots));
        Assert.That(Hex(KeyTablePacket.LoadHotBar(1, [bar])), Is.EqualTo("6F00" + "07" + "0100" + "0100" + slots));
    }

    // 0: wstr url (ReadUnicodeStringA reads the same bytes), u8 in game, i32 type.
    [Test]
    public void NewsNotification() {
        Assert.That(Hex(NewsNotificationPacket.ResponseToken("ab")), Is.EqualTo("9F00" + "00" + "0200" + "61006200" + "00" + "00000000"));
        Assert.That(Hex(NewsNotificationPacket.OpenUi(5)), Does.StartWith("9F00" + "00" + "2400").And.EndWith("01" + "05000000"));
    }

    // 2 and 3: u8 result; the two ints only for result 0.
    [Test]
    public void TreeWatering() {
        Assert.That(Hex(TreeWateringPacket.Water(0, 3, 10)), Is.EqualTo("1801" + "02" + "00" + "03000000" + "0A000000"));
        Assert.That(Hex(TreeWateringPacket.Water(2)), Is.EqualTo("1801" + "02" + "02"));
        Assert.That(Hex(TreeWateringPacket.Emotion(0, 1, 1)), Is.EqualTo("1801" + "03" + "00" + "01000000" + "01000000"));
        Assert.That(Hex(TreeWateringPacket.Emotion(3)), Is.EqualTo("1801" + "03" + "03"));
    }

    [Test]
    public void LimitBreak() {
        Assert.That(Hex(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_lack_ingredient)), Is.EqualTo("2E01" + "02" + "0200"));
    }

    // u8 type, i64 value, i64 delta, i32, i64 overflow.
    [Test]
    public void CurrencyToken() {
        var currency = new Currency { Treva = 0x0102030405060708 };
        Assert.That(Hex(CurrencyPacket.UpdateCurrency(currency, CurrencyType.Treva, 5, 2)),
            Is.EqualTo(Hex(Packet.Of(Maple2.Server.Core.Constants.SendOp.CurrencyToken)) + "04" + Uid + "0500000000000000" + "00000000" + "0200000000000000"));
    }

    // Twice three tables: u8 kind, then 99 floats.
    [Test]
    public void GlobalFactor() {
        byte[] bytes = GlobalFactorPacket.Rates().ToArray();
        Assert.That(bytes.Length, Is.EqualTo(2 + 6 * (1 + 99 * 4)));
        Assert.That(bytes[2 + 397], Is.EqualTo(1));
        Assert.That(BitConverter.ToSingle(bytes, 3), Is.EqualTo(1f));
    }

    [Test]
    public void CharMaxCount() {
        Assert.That(Hex(CharacterListPacket.SetMax(4, 7)), Is.EqualTo(Hex(Packet.Of(Maple2.Server.Core.Constants.SendOp.CharMaxCount)) + "04000000" + "07000000"));
    }
}
