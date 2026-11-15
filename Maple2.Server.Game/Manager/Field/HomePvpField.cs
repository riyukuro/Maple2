using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// A home's PvP match (HomeFieldManager.StartPvp, from the owner's PvP control dialog): the client has the dialog, its
// texts (s_ugcmap_fun_pvp_ffa_*) and the PvP row of the home (7, on: no pet summon), but no rule; see HomePvpState.
// The start says the fight comes in 5 seconds; at the fight every player in the home gets a PvP point of its side
// (Pvp 0x64 in mode 0: players with different points are enemies), the buff of the dialog's choice, and the home's
// rows the choices stop for everyone while it runs: 7 (PvP) always, 4 (potion) and 8 (the in-place revive) when the
// dialog said so. The end names the winners (or a draw), takes the points and buffs back, puts the home's own rows back
// and clears the field's PvpZone. The client and the data have no reward for it, so none is given.
public sealed class HomePvpField : IPvpZoneField {
    private readonly ILogger logger = Log.Logger.ForContext<HomePvpField>();
    private readonly object mutex = new();
    private readonly HomeFieldManager field;
    private readonly HomePvpState state;
    private readonly HomePermission[] overlay;
    private bool overlaid;

    public HomePvpField(HomeFieldManager field, HomePvpSettings settings, long tick) {
        this.field = field;
        state = new HomePvpState(settings, tick);
        overlay = Rows(settings);
    }

    public HomePvpSettings Settings => state.Settings;

    // The rows the match stops for everyone (setting 0 on an enabled row stops the owner too).
    public static HomePermission[] Rows(HomePvpSettings settings) {
        var rows = new List<HomePermission> { HomePermission.Pvp };
        if (settings.NoPotion) {
            rows.Add(HomePermission.Potion);
        }
        if (settings.NoRevive) {
            rows.Add(HomePermission.Revival);
        }
        return rows.ToArray();
    }

    // A home has no zone box.
    public int BoxId => 0;
    public bool Ended {
        get {
            lock (mutex) return state.Phase == HomePvpPhase.Ended;
        }
    }

    // While the fight runs the match's rows replace the home's own.
    public bool Overlays(HomePermission row) {
        lock (mutex) return overlaid && overlay.Contains(row);
    }

    public bool IsParticipant(FieldPlayer player) {
        lock (mutex) return state.InPlay(player.Value.Character.Id);
    }

    public bool Hostile(FieldPlayer caster, FieldPlayer target) {
        lock (mutex) return state.Hostile(caster.Value.Character.Id, target.Value.Character.Id);
    }

    // The start: the countdown's text to everyone here.
    public void Started() {
        field.Broadcast(Text("s_ugcmap_fun_pvp_ffa_activate_after_5sec"));
    }

    public void Update(long tick) {
        lock (mutex) {
            if (state.DueToActivate(tick)) {
                Activate(tick);
            } else if (state.DueToEnd(tick)) {
                End();
            }
        }
    }

    // The owner used the console during the match: it ends now (or never starts).
    // False when it had already ended (the field's update got there first).
    public bool Stop() {
        lock (mutex) {
            if (state.Phase == HomePvpPhase.Active) {
                End();
                return true;
            }
            if (state.Phase == HomePvpPhase.Countdown) {
                state.Activate([], field.FieldTick);
                Deactivate();
                return true;
            }
            return false;
        }
    }

