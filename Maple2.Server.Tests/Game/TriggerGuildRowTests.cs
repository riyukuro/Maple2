using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;
using Serilog;
using Serilog.Core;
using Serilog.Events;
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

    // set_user_value_from_guild_vs_game_score (12 KR uses: team 1 to BlueteamScore, 2 to RedteamScore): the team's
    // total becomes the trigger's user value.
    [Test]
    public void SetUserValueFromGuildVsGameScore_TheTeamsTotal() {
        StartMatch();
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues))!.SetValue(Field, new TriggerUserValues());
        Field.GuildVsGame!.State.ScoreRound(2, 1, 3);
        Field.GuildVsGame.State.ScoreRound(0, 1, 2);
        Field.GuildVsGame.State.ScoreRound(3, 1, 1);

        Action("""<action name="set_user_value_from_guild_vs_game_score" team_id="1" key="BlueteamScore" />""");
        Action("""<action name="set_user_value_from_guild_vs_game_score" team_id="2" key="RedteamScore" />""");
        Assert.That((Field.UserValues.Get(1, "BlueteamScore"), Field.UserValues.Get(1, "RedteamScore")), Is.EqualTo((4, 2)));
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

    // guild_vs_game_give_contribution (10 KR uses; 66200001_gd team_id="2" is_win="false", then team_id="1"): each player
    // of the team asks for the member contribution of a lost (or won) mini game, once per team; a team with no player
    // here asks for nothing.
    [Test]
    public void GuildVsGameGiveContribution_TheTeamsPlayersOnce() {
        StartMatch();
        GuildTable table = test.Session.TableMetadata.GuildTable;
        test.SetTable(table with {
            Contributions = new Dictionary<string, int> { [GuildContributionKind.WinMiniGame] = 20, [GuildContributionKind.LoseMiniGame] = 5 },
        });

        Action("""<action name="guild_vs_game_give_contribution" team_id="2" is_win="false" />""");
        Action("""<action name="guild_vs_game_give_contribution" team_id="1" is_win="false" />""");
        GuildRequest? request = NextRequest(10_000);
        Assert.That(request, Is.Not.Null);
        Assert.That((request!.RequestorId, request.AddContribution.GuildId, request.AddContribution.Kind),
            Is.EqualTo((test.CharacterId, GuildId, GuildContributionKind.LoseMiniGame)));

        Action("""<action name="guild_vs_game_give_contribution" team_id="1" is_win="true" />""");
        Assert.That(NextRequest(300), Is.Null);
    }

    // guild_vs_game_score_by_user (66200001_gd): the round's score goes to the team with more players in the box; a
    // box with none of them scores nothing.
    [Test]
    public void GuildVsGameScoreByUser_TheTeamInTheBox() {
        StartMatch();
        SetTriggers(new Ms2TriggerBox(Vector3.Zero, new Vector3(300, 300, 300), 901, true),
            new Ms2TriggerBox(new Vector3(5000, 0, 0), new Vector3(300, 300, 300), 902, true));
        context = NewContext();

        Action("""<action name="guild_vs_game_score_by_user" box_id="902" score="5" desc="" />""");
        Action("""<action name="guild_vs_game_score_by_user" box_id="901" score="3" desc="" />""");
        GuildVsGameState state = Field.GuildVsGame!.State;
        Assert.That((state.Score(GuildVsGameState.BlueTeam), state.Score(GuildVsGameState.RedTeam), state.LastScorer),
            Is.EqualTo((3, 0, GuildVsGameState.BlueTeam)));
    }

    // guild_vs_game_result: the teams' players get the result of both guilds; guild_vs_game_end_game ends the game, so
    // no later round scores.
    [Test]
    public void GuildVsGameResultAndEndGame() {
        StartMatch();
        Field.GuildVsGame!.State.ScoreRound(1, 0, 4);
        test.TakeSent();

        Action("""<action name="guild_vs_game_result" desc="" />""");
        ByteReader result = Sent(SendOp.Guild).Single();
        Assert.That(result.ReadByte(), Is.EqualTo(122));
        Assert.That(result.ReadLong(), Is.EqualTo(GuildId));

        Action("""<action name="guild_vs_game_end_game" />""");
        Assert.That(Field.GuildVsGame.State.Ended, Is.True);
        Assert.That(Field.GuildVsGame.State.ScoreRound(1, 0, 4), Is.EqualTo(GuildVsGameState.NoTeam));
    }

    private sealed class LogSink : ILogEventSink {
        public readonly List<LogEvent> Events = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    // guild_vs_game_log_result and guild_vs_game_log_won_by_default: the game's log gets the guilds and scores, and
    // the guild of the team that won by default.
    [Test]
    public void GuildVsGameLogs_TheGuildsOfTheMatch() {
        StartMatch();
        var log = new LogSink();
        Set<GuildVsGameField>(Field.GuildVsGame!, "logger", new LoggerConfiguration().WriteTo.Sink(log).CreateLogger());
        Field.GuildVsGame!.State.ScoreRound(1, 0, 4);

        Action("""<action name="guild_vs_game_log_result" desc="" />""");
        Action("""<action name="guild_vs_game_log_won_by_default" team_id="2" desc="" />""");
        Assert.That(log.Events, Has.Count.EqualTo(2));
        Assert.That((log.Events[0].Properties["BlueGuild"].ToString(), log.Events[0].Properties["BlueScore"].ToString(),
            log.Events[0].Properties["RedGuild"].ToString()), Is.EqualTo(("79", "4", "80")));
        Assert.That(log.Events[1].Properties["GuildId"].ToString(), Is.EqualTo("80"));
    }
}
