using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Channel.Service;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Tools.Scheduler;
using Serilog;

namespace Maple2.Server.Tests.Game;

// A guild skill or NPC upgrade the World sends (ChannelService.Guild) arrives on a gRPC thread, while the member's
// field thread reads and iterates the guild's lists (NpcLevel, the skill effects). Before, the gRPC thread replaced or
// added to the plain lists itself: an Add while a handler iterated Npcs threw there. The change now waits for the
// member's field thread (RunAsPlayer).
public class GuildListThreadTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private long characterId;
    private FieldManager field = null!;
    private Guild guild = null!;
    private ChannelService channel = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        GameSession session = test.Start();
        characterId = session.CharacterId;
        test.EnterMap(TestGameSession.Map(2000001));
        field = session.Field!;
        var queue = new EventQueue(Log.Logger);
        queue.Start();
        typeof(FieldManager).GetField("Scheduler", Any)!.SetValue(field, queue);

        guild = (Guild) RuntimeHelpers.GetUninitializedObject(typeof(Guild));
        typeof(Guild).GetField("<Npcs>k__BackingField", Any)!.SetValue(guild, new List<GuildNpc>());
        typeof(Guild).GetField("<Buffs>k__BackingField", Any)!.SetValue(guild, new List<GuildBuff> { new(1, 1, 0) });
        typeof(GuildManager).GetField("<Guild>k__BackingField", Any)!.SetValue(session.Guild, guild);
        typeof(GuildManager).GetField("session", Any)!.SetValue(session.Guild, session);

        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> {
            [session.CharacterId] = session,
        });
        channel = new ChannelService(server, null!, test.Storage, null!, null!, null!);
        test.TakeSent();
    }

    private void Call(string method, object update) {
        typeof(ChannelService).GetMethod(method, Any)!.Invoke(channel, [new[] { characterId }, update]);
    }

    [Test]
    public void NpcUpgrade_IsAddedByTheFieldThread() {
        Call("UpgradeGuildNpc", new GuildRequest.Types.UpgradeNpc { RequestorName = "a", Type = (int) GuildNpcType.Quest, Level = 2 });
        int onTheCall = guild.Npcs.Count;
        field.Scheduler.InvokeAll();

        Assert.Multiple(() => {
            Assert.That(onTheCall, Is.Zero, "the gRPC thread leaves the list alone");
            Assert.That(guild.Npcs, Is.EqualTo(new[] { new GuildNpc(GuildNpcType.Quest, 2) }), "the field thread adds it");
        });
    }

    [Test]
    public void SkillUpgrade_IsReplacedByTheFieldThread() {
        Call("UpgradeGuildBuff", new GuildRequest.Types.UpgradeBuff { RequestorName = "a", Id = 1, Level = 3, ExpiryTime = 0 });
        int onTheCall = guild.Buffs[0].Level;
        field.Scheduler.InvokeAll();

        Assert.Multiple(() => {
            Assert.That(onTheCall, Is.EqualTo(1), "the gRPC thread leaves the list alone");
            Assert.That(guild.Buffs[0].Level, Is.EqualTo(3), "the field thread replaces it");
        });
    }
}
