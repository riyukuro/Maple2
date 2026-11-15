using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// HomeBlock (HomeBlockManager, the buddy window's home block list): add 2, remove 3, update memo 5. Each answer is
// the packet HomeBlockManager::OnPacket reads (add, remove and memo echo the row; result 6 carries the notice code).
public class HomeBlockHandlerTests {
    private TestGameSession ownerTest = null!;
    private TestGameSession otherTest = null!;
    private GameSession owner => ownerTest.Session;
    private GameSession other => otherTest.Session;
    private HomeBlockHandler handler = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        ownerTest = new TestGameSession(options);
        otherTest = new TestGameSession(options);
        ownerTest.Start();
        otherTest.Start();
        handler = new HomeBlockHandler { BanWordStorage = TestGameSession.BanWords("badword") };
        ownerTest.TakeSent();
    }

    private void Add(GameSession session, long characterId, string memo) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(characterId);
            writer.WriteUnicodeString(memo);
        }));
    }

    private static byte[] Bytes(Maple2.PacketLib.Tools.ByteWriter packet) => packet.ToArray();

    private HomeBlockEntry Stored() {
        using GameStorage.Request db = owner.GameStorage.Context();
        return db.GetHomeBlocks(owner.AccountId).Single();
    }

    [Test]
    public void Add_StoresTheRowAndEchoesIt() {
        Add(owner, other.CharacterId, "memo");

        HomeBlockEntry entry = Stored();
        Assert.That(entry.CharacterId, Is.EqualTo(other.CharacterId));
        Assert.That(entry.AccountId, Is.EqualTo(other.AccountId));
        Assert.That(entry.Memo, Is.EqualTo("memo"));
        List<byte[]> sent = ownerTest.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0], Is.EqualTo(Bytes(HomeBlockPacket.Add(entry))));
    }

    [Test]
    public void Add_CutsTheMemoAtFiftyCharacters() {
        Add(owner, other.CharacterId, new string('a', 80));

        Assert.That(Stored().Memo, Has.Length.EqualTo(50));
    }

    [Test]
    public void Add_ABannedMemoIsRefusedWithTheBanWordBox() {
        Add(owner, other.CharacterId, "a badword here");

        Assert.That(ownerTest.TakeSent(), Is.EqualTo(new[] { Bytes(NoticePacket.MessageBox(StringCode.s_ban_check_err_any)) }));
        using GameStorage.Request db = owner.GameStorage.Context();
        Assert.That(db.CountHomeBlocks(owner.AccountId), Is.EqualTo(0));
    }

    [Test]
    public void Add_RefusalsAnswerWithTheirResultCode() {
        Add(owner, 999999, "");
        Add(owner, owner.CharacterId, "");
        Add(owner, other.CharacterId, "");
        ownerTest.TakeSent();
        Add(owner, other.CharacterId, "");

        Assert.That(ownerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeBlockPacket.Result(HomeBlockPacket.Error.AlreadyBlocked)) }));
        Add(owner, 999999, "");
        Assert.That(ownerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeBlockPacket.Result(HomeBlockPacket.Error.NoUser)) }));
        Add(owner, owner.CharacterId, "");
        Assert.That(ownerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeBlockPacket.Result(HomeBlockPacket.Error.SelfBlock)) }));
    }

    [Test]
    public void Add_StopsAtTheBlockLimit() {
        ownerTest.SetServerTable(ownerTest.ServerTableMetadata.ConstantsTable with { MaxBlockCount = 1 });
        Add(owner, other.CharacterId, "");
        ownerTest.TakeSent();
        var third = new TestGameSession(ownerTest.Options);
        third.Start();

        Add(owner, third.CharacterId, "");

        Assert.That(ownerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeBlockPacket.Result(HomeBlockPacket.Error.MaxBlock)) }));
    }

    [Test]
    public void Add_SendsAVisitorInTheOwnersHomeOut() {
        ownerTest.EnterHome();
        otherTest.EnterMap(TestGameSession.Map(Constant.DefaultHomeMapId));
        other.Player.Value.Character.MapId = Constant.DefaultHomeMapId;
        other.Field = owner.Field;
        var sessions = (ConcurrentDictionary<long, GameSession>) typeof(Maple2.Server.Game.GameServer)
            .GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(typeof(GameSession).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner))!;
        sessions[other.CharacterId] = other;
        otherTest.TakeSent();

        Add(owner, other.CharacterId, "");

        Assert.That(otherTest.TakeSent().Any(p => p.SequenceEqual(Bytes(HomeBlockPacket.Result(HomeBlockPacket.Error.Kicked)))), Is.True);
        Assert.That(otherTest.World.Moves, Has.Count.EqualTo(1));
    }

    [Test]
    public void Remove_DropsTheRowAndEchoesTheId() {
        Add(owner, other.CharacterId, "");
        long id = Stored().Id;
        ownerTest.TakeSent();

        handler.Handle(owner, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(id);
        }));

        Assert.That(ownerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeBlockPacket.Remove(id)) }));
        using GameStorage.Request db = owner.GameStorage.Context();
        Assert.That(db.CountHomeBlocks(owner.AccountId), Is.EqualTo(0));
    }

    [Test]
    public void Remove_AnUnknownRowAnswersTheUnknownNotice() {
        handler.Handle(owner, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(12345);
        }));

        Assert.That(ownerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeBlockPacket.Result(HomeBlockPacket.Error.Unknown)) }));
    }

    [Test]
    public void UpdateMemo_StoresAndEchoesIt_AndAnUnknownRowIsRefused() {
        Add(owner, other.CharacterId, "old");
        long id = Stored().Id;
        ownerTest.TakeSent();

        handler.Handle(owner, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(id);
            writer.WriteUnicodeString("new");
        }));
        Assert.That(ownerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeBlockPacket.UpdateMemo(id, "new")) }));
        Assert.That(Stored().Memo, Is.EqualTo("new"));

        handler.Handle(owner, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(id);
            writer.WriteUnicodeString("badword");
        }));
        Assert.That(ownerTest.TakeSent(), Is.EqualTo(new[] { Bytes(NoticePacket.MessageBox(StringCode.s_ban_check_err_any)) }));

        handler.Handle(owner, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(777);
            writer.WriteUnicodeString("x");
        }));
        Assert.That(ownerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeBlockPacket.Result(HomeBlockPacket.Error.Unknown)) }));
    }
}
