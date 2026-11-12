using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// The individual arena match a field was made for (FieldManager.Arena): the arena's row and the match's characters.
// The map's own script runs the round (its set_pvp_zone); when the zone ends the arena row's reward is given and the
// World is told the match ended.
public sealed class ArenaMatchField {
    private readonly ILogger logger = Log.Logger.ForContext<ArenaMatchField>();
    private readonly FieldManager field;
    private readonly Action<int> onEnd;

    public int MatchId { get; }
    public IndividualArenaTable.Entry Arena { get; }
    public IReadOnlyList<long> Characters { get; }

    public ArenaMatchField(FieldManager field, int matchId, IndividualArenaTable.Entry arena, IReadOnlyList<long> characters, Action<int> onEnd) {
        this.field = field;
        this.onEnd = onEnd;
        MatchId = matchId;
        Arena = arena;
        Characters = characters;
    }

    // The zone's ranking, the winners first (winners: how many; the free-for-all's first place, the Red Arena's
    // champion, the winning team). The arena window shows rewardItemId1 with reward1Winner and reward1Loser (and the
    // same for item 2; UIIndividualPvPDialog::BindRewardIcon). Chosen here: those are the winners, every other
    // participant here a loser.
    public void ZoneEnded(IReadOnlyList<long> ranking, int winners) {
        for (int index = 0; index < ranking.Count; index++) {
            if (!field.TryGetPlayerById(ranking[index], out FieldPlayer? player)) {
                continue;
            }
            bool winner = index < winners;
            Give(player, Arena.RewardItemId1, winner ? Arena.Reward1Winner : Arena.Reward1Loser);
            Give(player, Arena.RewardItemId2, winner ? Arena.Reward2Winner : Arena.Reward2Loser);
        }

        EndInWorld();
    }

    // A match character left the field: when none is left and the zone has not ended (they all left during the wait
    // or the fight, or too few came for the script to start), the World lets the match go.
    public void Left(FieldManager from) {
        if (from.Players.Values.Any(player => Characters.Contains(player.Value.Character.Id))) {
            return;
        }
        if (from.PvpZone is { Ended: true }) {
            return;
        }
        EndInWorld();
    }

    // The field is disposed: the match ends in the World if it has not.
    public void Close() => EndInWorld();

    private int endSent;

    private void EndInWorld() {
        if (Interlocked.Exchange(ref endSent, 1) != 0) {
            return;
        }
        int matchId = MatchId;
        Task.Run(() => {
            try {
                onEnd(matchId);
            } catch (Exception ex) {
                logger.Warning(ex, "Arena match {MatchId}: the end was not sent to the world", matchId);
            }
        });
    }

    private void Give(FieldPlayer player, int itemId, int count) {
        if (itemId <= 0 || count <= 0) {
            return;
        }
        try {
            Item? item = field.ItemDrop.CreateItem(itemId, amount: count);
            if (item == null || !player.Session.Item.Inventory.Add(item, notifyNew: true, commit: true)) {
                logger.Warning("Arena reward {ItemId} x{Count} of {CharacterId} not added", itemId, count, player.Value.Character.Id);
            }
        } catch (Exception ex) {
            logger.Error(ex, "Arena reward {ItemId} of {CharacterId} not given", itemId, player.Value.Character.Id);
        }
    }
}
