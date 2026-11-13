using Grpc.Core;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Database.Storage;
using Maple2.Server.Channel.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

// The rank duel matchmaker (Pvp 14/15/16) and the individual arena's (Pvp 9/10/11). The queue is under one lock; the channel calls a step needs (make the
// match field, tell a character) run after the lock is released, on a task of their own, so a channel that asks
// the World is never waited on by the World while it waits itself.
public sealed class PvpLookup : IDisposable {
    // Makes the match field: (match id, arena (0: rank duel), characters) -> (error, channel, map id, room id).
    public delegate (MigrationError Error, int Channel, int MapId, int RoomId) MakeField(int matchId, int arena, IReadOnlyList<long> characterIds,
                                                                                      IReadOnlyDictionary<long, long> units);
    // Tells a character on its channel: true, a match was found; false, the search or the match ended. arena 0 is the
    // rank duel.
    // squad: a Maple Survival squad member's queue state (1 registered, 2 unregistered; 0 for the other notices).
    public delegate void Notify(long characterId, bool matched, int arena, int squad);

    private static readonly TimeSpan ExpireInterval = TimeSpan.FromSeconds(5);

    private readonly ILogger logger = Log.Logger.ForContext<PvpLookup>();
    private readonly object mutex = new();
    private readonly RankDuelQueue queue = new();
    private readonly ArenaQueue arenas = new();
    private readonly MakeField makeField;
    private readonly Notify notify;
    private readonly Func<long> tick;
    private readonly Timer? timer;

    public PvpLookup(ChannelClientLookup channelClients, PlayerInfoLookup playerLookup, ServerTableMetadataStorage serverTableMetadata)
        : this(ChannelField(channelClients), ChannelNotify(channelClients, playerLookup), () => Environment.TickCount64, true) {
        channelClients.ChannelInactive += EndChannel;
        offer = () => {
            foreach ((int _, ChannelClient client) in channelClients) {
                try {
                    client.Pvp(new PvpNoticeRequest { ObserveOffer = true }, deadline: DateTime.UtcNow.AddSeconds(5));
                } catch (RpcException) { }
            }
        };
        this.serverTableMetadata = serverTableMetadata;
    }

    private readonly ServerTableMetadataStorage? serverTableMetadata;

    // The window's server constants, read when first needed (the metadata loads lazily).
    private void LoadConstants() {
        if (serverTableMetadata == null || constantsLoaded) {
            return;
        }
        try {
            ConstantsTable constants = serverTableMetadata.ConstantsTable;
            queue.MatchingTimeSeconds = constants.rankDuelPvpMatchingTime;
            queue.MatchingMinGap = constants.rankDuelPvpMatchingMinGap;
        } catch (Exception ex) {
            logger.Warning(ex, "Rank duel matching constants not read: every gap is allowed");
        }
        constantsLoaded = true;
    }
    private bool constantsLoaded;

    // The observe offer's broadcast (tests).
    public void SetOffer(Action broadcast) => offer = broadcast;

    // The window's constants (tests).
    public void SetWindow(int matchingTimeSeconds, int minGap) {
        lock (mutex) {
            queue.MatchingTimeSeconds = matchingTimeSeconds;
            queue.MatchingMinGap = minGap;
            constantsLoaded = true;
        }
    }

    public PvpLookup(MakeField makeField, Notify notify, Func<long> tick, bool expireTimer = false) {
        this.makeField = makeField;
        this.notify = notify;
        this.tick = tick;
        if (expireTimer) {
            timer = new Timer(_ => {
                Expire();
                Pair();
            }, null, ExpireInterval, ExpireInterval);
        }
    }

    // The task of the last match made (tests wait on it).
    public Task LastStep { get; private set; } = Task.CompletedTask;

    public RankDuelError Register(long characterId, int rating = RankDuelRecord.StartRating) {
        List<RankDuelMatch> made;
        RankDuelError error;
        lock (mutex) {
            LoadConstants();
            // One queue at a time: a character in the arena's queue or match is busy here.
            if (arenas.IsQueued(characterId) || arenas.MatchOf(characterId) != null) {
                return RankDuelError.Busy;
            }
            error = queue.Register(characterId, tick(), rating, out made);
        }
        Make(made);
        return error;
    }

