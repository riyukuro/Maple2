using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// The rank duel match of one field (FieldManager.RankDuel): the duelists' enter, leave and death, and the field's
// update, are given to its RankDuelMatchState, and what the state answers is sent. One round, see
// RankDuelMatchState for the rules.
public sealed class RankDuelField {
    // The maps whose environment feature is PvPRankingDuelMode (map/65010001.xml to 65010010.xml, the mapname
    // rows of that feature): the rank duel arenas. One is picked at random among those the server can load.
    public static readonly int[] Maps = Enumerable.Range(65010001, 10).ToArray();

    // The rating a side shows when its record could not be read (the client's own start value).
    public const int Rating = RankDuelRecord.StartRating;

    private readonly ILogger logger = Log.Logger.ForContext<RankDuelField>();
    private readonly object mutex = new();
    private readonly FieldManager field;
    private readonly RankDuelMatchState state;
    private readonly Action<int> onEnd;
    private readonly Dictionary<long, string> names = [];
    // The duelists sent out on arrival after the result (Dismiss): the exit leaves them alone.
    private readonly HashSet<long> dismissed = [];
    private readonly Dictionary<long, int> jobs = [];

    public int MatchId { get; }
    public RankDuelPhase Phase {
        get {
            lock (mutex) return state.Phase;
        }
    }

    public RankDuelField(FieldManager field, int matchId, long first, long second, long tick, Action<int> onEnd) {
        this.field = field;
        this.onEnd = onEnd;
        MatchId = matchId;
        state = new RankDuelMatchState(first, second, tick);
    }

    public bool IsDuelist(long characterId) => state.IsDuelist(characterId);

    // Whether the match field still takes its duelists: not once it has sent them out.
    public bool Runs => IsRunning(Phase);
    public static bool IsRunning(RankDuelPhase phase) => phase != RankDuelPhase.Closed;

    // Whether a character may leave its field for the rank duel (register or join). Chosen here (the client only
    // checks death itself): not while dead, not from a dungeon or a room it would lose by leaving
    // (RefusesInstance), not from a rank duel field.
    // The instance types left only by their own way out: solo rooms, dungeon lobbies, the event and game rooms, and
    // the PvP and survival rooms. Open fields (none, channelScale), homes, guild houses and wedding halls are not.
    public static bool RefusesInstance(InstanceType type) {
        return type is InstanceType.solo or InstanceType.massiveEvent or InstanceType.GameMaker or InstanceType.GuildEvent
            or InstanceType.GuildPvp or InstanceType.DungeonLobby or InstanceType.GuildVsGame or InstanceType.RankingPvp
            or InstanceType.MapleSurvival or InstanceType.MapleSurvivalSquad or InstanceType.FieldWar;
    }

    public static bool CanLeaveFor(bool dead, bool dungeon, bool instanced, bool rankDuel) {
        return !dead && !dungeon && !instanced && !rankDuel;
    }

    public bool Hostile(FieldPlayer caster, FieldPlayer target) {
        lock (mutex) {
            return state.Hostile(caster.Value.Character.Id, target.Value.Character.Id);
        }
    }

    public void Enter(FieldPlayer player) {
        long characterId = player.Value.Character.Id;
        lock (mutex) {
            if (!state.IsDuelist(characterId)) {
                return;
            }
            names[characterId] = player.Value.Character.Name;
            jobs[characterId] = (int) player.Value.Character.Job.Code();
            Apply(state.Enter(characterId, field.FieldTick));
        }
    }

    public void Leave(FieldPlayer player) {
        lock (mutex) {
            Apply(state.Leave(player.Value.Character.Id, field.FieldTick));
        }
    }

    public void Death(FieldPlayer player) {
        lock (mutex) {
            Apply(state.Death(player.Value.Character.Id, field.FieldTick));
        }
    }

    public void Update(long tick) {
        lock (mutex) {
            Apply(state.Update(tick));
        }
    }

