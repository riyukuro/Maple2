using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class TriggerPacket {
    private enum Command : byte {
        Load = 2,
        Update = 3,
        CameraStart = 5,
        LookCenterCamera = 6,
        Ui = 9,
        EditScript = 12,
        Dialog = 15,
        LoadEffects = 16,
        AddEffect = 17,
        RemoveEffect = 18,
        SidePopup = 19,
        SetVisibleUi = 20,
        DuelHpBar = 21,
        ResetScript = 22,
        Effect1 = 23,
        Effect2 = 24,
        EffectStop = 25,
    }

    public static ByteWriter Load(IReadOnlyCollection<ITriggerObject> triggers) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteInt(triggers.Count);
        foreach (ITriggerObject trigger in triggers) {
            pWriter.WriteClass<ITriggerObject>(trigger);
        }

        return pWriter;
    }

    public static ByteWriter Update(ITriggerObject trigger) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Update);
        pWriter.WriteClass<ITriggerObject>(trigger);

        return pWriter;
    }

    public static ByteWriter CameraStart(ICollection<int> pathIds, bool unknown = false) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.CameraStart);
        pWriter.WriteByte((byte) pathIds.Count);
        foreach (int pathId in pathIds) {
            pWriter.WriteInt(pathId);
        }
        pWriter.WriteBool(unknown);

        return pWriter;
    }

    // The client reads a sub type after the command: 0 switches to the camera that looks at a centre point.
    public static ByteWriter LookCenterCamera(Vector3 center, int outerRadius, int innerRadius) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.WriteByte((byte) Command.LookCenterCamera);
        pWriter.WriteByte(0);
        pWriter.WriteFloat(center.X);
        pWriter.WriteFloat(center.Y);
        pWriter.WriteFloat(center.Z);
        pWriter.WriteInt(outerRadius);
        pWriter.WriteInt(innerRadius);

        return pWriter;
    }

    public static ByteWriter EditScript(string scriptXml) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.EditScript);
        pWriter.WriteString(scriptXml);

        return pWriter;
    }

    // Client 2525 WorldService::FUN_141bd42f0 (worldservice_1.cpp:7302) with the map reader at
    // worldservice_2.cpp:225: int count, count x (int key, then the same block AddEffect (17) carries:
    // int object id, text nif path, bool outline, float scale, float z rotation). The client keys its
    // table by the int and shows every effect on the object its block names.
    public static ByteWriter LoadEffects(IReadOnlyCollection<(int ObjectId, string NifPath, bool IsOutline, float Scale, float RotateZ)> effects) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.LoadEffects);
        pWriter.WriteInt(effects.Count);
        foreach ((int objectId, string nifPath, bool isOutline, float scale, float rotateZ) in effects) {
            pWriter.WriteInt(objectId);
            pWriter.WriteInt(objectId);
            pWriter.WriteString(nifPath);
            pWriter.WriteBool(isOutline);
            pWriter.WriteFloat(scale);
            pWriter.WriteFloat(rotateZ);
        }

        return pWriter;
    }

    public static ByteWriter AddEffect(int objectId, string nifPath, bool isOutline = false, float scale = 1f, float rotateZ = 0f) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.AddEffect);
        pWriter.WriteInt(objectId);
        pWriter.WriteString(nifPath);
        pWriter.WriteBool(isOutline);
        pWriter.WriteFloat(scale);
        pWriter.WriteFloat(rotateZ);

        return pWriter;
    }

    public static ByteWriter RemoveEffect(int objectId) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.RemoveEffect);
        pWriter.WriteInt(objectId);

        return pWriter;
    }

    public static ByteWriter SidePopupTalk(SideNpcTalkType type = SideNpcTalkType.Default, int duration = 0, string illustration = "", string voice = "", string script = "", string sound = "", string usm = "") {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.SidePopup);
        pWriter.Write<SideNpcTalkType>(type);
        pWriter.WriteInt(duration);
        pWriter.WriteString(usm);
        pWriter.WriteString(illustration);
        pWriter.WriteString(voice);
        pWriter.WriteString(sound);
        pWriter.WriteUnicodeString(script);

        return pWriter;
    }

    public static ByteWriter SetVisibleUi(bool visible, string uiNames) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.SetVisibleUi);
        pWriter.WriteBool(visible);
        pWriter.WriteString(uiNames);

        return pWriter;
    }

    public static ByteWriter DuelHpBar(bool set, int objectId = 0, int durationTick = 0, int hpStep = 0) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.DuelHpBar);
        pWriter.WriteBool(set);
        if (set) {
            pWriter.WriteInt(objectId);
            pWriter.WriteInt(durationTick);
            pWriter.WriteInt(hpStep);
        }

        return pWriter;
    }

    public static ByteWriter ResetScript(bool error = false) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.ResetScript);
        pWriter.WriteInt(error ? 1 : 0); // 0 => s_user_trigger_msg_rollback

        return pWriter;
    }

    public static ByteWriter Effect1(ICollection<int> objectIds, string effect) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Effect1);
        pWriter.WriteInt(objectIds.Count);
        foreach (int objectId in objectIds) {
            pWriter.WriteInt(objectId);
        }
        pWriter.WriteString(effect); // the client reads a byte string here

        return pWriter;
    }

    public static ByteWriter Effect2(ICollection<int> objectIds, string effect) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Effect2);
        pWriter.WriteInt(objectIds.Count);
        foreach (int objectId in objectIds) {
            pWriter.WriteInt(objectId);
        }
        pWriter.WriteString(effect); // the client reads a byte string here

        return pWriter;
    }

    public static ByteWriter EffectStop() {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.WriteByte((byte) Command.EffectStop);

        return pWriter;
    }


    private enum UiCommand : byte {
        Guide = 1,
        ShowSummary = 2,
        HideSummary = 3,
        StartMovie = 4,
        SkipMovie = 5,
        EmotionSequence = 7,
        EmotionLoop = 8,
        FaceEmotion = 9,
        PlayerRotation = 10,
        LookAt = 11,
        TypingGame = 12,
    }

    public static ByteWriter UiGuide(int eventId) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.Guide);
        pWriter.WriteInt(eventId);

        return pWriter;
    }

    public static ByteWriter UiShowSummary(int entityId, int textId, int duration) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.ShowSummary);
        pWriter.WriteInt(entityId);
        pWriter.WriteInt(textId);
        pWriter.WriteInt(duration);

        return pWriter;
    }

    public static ByteWriter UiHideSummary(int entityId) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.HideSummary);
        pWriter.WriteInt(entityId);

        return pWriter;
    }

    public static ByteWriter UiStartMovie(string fileName, int movieId) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.StartMovie);
        pWriter.WriteString(fileName);
        pWriter.WriteInt(movieId);

        return pWriter;
    }

    public static ByteWriter UiSkipMovie(int movieId) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.SkipMovie);
        pWriter.WriteInt(movieId);

        return pWriter;
    }

    public static ByteWriter UiEmotionSequence(ICollection<string> sequenceNames) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.EmotionSequence);
        pWriter.WriteInt(sequenceNames.Count);
        foreach (string sequenceName in sequenceNames) {
            pWriter.WriteUnicodeString(sequenceName);
        }

        return pWriter;
    }

    public static ByteWriter UiEmotionLoop(string sequenceName, int duration, bool loop) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.EmotionLoop);
        pWriter.WriteBool(loop);
        pWriter.WriteInt(duration);
        pWriter.WriteUnicodeString(sequenceName);

        return pWriter;
    }

    public static ByteWriter UiFaceEmotion(int objectId, string emotionName) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.FaceEmotion);
        pWriter.WriteInt(objectId);
        pWriter.WriteString(emotionName);

        return pWriter;
    }

    // Client FUN_1418642e0 (triggerservice.cpp): the local player turns to rotation.z; with immediate the whole
    // rotation is set at once instead of turning over time.
    public static ByteWriter UiPlayerRotation(in Vector3S rotation, bool immediate = false) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.PlayerRotation);
        pWriter.Write<Vector3S>(rotation);
        pWriter.WriteBool(immediate);

        return pWriter;
    }

    // Client FUN_1418643c0 (triggerservice.cpp): the local player looks at the point (PCBase::LookAtPoint);
    // with immediate it turns there at once.
    public static ByteWriter UiLookAt(in Vector3 point, bool immediate = false) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.LookAt);
        pWriter.Write<Vector3S>(point);
        pWriter.WriteBool(immediate);

        return pWriter;
    }

    public static ByteWriter UiTypingGame(int digits, int duration) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.TypingGame);
        pWriter.WriteInt(digits);
        pWriter.WriteInt(duration);

        return pWriter;
    }


    private enum DialogCommand : byte {
        Timer = 1,
        PvpObserver = 2,
        PvpRanking = 3,
        PvpTeamScore = 4,
        PvpPlayer = 5,
    }

    public static ByteWriter TimerDialog(TickTimer timer) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Dialog);
        pWriter.Write<DialogCommand>(DialogCommand.Timer);
        pWriter.WriteInt(timer.StartTick);
        pWriter.WriteInt(timer.Duration);
        pWriter.WriteBool(timer.AutoRemove);
        pWriter.WriteInt(timer.VerticalOffset);
        pWriter.WriteUnicodeString(timer.Type);

        return pWriter;
    }

    // One fighter of the observer dialog (UIPvPObserverDialog 0x4e96, CTriggerService::FUN_141862110 kinds 2 and 5 read by
    // FUN_140743ca0 / FUN_140743f50): the character, its health and its total, its wins (a byte); a duel adds the rating.
    public readonly record struct PvpFighter(long CharacterId, int Hp, int MaxHp, byte Wins, int Rating = 0);

    // Kind 2: the two fighters of a normal match (the Red Arena's face-off). A character id of 0 closes the dialog.
    public static ByteWriter PvpObserverDialog(int duration, PvpFighter first, PvpFighter second, bool autoRemove = true) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Dialog);
        pWriter.Write<DialogCommand>(DialogCommand.PvpObserver);
        pWriter.WriteInt(Environment.TickCount);
        pWriter.WriteInt(duration);
        pWriter.WriteBool(autoRemove);
        foreach (PvpFighter fighter in new[] { first, second }) {
            pWriter.WriteLong(fighter.CharacterId);
            pWriter.WriteInt(fighter.Hp);
            pWriter.WriteInt(fighter.MaxHp);
            pWriter.WriteByte(fighter.Wins);
        }
        return pWriter;
    }

    public static ByteWriter PvpRankingDialog(int duration, bool autoRemove = true) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Dialog);
        pWriter.Write<DialogCommand>(DialogCommand.PvpRanking);
        pWriter.WriteInt(Environment.TickCount);
        pWriter.WriteInt(duration);
        pWriter.WriteBool(autoRemove);

        return pWriter;
    }

    public static ByteWriter PvpTeamScoreDialog(int duration, bool autoRemove = true) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Dialog);
        pWriter.Write<DialogCommand>(DialogCommand.PvpTeamScore);
        pWriter.WriteInt(Environment.TickCount);
        pWriter.WriteInt(duration);
        pWriter.WriteBool(autoRemove);

        return pWriter;
    }

    // Kind 5: the two fighters of a duel (the rank duel), each with its rating, then the camera step of the dialog's
    // center button (MyPC::FUN_14152a640). A character id of 0 closes the dialog.
    public static ByteWriter PvpPlayerDialog(int duration, PvpFighter first, PvpFighter second, int centerCamera = 0, bool autoRemove = true) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Dialog);
        pWriter.Write<DialogCommand>(DialogCommand.PvpPlayer);
        pWriter.WriteInt(Environment.TickCount);
        pWriter.WriteInt(duration);
        pWriter.WriteBool(autoRemove);
        foreach (PvpFighter fighter in new[] { first, second }) {
            pWriter.WriteLong(fighter.CharacterId);
            pWriter.WriteInt(fighter.Hp);
            pWriter.WriteInt(fighter.MaxHp);
            pWriter.WriteByte(fighter.Wins);
            pWriter.WriteInt(fighter.Rating);
        }
        pWriter.WriteInt(centerCamera);
        return pWriter;
    }
}
