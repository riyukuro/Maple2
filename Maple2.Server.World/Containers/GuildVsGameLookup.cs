using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Event;
using Maple2.Server.Channel.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

// The guild vs game matchmaker (Guild 115/116/120). A guild with the mini game right takes a ticket while a
// GuildVsGameOpen event is on; two ticket holders are matched into one field of the guild vs game map (66200001),
// and every member of both guilds gets the entry prompt (Guild 119 with the game's id). The queue is under one lock;
// the channel calls (make the field, tell the guilds) run after the lock is released, as in PvpLookup.
// Chosen here: guilds are matched in the order of their tickets, the first one blue; no minimum of members online
// (the client has s_guild_err_requireOnlineUserCount, but no data names the count); a match can be entered until its
// field ends the game, its channel goes down or MatchLifetime passes.
public sealed class GuildVsGameLookup : IDisposable {
    // Makes the match field: (match id, blue guild, red guild) -> (error, channel, map id, room id).
    public delegate (MigrationError Error, int Channel, int MapId, int RoomId) MakeField(int matchId, long blueGuildId, long redGuildId);
    // Sends a notice (the channel GuildRequest's VsGame cases) to the guild's members.
    public delegate void Notify(long guildId, GuildRequest notice);

    public sealed class Match {
        public required int Id { get; init; }
        public required long Blue { get; init; }
        public required long Red { get; init; }
        public required long MadeTick { get; init; }
        public int Channel { get; set; }
        public int MapId { get; set; }
        public int RoomId { get; set; }
        public bool Ready { get; set; }
    }

    public static readonly TimeSpan MatchLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    private readonly ILogger logger = Log.Logger.ForContext<GuildVsGameLookup>();
    private readonly object mutex = new();
    // The ticket holders, in the order of their tickets.
    private readonly List<long> waiting = [];
    private readonly Dictionary<int, Match> matches = [];
    private readonly MakeField makeField;
    private readonly Notify notify;
    private readonly Func<bool> isOpen;
    private readonly Func<long> tick;
    private readonly Timer? timer;
    private int nextMatchId;

    public GuildVsGameLookup(ChannelClientLookup channelClients, GuildLookup guildLookup, ServerTableMetadataStorage serverTableMetadata)
        : this(ChannelField(channelClients), GuildNotify(guildLookup), EventOpen(serverTableMetadata), () => Environment.TickCount64, true, FirstIdFromClock()) {
        channelClients.ChannelInactive += EndChannel;
    }

    public GuildVsGameLookup(MakeField makeField, Notify notify, Func<bool> isOpen, Func<long> tick, bool tickTimer = false, int lastMatchId = 0) {
        nextMatchId = lastMatchId;
        this.makeField = makeField;
        this.notify = notify;
        this.isOpen = isOpen;
        this.tick = tick;
        if (tickTimer) {
            timer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
        }
    }

    public void Dispose() {
        timer?.Dispose();
    }

    // The ids of a World start begin after the clock's seconds (below 2^30), so a match field left on a channel from
    // an earlier start (its VsGameEnd) cannot end a new match of the same id.
    private static int FirstIdFromClock() => (int) (DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0x3FFFFFFF);

    // The task of the last match made (tests wait on it).
    public Task LastStep { get; private set; } = Task.CompletedTask;

    // Guild 115: the guild takes a ticket; with another guild waiting, the two are matched.
    public GuildError Register(long guildId) {
        Match? made = null;
        bool wasWaiting;
        lock (mutex) {
            if (!isOpen()) {
                return GuildError.s_guild_err_isNotVsGameTime;
            }
            if (MatchOfInternal(guildId) != null) {
                // The client asks only without an open game (s_guild_vsGame_another_game_exist).
                return GuildError.s_guild_err_isNotVsGameTime;
            }
            wasWaiting = waiting.Contains(guildId);
            if (!wasWaiting) {
                long other = waiting.FirstOrDefault();
                if (other != 0) {
                    waiting.Remove(other);
                    made = new Match { Id = ++nextMatchId, Blue = other, Red = guildId, MadeTick = tick() };
                    matches[made.Id] = made;
                } else {
                    waiting.Add(guildId);
                }
            }
        }

        TryNotify(guildId, Ticket(true, !wasWaiting));
        if (made != null) {
            LastStep = Task.Run(() => MakeMatch(made));
        }
        return GuildError.none;
    }

    // Guild 116: the guild gives its ticket back. A guild without one gets the ticket state without a line.
    public GuildError Unregister(long guildId) {
        bool removed;
        lock (mutex) {
            removed = waiting.Remove(guildId);
        }
        TryNotify(guildId, Ticket(false, removed));
        return GuildError.none;
    }

    // Guild 120: the room of the guild's match. Not ready (or none): the error isNotVsGameTime.
    public GuildError Join(long guildId, out int channel, out int mapId, out int roomId) {
        lock (mutex) {
            Match? match = MatchOfInternal(guildId);
            channel = match?.Channel ?? 0;
            mapId = match?.MapId ?? 0;
            roomId = match?.RoomId ?? 0;
            return match is { Ready: true } ? GuildError.none : GuildError.s_guild_err_isNotVsGameTime;
        }
    }