    private void Apply(List<RankDuelEvent> events) {
        foreach (RankDuelEvent @event in events) {
            switch (@event.Type) {
                case RankDuelEventType.Round:
                    Send(state.First, PvpPacket.RankDuelRound(1, 1));
                    Send(state.Second, PvpPacket.RankDuelRound(1, 1));
                    break;
                case RankDuelEventType.Fight:
                    Send(state.First, PvpPacket.RankDuelFight());
                    Send(state.Second, PvpPacket.RankDuelFight());
                    StartFight();
                    break;
                case RankDuelEventType.Result:
                    SendResult(@event);
                    EndInWorld();
                    break;
                case RankDuelEventType.Exit:
                    SendOut();
                    break;
                case RankDuelEventType.Dismiss:
                    Dismiss(@event.Character);
                    break;
            }
        }
    }

    private void SendResult(RankDuelEvent @event) {
        switch (@event.End) {
            case RankDuelEnd.Win:
                logger.Information("Rank duel match {MatchId}: {Winner} won over {Loser}", MatchId, @event.Winner, @event.Loser);
                Send(@event.Winner, PvpPacket.RankDuelWin());
                Send(@event.Loser, PvpPacket.RankDuelLose());
                Dictionary<long, RankDuelChange> changes = ApplyResult(@event.Winner, @event.Loser);
                var left = Side(state.First, @event.Winner, changes);
                var right = Side(state.Second, @event.Winner, changes);
                Send(state.First, PvpPacket.RankDuelResult(@event.Winner, left, right));
                Send(state.Second, PvpPacket.RankDuelResult(@event.Winner, left, right));
                SendStanding();
                break;
            case RankDuelEnd.Draw:
                // The result dialog fills nothing without a winner: a draw has its intro only.
                logger.Information("Rank duel match {MatchId}: draw", MatchId);
                Send(state.First, PvpPacket.RankDuelDraw());
                Send(state.Second, PvpPacket.RankDuelDraw());
                break;
            case RankDuelEnd.Void:
                logger.Information("Rank duel match {MatchId}: nobody came", MatchId);
                break;
        }
        EndFight();
        // The rank duel state goes back to 0, so the duelists can register again.
        Send(state.First, PvpPacket.RankDuelUnregistered());
        Send(state.Second, PvpPacket.RankDuelUnregistered());
    }

    private PvpPacket.RankDuelSide Side(long characterId, long winner, Dictionary<long, RankDuelChange> changes) {
        RankDuelChange change = changes.GetValueOrDefault(characterId, new RankDuelChange(Rating, 0, 0));
        return new PvpPacket.RankDuelSide(Name(characterId), characterId, characterId == winner ? 1 : 0, change.Rating, change.RatingChange, change.Reward);
    }

    // Both duelists' records take the result (RankDuelRules.Apply) and are saved; the duelists in the field get
    // their valor tokens and the exp of rankDuelWin or rankDuelLose. One who left or never came gets neither.
    private Dictionary<long, RankDuelChange> ApplyResult(long winner, long loser) {
        var changes = new Dictionary<long, RankDuelChange>();
        try {
            int season = RankDuelStanding.CurrentSeason(field.TableMetadata);
            RankDuelGradeTable grades = field.TableMetadata.RankDuelGradeTable;
            using GameStorage.Request db = field.GameStorage.Context();
            var records = new Dictionary<long, RankDuelRecord>();
            foreach ((long characterId, bool won) in new[] { (winner, true), (loser, false) }) {
                RankDuelRecord record = RankDuelStanding.Load(db, characterId, season);
                bool here = field.TryGetPlayerById(characterId, out _);
                changes[characterId] = RankDuelRules.Apply(record, grades, won, JobOf(db, won ? loser : winner), here);
                records[characterId] = record;
            }
            // Both records in one save: when it fails neither changes, and nobody is rewarded.
            if (!db.SaveRankDuelRecords(records)) {
                logger.Warning("Rank duel match {MatchId}: the records were not saved", MatchId);
                changes.Clear();
            }
        } catch (Exception ex) {
            logger.Error(ex, "Rank duel match {MatchId}: the result was not applied", MatchId);
        }

        foreach ((long characterId, RankDuelChange change) in changes) {
            if (!field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                continue;
            }
            try {
                if (change.Reward > 0) {
                    player.Session.Currency[CurrencyType.ValorToken] += change.Reward;
                }
                player.Session.Exp.AddExp(characterId == winner ? ExpType.rankDuelWin : ExpType.rankDuelLose);
            } catch (Exception ex) {
                logger.Error(ex, "Rank duel match {MatchId}: the reward of {CharacterId} was not given", MatchId, characterId);
            }
        }
        return changes;
    }

