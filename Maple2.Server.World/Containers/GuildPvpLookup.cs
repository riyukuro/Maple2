using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Event;
using Maple2.Server.Channel.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

// The guild championship matchmaker (Pvp 1/2/3). While a GuildChampionship event is on, a guild registers (Guild
// 0x2b, then 0x27 state 1); the timer pairs the registered guilds by the closest score, the longest waiting first,
// makes the match field of the championship map (64000001) and tells both guilds (Guild 0x28, 0x27 state 2). A
// guild left unpaired for the matching time (the constant guildPVPMatchingTime, in minutes) is told so (0x28 false)
// and goes back to state 0. Chosen here (the client has no rule): after a match a guild cannot register again for
// Cooldown, and the same two guilds are not paired again for PairCooldown. A disbanded guild leaves the queue and its
// match (Drop). The queue is under one lock; the channel calls run after it, as in GuildVsGameLookup.
public sealed class GuildPvpLookup : IDisposable {
    // Makes the match field: (match id, guild A, guild B) -> (error, channel, map id, room id).
    public delegate (MigrationError Error, int Channel, int MapId, int RoomId) MakeField(int matchId, long guildA, long guildB);
    // Sends a notice (the channel GuildRequest's Pvp cases) to the guild's members online.
    public delegate void Notify(long guildId, GuildRequest notice);

    public sealed class Match {
        public required int Id { get; init; }
        public required long A { get; init; }
        public required long B { get; init; }
        public required long MadeTick { get; init; }
        public int Channel { get; set; }
        public int MapId { get; set; }
        public int RoomId { get; set; }
        public bool Ready { get; set; }
    }

    private sealed record Waiter(long GuildId, int Score, long Tick);

    public static readonly TimeSpan MatchLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PairCooldown = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    private readonly ILogger logger = Log.Logger.ForContext<GuildPvpLookup>();
    private readonly object mutex = new();
    private readonly List<Waiter> waiting = [];
    private readonly Dictionary<int, Match> matches = [];
    // The tick until which a guild cannot register, and a pair (lower id first) cannot be paired.
    private readonly Dictionary<long, long> registerAfter = [];
    private readonly Dictionary<(long, long), long> pairAfter = [];
    private readonly MakeField makeField;
    private readonly Notify notify;
    private readonly Func<bool> isOpen;
    private readonly Func<long> tick;
    private readonly TimeSpan matchingTime;
    private readonly Action? onTick;
    private readonly Timer? timer;
    private int nextMatchId;

    public GuildPvpLookup(ChannelClientLookup channelClients, GuildLookup guildLookup, ServerTableMetadataStorage serverTableMetadata)
        : this(ChannelField(channelClients), GuildNotify(guildLookup), EventOpen(serverTableMetadata), () => Environment.TickCount64,
            MatchingTime(serverTableMetadata), () => guildLookup.RollPvp(DateTime.Now), true, FirstIdFromClock()) {
        channelClients.ChannelInactive += EndChannel;
    }

    public GuildPvpLookup(MakeField makeField, Notify notify, Func<bool> isOpen, Func<long> tick, TimeSpan matchingTime,
                          Action? onTick = null, bool tickTimer = false, int lastMatchId = 0) {
        nextMatchId = lastMatchId;
        this.makeField = makeField;
        this.notify = notify;
        this.isOpen = isOpen;
        this.tick = tick;
        this.matchingTime = matchingTime;
        this.onTick = onTick;
        if (tickTimer) {
            timer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
        }
    }

    public void Dispose() {
        timer?.Dispose();
    }

    private static int FirstIdFromClock() => (int) (DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0x3FFFFFFF);

    // The task of the last match made (tests wait on it).
    public Task LastStep { get; private set; } = Task.CompletedTask;

    public bool IsOpen => isOpen();

    // Pvp 1: the guild (with its score) waits for a match; the members get Guild 0x2b with the registering member's
    // name and the state 1. A guild already waiting is told again; one in a match cannot register, one whose match
    // ended less than Cooldown ago is told to wait (s_guild_search_last_request, "Please wait a moment.").
    public GuildError Register(long guildId, int score, string memberName) {
        lock (mutex) {
            if (!isOpen()) {
                return GuildError.s_guild_err_isNotVsGameTime;
            }
            if (MatchOfInternal(guildId) != null) {
                return GuildError.s_guild_err_cant_during_pvp;
            }
            if (registerAfter.TryGetValue(guildId, out long after) && tick() < after) {
                return GuildError.s_guild_search_last_request;
            }
            if (waiting.All(waiter => waiter.GuildId != guildId)) {
                waiting.Add(new Waiter(guildId, score, tick()));
            }
        }
        TryNotify(guildId, State(GuildPvpState.Registered, memberName, 0x2b));
        return GuildError.none;
    }

