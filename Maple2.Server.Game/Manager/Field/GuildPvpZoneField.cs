using System.Numerics;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;
using Serilog;
using GuildRequest = Maple2.Server.World.Service.GuildRequest;
using GuildResponse = Maple2.Server.World.Service.GuildResponse;

namespace Maple2.Server.Game.Manager.Field;

// The World's guild championship match of a field (GuildPvpLookup, Factory.CreateGuildPvp): its id, its two guilds
// (guild A on stand 1, guild B on stand 2) and the call that ends it at the World (null: the answer's scores).
public sealed record GuildPvpFieldMatch(int MatchId, long GuildA, long GuildB, Func<GuildRequest.Types.PvpEnd, GuildResponse?> End);

// The guild championship's PvP zone (set_pvp_zone type 4, 64000001; GuildPvpZoneState has the rounds). The two
// guilds are the match's (a field without a match, entered by a GM, takes the two guilds with the most players here
// when the fight starts). Each player gets the guild team dialog (Pvp 'r'); each round's fighters go into the arena
// (portal 1, MoveByPortal) with the script's effect, the dialog follows the round (TeamPvp 's': its end, the fighters
// left, the round wins), and the fighters left go back to their stand (portals 10, 11). The deaths raise guildpvp_die
// and, for the other guild's fighter that hit within PvpZoneState.KillCreditMs, guildpvp_kill. At the end: TeamPvp 't'
// with the winner guild, the win or loss pose and guildpvp_win for the winners here; with a match, the World moves
// both guilds' scores (GuildPvpEnd), each player of the two guilds here gets the result (Pvp 'u'), and the match's
// round fighters the client constants' guild reward (PvpGuildRewardItem, PvpGuildRewardWinnerCount or LoserCount) when
// the World pays it. A guild with nobody left in the field forfeits. A draw has no result.
public sealed class GuildPvpZoneField : IPvpZoneField {
    // The fighters stand this far apart along X in the arena.
    private const float FighterSpacing = 150f;

    private readonly ILogger logger = Log.Logger.ForContext<GuildPvpZoneField>();
    private readonly object mutex = new();
    private readonly FieldManager field;
    private readonly GuildPvpZoneState state;
    private readonly int additionalEffectId;
    private readonly Func<int, IEnumerable<FieldPlayer>> playersInBox;
    private readonly GuildPvpFieldMatch? match;
    private readonly long[] guilds = new long[3];
    private readonly string[] guildNames = ["", "", ""];
    private readonly string[] guildEmblems = ["", "", ""];
    private readonly Dictionary<long, string> fighterNames = [];
    private readonly Dictionary<long, (long Attacker, long Tick)> lastHitBy = [];

    public int BoxId => state.BoxId;
    public bool Ended {
        get {
            lock (mutex) return state.Ended;
        }
    }

    public GuildPvpZoneField(FieldManager field, int boxId, int prepareSeconds, int matchSeconds, int additionalEffectId, IReadOnlyList<int> boxIds,
                             Func<int, IEnumerable<FieldPlayer>> playersInBox, GuildPvpFieldMatch? match, long tick) {
        this.field = field;
        this.additionalEffectId = additionalEffectId;
        this.playersInBox = playersInBox;
        this.match = match;
        state = new GuildPvpZoneState(boxId, tick, prepareSeconds, matchSeconds, boxIds);
        if (match != null) {
            guilds[GuildPvpZoneState.TeamA] = match.GuildA;
            guilds[GuildPvpZoneState.TeamB] = match.GuildB;
            FillGuilds();
            field.Broadcast(Teams());
        }
    }

    private int TeamOf(FieldPlayer player) {
        long guildId = player.Session.Guild.Id;
        if (guildId == 0) {
            return GuildPvpZoneState.NoTeam;
        }
        return guildId == guilds[GuildPvpZoneState.TeamA] ? GuildPvpZoneState.TeamA
            : guildId == guilds[GuildPvpZoneState.TeamB] ? GuildPvpZoneState.TeamB : GuildPvpZoneState.NoTeam;
    }

    // The guilds' names and emblems, from a member here (the field has no guild table of its own).
    private void FillGuilds() {
        foreach (FieldPlayer player in playersInBox(0)) {
            int team = TeamOf(player);
            if (team != GuildPvpZoneState.NoTeam && player.Session.Guild.Guild is { } guild) {
                guildNames[team] = guild.Name;
                guildEmblems[team] = guild.Emblem;
            }
        }
    }

    private ByteWriter Teams() => PvpPacket.GuildTeams(guilds[GuildPvpZoneState.TeamA], guildNames[GuildPvpZoneState.TeamA], guildEmblems[GuildPvpZoneState.TeamA],
        guilds[GuildPvpZoneState.TeamB], guildNames[GuildPvpZoneState.TeamB], guildEmblems[GuildPvpZoneState.TeamB]);