    // The job code of a duelist: as it came in, else from its character.
    private int JobOf(GameStorage.Request db, long characterId) {
        if (jobs.TryGetValue(characterId, out int job)) {
            return job;
        }
        return (int) (db.GetCharacter(characterId)?.Job.Code() ?? JobCode.None);
    }

    // The new Pvp 22 and 23 for the duelists here, so the rank duel window shows the new standing.
    private void SendStanding() {
        foreach (long characterId in new[] { state.First, state.Second }) {
            if (field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                try {
                    RankDuelStanding.Send(player.Session);
                } catch (Exception ex) {
                    logger.Warning(ex, "Rank duel standing of {CharacterId} not sent", characterId);
                }
            }
        }
    }

    // The client takes two players for enemies on a map without PK mode when both have a PvP point and the points
    // differ (PC::vfunc_122 and vfunc_116 with CPvPInfo::ReadPointTeamMode): each duelist gets its own point (Pvp 0x64)
    // for the fight, and the field's other players see it. The server constants name the rank duel arena's buffs
    // (ModePVPDuelRankArenaAdditionalEffect). Chosen here: the points are 1 and 2 with team 0 and mode 0, and the
    // buffs (level 1) are given when the fight starts.
    private void StartFight() {
        int point = 1;
        foreach (long characterId in new[] { state.First, state.Second }) {
            if (!field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                point++;
                continue;
            }
            field.Broadcast(PvpPacket.Point(player.ObjectId, point++, 0, 0));
            foreach (int buffId in Buffs()) {
                player.Buffs.AddBuff(field.FieldActor, player, buffId, 1, field.FieldTick);
            }
        }
    }

    // After the result the duelists are no enemies any more: their points go back to 0.
    private void EndFight() {
        foreach (long characterId in new[] { state.First, state.Second }) {
            if (field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                field.Broadcast(PvpPacket.Point(player.ObjectId, 0, 0, 0));
            }
        }
    }

    private IEnumerable<int> Buffs() {
        try {
            return field.ServerTableMetadata.ConstantsTable.ModePVPDuelRankArenaAdditionalEffect ?? [];
        } catch (Exception) {
            return [];
        }
    }

    private string Name(long characterId) {
        if (names.TryGetValue(characterId, out string? name)) {
            return name;
        }
        return field.TryGetPlayerById(characterId, out FieldPlayer? player) ? player.Value.Character.Name : string.Empty;
    }

    private void EndInWorld() {
        int matchId = MatchId;
        Task.Run(() => {
            try {
                onEnd(matchId);
            } catch (Exception ex) {
                logger.Warning(ex, "Rank duel match {MatchId}: the end was not sent to the world", matchId);
            }
        });
    }

    // A duelist that came after the result: its client goes back to the idle state (Pvp 15) and it goes back to its
    // return field once its entry is done.
    private void Dismiss(long characterId) {
        if (!dismissed.Add(characterId)) {
            return;
        }
        Send(characterId, PvpPacket.RankDuelUnregistered());
        field.Scheduler.Schedule(() => {
            if (field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                player.Session.ReturnField();
            }
        });
    }

    // After the result: the duelists still here go back to their return field.
    private void SendOut() {
        foreach (long characterId in new[] { state.First, state.Second }.Where(id => !dismissed.Contains(id))) {
            if (field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                player.Session.ReturnField();
            }
        }
    }

    private void Send(long characterId, Maple2.PacketLib.Tools.ByteWriter packet) {
        if (field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
            player.Session.Send(packet);
        }
    }
}