    // The match field ended the game (GuildVsGameEndGame): both guilds' open game is cleared (Guild 119 with 0).
    public void End(int matchId) {
        Match? match;
        lock (mutex) {
            matches.Remove(matchId, out match);
        }
        if (match != null) {
            TellClosed(match);
        }
    }

    public bool IsWaiting(long guildId) {
        lock (mutex) {
            return waiting.Contains(guildId);
        }
    }

    public Match? MatchOf(long guildId) {
        lock (mutex) {
            return MatchOfInternal(guildId);
        }
    }

    // The timer: when no GuildVsGameOpen event is on, the tickets are canceled (Guild 117); a match past its
    // lifetime ends.
    public void Tick() {
        try {
            TickInternal();
        } catch (Exception ex) {
            logger.Error(ex, "Guild vs game tick failed");
        }
    }

    private void TickInternal() {
        List<long> canceled = [];
        List<Match> expired;
        lock (mutex) {
            if (waiting.Count > 0 && !isOpen()) {
                canceled.AddRange(waiting);
                waiting.Clear();
            }
            long now = tick();
            expired = matches.Values.Where(match => now - match.MadeTick >= (long) MatchLifetime.TotalMilliseconds).ToList();
            foreach (Match match in expired) {
                matches.Remove(match.Id);
            }
        }
        foreach (long guildId in canceled) {
            TryNotify(guildId, new GuildRequest { VsGameCanceled = new GuildRequest.Types.VsGameCanceled() });
        }
        foreach (Match match in expired) {
            logger.Information("Guild vs game match {MatchId} expired", match.Id);
            TellClosed(match);
        }
    }

    // A channel went down: its match fields are gone.
    public void EndChannel(int channel) {
        List<Match> ended;
        lock (mutex) {
            ended = matches.Values.Where(match => match.Ready && match.Channel == channel).ToList();
            foreach (Match match in ended) {
                matches.Remove(match.Id);
            }
        }
        foreach (Match match in ended) {
            TellClosed(match);
        }
    }

    private Match? MatchOfInternal(long guildId) => matches.Values.FirstOrDefault(match => match.Blue == guildId || match.Red == guildId);

    private void MakeMatch(Match match) {
        (MigrationError error, int channel, int mapId, int roomId) result;
        try {
            result = makeField(match.Id, match.Blue, match.Red);
        } catch (Exception ex) {
            logger.Warning(ex, "Guild vs game match {MatchId}: the match field was not made", match.Id);
            result = (MigrationError.s_move_err_FailCreateDungeon, 0, 0, 0);
        }

        bool ready;
        lock (mutex) {
            ready = result.error == MigrationError.ok && matches.ContainsKey(match.Id);
            if (ready) {
                match.Channel = result.channel;
                match.MapId = result.mapId;
                match.RoomId = result.roomId;
                match.Ready = true;
            } else {
                matches.Remove(match.Id);
            }
        }
        foreach (long guildId in new[] { match.Blue, match.Red }) {
            TryNotify(guildId, ready
                ? new GuildRequest { VsGameOpen = new GuildRequest.Types.VsGameOpen { GameId = match.Id } }
                : new GuildRequest { VsGameCanceled = new GuildRequest.Types.VsGameCanceled() });
        }
    }

    private void TellClosed(Match match) {
        foreach (long guildId in new[] { match.Blue, match.Red }) {
            TryNotify(guildId, new GuildRequest { VsGameOpen = new GuildRequest.Types.VsGameOpen { GameId = 0 } });
        }
    }

    private static GuildRequest Ticket(bool registered, bool notify) => new() {
        VsGameTicket = new GuildRequest.Types.VsGameTicket { Registered = registered, Notify = notify },
    };

    private void TryNotify(long guildId, GuildRequest notice) {
        try {
            notify(guildId, notice);
        } catch (Exception ex) {
            logger.Warning(ex, "Guild vs game notice {Notice} to guild {GuildId} not sent", notice.GuildCase, guildId);
        }
    }

    private static MakeField ChannelField(ChannelClientLookup channelClients) {
        return (matchId, blue, red) => {
            int channel = channelClients.FirstChannel();
            if (!channelClients.TryGetClient(channel, out ChannelClient? client)) {
                return (MigrationError.s_move_err_no_server, 0, 0, 0);
            }
            FieldResponse response = client.Field(new FieldRequest {
                CreateGuildVsGame = new FieldRequest.Types.CreateGuildVsGame { MatchId = matchId, BlueGuildId = blue, RedGuildId = red },
            }, deadline: DateTime.UtcNow.AddSeconds(10));
            return ((MigrationError) response.Error, channel, response.MapId, response.RoomId);
        };
    }

    private static Notify GuildNotify(GuildLookup guildLookup) {
        return (guildId, notice) => {
            if (guildLookup.TryGet(guildId, out GuildManager? manager)) {
                manager.Broadcast(notice, member => member.Info.Online);
            }
        };
    }

    // Whether a GuildVsGameOpen event is on now, its day of the week and time of day too.
    private static Func<bool> EventOpen(ServerTableMetadataStorage serverTableMetadata) {
        return () => serverTableMetadata.GetGameEvents()
            .Any(gameEvent => gameEvent.Metadata.Type == GameEventType.GuildVsGameOpen && gameEvent.IsActive(DateTime.Now));
    }
}