    public bool Hostile(FieldPlayer caster, FieldPlayer target) {
        lock (mutex) return state.Hostile(caster.Value.Character.Id, target.Value.Character.Id);
    }

    public bool IsParticipant(FieldPlayer player) {
        lock (mutex) return !state.Ended && state.InFight(player.Value.Character.Id);
    }

    public void Update(long tick) {
        lock (mutex) {
            if (state.Ended) {
                return;
            }
            if (guilds[GuildPvpZoneState.TeamA] == 0) {
                if (tick < state.StartTick) {
                    return;
                }
                if (!PickGuilds()) {
                    logger.Information("Guild PvP zone {BoxId} on {MapId}: no two guilds here, ended", state.BoxId, field.MapId);
                    Apply(state.Stop(tick));
                    return;
                }
            }
            HashSet<long> inArena = playersInBox(state.BoxId).Where(player => !player.IsDead).Select(player => player.Value.Character.Id).ToHashSet();
            Apply(state.Update(tick, team => Living(state.StandOf(team), team), team => Living(0, team), inArena.Contains,
                team => playersInBox(0).Any(player => TeamOf(player) == team)));
        }
    }

    private IReadOnlyList<long> Living(int boxId, int team) {
        return playersInBox(boxId).Where(player => !player.IsDead && TeamOf(player) == team).Select(player => player.Value.Character.Id).ToList();
    }

