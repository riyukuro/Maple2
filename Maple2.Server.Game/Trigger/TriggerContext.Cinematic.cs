using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    public void AddCinematicTalk(int npcId, string illustId, string msg, int duration, Align align, int delayTick) {
        DebugLog("[AddCinematicTalk] npcId:{NpcId}, illustId:{IllustrationId}, msg:{Message}, duration:{Duration}, align:{Align}, delayTick:{Delay}",
            npcId, illustId, msg, duration, align, delayTick);
        Broadcast(CinematicPacket.Talk(npcId, illustId, msg, duration, align));
    }

    public void RemoveCinematicTalk() {
        DebugLog("[RemoveCinematicTalk]");
        Broadcast(CinematicPacket.RemoveTalk());
    }

    public void CloseCinematic() {
        DebugLog("[CloseCinematic]");
        // guessing
        Broadcast(CinematicPacket.HideUi());
    }

    public void PlaySceneMovie(string fileName, int movieId, string skipType) {
        DebugLog("[PlaySceneMovie] fileName:{FileName}, movieId:{MovieId}, skipType:{SkipType}", fileName, movieId, skipType);
        Broadcast(TriggerPacket.UiStartMovie(fileName, movieId));
    }

    public void SetCinematicIntro(string text) {
        DebugLog("[SetCinematicIntro] text:{Text}", text);
        Broadcast(CinematicPacket.Intro(text));
    }

    public void SetOnetimeEffect(int id, bool enabled, string path) {
        DebugLog("[SetOnetimeEffect] id:{Id}, enabled:{Enabled}, path:{Path}", id, enabled, path);
        Broadcast(CinematicPacket.OneTimeEffect(id, enabled, path));
    }

    public void SetCinematicUi(int type, string script, bool arg3) {
        DebugLog("[SetCinematicUI] type:{Type}, script:{Script}, arg3:{Arg3}", type, script, arg3);
        switch (type) {
            case 0:
                Broadcast(CinematicPacket.ToggleUi(false));
                return;
            case 1:
                Broadcast(CinematicPacket.ToggleUi(true));
                return;
            case 2:
                Broadcast(CinematicPacket.HideUi());
                return;
            case 3:
                Broadcast(CinematicPacket.LetterboxTransition(script));
                return;
            case 4:
                Broadcast(CinematicPacket.FadeTransition(script));
                return;
            case 5:
                Broadcast(CinematicPacket.HorizontalTransition(script));
                return;
            case 6:
                Broadcast(CinematicPacket.VerticalTransition(script));
                return;
            case 7: // many
                return;
            case 9:
                Broadcast(CinematicPacket.Opening(script, arg3));
                return;
        }
    }

    public void SetSceneSkip(string state, string nextState) {
        WarnLog("[SetSceneSkip] state:{State}, nextState:{NextState}", state, nextState);
        skipState = owner.GetState(state);
        Broadcast(skipState == null ? CinematicPacket.ClearSkip() : CinematicPacket.SetSkipScene(nextState));
    }

    public void SetSkip(string state) {
        WarnLog("[SetSkip] state:{State}", state);
        // KR scripts end a skippable part with an empty state (1002 of 2258 set_skip rows, 359 of 736 set_scene_skip
        // rows): with no state to skip to, the skip key goes away.
        skipState = owner.GetState(state);
        Broadcast(skipState == null ? CinematicPacket.ClearSkip() : CinematicPacket.SetSkipState(""));
    }

    public void AddBalloonTalk(int spawnId, string msg, int duration, int delayTick, int npcId) {
        DebugLog("[AddBalloonTalk] spawnId:{SpawnId}, msg:{Message}, duration:{Duration}, delayTick:{Delay}, npcId:{NpcId}", spawnId, msg, duration, delayTick, npcId);
        if (spawnId == 0) {
            SendOwnBalloon(msg, duration, delayTick);
            return;
        }

        FieldNpc? fieldNpc = SpawnPointNpcs(Field.Npcs.Values, Field.Mobs.Values, npc => npc.SpawnPointId, spawnId).FirstOrDefault();
        if (fieldNpc == null) {
            ErrorLog("[AddBalloonTalk] No NPC with spawnId:{SpawnId} found in field", spawnId);
            return;
        }

        Broadcast(CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.Trigger, fieldNpc.ObjectId, msg, duration, delayTick));
    }

    public void RemoveBalloonTalk(int spawnId) {
        DebugLog("[RemoveBalloonTalk] spawnId:{SpawnId}", spawnId);
        IEnumerable<FieldNpc> talkers = spawnId == 0
            ? Field.Npcs.Values.Concat(Field.Mobs.Values)
            : SpawnPointNpcs(Field.Npcs.Values, Field.Mobs.Values, npc => npc.SpawnPointId, spawnId);
        foreach (FieldNpc npc in talkers) {
            Broadcast(CinematicPacket.RemoveBalloonTalk(npc.ObjectId));
        }
        if (spawnId == 0) {
            // Chosen here: spawn 0 also clears each player's own balloon (see SendOwnBalloon).
            ForEachPlayer(Field.Players.Values, player => player.ObjectId, (player, packet) => player.Session.Send(packet),
                CinematicPacket.RemoveBalloonTalk);
        }
    }

    // Chosen here: a balloon on spawn id 0 is the player's own character. The client keys a balloon by the object
    // id in the packet (UICutSceneCinematic 0x141909eb0) and runs no trigger itself, so each player in the field gets
    // the balloon over its own character, sent to its own session. Before, everyone saw it over the first player.
    private void SendOwnBalloon(string script, int duration, int delay) {
        ForEachPlayer(Field.Players.Values, player => player.ObjectId, (player, packet) => player.Session.Send(packet),
            objectId => CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.Trigger, objectId, script, duration, delay));
    }

    internal static void ForEachPlayer<T>(IEnumerable<T> players, Func<T, int> objectId, Action<T, ByteWriter> send, Func<int, ByteWriter> packet) {
        foreach (T player in players) {
            send(player, packet(objectId(player)));
        }
    }

    // The npcs and then the monsters of a spawn point: a balloon talk goes to either (the Colosseum's round boss
    // is a monster).
    public static IEnumerable<T> SpawnPointNpcs<T>(IEnumerable<T> npcs, IEnumerable<T> mobs, Func<T, int> spawnPointId, int spawnId) =>
        npcs.Concat(mobs).Where(npc => spawnPointId(npc) == spawnId);

    public void ShowCaption(
        string type,
        string title,
        string script,
        Align align,
        float offsetRateX,
        float offsetRateY,
        int duration,
        float scale
    ) {
        DebugLog("[ShowCaption] type:{Type}, title:{Title}, script:{Script} align:{Align}, offsetRateX:{OffsetRateX}, offsetRateY:{OffsetRateY}, duration:{Duration}, scale:{Scale}",
            type, title, script, align, offsetRateX, offsetRateY, duration, scale);
        // Not sure if this is correct, but I only found packets where NameCaption and offsetRateX and offsetRateY are both not 0 or both 0. even though in the trigger only one of them is set to a value.
        if (type == "NameCaption" && (offsetRateX == 0 || offsetRateY == 0)) {
            offsetRateX = offsetRateY = 0;
        }
        Broadcast(CinematicPacket.Caption(type, title, script, align, offsetRateX, offsetRateY, duration, scale));
    }
}
