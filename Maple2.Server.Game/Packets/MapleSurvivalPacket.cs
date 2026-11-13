using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The Maple Survival match packets of the restored client (Survival 0x119 and SurvivalEvent 0x116).
// The layouts are the restored client's readers. Every body starts with its mode byte.
public static class MapleSurvivalPacket {
    private static ByteWriter Survival(byte mode) {
        var pWriter = Packet.Of(SendOp.Survival);
        pWriter.WriteByte(mode);
        return pWriter;
    }

    // 0x00: state 1 (matching), with the matching sound.
    public static ByteWriter Matching() => Survival(0x00);

    // 0x01: state 0 (idle).
    public static ByteWriter Idle() => Survival(0x01);

    // 0x02: state 2 and the match box (join sends 0xC2 mode 1, cancel or the timer mode 3); short: a 5 s timer, else
    // 15 s; a character id of 0 adds the first-match line.
    public static ByteWriter Matched(long characterId, bool shortTimer) {
        ByteWriter pWriter = Survival(0x02);
        pWriter.WriteLong(characterId);
        pWriter.WriteBool(shortTimer);
        return pWriter;
    }

    // 0x06: the same box as a re-invite, no state change (join sends mode 6).
    public static ByteWriter Reinvite(long characterId, bool shortTimer) {
        ByteWriter pWriter = Survival(0x06);
        pWriter.WriteLong(characterId);
        pWriter.WriteBool(shortTimer);
        return pWriter;
    }

    // 0x11: the game result: 16 ints (SurvivalRules.ResultValues), 4 floats (a buff when above 0; none here), then
    // the items (none here).
    public static ByteWriter Result(IReadOnlyList<int> values) {
        ByteWriter pWriter = Survival(0x11);
        for (int i = 0; i < 16; i++) {
            pWriter.WriteInt(i < values.Count ? values[i] : 0);
        }
        for (int i = 0; i < 4; i++) {
            pWriter.WriteFloat(0);
        }
        pWriter.WriteByte(0);
        return pWriter;
    }

    // 0x12: the lobby value (the restored lobby shows the reward of the level after it: the level whose reward was
    // given last, here the survival level).
    public static ByteWriter LobbyValue(int value) {
        ByteWriter pWriter = Survival(0x12);
        pWriter.WriteInt(value);
        return pWriter;
    }

    // 0x16: the season (stored by the client only with active 1; the lobby and pass windows look the id up in
    // maplesurvivalopenperiod): the id, active, a time (the season's end, epoch seconds), the name, the value the lobby
    // shows as the rating, then six ints (here the games, wins and kills, the rest 0).
    public static ByteWriter Season(int seasonId, bool active, long endTime, string name, int rating, int games, int wins, int kills) {
        ByteWriter pWriter = Survival(0x16);
        pWriter.WriteInt(seasonId);
        pWriter.WriteInt(active ? 1 : 0);
        pWriter.WriteLong(endTime);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteInt(rating);
        pWriter.WriteInt(games);
        pWriter.WriteInt(wins);
        pWriter.WriteInt(kills);
        pWriter.WriteInt(0);
        pWriter.WriteInt(0);
        pWriter.WriteInt(0);
        return pWriter;
    }

    // 0x15: the lobby's level, the exp inside it and the level whose reward was given last (the restored reader,
    // maplesurvival.cpp:502: i64 m_exp, i32 m_level, i32 m_1a, then the lobby refresh; the lobby's next level reward is
    // m_1a + 1, as for 0x12).
    public static ByteWriter LevelProgress(long exp, int level, int rewardLevel) {
        ByteWriter pWriter = Survival(0x15);
        pWriter.WriteLong(exp);
        pWriter.WriteInt(level);
        pWriter.WriteInt(rewardLevel);
        return pWriter;
    }

    // 0x1f: a skin added to a slot (0 tail, 1 riding, 2 gliding): the skin and its end (the reader,
    // maplesurvival.cpp:616, appends it to the slot's list and refreshes the skin window).
    public static ByteWriter SkinAdded(byte slot, int skinId, long expiry) {
        ByteWriter pWriter = Survival(0x1f);
        pWriter.WriteByte(slot);
        pWriter.WriteInt(skinId);
        pWriter.WriteLong(expiry);
        return pWriter;
    }

    // 0x21: the "skin learned" system line (the reader, maplesurvival.cpp:636: the slot, an int, then
    // s_maple_survival_learn_skin; GMS formats the item name into it).
    public static ByteWriter SkinLearned(byte slot, int skinId) {
        ByteWriter pWriter = Survival(0x21);
        pWriter.WriteByte(slot);
        pWriter.WriteInt(skinId);
        return pWriter;
    }

    // 0x20: a skin slot's equipped skin (0 tail, 1 riding, 2 gliding).
    public static ByteWriter SkinEquipped(byte slot, int skinId) {
        ByteWriter pWriter = Survival(0x20);
        pWriter.WriteByte(slot);
        pWriter.WriteInt(skinId);
        return pWriter;
    }

    // 0x23: pass progress; the client shows the free line when a <= b and the paid line when d >= c. Sent as 1, the
    // free levels claimed, 1, the paid levels claimed (a claim of none shows neither).
    public static ByteWriter PassProgress(int freeClaimed, int paidClaimed) {
        ByteWriter pWriter = Survival(0x23);
        pWriter.WriteInt(1);
        pWriter.WriteInt(freeClaimed);
        pWriter.WriteInt(1);
        pWriter.WriteInt(paidClaimed);
        return pWriter;
    }

