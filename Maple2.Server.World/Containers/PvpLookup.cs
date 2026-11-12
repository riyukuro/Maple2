using Grpc.Core;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Server.Channel.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

// The rank duel matchmaker (Pvp 14/15/16). The queue is under one lock; the channel calls a step needs (make the
// match field, tell a character) run after the lock is released, on a task of their own, so a channel that asks
// the World is never waited on by the World while it waits itself.
public sealed class PvpLookup : IDisposable {
    // Makes the match field: (match id, characters) -> (error, channel, map id, room id).
    public delegate (MigrationError Error, int Channel, int MapId, int RoomId) MakeField(int matchId, IReadOnlyList<long> characterIds);
    // Tells a character on its channel: true, a match was found; false, the search or the match ended.
    public delegate void Notify(long characterId, bool matched);

    private static readonly TimeSpan ExpireInterval = TimeSpan.FromSeconds(5);

    private readonly ILogger logger = Log.Logger.ForContext<PvpLookup>();
    private readonly object mutex = new();
    private readonly RankDuelQueue queue = new();
    private readonly MakeField makeField;
    private readonly Notify notify;
    private readonly Func<long> tick;
    private readonly Timer? timer;

    public PvpLookup(ChannelClientLookup channelClients, PlayerInfoLookup playerLookup)
        : this(ChannelField(channelClients), ChannelNotify(channelClients, playerLookup), () => Environment.TickCount64, true) {
        channelClients.ChannelInactive += EndChannel;
    }

    public PvpLookup(MakeField makeField, Notify notify, Func<long> tick, bool expireTimer = false) {
        this.makeField = makeField;
        this.notify = notify;
        this.tick = tick;
        if (expireTimer) {
            timer = new Timer(_ => Expire(), null, ExpireInterval, ExpireInterval);
        }
    }

    // The task of the last match made (tests wait on it).
    public Task LastStep { get; private set; } = Task.CompletedTask;

    public RankDuelError Register(long characterId) {
        RankDuelMatch? paired;
        RankDuelError error;
        lock (mutex) {
            error = queue.Register(characterId, tick(), out paired);
        }
        if (paired != null) {
            RankDuelMatch match = paired;
            LastStep = Task.Run(() => MakeMatch(match));
        }
        return error;
    }

    public RankDuelError Unregister(long characterId) {
        lock (mutex) {
            return queue.Unregister(characterId);
        }
    }

    public RankDuelError Join(long characterId, out int channel, out int mapId, out int roomId) {
        lock (mutex) {
            RankDuelError error = queue.Join(characterId, out RankDuelMatch? match);
            channel = match?.Channel ?? 0;
            mapId = match?.MapId ?? 0;
            roomId = match?.RoomId ?? 0;
            return error;
        }
    }

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
        lock (mutex) {
            ended = queue.EndChannel(channel);
        }
        foreach (RankDuelMatch match in ended) {
            logger.Information("Rank duel match {MatchId} ended: channel {Channel} went down", match.Id, channel);
            TellAbsent(match);
        }
    }

    public void Expire() {
        List<RankDuelMatch> expired;
        lock (mutex) {
            expired = queue.Expire(tick());
        }
        foreach (RankDuelMatch match in expired) {
            logger.Information("Rank duel match {MatchId} expired", match.Id);
            TellAbsent(match);
        }
    }

    private void MakeMatch(RankDuelMatch match) {
        (MigrationError error, int channel, int mapId, int roomId) result;
        try {
            result = makeField(match.Id, [match.First, match.Second]);
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
            TryNotify(characterId, ready);
        }
    }

    private void TellAbsent(RankDuelMatch match) {
        foreach (long characterId in match.Characters.Where(id => !match.Joined.Contains(id))) {
            TryNotify(characterId, false);
        }
    }

    private void TryNotify(long characterId, bool matched) {
        try {
            notify(characterId, matched);
        } catch (Exception ex) {
            logger.Warning(ex, "Rank duel notice to {CharacterId} not sent", characterId);
        }
    }

    private static MakeField ChannelField(ChannelClientLookup channelClients) {
        return (matchId, characterIds) => {
            int channel = channelClients.FirstChannel();
            if (!channelClients.TryGetClient(channel, out ChannelClient? client)) {
                return (MigrationError.s_move_err_no_server, 0, 0, 0);
            }
            var request = new FieldRequest {
                CreatePvpMatch = new FieldRequest.Types.CreatePvpMatch { MatchId = matchId },
            };
            request.CreatePvpMatch.CharacterIds.AddRange(characterIds);
            FieldResponse response = client.Field(request, deadline: DateTime.UtcNow.AddSeconds(10));
            return ((MigrationError) response.Error, channel, response.MapId, response.RoomId);
        };
    }

    private static Notify ChannelNotify(ChannelClientLookup channelClients, PlayerInfoLookup playerLookup) {
        return (characterId, matched) => {
            if (!playerLookup.TryGet(characterId, out PlayerInfo? info) || !info.Online) {
                return;
            }
            if (!channelClients.TryGetClient(info.Channel, out ChannelClient? client)) {
                return;
            }
            try {
                client.Pvp(new PvpNoticeRequest { CharacterId = characterId, Matched = matched }, deadline: DateTime.UtcNow.AddSeconds(5));
            } catch (RpcException) { }
        };
    }

    public void Dispose() {
        timer?.Dispose();
    }
}
