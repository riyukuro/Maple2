using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Error;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RoomDungeon 0x15 (UIDungeonSearchUnitedReward::SendRoomDungeon): the reset of the united weekly reward count
// (record 1006). Once a week for the whole account (0x1f), only at the weekly limit and from the reset level (0x20);
// a reset sends the record, the boost state (0x16) and 0x17, and stores the record and the account's mark.
public class DungeonUnitedRewardResetTests {
    private const int Room = DungeonRoomRules.UnitedRewardRoomId;
    private const int Weekly = 5;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private long now;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        now = DateTimeOffset.Now.ToUnixTimeSeconds();
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> {
            [Room] = TestGameSession.DungeonRoom(Room, 2000023, rewardCount: Weekly),
        }));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { DungeonUnitedRewardCountResetLevel = 50 });
        test.Start();
        session.Player.Value.Character.Level = 60;
        test.TakeSent();
    }

    private DungeonRecord Record(byte clears) {
        DungeonRecord record = session.Dungeon.Records[Room];
        record.UnionClears = clears;
        record.UnionCooldownTimestamp = now + 3600;
        return record;
    }

    private List<string> Reset() {
        new DungeonRoomHandler { TableMetadata = test.TableMetadata, World = test.World }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0x15)));
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter writer) => Convert.ToHexString(writer.ToArray());

    [Test]
    public void AtTheLimitTheCountIsResetAndStored() {
        DungeonRecord record = Record(Weekly);
        List<string> sent = Reset();

        Assert.That(record.UnionClears, Is.Zero);
        Assert.That(sent, Is.EqualTo(new[] {
            Hex(DungeonRoomPacket.Update(record)),
            Hex(DungeonRoomPacket.BoostState(now + 3600, test.CharacterId)),
            Hex(DungeonRoomPacket.UnitedRewardReset()),
        }));
        using var check = new Ms2Context(test.Options);
        Maple2.Database.Model.Account account = check.Account.Find(test.AccountId)!;
        Assert.That((account.UnitedRewardResetCharacterId, account.UnitedRewardResetExpiry), Is.EqualTo((test.CharacterId, now + 3600)));
        Assert.That(check.DungeonRecord.Single(row => row.DungeonId == Room && row.OwnerId == test.CharacterId).CurrentClears, Is.Zero);
    }

    [Test]
    public void ASecondResetInTheWeekIs0x1f() {
        Record(Weekly);
        Reset();
        Record(Weekly);
        Assert.That(Reset(), Is.EqualTo(new[] { Hex(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_error_used_reset_united_reward)) }));
        Assert.That(session.Dungeon.Records[Room].UnionClears, Is.EqualTo(Weekly));
    }

    [TestCase(Weekly - 1, 60)] // under the weekly limit
    [TestCase(Weekly, 49)] // under the reset level
    public void OtherwiseIs0x20(int clears, int level) {
        Record((byte) clears);
        session.Player.Value.Character.Level = (short) level;
        Assert.That(Reset(), Is.EqualTo(new[] { Hex(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_error_still_have_united_reawrd)) }));
        Assert.That(session.Dungeon.Records[Room].UnionClears, Is.EqualTo(clears));
        Assert.That(session.Player.Value.Account.UnitedRewardResetExpiry, Is.Zero);
    }

    [Test]
    public void FeatureOffIs0x21() {
        test.SetFeaturesOff(DungeonRoomRules.UnitedRewardResetFeature);
        Record(Weekly);
        Assert.That(Reset(), Is.EqualTo(new[] { Hex(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_error_shutdown_united_reawrd_reset)) }));
        Assert.That(session.Dungeon.Records[Room].UnionClears, Is.EqualTo(Weekly));
    }

    [Test]
    public void RuleOrder() {
        Assert.That(DungeonRoomRules.UnitedRewardResetError(60, 50, 5, 5, 0, 100), Is.Null);
        Assert.That(DungeonRoomRules.UnitedRewardResetError(60, 50, 6, 5, 99, 100), Is.Null); // the last reset's week is over
        Assert.That(DungeonRoomRules.UnitedRewardResetError(10, 50, 0, 5, 101, 100), Is.EqualTo(DungeonRoomError.s_room_dungeon_error_used_reset_united_reward));
        Assert.That(DungeonRoomRules.UnitedRewardResetError(60, 50, 0, 0, 0, 100), Is.EqualTo(DungeonRoomError.s_room_dungeon_error_still_have_united_reawrd));
        Assert.That(((byte) DungeonRoomError.s_room_dungeon_error_used_reset_united_reward, (byte) DungeonRoomError.s_room_dungeon_error_still_have_united_reawrd),
            Is.EqualTo(((byte) 0x1f, (byte) 0x20)));
    }

    [Test]
    public void PacketBytes() {
        Assert.That(Hex(DungeonRoomPacket.BoostState(0x0102, 7)), Is.EqualTo(Hex(Raw(0x16, w => { w.WriteLong(0x0102); w.WriteLong(7); }))));
        Assert.That(Hex(DungeonRoomPacket.UnitedRewardReset()), Is.EqualTo(Hex(Raw(0x17, _ => { }))));
        var gvg = new Maple2.PacketLib.Tools.ByteWriter();
        gvg.Write<ushort>((ushort) Maple2.Server.Core.Constants.SendOp.Gvg);
        gvg.WriteInt(1);
        gvg.WriteInt(42);
        gvg.WriteFloat(1.5f);
        gvg.WriteFloat(2f);
        Assert.That(Hex(GvgPacket.DungeonRewardRates(1.5f, 2f)), Is.EqualTo(Hex(gvg)));
    }

    private static Maple2.PacketLib.Tools.ByteWriter Raw(byte command, Action<Maple2.PacketLib.Tools.ByteWriter> body) {
        var writer = new Maple2.PacketLib.Tools.ByteWriter();
        writer.Write<ushort>((ushort) Maple2.Server.Core.Constants.SendOp.RoomDungeon);
        writer.WriteByte(command);
        body(writer);
        return writer;
    }
}