    // The individual arena: queued for one arena; matches are made by ArenaQueue.Pair.
    public RankDuelError ArenaRegister(long characterId, int arena, int playerCount, int minPlayers = ArenaQueue.MinPlayers) {
        List<ArenaMatch> made;
        RankDuelError error;
        lock (mutex) {
            if (queue.IsQueued(characterId) || queue.MatchOf(characterId) != null) {
                return RankDuelError.Busy;
            }
            error = arenas.Register(characterId, arena, playerCount, tick(), minPlayers, out made);
        }
        MakeArenas(made);
        return error;
    }

    public Dictionary<int, int> ArenaWaits() {
        lock (mutex) {
            return arenas.WaitTimes(tick()).ToDictionary(wait => wait.Key, wait => (int) Math.Min(wait.Value, int.MaxValue));
        }
    }

    public int ArenaDecline(long characterId) {
        lock (mutex) {
            return arenas.Decline(characterId);
        }
    }

    // 0 none, 1 queued, 2 in a match, with the arena.
    public (int State, int Arena) QueueState(long characterId) {
        lock (mutex) {
            if (arenas.MatchOf(characterId) is { } match) {
                return (2, match.Arena);
            }
            if (arenas.IsQueued(characterId)) {
                return (1, arenas.QueuedArena(characterId));
            }
            if (queue.MatchOf(characterId) != null) {
                return (2, 0);
            }
            return (queue.IsQueued(characterId) ? 1 : 0, 0);
        }
    }

    public RankDuelError ArenaUnregister(long characterId) {
        List<long> unit;
        RankDuelError error;
        lock (mutex) {
            unit = arenas.UnitOf(characterId);
            error = arenas.Unregister(characterId);
        }
        // A squad's cancel takes the whole squad out: every member's party window shows it (Party 0x37 false).
        if (error == RankDuelError.none && unit.Count > 1) {
            foreach (long member in unit) {
                TryNotify(member, false, 0, 2);
            }
        }
        return error;
    }

    // A Maple Survival squad registers (its chief's 0xC2 mode 4): the members wait as one unit; each member's party window
    // shows the squad registered (Party 0x37 true).
    public RankDuelError ArenaRegisterSquad(IReadOnlyList<long> members, int arena, int playerCount, int minPlayers) {
        List<ArenaMatch> made;
        RankDuelError error;
        lock (mutex) {
            if (members.Any(member => queue.IsQueued(member) || queue.MatchOf(member) != null)) {
                return RankDuelError.Busy;
            }
            error = arenas.RegisterSquad(members, arena, playerCount, tick(), minPlayers, out made);
        }
        if (error == RankDuelError.none) {
            foreach (long member in members) {
                TryNotify(member, false, 0, 1);
            }
        }
        MakeArenas(made);
        return error;
    }

    public RankDuelError ArenaJoin(long characterId, out int channel, out int mapId, out int roomId) {
        lock (mutex) {
            RankDuelError error = arenas.Join(characterId, out ArenaMatch? match);
            channel = match?.Channel ?? 0;
            mapId = match?.MapId ?? 0;
            roomId = match?.RoomId ?? 0;
            return error;
        }
    }

    public void ArenaEnd(int matchId) {
        ArenaMatch? match;
        lock (mutex) {
            match = arenas.End(matchId);
        }
        if (match != null) {
            TellAbsent(match);
        }
    }

    public ArenaMatch? ArenaMatchOf(long characterId) {
        lock (mutex) {
            return arenas.MatchOf(characterId);
        }
    }

    // Neither queued nor matched in either queue (tests).
    public bool RankDuelOrArenaFree(long characterId) {
        lock (mutex) {
            return !queue.IsQueued(characterId) && queue.MatchOf(characterId) == null && !arenas.IsQueued(characterId) && arenas.MatchOf(characterId) == null;
        }
    }

    public bool IsArenaQueued(long characterId) {
        lock (mutex) {
            return arenas.IsQueued(characterId);
        }
    }

