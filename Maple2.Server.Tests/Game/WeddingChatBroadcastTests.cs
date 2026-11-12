using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game;

// ChannelService.Chat for a wedding megaphone line (kind 0x19): the world hands it to every channel, and each
// channel sends the line with its hall block to every session it holds, not only to the sender or the couple.
public class WeddingChatBroadcastTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void WeddingLine_GoesToEverySessionOfTheChannel() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        TestGameSession[] tests = [new(options), new(options), new(options)];
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        foreach (TestGameSession test in tests) {
            test.AddMap(TestGameSession.Map(2000023));
            GameSession session = test.Start();
            test.EnterMap(TestGameSession.Map(2000023));
            test.TakeSent();
            sessions[session.CharacterId] = session;
        }
        Assume.That(sessions, Has.Count.EqualTo(tests.Length));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        var channel = new ChannelService(server, null!, tests[0].Storage, null!, null!, null!);

        channel.Chat(new ChatRequest {
            AccountId = 1,
            CharacterId = 2,
            Name = "sender",
            Message = "come",
            Channel = 3,
            Wedding = new ChatRequest.Types.Wedding {
                ItemId = 20800100,
                MarriageId = 7001,
                PackageId = 4,
                HallId = 5,
                CeremonyTime = 1700000000,
                Groom = "groom",
                Bride = "bride",
            },
        }, null!).GetAwaiter().GetResult();

        string expected = Convert.ToHexString(ChatPacket.Wedding(1, 2, "sender", "come", 20800100, 7001, 4, 5, 1700000000, "groom", "bride", channel: 3).ToArray());
        foreach (TestGameSession test in tests) {
            string[] lines = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.UserChat).Select(Convert.ToHexString).ToArray();
            Assert.That(lines, Is.EqualTo(new[] { expected }));
        }
    }
}
