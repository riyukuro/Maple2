using System.Diagnostics;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class HomeActionPacket {
    private enum HomeActionCommand : byte {
        Alarm = 4,
        Survey = 5,
        PortalCube = 6,
        Ball = 7,
        UgcOx = 10,
        Roll = 11,
        NoticeCube = 12,
    }

    private enum SurveyCommand : byte {
        Message = 0,
        Progress = 1,
        Question = 2,
        AddOption = 3,
        Start = 4,
        Answer = 5,
        End = 6,
    }

    // HomeAction 10, the home OX quiz event: the sub-types the client reads (FUN_140dff600).
    private enum UgcOxCommand : byte {
        Fees = 0,
        Promote = 1,
        Confirm = 2,
        Created = 4,
        Move = 5,
        HostDialog = 12,
        Stage = 13,
    }

    // The steps sub 13 carries: Welcome opens the welcome dialog (with the start button for the host), Round the
    // round's dialogs; any other step closes them.
    public enum UgcOxStageStep : byte {
        Close = 0,
        Welcome = 10,
        Round = 11,
    }

    // The modes of sub 12 with an effect in the client: InputQuiz enables the question input, SelectAnswer opens
    // the O/X select dialog.
    public enum UgcOxHostMode : byte {
        InputQuiz = 1,
        SelectAnswer = 3,
    }

    private enum BallCommand : byte {
        Add = 0,
        Remove = 1,
        Update = 2,
        Hit = 3,
    }

    public static ByteWriter SendCubePortalSettings(PlotCube cube, List<string> otherPortalsNames) {
        Debug.Assert(cube.Interact?.PortalSettings != null, nameof(cube.Interact.PortalSettings) + " != null");

        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.PortalCube);
        pWriter.WriteByte();
        pWriter.Write<Vector3B>(cube.Position);
        pWriter.WriteClass<CubePortalSettings>(cube.Interact.PortalSettings);
        pWriter.WriteInt(otherPortalsNames.Count);
        foreach (string portalName in otherPortalsNames) {
            pWriter.WriteUnicodeString(portalName);
        }

        return pWriter;
    }

    public static ByteWriter SendCubeNoticeSettings(PlotCube cube, bool editing = false) {
        Debug.Assert(cube.Interact?.NoticeSettings != null, nameof(cube.Interact.NoticeSettings) + " != null");

        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.NoticeCube);
        pWriter.WriteBool(editing);
        pWriter.Write<Vector3B>(cube.Position);
        pWriter.WriteClass<CubeNoticeSettings>(cube.Interact.NoticeSettings);

        return pWriter;
    }

    public static ByteWriter HostAlarm(string message) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Alarm);
        pWriter.WriteByte();
        pWriter.WriteInt();
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    public static ByteWriter SurveyMessage() {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Survey);
        pWriter.Write<SurveyCommand>(SurveyCommand.Message);

        return pWriter;
    }

    public static ByteWriter SurveyProgress(HomeSurvey survey) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Survey);
        pWriter.Write<SurveyCommand>(SurveyCommand.Progress);
        pWriter.WriteUnicodeString(survey.Question);
        pWriter.WriteBool(survey.Public);
        pWriter.WriteByte((byte) survey.Options.Count);
        foreach (string option in survey.Options.Keys) {
            pWriter.WriteUnicodeString(option);
        }

        return pWriter;
    }

    public static ByteWriter SurveyQuestion(HomeSurvey survey) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Survey);
        pWriter.Write<SurveyCommand>(SurveyCommand.Question);
        pWriter.WriteUnicodeString(survey.Question);
        pWriter.WriteBool(survey.Public);

        return pWriter;
    }

    public static ByteWriter SurveyAddOption(HomeSurvey survey, bool success = true) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Survey);
        pWriter.Write<SurveyCommand>(SurveyCommand.AddOption);
        pWriter.WriteUnicodeString(survey.Question);
        pWriter.WriteBool(survey.Public);
        pWriter.WriteUnicodeString(survey.Options.Keys.Last());
        pWriter.WriteBool(success);

        return pWriter;
    }

    public static ByteWriter SurveyStart(HomeSurvey survey) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Survey);
        pWriter.Write<SurveyCommand>(SurveyCommand.Start);
        pWriter.WriteLong(survey.OwnerId);
        pWriter.WriteLong(survey.Id);
        pWriter.WriteBool(survey.Public);
        pWriter.WriteUnicodeString(survey.Question);
        pWriter.WriteByte((byte) survey.Options.Count);
        foreach (string option in survey.Options.Keys) {
            pWriter.WriteUnicodeString(option);
        }

        return pWriter;
    }

    public static ByteWriter SurveyAnswer(string name) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Survey);
        pWriter.Write<SurveyCommand>(SurveyCommand.Answer);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    public static ByteWriter SurveyEnd(HomeSurvey survey) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Survey);
        pWriter.Write<SurveyCommand>(SurveyCommand.End);
        pWriter.WriteLong(survey.Id);
        pWriter.WriteBool(survey.Public);
        pWriter.WriteUnicodeString(survey.Question);
        pWriter.WriteByte((byte) survey.Options.Count);
        foreach (KeyValuePair<string, List<string>> option in survey.Options) {
            pWriter.WriteUnicodeString(option.Key);
            pWriter.WriteByte((byte) option.Value.Count);

            if (!survey.Public) {
                continue;
            }

            foreach (string name in option.Value) {
                pWriter.WriteUnicodeString(name);
            }
        }

        pWriter.WriteByte((byte) survey.AvailableCharacters.Count);
        if (!survey.Public) {
            return pWriter;
        }

        foreach (string name in survey.AvailableCharacters) {
            pWriter.WriteUnicodeString(name);
        }

        return pWriter;
    }

    public static ByteWriter Roll(Character character, int number) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Roll);
        // The client (CSceneStateGame::OnHomeAction 11) drops the line when the roller is on its block list, looked up
        // by character id (CBuddyManager::FUN_140ae51b0, the buddy map keyed by the entry's character id), as for chat.
        pWriter.WriteLong(character.Id);
        pWriter.WriteUnicodeString(character.Name);
        pWriter.WriteByte();
        pWriter.WriteByte(1);
        pWriter.WriteInt(1);
        pWriter.WriteInt(StringCodeMap.ToClient(StringCode.s_ugcmap_fun_roll)); // the client's string id, not the enum value
        pWriter.WriteInt(2); // number of strings
        pWriter.WriteUnicodeString(character.Name);
        pWriter.WriteUnicodeString(number.ToString());

        return pWriter;
    }

    public static ByteWriter AddBall(FieldGuideObject guideObject) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Ball);
        pWriter.Write<BallCommand>(BallCommand.Add);
        pWriter.WriteInt(guideObject.ObjectId);
        pWriter.WriteLong(guideObject.CharacterId);
        pWriter.Write<Vector3>(guideObject.Position);
        guideObject.Value.WriteTo(pWriter);

        return pWriter;
    }

    public static ByteWriter RemoveBall(FieldGuideObject guideObject) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Ball);
        pWriter.Write<BallCommand>(BallCommand.Remove);
        pWriter.WriteInt(guideObject.ObjectId);

        return pWriter;
    }

    public static ByteWriter UpdateBall(FieldGuideObject guideObject, Vector3 velocity1, Vector3 velocity2) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Ball);
        pWriter.Write<BallCommand>(BallCommand.Update);
        pWriter.WriteInt(guideObject.ObjectId);
        pWriter.WriteLong(guideObject.CharacterId);
        pWriter.Write<Vector3>(guideObject.Position);
        pWriter.Write<Vector3>(velocity1);
        pWriter.Write<Vector3>(velocity2);

        return pWriter;
    }

    public static ByteWriter HitBall(FieldGuideObject guideObject, Vector3 force, Vector3 direction) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.Ball);
        pWriter.Write<BallCommand>(BallCommand.Hit);
        pWriter.WriteInt(guideObject.ObjectId);
        pWriter.WriteLong(guideObject.CharacterId);
        pWriter.Write<Vector3>(force);
        pWriter.Write<Vector3>(direction);

        return pWriter;
    }

    // Sub 0: the fees the create dialog offers; the client takes the first one as the host fee text.
    public static ByteWriter UgcOxFees(IReadOnlyList<int> fees) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.WriteByte((byte) HomeActionCommand.UgcOx);
        pWriter.WriteByte((byte) UgcOxCommand.Fees);
        pWriter.WriteByte((byte) fees.Count);
        foreach (int fee in fees) {
            pWriter.WriteInt(fee);
        }

        return pWriter;
    }

    // Sub 1: the promote tooltip and its cooldown. The leading byte has no effect the client shows (U4); 0 here.
    public static ByteWriter UgcOxPromote(int promoted, int cooldownLeftMs, int total, int cooldownMs) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.WriteByte((byte) HomeActionCommand.UgcOx);
        pWriter.WriteByte((byte) UgcOxCommand.Promote);
        pWriter.WriteByte();
        pWriter.WriteInt(promoted);
        pWriter.WriteInt(cooldownLeftMs);
        pWriter.WriteInt(total);
        pWriter.WriteInt(cooldownMs);

        return pWriter;
    }

    // Sub 2: the "create the portal?" box; the client answers it with c2s 3.
    public static ByteWriter UgcOxConfirm(string hostName, long fee, int max, int share) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.WriteByte((byte) HomeActionCommand.UgcOx);
        pWriter.WriteByte((byte) UgcOxCommand.Confirm);
        pWriter.WriteUnicodeString(hostName);
        pWriter.WriteLong(fee);
        pWriter.WriteInt(max);
        pWriter.WriteInt(share);

        return pWriter;
    }

    // Sub 4: the event is made; the client enables the owner bar and shows s_ugcox_create_complete with the fee.
    public static ByteWriter UgcOxCreated(long fee, int promoted, int cooldownLeftMs, int total, int cooldownMs) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.WriteByte((byte) HomeActionCommand.UgcOx);
        pWriter.WriteByte((byte) UgcOxCommand.Created);
        pWriter.WriteLong(fee);
        pWriter.WriteInt(promoted);
        pWriter.WriteInt(cooldownLeftMs);
        pWriter.WriteInt(total);
        pWriter.WriteInt(cooldownMs);

        return pWriter;
    }

    // Sub 5: the way into the stadium, a portal of the home. The host (hostAccountId is its own account id) moves at
    // once; anyone else gets the fee box first. Both then send RequestMoveField mode 0 with this map and portal.
    public static ByteWriter UgcOxMove(int mapId, int portalId, long hostAccountId, string hostName, long fee, int max, int share) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.WriteByte((byte) HomeActionCommand.UgcOx);
        pWriter.WriteByte((byte) UgcOxCommand.Move);
        pWriter.WriteInt(mapId);
        pWriter.WriteInt(portalId);
        pWriter.WriteLong(hostAccountId);
        pWriter.WriteUnicodeString(hostName);
        pWriter.WriteLong(fee);
        pWriter.WriteInt(max);
        pWriter.WriteInt(share);

        return pWriter;
    }

    // Sub 12: the host dialog's mode; count above 1 enables its finish button.
    public static ByteWriter UgcOxHostDialog(UgcOxHostMode mode, byte count) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.WriteByte((byte) HomeActionCommand.UgcOx);
        pWriter.WriteByte((byte) UgcOxCommand.HostDialog);
        pWriter.WriteByte((byte) mode);
        pWriter.WriteByte(count);

        return pWriter;
    }

    // Sub 13: the stage everyone in the stadium sees (the prize dialog, the round), the host's dialogs by step.
    public static ByteWriter UgcOxStage(long hostAccountId, UgcOxStageStep step, byte round, long prize1, long prize2) {
        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.WriteByte((byte) HomeActionCommand.UgcOx);
        pWriter.WriteByte((byte) UgcOxCommand.Stage);
        pWriter.WriteLong(hostAccountId);
        pWriter.WriteByte((byte) step);
        pWriter.WriteByte(round);
        pWriter.WriteLong(prize1);
        pWriter.WriteLong(prize2);

        return pWriter;
    }
}
