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
// update, are given to its RankDuelMatchState, and what the state answers is sent. A best of
// RankDuelMatchState.MaxRounds rounds, see RankDuelMatchState for the rules.
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
    private readonly Action<int>? onObservable;
    // The onlookers in the field (joined through the observe offer).
    private readonly HashSet<long> observers = [];
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

    public RankDuelField(FieldManager field, int matchId, long first, long second, long tick, Action<int> onEnd, Action<int>? onObservable = null) {
        this.onObservable = onObservable;
        this.field = field;
        this.onEnd = onEnd;
        MatchId = matchId;
        state = new RankDuelMatchState(first, second, tick, RankDuelMatchState.LoadMs);
    }

    public bool IsDuelist(long characterId) => state.IsDuelist(characterId);

    public bool IsOnlooker(long characterId) {
        lock (mutex) return observers.Contains(characterId);
    }

    // The duelists' ratings, read once when the fight starts (for the onlookers' dialog).
    private readonly Dictionary<long, int> ratings = [];

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
                Observe(player);
                return;
            }
            names[characterId] = player.Value.Character.Name;
            jobs[characterId] = (int) player.Value.Character.Job.Code();
            Apply(state.Enter(characterId, field.FieldTick));
        }
    }

    public void Leave(FieldPlayer player) {
        lock (mutex) {
            observers.Remove(player.Value.Character.Id);
            Apply(state.Leave(player.Value.Character.Id, field.FieldTick));
        }
    }

    // An onlooker (a player that is not a duelist: the join of the observe offer). During the duel it becomes an
    // onlooker (Pvp 0x13 false) and gets the duel's observer dialog (Trigger dialog kind 5); after the result there is
    // nothing to watch and it goes back to its return field.
    private void Observe(FieldPlayer player) {
        long characterId = player.Value.Character.Id;
        if (state.Phase is RankDuelPhase.Ended or RankDuelPhase.Closed) {
            field.Scheduler.Schedule(() => {
                if (field.TryGetPlayerById(characterId, out FieldPlayer? late)) {
                    late.Session.ReturnField();
                }
            });
            return;
        }
        observers.Add(characterId);
        player.Session.Send(PvpPacket.RankDuelObserving(false));
        // The duelists' points, as the field got them when the fight started: the client follows the health of a
        // player with a point (PC::vfunc_74) into the dialog.
        if (state.Phase == RankDuelPhase.Fighting) {
            int point = PvpPoints.First;
            foreach (long duelist in new[] { state.First, state.Second }) {
                if (field.TryGetPlayerById(duelist, out FieldPlayer? fighter)) {
                    player.Session.Send(PvpPacket.Point(fighter.ObjectId, point, 0, 0));
                }
                point++;
            }
        }
        if (DuelDialog((int) RankDuelMatchState.FightMs) is { } dialog) {
            player.Session.Send(dialog);
        }
    }

    // The duel's observer dialog with both duelists' health, rounds won and ratings. Chosen here:
    // the center button's camera step is 0.
    private Maple2.PacketLib.Tools.ByteWriter? DuelDialog(int duration) {
        if (!field.TryGetPlayerById(state.First, out FieldPlayer? a) || !field.TryGetPlayerById(state.Second, out FieldPlayer? b)) {
            return null;
        }
        return TriggerPacket.PvpPlayerDialog(duration, Fighter(a), Fighter(b));
    }

    private TriggerPacket.PvpFighter Fighter(FieldPlayer player) {
        int rating = ratings.GetValueOrDefault(player.Value.Character.Id, Rating);
        return new TriggerPacket.PvpFighter(player.Value.Character.Id, (int) player.Stats.Values[BasicAttribute.Health].Current,
            (int) player.Stats.Values[BasicAttribute.Health].Total, (byte) state.Wins(player.Value.Character.Id), rating);
    }

    // Once per duel, when its fight starts.
    private void ReadRatings() {
        try {
            using GameStorage.Request db = field.GameStorage.Context();
            int season = RankDuelStanding.CurrentSeason(field.TableMetadata);
            foreach (long duelist in new[] { state.First, state.Second }) {
                ratings[duelist] = RankDuelStanding.Load(db, duelist, season).Rating;
            }
        } catch (Exception) {
            // The records could not be read: the start rating.
        }
    }

    // The duel's observer dialog goes to the duelists too: it is their fight's timer (setTime) and both sides' health
    // bars, names and win marks, and the client has no other (UIPvPObserverDialog reads its two characters from the
    // packet, not from who is watching). Before, only the onlookers got it, so a duelist saw no timer and no
    // opponent's bar.
    private void SendDialog(Maple2.PacketLib.Tools.ByteWriter packet) {
        Send(state.First, packet);
        Send(state.Second, packet);
        SendObservers(packet);
    }

    private void SendObservers(Maple2.PacketLib.Tools.ByteWriter packet) {
        foreach (long characterId in observers) {
            Send(characterId, packet);
        }
    }

    private readonly KillCredit killCredit = new();

    // A hit between the duelists that took health, for the kill credit and the perfect win.
    public void Damage(FieldPlayer attacker, FieldPlayer victim) {
        lock (mutex) {
            if (state.Hostile(attacker.Value.Character.Id, victim.Value.Character.Id)) {
                killCredit.Hit(attacker.Value.Character.Id, victim.Value.Character.Id, field.FieldTick);
                state.Hurt(victim.Value.Character.Id);
            }
        }
    }

    public void Death(FieldPlayer player) {
        lock (mutex) {
            long characterId = player.Value.Character.Id;
            long killerId = killCredit.Killer(characterId, field.FieldTick);
            List<RankDuelEvent> events = state.Death(characterId, field.FieldTick);
            // A duelist that falls in the fight: pvp_die for it, pvp_kill for its opponent when the opponent hit it
            // within PvpZoneState.KillCreditMs (a fall or its own skill is no kill).
            if (events.Count > 0) {
                PvpConditions.Death(player, field.MapId);
                if (killerId != 0 && killerId == state.Opponent(characterId) && field.TryGetPlayerById(killerId, out FieldPlayer? killer)) {
                    PvpConditions.Kill(killer, field.MapId);
                }
            }
            Apply(events);
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
                    if (@event.Round > 1) {
                        StartNextRound();
                    }
                    Send(state.First, PvpPacket.RankDuelRound(@event.Round));
                    Send(state.Second, PvpPacket.RankDuelRound(@event.Round));
                    break;
                case RankDuelEventType.Fight:
                    Send(state.First, PvpPacket.RankDuelFight());
                    Send(state.Second, PvpPacket.RankDuelFight());
                    StartFight();
                    // Once per match: the ratings, the participation and the world's observe offer.
                    if (state.Round == 1) {
                        ReadRatings();
                        foreach (long characterId in new[] { state.First, state.Second }) {
                            if (field.TryGetPlayerById(characterId, out FieldPlayer? duelist)) {
                                PvpConditions.Participation(duelist, field.MapId);
                            }
                        }
                        Tell(onObservable);
                    }
                    if (DuelDialog((int) RankDuelMatchState.FightMs) is { } dialog) {
                        SendDialog(dialog);
                    }
                    break;
                case RankDuelEventType.RoundEnd:
                    EndRound(@event);
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
                // A perfect win shows the perfect intro in place of the win intro.
                Send(@event.Winner, state.Perfect ? PvpPacket.RankDuelPerfect() : PvpPacket.RankDuelWin());
                Send(@event.Loser, PvpPacket.RankDuelLose());
                Dictionary<long, RankDuelChange> changes = ApplyResult(@event.Winner, @event.Loser);
                var left = Side(state.First, @event.Winner, changes);
                var right = Side(state.Second, @event.Winner, changes);
                Send(state.First, PvpPacket.RankDuelResult(@event.Winner, left, right));
                Send(state.Second, PvpPacket.RankDuelResult(@event.Winner, left, right));
                SendStanding();
                // The conditions count only once the fight started (see PvpConditions.DuelWin). The grade only when
                // the record was saved.
                if (field.TryGetPlayerById(@event.Winner, out FieldPlayer? winner)) {
                    int? grade = changes.TryGetValue(@event.Winner, out RankDuelChange won)
                        ? RankDuelRules.GradeOf(field.TableMetadata.RankDuelGradeTable, won.Rating)?.Id : null;
                    PvpConditions.DuelWin(winner, field.MapId, state.WinBy, grade);
                }
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
        SendDialog(TriggerPacket.PvpPlayerDialog(0, default, default));
        SendObservers(PvpPacket.RankDuelObserving(true));
        // The rank duel state goes back to 0, so the duelists can register again.
        Send(state.First, PvpPacket.RankDuelUnregistered());
        Send(state.Second, PvpPacket.RankDuelUnregistered());
    }

    // A side's score is its rounds won (a win by walkover or by leaving shows the rounds won before it).
    private PvpPacket.RankDuelSide Side(long characterId, long winner, Dictionary<long, RankDuelChange> changes) {
        RankDuelChange change = changes.GetValueOrDefault(characterId, new RankDuelChange(Rating, 0, 0));
        return new PvpPacket.RankDuelSide(Name(characterId), characterId, state.Wins(characterId), change.Rating, change.RatingChange, change.Reward);
    }

    // A round ended and the match goes on: the round's win and lose intros, or the draw intro; the onlookers' dialog
    // shows the new round wins.
    private void EndRound(RankDuelEvent @event) {
        if (@event.End == RankDuelEnd.Win) {
            Send(@event.Winner, PvpPacket.RankDuelWin());
            Send(@event.Loser, PvpPacket.RankDuelLose());
        } else {
            Send(state.First, PvpPacket.RankDuelDraw());
            Send(state.Second, PvpPacket.RankDuelDraw());
        }
        if (DuelDialog(0) is { } dialog) {
            SendDialog(dialog);
        }
    }

    // Before the intro of a later round: a duelist that died comes back where it fell (an instant revive, so it does
    // not leave the arena), and both start at full health. Chosen here: with two or more player spawns the first
    // duelist goes to the first and the second to the second; otherwise they stay where they are.
    private void StartNextRound() {
        // A hit of the round before is no kill credit in this one (the break is shorter than the credit window).
        killCredit.Clear();
        List<FieldPlayerSpawnPoint> spawns = field.GetPlayerSpawns();
        int index = 0;
        foreach (long characterId in new[] { state.First, state.Second }) {
            if (field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                if (player.IsDead) {
                    player.Revive(instant: true);
                }
                player.RecoverHp((int) player.Stats.Values[BasicAttribute.Health].Total);
                if (spawns.Count >= 2) {
                    player.MoveToPosition(spawns[index].Position, spawns[index].Rotation);
                }
            }
            index++;
        }
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
                    player.Session.Currency.Change(CurrencyType.ValorToken, change.Reward);
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
    // (ModePVPDuelRankArenaAdditionalEffect). Chosen here: the points are 101 and 102 (PvpPoints: 1 and 2 are the client's own teams) with team 0 and mode 0, and the
    // buffs (level 1) are given when the fight starts.
    private void StartFight() {
        int point = PvpPoints.First;
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
        foreach (long characterId in new[] { state.First, state.Second }.Where(id => !dismissed.Contains(id)).Concat(observers.ToList())) {
            if (field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                player.Session.ReturnField();
            }
        }
    }

    private void Tell(Action<int>? world) {
        if (world == null) {
            return;
        }
        int matchId = MatchId;
        Task.Run(() => {
            try {
                world(matchId);
            } catch (Exception ex) {
                logger.Warning(ex, "Rank duel match {MatchId}: the world was not told", matchId);
            }
        });
    }

    private void Send(long characterId, Maple2.PacketLib.Tools.ByteWriter packet) {
        if (field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
            player.Session.Send(packet);
        }
    }
}
