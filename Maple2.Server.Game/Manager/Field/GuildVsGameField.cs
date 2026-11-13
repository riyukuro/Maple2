using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// The rules of a guild vs game (66200001_gd), without the field: the two guilds' teams, the round scores and the
// last round's scorer.
public sealed class GuildVsGameState {
    // The script's user tags: 1 the blue team (guild_game_blue), 2 the red one; 0 is a player of neither guild.
    public const int NoTeam = 0;
    public const int BlueTeam = 1;
    public const int RedTeam = 2;

    private readonly long[] guildIds = new long[3];
    private readonly int[] scores = new int[3];
    // The rewards given, by team and kind (once per game each).
    private readonly HashSet<(int, string)> rewarded = [];

    // The team that scored the last round: 0 when the round was even; -1 before the first round.
    public int LastScorer { get; private set; } = -1;
    public bool Ended { get; private set; }

    // Chosen here: the teams by the guilds' arrival, the first guild to arrive blue and the second red; a player of
    // any other guild (or none) is on no team. The match only sends the two guilds' members here.
    public int Join(long guildId) {
        if (guildId == 0) {
            return NoTeam;
        }
        int team = TeamOf(guildId);
        if (team != NoTeam) {
            return team;
        }
        for (int i = BlueTeam; i <= RedTeam; i++) {
            if (guildIds[i] == 0) {
                guildIds[i] = guildId;
                return i;
            }
        }
        return NoTeam;
    }

    public int TeamOf(long guildId) {
        if (guildId == 0) {
            return NoTeam;
        }
        for (int i = BlueTeam; i <= RedTeam; i++) {
            if (guildIds[i] == guildId) {
                return i;
            }
        }
        return NoTeam;
    }

    public long GuildOf(int team) => IsTeam(team) ? guildIds[team] : 0;

    public int Score(int team) => IsTeam(team) ? scores[team] : 0;

    // Chosen here: a round (GuildVsGameScoreByUser) goes to the team with more players left in the box, which gains
    // the score; an even count gives no team the score. Returns the scorer, 0 for an even round.
    public int ScoreRound(int blueCount, int redCount, int score) {
        int scorer = blueCount > redCount ? BlueTeam : redCount > blueCount ? RedTeam : NoTeam;
        if (scorer != NoTeam) {
            scores[scorer] += score;
        }
        LastScorer = scorer;
        return scorer;
    }

    // The team with the higher total, 0 for a draw.
    public int Winner => scores[BlueTeam] > scores[RedTeam] ? BlueTeam : scores[RedTeam] > scores[BlueTeam] ? RedTeam : NoTeam;

    // Whether this reward of the team is still to give; true once per team and kind.
    public bool TakeReward(int team, string kind) => IsTeam(team) && rewarded.Add((team, kind));

    public void End() => Ended = true;

    private static bool IsTeam(int team) => team is BlueTeam or RedTeam;
}

// The guild vs game of its field (CreateFieldGame GuildVsGame, or the first arrival in the map): it tags the players
// with their guild's team, scores the rounds and gives the result and rewards the script asks for.
public sealed class GuildVsGameField {
    public const string ContributionWin = "winMiniGame";
    public const string ContributionLose = "loseMiniGame";
    // OnPacketVsGameReward's kinds (guildmanager.cpp:2707).
    public const byte RewardExp = 1;
    public const byte RewardFund = 2;

    private readonly ILogger logger = Log.Logger.ForContext<GuildVsGameField>();
    private readonly FieldManager field;
    // The name and mark each team's guild had when its first member arrived, for the result.
    private readonly Dictionary<int, (string Name, string Mark)> guildInfo = [];

    public GuildVsGameState State { get; } = new();

    public GuildVsGameField(FieldManager field) {
        this.field = field;
    }

    // Tags an arriving player with the team of its guild before the field sends the player (FieldPacket.AddPlayer).
    public void Arrive(FieldPlayer player) {
        Guild? guild = player.Session.Guild.Guild;
        int team = State.Join(guild?.Id ?? 0);
        player.TagId = team;
        if (guild != null && team != GuildVsGameState.NoTeam) {
            guildInfo.TryAdd(team, (guild.Name, guild.Emblem));
        }
    }

    // The team's players here who are still in its guild.
    public List<FieldPlayer> TeamPlayers(int team) {
        long guildId = State.GuildOf(team);
        if (guildId == 0) {
            return [];
        }
        return field.Players.Values.Where(player => player.TagId == team && player.Session.Guild.Guild?.Id == guildId).ToList();
    }