    private void MakeArenas(List<ArenaMatch> made) {
        foreach (ArenaMatch match in made) {
            LastStep = Task.Run(() => MakeArena(match));
        }
    }

    // Pairs again: the windows widen with the wait (the timer calls this).
    public void Pair() {
        List<RankDuelMatch> made;
        List<ArenaMatch> arenaMade;
        lock (mutex) {
            LoadConstants();
            made = queue.Pair(tick());
            arenaMade = arenas.Pair(tick());
        }
        Make(made);
        MakeArenas(arenaMade);
    }

    private void Make(List<RankDuelMatch> made) {
        foreach (RankDuelMatch match in made) {
            LastStep = Task.Run(() => MakeMatch(match));
        }
    }

    public RankDuelError Unregister(long characterId) {
        lock (mutex) {
            return queue.Unregister(characterId);
        }
    }

    public RankDuelError Join(long characterId, out int channel, out int mapId, out int roomId) => Join(characterId, out channel, out mapId, out roomId, out _);

    // The join (Pvp 16): into its own match, or, with none, to watch the duel whose fight started last (the client
    // sends the same request from the observe offer's box).
    public RankDuelError Join(long characterId, out int channel, out int mapId, out int roomId, out bool observer) {
        lock (mutex) {
            RankDuelError error = queue.Join(characterId, out RankDuelMatch? match);
            observer = false;
            if (error == RankDuelError.NoMatch && queue.MatchOf(characterId) == null
                && arenas.MatchOf(characterId) == null && !arenas.IsQueued(characterId) && queue.Observe(characterId) is { } watched) {
                match = watched;
                observer = true;
                error = RankDuelError.none;
            }
            channel = match?.Channel ?? 0;
            mapId = match?.MapId ?? 0;
            roomId = match?.RoomId ?? 0;
            return error;
        }
    }

    // A match field's fight started: the match can be watched, and the channels offer it (Pvp 0x19).
    public void Observable(int matchId) {
        bool set;
        lock (mutex) {
            set = queue.SetObservable(matchId, tick());
        }
        if (set) {
            try {
                offer?.Invoke();
            } catch (Exception ex) {
                logger.Warning(ex, "Rank duel observe offer not sent");
            }
        }
    }

    private Action? offer;

    // The match field ended the match. The characters that never joined are told it is over.
    public void End(int matchId) {
        RankDuelMatch? match;
        lock (mutex) {
            match = queue.End(matchId);
        }
        if (match != null) {
            TellAbsent(match);
        }
    }

    // A logout: a queued character leaves the queue.
    public void Remove(long characterId) {
        lock (mutex) {
            queue.Remove(characterId);
            arenas.Remove(characterId);
        }
    }

    public bool IsQueued(long characterId) {
        lock (mutex) {
            return queue.IsQueued(characterId);
        }
    }

    public RankDuelMatch? MatchOf(long characterId) {
        lock (mutex) {
            return queue.MatchOf(characterId);
        }
    }

    // A channel went down: its match fields are gone, so their matches end now (else both duelists stay busy until
    // the lifetime runs out). The duelists that had not joined are told.
    public void EndChannel(int channel) {
        List<RankDuelMatch> ended;
        List<ArenaMatch> arenaEnded;
        lock (mutex) {
            ended = queue.EndChannel(channel);
            arenaEnded = arenas.EndChannel(channel);
        }
        foreach (RankDuelMatch match in ended) {
            logger.Information("Rank duel match {MatchId} ended: channel {Channel} went down", match.Id, channel);
            TellAbsent(match);
        }
        foreach (ArenaMatch match in arenaEnded) {
            logger.Information("Arena match {MatchId} ended: channel {Channel} went down", match.Id, channel);
            TellAbsent(match);
        }
    }

    public void Expire() {
        List<RankDuelMatch> expired;
        List<ArenaMatch> arenaExpired;
        List<(long CharacterId, int Arena)> released;
        lock (mutex) {
            expired = queue.Expire(tick());
            arenaExpired = arenas.Expire(tick());
            released = arenas.Release(tick());
        }
        foreach ((long characterId, int arena) in released) {
            TryNotify(characterId, false, arena);
        }
        foreach (RankDuelMatch match in expired) {
            logger.Information("Rank duel match {MatchId} expired", match.Id);
            TellAbsent(match);
        }
        foreach (ArenaMatch match in arenaExpired) {
            logger.Information("Arena match {MatchId} expired", match.Id);
            TellAbsent(match);
        }
    }