    private void Activate(long tick) {
        List<FieldPlayer> players = field.Players.Values.ToList();
        IReadOnlyList<HomePvpEntry> entries = state.Activate(
            players.Select(player => (player.Value.Character.Id, player.Session.Party?.Id ?? 0L)), tick);
        if (entries.Count == 0) {
            logger.Information("Home PvP on {MapId} of {OwnerId} called off: fewer than two sides", field.MapId, field.OwnerId);
            Deactivate();
            return;
        }
        field.Broadcast(Text("s_ugcmap_fun_pvp_ffa_activate"));
        overlaid = true;
        foreach (HomePermission row in overlay) {
            field.Broadcast(CubePacket.EnablePermission(row, true));
            field.Broadcast(CubePacket.SetPermission(row, HomePermissionSetting.None));
        }
        (int effectId, short level) = state.Settings.Effect;
        foreach (HomePvpEntry entry in entries) {
            if (!field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? player)) {
                continue;
            }
            field.Broadcast(PvpPacket.Point(player.ObjectId, entry.Point, 0, 0));
            player.Buffs.AddBuff(field.FieldActor, player, effectId, level, tick);
        }
        logger.Information("Home PvP on {MapId} of {OwnerId} started with {Count} players", field.MapId, field.OwnerId, entries.Count);
    }

    public void Damage(FieldPlayer attacker, FieldPlayer victim, long amount) {
        lock (mutex) {
            state.Damage(attacker.Value.Character.Id, victim.Value.Character.Id, field.FieldTick);
        }
    }

    public void Death(FieldPlayer victim) {
        lock (mutex) {
            long characterId = victim.Value.Character.Id;
            if (!state.InPlay(characterId)) {
                return;
            }
            long killer = state.Death(characterId, field.FieldTick);
            if (killer != 0 && field.TryGetPlayerById(killer, out FieldPlayer? killerPlayer)) {
                field.Broadcast(PlayerKillNoticePacket.Kill(killerPlayer.Value.Character.Name, victim.Value.Character.Name));
            }
            if (state.Entry(characterId) is { Out: true }) {
                TakeBack(victim);
            }
        }
    }

    // A newcomer during the fight sees the participants' points and the match's rows; it is no participant.
    public void Enter(FieldPlayer player) {
        lock (mutex) {
            if (state.Phase != HomePvpPhase.Active) {
                return;
            }
            foreach (HomePvpEntry entry in state.Entries.Where(entry => !entry.Out)) {
                if (field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? other)) {
                    player.Session.Send(PvpPacket.Point(other.ObjectId, entry.Point, 0, 0));
                }
            }
            foreach (HomePermission row in overlay) {
                player.Session.Send(CubePacket.EnablePermission(row, true));
                player.Session.Send(CubePacket.SetPermission(row, HomePermissionSetting.None));
            }
        }
    }

    // A participant that left is out; its buff ends with the field (or here if it is still listed).
    public void Leave(FieldPlayer player) {
        lock (mutex) {
            if (state.Leave(player.Value.Character.Id)) {
                player.Buffs.Remove(state.Settings.Effect.Id, field.FieldActor.ObjectId);
            }
        }
    }

    private void End() {
        int winner = state.End();
        if (winner == 0) {
            field.Broadcast(NoticePacket.Notice(Shown, StringCode.s_ugcmap_fun_pvp_ffa_finished_draw));
        } else {
            field.Broadcast(NoticePacket.Notice(Shown, StringCode.s_ugcmap_fun_pvp_ffa_finished));
            foreach (long characterId in state.Members(winner)) {
                if (field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                    field.Broadcast(NoticePacket.Notice(Shown, new InterfaceText(StringCode.s_ugcmap_fun_pvp_ffa_winnter_name, player.Value.Character.Name)));
                }
            }
        }
        foreach (HomePvpEntry entry in state.Entries) {
            if (field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? player)) {
                TakeBack(player);
            }
        }
        Deactivate();
        logger.Information("Home PvP on {MapId} of {OwnerId} ended, winning point {Point}", field.MapId, field.OwnerId, winner);
    }

    private void TakeBack(FieldPlayer player) {
        field.Broadcast(PvpPacket.Point(player.ObjectId, 0, 0, 0));
        player.Buffs.Remove(state.Settings.Effect.Id, field.FieldActor.ObjectId);
    }

    // The home's own rows back, the end's text, and the field's PvpZone free for the next match.
    private void Deactivate() {
        if (overlaid) {
            overlaid = false;
            foreach (HomePermission row in overlay) {
                bool enabled = field.Home.Permissions.TryGetValue(row, out HomePermissionSetting setting);
                field.Broadcast(CubePacket.EnablePermission(row, enabled));
                if (enabled) {
                    field.Broadcast(CubePacket.SetPermission(row, setting));
                }
            }
        }
        field.Broadcast(Text("s_ugcmap_fun_pvp_ffa_deactivate"));
        field.EndPvp(this);
    }

    // The texts show in the chat and as the alert at the top (chosen here; the client names no place for them).
    private const NoticePacket.Flags Shown = NoticePacket.Flags.Message | NoticePacket.Flags.Alert;

    private static ByteWriter Text(string key) => NoticePacket.Notice(Shown, new InterfaceText(key));
}
