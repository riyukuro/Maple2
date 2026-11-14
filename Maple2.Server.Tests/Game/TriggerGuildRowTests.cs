using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Game;

// The guild vs game trigger actions on a field whose player is in a guild of the match.
public class TriggerGuildRowTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const long GuildId = 79;

    private sealed class GuildWorld : WorldClient {
        public readonly ConcurrentQueue<GuildRequest> Requests = new();
        public readonly SemaphoreSlim Received = new(0);

        public override GuildResponse Guild(GuildRequest request, CallOptions options) {
            Requests.Enqueue(request);
            Received.Release();
            return new GuildResponse { GuildId = GuildId, AddedExp = request.AddExpFund?.Exp ?? 0 };
        }
    }

    private GuildWorld world = null!;

    private static void Set<T>(object target, string field, object? value) {
        (typeof(T).GetField(field, Any) ?? throw new MissingFieldException(typeof(T).Name, field)).SetValue(target, value);
    }

    // The player's guild plays blue against guild 80; the guild table's one level gives 30 exp for a won mini game and
    // 10 for a lost one.
    private void StartMatch() {
        Start();
        GameSession session = test.Session;
        world = new GuildWorld();
        Set<GameSession>(session, "<World>k__BackingField", world);
        var guild = new Guild(GuildId, "Vs", test.AccountId, test.CharacterId, "Master");
        var info = new PlayerInfo(new CharacterInfo(test.AccountId, test.CharacterId, "Master", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        guild.Members[test.CharacterId] = new GuildMember { GuildId = GuildId, Info = info };
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        Set<GuildManager>(manager, "session", session);
        Set<GuildManager>(manager, "logger", new LoggerConfiguration().CreateLogger());
        Set<GuildManager>(manager, "<Guild>k__BackingField", guild);
        session.Guild = manager;
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> {
                [1] = new(1, 1000, 50, 1000, 0, 0, 30, 10, 0, 0, 0, 0, 0, 0f, 0f, 0, 0, 0, 0),
            }));

        GuildVsGameField game = Field.StartGuildVsGame();
        game.SetMatch(0, GuildId, 80, _ => { });
        game.Arrive(session.Player);
    }

    private GuildRequest? NextRequest(int waitMs) {
        return world.Received.Wait(waitMs) && world.Requests.TryDequeue(out GuildRequest? request) ? request : null;
    }

    // guild_vs_game_give_reward (28): the team's guild gets the table's exp for a win (or a loss) once per kind; a team
    // with no player here, or a second reward of the kind, adds nothing.
    [Test]
    public void GuildVsGameGiveReward_TheTeamsGuildOnce() {
        StartMatch();

        Action("""<action name="guild_vs_game_give_reward" type="exp" team_id="2" is_win="1" desc="" />""");
        Action("""<action name="guild_vs_game_give_reward" type="exp" team_id="1" is_win="0" desc="" />""");
        GuildRequest? request = NextRequest(10_000);
        Assert.That(request, Is.Not.Null);
        Assert.That((request!.AddExpFund.GuildId, request.AddExpFund.Exp, request.AddExpFund.Fund), Is.EqualTo((GuildId, 10, 0)));

        Action("""<action name="guild_vs_game_give_reward" type="exp" team_id="1" is_win="1" desc="" />""");
        Assert.That(NextRequest(300), Is.Null);
    }
}
