using System.Reflection;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// The character block's survival level (Unk_UserProxy::FUN_1408eeb60, userproxy.cpp: i16 +0x1ac, then the last i64),
// which the client puts on the PC (PC::FUN_141663b10) for the SURVIVAL_LEVEL nametag icon at 5 or more.
public class SurvivalLevelNametagTests {
    private static short LevelBeforeLastLong(ByteWriter block) {
        var reader = new ByteReader(block.Buffer[..block.Length], block.Length - 8 - 2);
        return reader.ReadShort();
    }

    [TestCase(0, 0)]
    [TestCase(5, 5)]
    [TestCase(70000, short.MaxValue)]
    public void FieldAddUser_WritesTheAccountsLevel(int level, short expected) {
        var test = new TestGameSession();
        test.Start();
        GameSession session = test.Session;
        session.Player.Value.Account.SurvivalLevel = level;

        var block = new ByteWriter();
        typeof(FieldPacket).GetMethod("WriteCharacter", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [block, session]);

        Assert.That(LevelBeforeLastLong(block), Is.EqualTo(expected));
    }

    [Test]
    public void CharacterList_WritesTheAccountsLevel() {
        var account = new Account { Username = "user", SurvivalLevel = 12 };
        var character = new Character { Name = "name", Mastery = new Mastery() };
        character.ReturnMaps.Push(2000062);

        var block = new ByteWriter();
        typeof(CharacterListPacket).GetMethod("WriteCharacter", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [block, account, character]);

        Assert.That(LevelBeforeLastLong(block), Is.EqualTo(12));
    }
}
