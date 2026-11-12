using Maple2.Model.Game.Pvp;
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

    // The rating the result shows. The client starts every character at 1000 (CPvpRankDuelComponent::Reset) and
    // the server keeps no rating yet, so it shows 1000 with no change.
    public const int Rating = 1000;

    private readonly ILogger logger = Log.Logger.ForContext<RankDuelField>();
    private readonly object mutex = new();
    private readonly FieldManager field;
    private readonly RankDuelMatchState state;
    private readonly Action<int> onEnd;
    private readonly Dictionary<long, string> names = [];

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
    // checks death itself): not while dead, not from a dungeon or another instanced room (a home is allowed), not
    // from a rank duel field.
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
                var left = new PvpPacket.RankDuelSide(Name(state.First), state.First, state.First == @event.Winner ? 1 : 0, Rating, 0, 0);
                var right = new PvpPacket.RankDuelSide(Name(state.Second), state.Second, state.Second == @event.Winner ? 1 : 0, Rating, 0, 0);
                Send(state.First, PvpPacket.RankDuelResult(@event.Winner, left, right));
                Send(state.Second, PvpPacket.RankDuelResult(@event.Winner, left, right));
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
        // The rank duel state goes back to 0, so the duelists can register again.
        Send(state.First, PvpPacket.RankDuelUnregistered());
        Send(state.Second, PvpPacket.RankDuelUnregistered());
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
        Send(characterId, PvpPacket.RankDuelUnregistered());
        field.Scheduler.Schedule(() => {
            if (field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                player.Session.ReturnField();
            }
        });
    }

    // After the result: the duelists still here go back to their return field.
    private void SendOut() {
        foreach (long characterId in new[] { state.First, state.Second }) {
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