    // A field without a match: the two guilds with the most players here (the more players guild A).
    private bool PickGuilds() {
        List<long> picked = playersInBox(0).Select(player => player.Session.Guild.Id).Where(id => id != 0)
            .GroupBy(id => id).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).Take(2).Select(group => group.Key).ToList();
        if (picked.Count < 2) {
            return false;
        }
        guilds[GuildPvpZoneState.TeamA] = picked[0];
        guilds[GuildPvpZoneState.TeamB] = picked[1];
        FillGuilds();
        field.Broadcast(Teams());
        return true;
    }

    public void Damage(FieldPlayer attacker, FieldPlayer victim, long amount) {
        lock (mutex) {
            if (state.Hostile(attacker.Value.Character.Id, victim.Value.Character.Id)) {
                lastHitBy[victim.Value.Character.Id] = (attacker.Value.Character.Id, field.FieldTick);
            }
        }
    }

    public void Death(FieldPlayer victim) {
        lock (mutex) {
            long victimId = victim.Value.Character.Id;
            if (state.TeamOfFighter(victimId) == GuildPvpZoneState.NoTeam || !state.Fighting) {
                return;
            }
            long killer = 0;
            if (lastHitBy.Remove(victimId, out (long Attacker, long Tick) hit) && field.FieldTick - hit.Tick <= PvpZoneState.KillCreditMs) {
                killer = hit.Attacker;
            }
            PvpConditions.GuildDeath(victim, field.MapId);
            if (killer != 0 && field.TryGetPlayerById(killer, out FieldPlayer? killerPlayer)) {
                field.Broadcast(PlayerKillNoticePacket.Kill(killerPlayer.Value.Character.Name, victim.Value.Character.Name));
                PvpConditions.GuildKill(killerPlayer, field.MapId);
            }
            Apply(state.Out(victimId, field.FieldTick));
        }
    }

    // An arrival sees the two guilds and the running round.
    public void Enter(FieldPlayer player) {
        lock (mutex) {
            if (state.Ended || guilds[GuildPvpZoneState.TeamA] == 0) {
                return;
            }
            int team = TeamOf(player);
            if (team != GuildPvpZoneState.NoTeam && guildNames[team].Length == 0 && player.Session.Guild.Guild is { } guild) {
                guildNames[team] = guild.Name;
                guildEmblems[team] = guild.Emblem;
                field.Broadcast(Teams());
            } else {
                player.Session.Send(Teams());
            }
            if (state.Fighting) {
                player.Session.Send(Round(state.RoundEndTick.Truncate32()));
            }
        }
    }

    // A fighter leaving is out; the last player of a team seen in the field forfeits the match (once it started).
    public void Leave(FieldPlayer player) {
        lock (mutex) {
            long characterId = player.Value.Character.Id;
            int team = TeamOf(player);
            if (team != GuildPvpZoneState.NoTeam && !playersInBox(0).Any(other => other.Value.Character.Id != characterId && TeamOf(other) == team)) {
                List<GuildPvpZoneEvent> events = state.Forfeit(team, field.FieldTick);
                if (events.Count > 0) {
                    Apply(events);
                    return;
                }
            }
            Apply(state.Out(characterId, field.FieldTick));
        }
    }

    private ByteWriter Round(int endTick) {
        return TeamPvpPacket.Round(state.Round, endTick, state.FightersLeft.ToList(), guilds[GuildPvpZoneState.TeamA], state.Wins(GuildPvpZoneState.TeamA),
            guilds[GuildPvpZoneState.TeamB], state.Wins(GuildPvpZoneState.TeamB));
    }

    private void Apply(List<GuildPvpZoneEvent> events) {
        foreach (GuildPvpZoneEvent @event in events) {
            switch (@event.Type) {
                case GuildPvpZoneEventType.RoundStart:
                    StartRound();
                    break;
                case GuildPvpZoneEventType.Out:
                    lastHitBy.Remove(@event.CharacterId);
                    field.Broadcast(Round(state.RoundEndTick.Truncate32()));
                    break;
                case GuildPvpZoneEventType.RoundEnd:
                    field.Broadcast(Round(0));
                    // The fighters still in the arena go back to their stand.
                    for (int team = GuildPvpZoneState.TeamA; team <= GuildPvpZoneState.TeamB; team++) {
                        HashSet<long> arena = playersInBox(state.BoxId).Select(player => player.Value.Character.Id).ToHashSet();
                        foreach (long characterId in state.Fighters(team).Where(arena.Contains)) {
                            MoveTo(characterId, team == GuildPvpZoneState.TeamA ? state.PortalA : state.PortalB, 0);
                        }
                    }
                    logger.Debug("Guild PvP zone {BoxId} on {MapId}: round {Round} to team {Team}", state.BoxId, field.MapId, state.Round, @event.Team);
                    break;
                case GuildPvpZoneEventType.End:
                    End(@event.Team);
                    break;
            }
        }
    }

    private void StartRound() {
        for (int team = GuildPvpZoneState.TeamA; team <= GuildPvpZoneState.TeamB; team++) {
            IReadOnlyList<long> fighters = state.Fighters(team);
            for (int i = 0; i < fighters.Count; i++) {
                if (!field.TryGetPlayerById(fighters[i], out FieldPlayer? fighter)) {
                    continue;
                }
                fighterNames[fighters[i]] = fighter.Value.Character.Name;
                // Guild A on the arena's -X side, guild B on its +X side.
                float offset = (team == GuildPvpZoneState.TeamA ? -1 : 1) * FighterSpacing * (1 + i);
                MoveTo(fighters[i], state.ArenaPortal, offset);
                if (additionalEffectId > 0) {
                    fighter.Buffs.AddBuff(field.FieldActor, fighter, additionalEffectId, 1, field.FieldTick);
                }
            }
        }
        field.Broadcast(Round(state.RoundEndTick.Truncate32()));
    }

    // Sent as is (MoveByPortal), as the Red Arena's stage moves.
    private void MoveTo(long characterId, int portalId, float offset) {
        if (!field.TryGetPlayerById(characterId, out FieldPlayer? player) || !field.TryGetPortal(portalId, out FieldPortal? portal)) {
            return;
        }
        player.Session.Send(PortalPacket.MoveByPortal(player, field.StageSpot(portal.Value.Position + new Vector3(offset, 0, 0)), portal.Value.Rotation));
    }

    private void End(int winner) {
        long winnerGuild = guilds[winner];
        field.Broadcast(TeamPvpPacket.End(winnerGuild));
        foreach (FieldPlayer player in playersInBox(0)) {
            int team = TeamOf(player);
            if (team == GuildPvpZoneState.NoTeam || winner == GuildPvpZoneState.NoTeam) {
                continue;
            }
            player.Session.Send(PvpPacket.WinLose(team == winner));
            // The win's trophies only for a fought round won (not walkovers or a forfeit alone).
            if (team == winner && state.WonFoughtRound(winner)) {
                PvpConditions.GuildWin(player, field.MapId);
            }
        }
        logger.Information("Guild PvP zone {BoxId} on {MapId} ended: {A} {WinsA} to {B} {WinsB}", state.BoxId, field.MapId,
            guilds[GuildPvpZoneState.TeamA], state.Wins(GuildPvpZoneState.TeamA), guilds[GuildPvpZoneState.TeamB], state.Wins(GuildPvpZoneState.TeamB));
        if (match == null) {
            return;
        }

        var end = new GuildRequest.Types.PvpEnd { MatchId = match.MatchId, WinnerGuildId = winnerGuild };
        foreach (GuildPvpRound round in state.Rounds) {
            end.Rounds.Add(new GuildRequest.Types.PvpEnd.Types.Round {
                PlayersA = Players(round.FightersA),
                PlayersB = Players(round.FightersB),
                Winner = round.Winner,
                Fought = round.Fought,
            });
        }
        // Only the players sent into a fought round took part (not walk-ins, not a walkover).
        end.Fighters.AddRange(state.RoundFighters);
        // The fighters the result can pay (in the field now, under their own daily cap): the World takes a guild's
        // daily slot and moves its score only for a guild with one of them.
        HashSet<long> roundFighters = state.RoundFighters.ToHashSet();
        DateTime now = DateTime.Now;
        end.PayableFighters.AddRange(playersInBox(0)
            .Where(player => roundFighters.Contains(player.Value.Character.Id) && Maple2.Model.Game.GuildPvp.HasFighterReward(player.Value.Character, now))
            .Select(player => player.Value.Character.Id).Distinct());
        int winsA = state.Wins(GuildPvpZoneState.TeamA);
        int winsB = state.Wins(GuildPvpZoneState.TeamB);
        HashSet<long> fighters = end.Fighters.ToHashSet();
        Task.Run(() => {
            GuildResponse? response = null;
            try {
                response = match.End(end);
            } catch (Exception ex) {
                logger.Warning(ex, "Guild PvP match {MatchId}: the end was not sent", match.MatchId);
            }
            if (response is { PvpScores.Count: >= 2 }) {
                field.Scheduler.Schedule(() => Result(response, winsA, winsB, fighters));
            }
        });
    }

    // "id,name:id,name" (CGuildPVPHistoryData f58/f80).
    private string Players(IEnumerable<long> fighters) {
        return string.Join(":", fighters.Select(id => $"{id},{(fighterNames.TryGetValue(id, out string? name) ? name : string.Empty)}"));
    }

    // The World counted the match (a winner): each player of the two guilds here gets the result window. The reward
    // (PvpGuildRewardItem, mailed when the bag is full) and guild_championship of the season go only to the match's
    // fighters, once each, only for a guild the World pays (Reward: a fought match under its daily cap) and while
    // the character has one of the day's GuildPvp.DailyRewardCap left (whatever its guild); the others' window
    // shows no reward.
    private void Result(GuildResponse response, int winsA, int winsB, IReadOnlySet<long> fighters) {
        GuildResponse.Types.PvpScore winnerRow = response.PvpScores[0];
        GuildResponse.Types.PvpScore loserRow = response.PvpScores[1];
        int Rounds(long guildId) => guildId == guilds[GuildPvpZoneState.TeamA] ? winsA : guildId == guilds[GuildPvpZoneState.TeamB] ? winsB : 0;
        var winnerSide = new PvpPacket.GuildSide(winnerRow.Name, winnerRow.GuildId, Rounds(winnerRow.GuildId), winnerRow.Score, winnerRow.Delta, winnerRow.Emblem);
        var loserSide = new PvpPacket.GuildSide(loserRow.Name, loserRow.GuildId, Rounds(loserRow.GuildId), loserRow.Score, loserRow.Delta, loserRow.Emblem);
        ConstantsTable constants = field.ServerTableMetadata.ConstantsTable;
        int seasonId = Maple2.Model.Game.GuildPvp.CurrentSeason(field.TableMetadata.SeasonDataTable.GuildPvp, DateTime.Now)?.Id ?? 0;
        var paid = new HashSet<long>();
        DateTime now = DateTime.Now;
        foreach (FieldPlayer player in playersInBox(0).ToList()) {
            long guildId = player.Session.Guild.Id;
            if (guildId == 0 || (guildId != winnerRow.GuildId && guildId != loserRow.GuildId)) {
                continue;
            }
            long characterId = player.Value.Character.Id;
            GuildResponse.Types.PvpScore row = guildId == winnerRow.GuildId ? winnerRow : loserRow;
            bool fighter = fighters.Contains(characterId) && paid.Add(characterId);
            bool rewarded = Maple2.Model.Game.GuildPvp.TakeFighterReward(player.Value.Character, fighter, row.Reward, now);
            int count = !rewarded ? 0 : guildId == winnerRow.GuildId ? constants.PvpGuildRewardWinnerCount : constants.PvpGuildRewardLoserCount;
            player.Session.Send(PvpPacket.GuildResult(winnerSide, loserSide, count));
            if (seasonId > 0 && rewarded) {
                PvpConditions.GuildChampionship(player, seasonId);
            }
            try {
                if (count > 0 && constants.PvpGuildRewardItem > 0) {
                    Item? item = field.ItemDrop.CreateItem(constants.PvpGuildRewardItem, amount: count);
                    if (item == null || !player.Session.Item.AddOrMail(item, notifyNew: true, commit: true)) {
                        logger.Warning("Guild PvP reward of {CharacterId} not added", characterId);
                    }
                }
            } catch (Exception ex) {
                logger.Error(ex, "Guild PvP reward of {CharacterId} not given", characterId);
            }
        }
    }
}
