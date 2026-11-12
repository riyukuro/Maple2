using System.Net;
using System.Text.RegularExpressions;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

// The chat spam restriction of one player. The client has no check of its own: the values are the constants table's
// (FastChat_*, SameChat_*, RestrictChat_AddRestrictTime, AccumWarning_AddRestrictTime, RestrictWarning_ReleaseTime,
// milliseconds), and the texts s_chat_restrict_* name the restricted lines: normal, channel, world and whisper chat.
// Each text takes the seconds the player has to wait. A constant of 0 turns its check off.
public sealed class ChatRestriction {
    public readonly record struct Result(bool Allowed, StringCode Notice = default, int Seconds = 0);

    private readonly object mutex = new();
    private readonly Queue<(long Time, string Text)> history = new();
    private long restrictedUntil;

    // The line types the restriction covers.
    public static bool Covers(ChatType type) {
        return type is ChatType.Normal or ChatType.WhisperTo or ChatType.World or ChatType.Channel;
    }

    // A line at now (milliseconds of any steady clock). A refused line goes nowhere; the notice tells the player why.
    // - While restricted: the restriction grows by RestrictChat_AddRestrictTime (s_chat_restrict_addtime).
    // - The SameChat_CheckCount-th equal line within SameChat_CheckTime restricts for SameChat_RestrictTime
    //   (s_chat_restrict_samechat); the FastChat_CheckCount-th line within FastChat_CheckTime for FastChat_RestrictTime
    //   (s_chat_restrict_fastchat).
    // - A restriction that starts within RestrictWarning_ReleaseTime after the last one ended is the repeated warning:
    //   AccumWarning_AddRestrictTime (s_chat_restrict_accumwarning).
    public Result Check(ConstantsTable constants, string text, long now) {
        lock (mutex) {
            if (now < restrictedUntil) {
                restrictedUntil += Math.Max(0, constants.RestrictChat_AddRestrictTime);
                return new Result(false, StringCode.s_chat_restrict_addtime, Seconds(restrictedUntil - now));
            }

            long keep = Math.Max(constants.SameChat_CheckTime, constants.FastChat_CheckTime);
            while (history.Count > 0 && now - history.Peek().Time >= keep) {
                history.Dequeue();
            }
            history.Enqueue((now, text));

            StringCode notice;
            long duration;
            if (Repeated(constants.SameChat_CheckTime, constants.SameChat_CheckCount, now, text)) {
                notice = StringCode.s_chat_restrict_samechat;
                duration = constants.SameChat_RestrictTime;
            } else if (Repeated(constants.FastChat_CheckTime, constants.FastChat_CheckCount, now, null)) {
                notice = StringCode.s_chat_restrict_fastchat;
                duration = constants.FastChat_RestrictTime;
            } else {
                return new Result(true);
            }

            history.Clear();
            if (duration <= 0) {
                return new Result(true);
            }
            if (restrictedUntil > 0 && constants.AccumWarning_AddRestrictTime > 0 && now - restrictedUntil < constants.RestrictWarning_ReleaseTime) {
                notice = StringCode.s_chat_restrict_accumwarning;
                duration = constants.AccumWarning_AddRestrictTime;
            }
            restrictedUntil = now + duration;
            return new Result(false, notice, Seconds(duration));
        }
    }

    // text null counts every line.
    private bool Repeated(int window, int count, long now, string? text) {
        if (window <= 0 || count <= 0) {
            return false;
        }
        return history.Count(line => now - line.Time < window && (text == null || line.Text == text)) >= count;
    }

    private static int Seconds(long milliseconds) => (int) ((milliseconds + 999) / 1000);

    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.Compiled);

    // The text the player typed. The client caps its input box at MaxChatLength characters (SetMaxChars,
    // uimessenger.cpp:775) and sends a line with item links as html, so the tags and entities are not counted.
    public static int VisibleLength(string message) {
        return WebUtility.HtmlDecode(Tag.Replace(message, string.Empty)).Length;
    }

    // The line types whose text the client takes from the input box. The client builds the others itself, with no
    // length cap: the whisper reject notice, the party and chaos DPS reports it sends as party lines (onNotice,
    // uipartydialog.cpp:1342-1367, uichaosdpsdialog.cpp:238-262) and the mini game lines (statemicrogame.cpp:457).
    public static bool LimitsLength(ChatType type) {
        return type is not (ChatType.WhisperReject or ChatType.Party or ChatType.MicroGame);
    }

    // Lines longer than the input box takes did not come from its box: they are dropped. 0 is no limit.
    public static bool TooLong(ConstantsTable constants, string message) {
        return constants.MaxChatLength > 0 && VisibleLength(message) > constants.MaxChatLength;
    }

    // The cap on the lines the client builds itself (the types LimitsLength leaves out), counted with their html.
    // The longest is the DPS report of a full party: the prefix, 10 rows of " [<b>n. name</b> - meter]" and the
    // total time come to about 650 characters with the English strings (s_partydps_notice_history_*); this is three
    // times that.
    public const int ClientBuiltMaxLength = 2000;

    // A line the client could not have sent: past the input box for a typed type, past ClientBuiltMaxLength for
    // the others.
    public static bool Rejects(ConstantsTable constants, ChatType type, string message) {
        return LimitsLength(type) ? TooLong(constants, message) : message.Length > ClientBuiltMaxLength;
    }
}
