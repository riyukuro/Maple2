using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    public void CreateWidget(string type) {
        DebugLog("[CreateWidget] type:{Type}", type);
        var widget = type switch {
            "OxQuiz" => new OxQuizWidget(Field),
            "OxQuizUGC" => new OxQuizUgcWidget(Field),
            "SceneMovie" => new SceneMovieWidget(Field),
            "Guide" => new GuideWidget(Field),
            "SurvivalContents" => new SurvivalContentsWidget(Field),
            "TypingGame" => new TypingGameWidget(Field),
            "Round" => new RoundWidget(Field),
            "RainbowMonster" => new RainbowMonsterWidget(Field),
            _ => new Widget(Field),
        };
        Field.Widgets[type] = widget;
    }

    public void GuideEvent(int eventId) {
        DebugLog("[GuideEvent] eventId:{Id}", eventId);
        Broadcast(TriggerPacket.UiGuide(eventId));
    }

    public void HideGuideSummary(int entityId, int textId) {
        DebugLog("[HideGuideSummary] entityId:{EntityId}, textId:{TextId}", entityId, textId);
        Broadcast(TriggerPacket.UiHideSummary(entityId));
    }

    public void Notice(int type, string script, bool arg3) {
        DebugLog("[Notice] type:{Type}, script:{Script}, arg3:{Arg3}", type, script, arg3);
        Broadcast(NoticePacket.Notice(NoticePacket.Flags.Mint, new InterfaceText(script), 3000));
    }

    public void PlaySystemSoundByUserTag(int userTagId, string soundKey) {
        DebugLog("[PlaySystemSoundByUserTag] userTagId:{TagId}, soundKey:{SoundKey}", userTagId, soundKey);
        foreach (FieldPlayer player in Field.Players.Values) {
            if (player.TagId == userTagId) {
                player.Session.Send(SoundPacket.System(soundKey));
            }
        }
    }

    public void PlaySystemSoundInBox(string sound, params int[] boxIds) {
        DebugLog("[PlaySystemSoundInBox] sound:{Sound}, boxIds:{BoxIds}", sound, string.Join(", ", boxIds));
        if (boxIds.Length == 0) {
            Broadcast(SoundPacket.System(sound));
            return;
        }

        foreach (int boxId in boxIds) {
            foreach (FieldPlayer player in PlayersInBox(boxId)) {
                player.Session.Send(SoundPacket.System(sound));
            }
        }
    }

    // score_board_create opens the 0xE1 board of its type: "ShadowGauge" the boss gauge (with most points),
    // "ScoreBoardTopCenter" the top-center score dialog. The type "" (02020063_bf, 02020065_bf, 99999840,
    // 99999841) names no kind the client shows, so it only logs. set_score sends the points to the open board.
    public void ScoreBoardCreate(string type, string title, int maxScore) {
        Field.ScoreBoardScore = null;
        Field.ShadowGauge = null;
        byte? kind = ScoreBoard.Kind(type, maxScore);
        if (kind == null) {
            Field.ScoreBoardOpen = null;
            ErrorLog("[ScoreBoardCreate] type:{Type}, maxScore:{MaxScore}", type, maxScore);
            return;
        }

        DebugLog("[ScoreBoardCreate] type:{Type}, kind:{Kind}, maxScore:{MaxScore}", type, kind, maxScore);
        Field.ScoreBoardOpen = new ScoreBoard.Board(kind.Value, title, 0, maxScore);
        Field.Broadcast(ScoreBoard.OpenPacket(Field.ScoreBoardOpen));
    }

    public void ScoreBoardRemove() {
        Field.ScoreBoardScore = null;
        DebugLog("[ScoreBoardRemove] kind:{Kind}", Field.ScoreBoardOpen?.Kind);
        if (Field.ScoreBoardOpen != null) {
            Field.CloseScoreBoard();
        }
    }

    public void ScoreBoardSetScore(int score) {
        Field.ScoreBoardScore = score;
        DebugLog("[ScoreBoardSetScore] score:{Score}, kind:{Kind}", score, Field.ScoreBoardOpen?.Kind);
        if (Field.ScoreBoardOpen != null) {
            Field.ScoreBoardOpen = Field.ScoreBoardOpen with { Points = score };
            Field.Broadcast(ShadowExpeditionPacket.SetPoints(score));
        }
    }

    public void SetEventUiRound(int[] rounds, int vOffset, int arg3) {
        DebugLog("[SetEventUIRound] rounds:{Rounds}, arg3:{Arg3}, vOffset:{VOffset}", string.Join(", ", rounds), arg3, vOffset);
        int round = rounds.ElementAtOrDefault(0, 1);
        int maxRound = rounds.ElementAtOrDefault(1, 1);
        int minRound = rounds.ElementAtOrDefault(2, 1);
        if (minRound == maxRound) {
            return;
        }
        Broadcast(MassiveEventPacket.Round(round, maxRound, minRound, vOffset));
    }

    // The boxes a banner goes to: "!id" is everyone outside the box. An action with no box (most of them) or
    // box 0 goes to the whole field, as KMS2 sends it.
    public static (int[] In, int[] NotIn) EventUiBoxes(string[] boxIds) {
        var inBoxes = new List<int>();
        var notInBoxes = new List<int>();
        foreach (string boxId in boxIds) {
            string id = boxId.Trim();
            if (id.StartsWith('!')) {
                if (int.TryParse(id.AsSpan(1), out int notBoxId)) {
                    notInBoxes.Add(notBoxId);
                }
            } else if (int.TryParse(id, out int inBoxId)) {
                inBoxes.Add(inBoxId);
            }
        }
        if (inBoxes.Count == 0 && notInBoxes.Count == 0) {
            inBoxes.Add(0);
        }
        return (inBoxes.ToArray(), notInBoxes.ToArray());
    }

    public void SetEventUiScript(BannerType type, string script, int duration, string[] boxIds) {
        DebugLog("[SetEventUIScript] type:{Type}, script:{Script}, duration:{Duration}, boxIds:{BoxIds}", type, script, duration, string.Join(", ", boxIds));
        ByteWriter packet = MassiveEventPacket.Banner(type, script, duration);

        (int[] boxIdInts, int[] notBoxIdInts) = EventUiBoxes(boxIds);

        foreach (int notBoxId in notBoxIdInts) {
            foreach (FieldPlayer player in PlayersNotInBox(notBoxId)) {
                player.Session.Send(packet);
            }
        }

        foreach (int boxId in boxIdInts) {
            foreach (FieldPlayer player in PlayersInBox(boxId)) {
                player.Session.Send(packet);
            }
        }

    }

    public void SetEventUiCountdown(string script, int[] roundCountdown, string[] boxIds) {
        DebugLog("[SetEventUICountdown] script:{Script}, roundCountdown:{Countdown}, boxIds:{BoxIds}", script, string.Join(", ", roundCountdown), string.Join(", ", boxIds));
        if (roundCountdown.Length != 2) {
            return;
        }

        ByteWriter packet = MassiveEventPacket.Countdown(script, roundCountdown[0], roundCountdown[1]);
        foreach (string boxIdString in boxIds) {
            if (boxIdString.Contains('!')) {
                if (int.TryParse(boxIdString.Substring(1), out int notBoxId)) {
                    foreach (FieldPlayer player in PlayersNotInBox(notBoxId)) {
                        player.Session.Send(packet);
                    }
                }
                continue;
            }

            if (int.TryParse(boxIdString, out int boxId)) {
                if (boxId == 0) {
                    Broadcast(packet);
                    continue;
                }
                foreach (FieldPlayer player in PlayersInBox(boxId)) {
                    player.Session.Send(packet);
                }
            }
        }
    }

    public void SetVisibleUi(string[] uiNames, bool visible) {
        DebugLog("[SetVisibleUI] uiNames:{UiNames}, visible:{Visible}", string.Join(", ", uiNames), visible);
        Broadcast(TriggerPacket.SetVisibleUi(visible, string.Join(",", uiNames)));
    }

    public void ShowCountUi(string text, int stage, int count, int soundType) {
        DebugLog("[ShowCountUI] text:{Text}, stage:{Stage}, count:{Count}, soundType:{SoundType}", text, stage, count, soundType);
        Broadcast(MassiveEventPacket.Countdown(text, stage, count, soundType));
    }

    // "notice" is the massive event typed message (MassiveEvent op 2) of subtype 6: the client resolves the text as a
    // server string, turns "\\n" into a line break and shows it as a notice (UIMassiveEvent::ShowMessage). It goes to
    // the players of the user tag when one is named, and of the box (or outside it) when one is named.
    public void ShowEventResult(string type, string text, int duration, int userTagId, int boxId, bool isOutSide) {
        DebugLog("[ShowEventResult] type:{Type}, text:{Text}, duration:{Duration}, userTagId:{TagId}, boxId:{BoxId}, isOutSide:{IsOutside}",
            type, text, duration, userTagId, boxId, isOutSide);
        if (type != "notice") {
            return;
        }

        TriggerBox? box = null;
        if (boxId > 0 && !Objects.Boxes.TryGetValue(boxId, out box)) {
            return;
        }

        ByteWriter packet = MassiveEventPacket.Banner(BannerType.Text, text, duration);
        foreach (FieldPlayer player in Field.Players.Values) {
            if (ShowsEventResult(player.TagId, box?.Contains(player.Position), userTagId, isOutSide)) {
                player.Session.Send(packet);
            }
        }
    }

    // inBox is null when the action names no box.
    internal static bool ShowsEventResult(int playerTagId, bool? inBox, int userTagId, bool isOutSide) {
        return (userTagId <= 0 || playerTagId == userTagId) && (inBox == null || inBox.Value != isOutSide);
    }

    public void ShowGuideSummary(int entityId, int textId, int duration) {
        DebugLog("[ShowGuideSummary] entityId:{EntityId}, textId:{TextId}, duration:{Duration}", entityId, textId, duration);
        Broadcast(TriggerPacket.UiShowSummary(entityId, textId, duration));
    }

    // Client UIMassiveEvent mode 7: a final round shows the event window's final-round banner (onFinalRound), any
    // other round its round banner (onRound); neither is shown when the third int is 0.
    public void ShowRoundUi(int round, int duration, bool isFinalRound) {
        DebugLog("[ShowRoundUI] round:{Round}, duration:{Duration}, isFinalRound:{IsFinalRound}", round, duration, isFinalRound);
        Broadcast(MassiveEventPacket.StartRound(round, duration, isFinalRound));
    }

    public void SideNpcTalk(int npcId, string illust, int duration, string script, string voice) {
        DebugLog("[SideNpcTalkBottom] npcId:{NpcId}, illust:{Illustration}, duration:{Duration}, script:{Script}, voice:{Voice}",
            npcId, illust, duration, script, voice);
        Broadcast(TriggerPacket.SidePopupTalk(SideNpcTalkType.Default, duration, illust, voice, script));
    }

    public void SideNpcTalkBottom(int npcId, string illust, int duration, string script) {
        DebugLog("[SideNpcTalkBottom] npcId:{NpcId}, illust:{Illustration}, duration:{Duration}, script:{Script}", npcId, illust, duration, script);
        Broadcast(TriggerPacket.SidePopupTalk(SideNpcTalkType.TalkBottom, duration, illust, string.Empty, script));
    }

    public void SideNpcMovie(string usm, int duration) {
        DebugLog("[SideNpcMovie] usm:{Usm}, duration:{Duration}", usm, duration);
        Broadcast(TriggerPacket.SidePopupTalk(SideNpcTalkType.Movie, duration, usm: usm));
    }

    public void SideNpcCutin(string illust, int duration) {
        DebugLog("[SideNpcMovie] illust:{Illustration}, duration:{Duration}", illust, duration);
        Broadcast(TriggerPacket.SidePopupTalk(SideNpcTalkType.CutIn, duration, illust));
    }

    public void WidgetAction(string type, string func, string widgetArg, string desc, int widgetArgNum) {
        DebugLog("[WidgetAction] type:{Type}, func:{Func}, widgetArg:{Args}, desc:{Desc}, widgetArgNum:{ArgNum}", type, func, widgetArg, desc, widgetArgNum);
        if (!Field.Widgets.TryGetValue(type, out Widget? widget)) {
            return;
        }

        DebugLog("Calling Action on widget of type {Type}", widget.GetType().Name);
        widget.Action(func, widgetArgNum, widgetArg);
    }

    #region Conditions
    public bool WidgetValue(string type, string widgetName, string widgetArg, bool negate, string desc = "") {
        DebugLog("[WidgetValue] type:{Type}, widgetName:{Name}, desc:{Desc}", type, widgetName, desc);
        if (!Field.Widgets.TryGetValue(type, out Widget? widget)) {
            return negate;
        }

        bool result = widget.Check(widgetName, widgetArg);
        if (negate) {
            return !result;
        }
        return result;
    }
    #endregion
}