    // Pvp 2: the registration is taken back (Guild 0x2c with the member's name, state 0). In a match: refused.
    public GuildError Unregister(long guildId, string memberName) {
        lock (mutex) {
            if (MatchOfInternal(guildId) != null) {
                return GuildError.s_guild_err_cant_during_pvp;
            }
            waiting.RemoveAll(waiter => waiter.GuildId == guildId);
        }
        TryNotify(guildId, State(GuildPvpState.None, memberName, 0x2c));
        return GuildError.none;
    }

    // Pvp 3: the room of the guild's match. Not ready (or none): isNotVsGameTime.
    public GuildError Join(long guildId, out int channel, out int mapId, out int roomId) {
        lock (mutex) {
            Match? match = MatchOfInternal(guildId);
            channel = match?.Channel ?? 0;
            mapId = match?.MapId ?? 0;
            roomId = match?.RoomId ?? 0;
            return match is { Ready: true } ? GuildError.none : GuildError.s_guild_err_isNotVsGameTime;
        }
    }

    // The match field ended the match: the match is gone and both guilds go back to state 0. Returns the match
    // (null when it was not held, so its result is not applied twice).
    public Match? End(int matchId) {
        Match? match;
        lock (mutex) {
            if (matches.Remove(matchId, out match)) {
                Rest(match);
            }
        }
        if (match != null) {
            TellClosed(match);
        }
        return match;
    }

    // A disbanded guild: out of the queue, and its match is gone (the field's end then finds no match).
    public void Drop(long guildId) {
        Match? match;
        lock (mutex) {
            waiting.RemoveAll(waiter => waiter.GuildId == guildId);
            registerAfter.Remove(guildId);
            match = MatchOfInternal(guildId);
            if (match != null) {
                matches.Remove(match.Id);
                Rest(match);
            }
        }
        if (match != null) {
            logger.Information("Guild championship match {MatchId}: guild {GuildId} disbanded", match.Id, guildId);
            TellClosed(match);
        }
    }

    // Under the lock: the match's guilds rest for Cooldown, the pair for PairCooldown.
    private void Rest(Match match) {
        long now = tick();
        registerAfter[match.A] = now + (long) Cooldown.TotalMilliseconds;
        registerAfter[match.B] = now + (long) Cooldown.TotalMilliseconds;
        pairAfter[Pair(match.A, match.B)] = now + (long) PairCooldown.TotalMilliseconds;
    }

    private static (long, long) Pair(long a, long b) => a < b ? (a, b) : (b, a);

    private bool Paired(long a, long b, long now) => pairAfter.TryGetValue(Pair(a, b), out long after) && now < after;

    public bool IsWaiting(long guildId) {
        lock (mutex) {
            return waiting.Any(waiter => waiter.GuildId == guildId);
        }
    }

    public Match? MatchOf(long guildId) {
        lock (mutex) {
            return MatchOfInternal(guildId);
        }
    }

    public void Tick() {
        try {
            onTick?.Invoke();
        } catch (Exception ex) {
            logger.Error(ex, "Guild championship season roll failed");
        }
        try {
            TickInternal();
        } catch (Exception ex) {
            logger.Error(ex, "Guild championship tick failed");
        }
    }