    // SpectateMushtopia (0x11A) mode 1: the local player watches the target (CSceneStateGame::OnSpectateMushtopia: PC
    // state 0x3f with StateObserverData(target), which opens the restored observer window).
    public static ByteWriter Spectate(long target) {
        var pWriter = Packet.Of(SendOp.SpectateMushtopia);
        pWriter.WriteByte(1);
        pWriter.WriteLong(target);
        return pWriter;
    }

    // 0x13: the game starts (the client clears its finish list).
    public static ByteWriter GameStart() => Survival(0x13);

    // 0x14: the finished characters; end: the game is over (state 0).
    public static ByteWriter GameFinish(bool end, IReadOnlyCollection<long> characterIds) {
        ByteWriter pWriter = Survival(0x14);
        pWriter.WriteBool(end);
        pWriter.WriteInt(characterIds.Count);
        foreach (long characterId in characterIds) {
            pWriter.WriteLong(characterId);
        }
        return pWriter;
    }

    // 0x19 type 0: a kill (the killer, the victim, the weapon byte, an int).
    public static ByteWriter KillFeed(string killer, string victim, byte weapon, int value) {
        ByteWriter pWriter = Survival(0x19);
        pWriter.WriteByte(0);
        pWriter.WriteUnicodeString(killer);
        pWriter.WriteUnicodeString(victim);
        pWriter.WriteByte(weapon);
        pWriter.WriteInt(value);
        return pWriter;
    }

    // The kill log's status line (UISurvivalKillLogDialog::RefreshStatus, survivalkilllogdialog.cpp:227) calls the
    // movie's setStatus with the 0x1b ints and the 0x1a int (maplesurvival.cpp:568). The movie
    // (uisurvivalkilllogdialog.gfx) shows the first in m_textSurvivor and the third in m_textKilledByMe unless it is
    // 65535; it does not read the second.

    // 0x1a: the player's own kills (m_textKilledByMe).
    public static ByteWriter OwnKills(int kills) {
        ByteWriter pWriter = Survival(0x1a);
        pWriter.WriteInt(kills);
        return pWriter;
    }

    // 0x1b: the survivors (m_textSurvivor), a second int and a flag. Chosen here: the second int is the match's
    // participants and the flag 0, as no client code shown here reads them.
    public static ByteWriter Survivors(int alive, int participants) {
        ByteWriter pWriter = Survival(0x1b);
        pWriter.WriteInt(alive);
        pWriter.WriteInt(participants);
        pWriter.WriteByte(0);
        return pWriter;
    }

    // 0x1d: "outside the safe zone" (s_maple_survival_in_storm).
    public static ByteWriter InStorm() => Survival(0x1d);

    private static ByteWriter StormEvent(byte mode) {
        var pWriter = Packet.Of(SendOp.SurvivalEvent);
        pWriter.WriteByte(mode);
        return pWriter;
    }

    // SurvivalEvent 0: add a storm zone: i16 id, i16, i16, a position, i16, a vector. Chosen here (the restored client
    // only stores them): the zone id, the storm's outer radius, the step, the storm's center, the safe radius and the
    // safe zone's center.
    public static ByteWriter AddZone(short id, short outerRadius, short step, Vector3 center, short safeRadius, Vector3 safeCenter) {
        ByteWriter pWriter = StormEvent(0);
        pWriter.WriteShort(id);
        pWriter.WriteShort(outerRadius);
        pWriter.WriteShort(step);
        WriteVector(pWriter, center);
        pWriter.WriteShort(safeRadius);
        WriteVector(pWriter, safeCenter);
        return pWriter;
    }

    // SurvivalEvent 1: a zone's new radius and center (GMS 0x74b8d0 -> 0x74b290 scales the storm by the radius; the
    // "storm" minimap marker and the storm bar take both).
    public static ByteWriter UpdateZone(short id, float radius, Vector3 center) {
        ByteWriter pWriter = StormEvent(1);
        pWriter.WriteShort(id);
        pWriter.WriteFloat(radius);
        WriteVector(pWriter, center);
        return pWriter;
    }

    public static ByteWriter RemoveZone(short id) {
        ByteWriter pWriter = StormEvent(2);
        pWriter.WriteShort(id);
        return pWriter;
    }

    public static ByteWriter ClearZones() => StormEvent(3);

    // SurvivalEvent 4: s_survival_event_reduce_safezone_ready with the seconds.
    public static ByteWriter ReduceReady(short seconds) {
        ByteWriter pWriter = StormEvent(4);
        pWriter.WriteShort(seconds);
        return pWriter;
    }

    // SurvivalEvent 5: s_survival_event_reduce_safezone_start.
    public static ByteWriter ReduceStart() => StormEvent(5);

    // SurvivalEvent 6: the storm bar's countdown (seconds) with the "ready" message.
    public static ByteWriter Countdown(short seconds) {
        ByteWriter pWriter = StormEvent(6);
        pWriter.WriteShort(seconds);
        return pWriter;
    }

    private static void WriteVector(ByteWriter pWriter, Vector3 value) {
        pWriter.WriteFloat(value.X);
        pWriter.WriteFloat(value.Y);
        pWriter.WriteFloat(value.Z);
    }
}
