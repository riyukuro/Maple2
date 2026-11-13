using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.Packets;

// Client: CBlockManager::OnBlockPacket (client/ms2/blockmanager.cpp:85), AccountBlockInfo::Read 0x140268750 and
// CBlockChecker (commonfiles/blockchecker.cpp). Made-up ids and times.
public class AdminBlockPacketTests {
    private static AccountBlock Block(int kind, long expiry, long id = 7) => new() {
        Id = id,
        AccountId = 1001,
        Kind = kind,
        Reason = "spam",
        StartTime = 1_000,
        ExpiryTime = expiry,
    };

    private static ByteReader Reader(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.AdminBlock));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    [Test]
    public void Load_WritesEachRecordAsTheClientReadsIt() {
        ByteReader reader = Reader(AdminBlockPacket.Load([Block(2, 5_000), Block(6, 6_000, id: 8)]), 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That((reader.ReadLong(), reader.ReadInt(), reader.ReadLong(), reader.ReadUnicodeString(), reader.ReadLong(), reader.ReadLong()),
            Is.EqualTo((7L, 2, 1001L, "spam", 1_000L, 5_000L)));
        Assert.That((reader.ReadLong(), reader.ReadInt()), Is.EqualTo((8L, 6)));
        reader.ReadLong();
        reader.ReadUnicodeString();
        Assert.That((reader.ReadLong(), reader.ReadLong()), Is.EqualTo((1_000L, 6_000L)));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void Remove_WritesTheId() {
        ByteReader reader = Reader(AdminBlockPacket.Remove(7), 1);
        Assert.That(reader.ReadLong(), Is.EqualTo(7));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void SetExpiry_WritesIdKindExpiryDelta() {
        ByteReader reader = Reader(AdminBlockPacket.SetExpiry(7, 2, 4_000, -3600), 2);
        Assert.That((reader.ReadLong(), reader.ReadInt(), reader.ReadLong(), reader.ReadInt()), Is.EqualTo((7L, 2, 4_000L, -3600)));
        Assert.That(reader.Available, Is.Zero);
    }

    // CBlockChecker::IsExpired: expiry < server time.
    [TestCase(4_999, true)]
    [TestCase(5_000, true)]
    [TestCase(5_001, false)]
    public void Live_UntilExpiryPasses(long now, bool live) {
        Assert.That(AccountBlockRules.IsLive(Block(2, 5_000), now), Is.EqualTo(live));
    }

    // IsBlockedAction: chat (action 2) is blocked by kinds 2 and 6 only; login (1) by kinds 1 and 9.
    [Test]
    public void ChatAndLoginKinds() {
        int[] kinds = Enumerable.Range(AccountBlockRules.MinKind, AccountBlockRules.MaxKind).ToArray();
        Assert.That(kinds.Where(kind => AccountBlockRules.BlocksAction(kind, AccountBlockRules.ActionChat)), Is.EqualTo(new[] { 2, 6 }));
        Assert.That(kinds.Where(kind => AccountBlockRules.BlocksAction(kind, AccountBlockRules.ActionLogin)), Is.EqualTo(new[] { 1, 9 }));
        Assert.That(kinds.Where(AccountBlockRules.GmKind), Is.EqualTo(new[] { 2, 3, 4, 5, 6, 7, 8, 10 }));
        Assert.That(AccountBlockRules.BlocksAction(11, AccountBlockRules.ActionChat), Is.False);
    }

    // FUN_141276f80: a Penalty shows 0x433 (s_admin_block_velma_notice_chat), another chat kind 0x442 (s_admin_block_chat).
    [Test]
    public void ChatRefusal_UsesTheClientsText() {
        Assert.That(AccountBlockRules.ChatRefusal([Block(2, 5_000)], 4_000), Is.EqualTo(StringCode.s_admin_block_velma_notice_chat));
        Assert.That(AccountBlockRules.ChatRefusal([Block(6, 5_000)], 4_000), Is.EqualTo(StringCode.s_admin_block_chat));
        Assert.That(StringCodeMap.ToClient(StringCode.s_admin_block_velma_notice_chat), Is.EqualTo(0x433));
        Assert.That(StringCodeMap.ToClient(StringCode.s_admin_block_chat), Is.EqualTo(0x442));
    }

    [Test]
    public void ChatRefusal_NoneWhenExpiredOrOtherKind() {
        Assert.That(AccountBlockRules.ChatRefusal([Block(2, 5_000)], 5_001), Is.Null);
        Assert.That(AccountBlockRules.ChatRefusal([Block(4, 9_000), Block(7, 9_000)], 4_000), Is.Null);
        Assert.That(AccountBlockRules.ChatRefusal([], 4_000), Is.Null);
    }

    // A stored expiry is a DateTime: the last second of 9999 still fits, the next does not.
    [Test]
    public void ExpiryFits_UpToTheLastDateTimeSecond() {
        Assert.That(AccountBlockRules.MaxExpiry, Is.EqualTo(new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero).ToUnixTimeSeconds()));
        Assert.That(AccountBlockRules.ExpiryFits(AccountBlockRules.MaxExpiry), Is.True);
        Assert.That(AccountBlockRules.ExpiryFits(AccountBlockRules.MaxExpiry + 1), Is.False);
        Assert.That(AccountBlockRules.ExpiryFits(-1), Is.False);
    }

    // The channel holding the account: mode 0 sends the added record as stored, none when it is gone; 1 the removal;
    // 2 the new expiry and the change.
    [Test]
    public void ChannelDispatch_SendsTheModesPacket() {
        AccountBlock[] held = [Block(6, 9_000)];
        var add = new AdminRequest.Types.AccountBlock { AccountId = 1001, Mode = 0, BlockId = 7 };
        Assert.That(ChannelService.AccountBlockPacket(held, add)!.ToArray(), Is.EqualTo(AdminBlockPacket.Load(held).ToArray()));
        Assert.That(ChannelService.AccountBlockPacket([], add), Is.Null);

        var remove = new AdminRequest.Types.AccountBlock { AccountId = 1001, Mode = 1, BlockId = 7 };
        Assert.That(ChannelService.AccountBlockPacket([], remove)!.ToArray(), Is.EqualTo(AdminBlockPacket.Remove(7).ToArray()));

        var extend = new AdminRequest.Types.AccountBlock { AccountId = 1001, Mode = 2, BlockId = 7, Kind = 6, ExpiryTime = 12_600, Delta = 3_600 };
        Assert.That(ChannelService.AccountBlockPacket(held, extend)!.ToArray(), Is.EqualTo(AdminBlockPacket.SetExpiry(7, 6, 12_600, 3_600).ToArray()));

        Assert.That(ChannelService.AccountBlockPacket(held, new AdminRequest.Types.AccountBlock { Mode = 3 }), Is.Null);
    }

    // FindAction walks the kinds from 1: with both a Penalty and kind 6 live, the Penalty answers.
    [Test]
    public void ChatRefusal_LowestKindFirst() {
        Assert.That(AccountBlockRules.ChatRefusal([Block(6, 9_000, id: 8), Block(2, 9_000)], 4_000), Is.EqualTo(StringCode.s_admin_block_velma_notice_chat));
    }
}