    public void ScoreByUser(TriggerBox box, int score) {
        int blue = field.Players.Values.Count(player => player.TagId == GuildVsGameState.BlueTeam && box.Contains(player.Position));
        int red = field.Players.Values.Count(player => player.TagId == GuildVsGameState.RedTeam && box.Contains(player.Position));
        int scorer = State.ScoreRound(blue, red, score);
        logger.Debug("Guild vs game round: blue {Blue}, red {Red}, scorer {Scorer}", blue, red, scorer);
    }

    // A reward of the team's guild (table/guildproperty.xml's mini game values at the guild's level): exp and fund
    // for the guild, added by the World and noticed to the team (Guild 0x7b); guildCoin as guild coin items to each
    // team player here.
    public void GiveReward(string type, int team, bool isWin) {
        List<FieldPlayer> players = TeamPlayers(team);
        Guild? guild = players.FirstOrDefault()?.Session.Guild.Guild;
        if (guild == null || !State.TakeReward(team, type)) {
            return;
        }
        GuildTable.Property? property = GuildRules.PropertyAt(field.TableMetadata.GuildTable.Properties, guild.Experience);
        if (property == null) {
            return;
        }

        switch (type) {
            case "exp": {
                    int exp = isWin ? property.WinMiniGameExp : property.LoseMiniGameExp;
                    if (exp > 0) {
                        players[0].Session.Guild.AddExpFund(exp, 0);
                        Send(players, GuildPacket.VsGameReward(RewardExp, exp));
                    }
                    break;
                }
            case "fund": {
                    int fund = isWin ? property.WinMiniGameFund : property.LoseMiniGameFund;
                    if (fund > 0) {
                        players[0].Session.Guild.AddExpFund(0, fund);
                        Send(players, GuildPacket.VsGameReward(RewardFund, fund));
                    }
                    break;
                }
            case "guildCoin": {
                    int coin = isWin ? property.WinMiniGameCoin : property.LoseMiniGameCoin;
                    if (coin <= 0) {
                        break;
                    }
                    foreach (FieldPlayer player in players) {
                        Item? item = field.ItemDrop.CreateItem(Constant.GuildCoinId, Constant.GuildCoinRarity, coin);
                        if (item == null || !player.Session.Item.Inventory.Add(item, true)) {
                            logger.Warning("Guild vs game coin of {CharacterId} not given", player.Session.CharacterId);
                        }
                    }
                    break;
                }
            default:
                logger.Warning("Unknown guild vs game reward type {Type}", type);
                break;
        }
    }

    // Each team player here earns the guild contribution of a win or a loss (table/guildcontribution.xml).
    public void GiveContribution(int team, bool isWin) {
        if (!State.TakeReward(team, "contribution")) {
            return;
        }
        int value = field.TableMetadata.GuildTable.Contributions?.GetValueOrDefault(isWin ? ContributionWin : ContributionLose) ?? 0;
        if (value <= 0) {
            return;
        }
        foreach (FieldPlayer player in TeamPlayers(team)) {
            player.Session.Guild.AddContribution(value);
        }
    }

    // The result dialog (Guild 0x7a) to both teams' players: the blue guild first, then the red one.
    public void Result() {
        var guilds = new List<(long, string, string, int)>();
        for (int team = GuildVsGameState.BlueTeam; team <= GuildVsGameState.RedTeam; team++) {
            (string name, string mark) = guildInfo.GetValueOrDefault(team, (string.Empty, string.Empty));
            guilds.Add((State.GuildOf(team), name, mark, State.Score(team)));
        }
        var packet = GuildPacket.VsGameResult(guilds);
        Send(TeamPlayers(GuildVsGameState.BlueTeam).Concat(TeamPlayers(GuildVsGameState.RedTeam)), packet);
    }

    public void LogResult() {
        logger.Information("Guild vs game {MapId}/{RoomId}: blue {BlueGuild} {BlueScore}, red {RedGuild} {RedScore}", field.MapId, field.RoomId,
            State.GuildOf(GuildVsGameState.BlueTeam), State.Score(GuildVsGameState.BlueTeam),
            State.GuildOf(GuildVsGameState.RedTeam), State.Score(GuildVsGameState.RedTeam));
    }

    public void LogWonByDefault(int team) {
        logger.Information("Guild vs game {MapId}/{RoomId}: guild {GuildId} won by default", field.MapId, field.RoomId, State.GuildOf(team));
    }

    private static void Send(IEnumerable<FieldPlayer> players, Maple2.PacketLib.Tools.ByteWriter packet) {
        foreach (FieldPlayer player in players) {
            player.Session.Send(packet);
        }
    }
}