    private void MakeMatch(RankDuelMatch match) {
        (MigrationError error, int channel, int mapId, int roomId) result;
        try {
            result = makeField(match.Id, 0, [match.First, match.Second], new Dictionary<long, long>());
        } catch (Exception ex) {
            logger.Warning(ex, "Rank duel match {MatchId}: the match field was not made", match.Id);
            result = (MigrationError.s_move_err_FailCreateDungeon, 0, 0, 0);
        }

        bool ready;
        lock (mutex) {
            ready = result.error == MigrationError.ok && queue.SetRoom(match.Id, result.channel, result.mapId, result.roomId);
            if (!ready) {
                queue.End(match.Id);
            }
        }
        foreach (long characterId in match.Characters) {
            TryNotify(characterId, ready, 0);
        }
    }

    private void TellAbsent(RankDuelMatch match) {
        foreach (long characterId in match.Characters.Where(id => !match.Joined.Contains(id))) {
            TryNotify(characterId, false, 0);
        }
    }

    private void TellAbsent(ArenaMatch match) {
        foreach (long characterId in match.Characters.Where(id => !match.Joined.Contains(id) && !match.Released.Contains(id))) {
            TryNotify(characterId, false, match.Arena);
        }
    }

    private void MakeArena(ArenaMatch match) {
        (MigrationError error, int channel, int mapId, int roomId) result;
        try {
            result = makeField(match.Id, match.Arena, match.Characters, match.Units);
        } catch (Exception ex) {
            logger.Warning(ex, "Arena match {MatchId}: the match field was not made", match.Id);
            result = (MigrationError.s_move_err_FailCreateDungeon, 0, 0, 0);
        }

        bool ready;
        lock (mutex) {
            ready = result.error == MigrationError.ok && arenas.SetRoom(match.Id, result.channel, result.mapId, result.roomId);
            if (!ready) {
                arenas.End(match.Id);
            }
        }
        foreach (long characterId in match.Characters) {
            TryNotify(characterId, ready, match.Arena);
        }
    }

    private void TryNotify(long characterId, bool matched, int arena, int squad = 0) {
        try {
            notify(characterId, matched, arena, squad);
        } catch (Exception ex) {
            logger.Warning(ex, "Rank duel notice to {CharacterId} not sent", characterId);
        }
    }

    private static MakeField ChannelField(ChannelClientLookup channelClients) {
        return (matchId, arena, characterIds, units) => {
            int channel = channelClients.FirstChannel();
            if (!channelClients.TryGetClient(channel, out ChannelClient? client)) {
                return (MigrationError.s_move_err_no_server, 0, 0, 0);
            }
            var request = new FieldRequest {
                CreatePvpMatch = new FieldRequest.Types.CreatePvpMatch { MatchId = matchId, Arena = arena },
            };
            request.CreatePvpMatch.CharacterIds.AddRange(characterIds);
            request.CreatePvpMatch.UnitIds.AddRange(characterIds.Select(id => units.GetValueOrDefault(id, id)));
            FieldResponse response = client.Field(request, deadline: DateTime.UtcNow.AddSeconds(10));
            return ((MigrationError) response.Error, channel, response.MapId, response.RoomId);
        };
    }

    private static Notify ChannelNotify(ChannelClientLookup channelClients, PlayerInfoLookup playerLookup) {
        return (characterId, matched, arena, squad) => {
            if (!playerLookup.TryGet(characterId, out PlayerInfo? info) || !info.Online) {
                return;
            }
            if (!channelClients.TryGetClient(info.Channel, out ChannelClient? client)) {
                return;
            }
            try {
                client.Pvp(new PvpNoticeRequest { CharacterId = characterId, Matched = matched, Arena = arena, Squad = squad }, deadline: DateTime.UtcNow.AddSeconds(5));
            } catch (RpcException) { }
        };
    }

    public void Dispose() {
        timer?.Dispose();
    }
}