    private void TickInternal() {
        List<long> timedOut = [];
        List<Match> made = [];
        List<Match> expired;
        lock (mutex) {
            long now = tick();
            if (!isOpen()) {
                timedOut.AddRange(waiting.Select(waiter => waiter.GuildId));
                waiting.Clear();
            }
            // The longest waiting guild takes the guild of the closest score (the longer waiting one of a tie) it was
            // not paired with within PairCooldown; a guild without one keeps waiting.
            for (int i = 0; i < waiting.Count;) {
                Waiter first = waiting[i];
                Waiter? other = waiting.Skip(i + 1).Where(waiter => !Paired(first.GuildId, waiter.GuildId, now))
                    .OrderBy(waiter => Math.Abs((long) waiter.Score - first.Score)).FirstOrDefault();
                if (other == null) {
                    i++;
                    continue;
                }
                waiting.Remove(first);
                waiting.Remove(other);
                var match = new Match { Id = ++nextMatchId, A = first.GuildId, B = other.GuildId, MadeTick = now };
                matches[match.Id] = match;
                made.Add(match);
            }
            foreach (long guildId in registerAfter.Where(pair => now >= pair.Value).Select(pair => pair.Key).ToList()) {
                registerAfter.Remove(guildId);
            }
            foreach ((long, long) pair in pairAfter.Where(pair => now >= pair.Value).Select(pair => pair.Key).ToList()) {
                pairAfter.Remove(pair);
            }
            foreach (Waiter waiter in waiting.Where(waiter => now - waiter.Tick >= (long) matchingTime.TotalMilliseconds).ToList()) {
                waiting.Remove(waiter);
                timedOut.Add(waiter.GuildId);
            }
            expired = matches.Values.Where(match => now - match.MadeTick >= (long) MatchLifetime.TotalMilliseconds).ToList();
            foreach (Match match in expired) {
                matches.Remove(match.Id);
            }
        }
        foreach (long guildId in timedOut) {
            TryNotify(guildId, Matched(false));
            TryNotify(guildId, State(GuildPvpState.None, string.Empty, 0));
        }
        foreach (Match match in made) {
            Match step = match;
            LastStep = Task.Run(() => MakeMatch(step));
        }
        foreach (Match match in expired) {
            logger.Information("Guild championship match {MatchId} expired", match.Id);
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

    private Match? MatchOfInternal(long guildId) => matches.Values.FirstOrDefault(match => match.A == guildId || match.B == guildId);

    private void MakeMatch(Match match) {
        (MigrationError error, int channel, int mapId, int roomId) result;
        try {
            result = makeField(match.Id, match.A, match.B);
        } catch (Exception ex) {
            logger.Warning(ex, "Guild championship match {MatchId}: the match field was not made", match.Id);
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
        foreach (long guildId in new[] { match.A, match.B }) {
            TryNotify(guildId, Matched(ready));
            TryNotify(guildId, State(ready ? GuildPvpState.Matched : GuildPvpState.None, string.Empty, 0));
        }
    }

    private void TellClosed(Match match) {
        foreach (long guildId in new[] { match.A, match.B }) {
            TryNotify(guildId, State(GuildPvpState.None, string.Empty, 0));
        }
    }

    private static GuildRequest State(GuildPvpState state, string name, int line) => new() {
        PvpState = new GuildRequest.Types.PvpState { State = (int) state, Name = name, Line = line },
    };

    private static GuildRequest Matched(bool matched) => new() {
        PvpMatched = new GuildRequest.Types.PvpMatched { Matched = matched },
    };

    private void TryNotify(long guildId, GuildRequest notice) {
        try {
            notify(guildId, notice);
        } catch (Exception ex) {
            logger.Warning(ex, "Guild championship notice {Notice} to guild {GuildId} not sent", notice.GuildCase, guildId);
        }
    }

    private static MakeField ChannelField(ChannelClientLookup channelClients) {
        return (matchId, guildA, guildB) => {
            int channel = channelClients.FirstChannel();
            if (!channelClients.TryGetClient(channel, out ChannelClient? client)) {
                return (MigrationError.s_move_err_no_server, 0, 0, 0);
            }
            FieldResponse response = client.Field(new FieldRequest {
                CreateGuildPvp = new FieldRequest.Types.CreateGuildPvp { MatchId = matchId, GuildAId = guildA, GuildBId = guildB },
            }, deadline: DateTime.UtcNow.AddSeconds(10));
            return ((MigrationError) response.Error, channel, response.MapId, response.RoomId);
        };
    }

    // The notice goes to the members online; a state notice is also kept with the guild for members who log in.
    private static Notify GuildNotify(GuildLookup guildLookup) {
        return (guildId, notice) => {
            if (!guildLookup.TryGet(guildId, out GuildManager? manager)) {
                return;
            }
            if (notice.PvpState != null) {
                manager.SetPvpState((GuildPvpState) notice.PvpState.State);
            }
            manager.Broadcast(notice, member => member.Info.Online);
        };
    }

    private static TimeSpan MatchingTime(ServerTableMetadataStorage serverTableMetadata) {
        int minutes = serverTableMetadata.ConstantsTable.guildPVPMatchingTime;
        return TimeSpan.FromMinutes(minutes > 0 ? minutes : 3);
    }

    // Whether a GuildChampionship event is on now.
    private static Func<bool> EventOpen(ServerTableMetadataStorage serverTableMetadata) {
        return () => serverTableMetadata.GetGameEvents()
            .Any(gameEvent => gameEvent.Metadata.Type == GameEventType.GuildChampionship && gameEvent.IsActive(DateTime.Now));
    }
}
