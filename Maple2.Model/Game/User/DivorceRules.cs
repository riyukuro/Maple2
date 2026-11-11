using Maple2.Model.Enum;
using Maple2.Model.Error;

namespace Maple2.Model.Game;

// The rules of a divorce. The checks follow the client's own (WeddingProxy, weddingproxy.cpp:600-703), which the
// client never calls: it only shows the result byte the server sends.
//
// The client knows two filed states, 3 and 4. WeddingProxy::IsPartner (weddingproxy.cpp:639) names the filer:
// in state 3 the groom, the first partner of the wedding packet, and in state 4 the bride, the second.
public static class DivorceRules {
    public const long DaySeconds = 86400;

    // The npc script function names (server table scriptFunction, attribute divorce).
    public const string FunctionMutual = "mutual";
    public const string FunctionFile = "individual";
    public const string FunctionAgree = "individualAgree";
    public const string FunctionCancel = "individualcancel";
    // The npc script condition values (server table scriptCondition, attribute coolingOff).
    public const string CoolingOffRequest = "request";
    public const string CoolingOffResponse = "response";
    // The answers of the client's box for a mutual divorce (WeddingManager::OnDivorceAnswer, weddingmanager.cpp:478).
    public const short AnswerYes = 13;
    public const short AnswerNo = 14;

    public static bool IsFiled(MaritalStatus status) {
        return status is MaritalStatus.DivorceFiledByGroom or MaritalStatus.DivorceFiledByBride;
    }

    // The filed state for a filer, given who is the first partner.
    public static MaritalStatus FiledStatus(long filerId, long firstId) {
        return filerId == firstId ? MaritalStatus.DivorceFiledByGroom : MaritalStatus.DivorceFiledByBride;
    }

    // The character that filed; 0 when nothing is filed.
    public static long FilerId(MaritalStatus status, long firstId, long secondId) {
        return status switch {
            MaritalStatus.DivorceFiledByGroom => firstId,
            MaritalStatus.DivorceFiledByBride => secondId,
            _ => 0,
        };
    }

    // The state as seen with the two partners in the other order. A character is sent its marriage with the
    // partner first and itself second, so for the first stored partner the two filed states change places.
    public static MaritalStatus Swapped(MaritalStatus status) {
        return status switch {
            MaritalStatus.DivorceFiledByGroom => MaritalStatus.DivorceFiledByBride,
            MaritalStatus.DivorceFiledByBride => MaritalStatus.DivorceFiledByGroom,
            _ => status,
        };
    }

    // Client IsOlderThanDays (commonfiles/timeutil.cpp:541): more than that many days of 86400 s have passed.
    public static bool OlderThanDays(long time, int days, long now) {
        return time + days * DaySeconds < now;
    }

    // Client WeddingProxy::GetDivorceTime (weddingproxy.cpp:658): the filing time plus the cooling-off days.
    public static long CoolingOffEnd(long filingTime, int coolingOffDays) {
        return filingTime + coolingOffDays * DaySeconds;
    }

    // A filed divorce whose cooling off has ended: it is then complete without the other partner's word.
    public static bool CoolingOffOver(MaritalStatus status, long filingTime, int coolingOffDays, long now) {
        return IsFiled(status) && now > CoolingOffEnd(filingTime, coolingOffDays);
    }

    // Client WeddingProxy::IsProposeCooldownOver (weddingproxy.cpp:697): divorced, and the propose cooltime
    // since the divorce has passed. The marriage is then over for good.
    public static bool ProposeCooltimeOver(MaritalStatus status, long divorceTime, int cooltimeDays, long now) {
        return status == MaritalStatus.Divorced && OlderThanDays(divorceTime, cooltimeDays, now);
    }

    // The first time of the wedding packet (client m_n28): the client counts the cooling off and the propose
    // cooltime from it in the states 3 to 5, and the promise limit in state 1.
    public static long StateTime(MaritalStatus status, long creationTime, long divorceTime) {
        return IsFiled(status) || status == MaritalStatus.Divorced ? divorceTime : creationTime;
    }

    // Filing alone (function individual). Client check at weddingproxy.cpp:600: married (0x2d), for more than
    // the required days (0x2e). The fee comes after it (0x2f).
    public static WeddingError CheckFile(MaritalStatus status, long marriageTime, int requireDays, long meso, long fee, long now) {
        if (status != MaritalStatus.Married) {
            return WeddingError.s_wedding_result_invaild_state_divorce;
        }
        if (!OlderThanDays(marriageTime, requireDays, now)) {
            return WeddingError.s_wedding_result_lack_marriage_date;
        }
        if (meso < fee) {
            return WeddingError.s_wedding_result_lack_meso_divorce;
        }
        return WeddingError.none;
    }

    // Agreeing to a filed divorce (function individualAgree, and Wedding 7 with late 0). Client check at
    // weddingproxy.cpp:609: not the filer (0x31), a divorce is filed (0x2d). After the cooling off it is too
    // late to agree (0x30).
    public static WeddingError CheckAgree(MaritalStatus status, bool isFiler, long filingTime, int coolingOffDays, long now) {
        if (isFiler) {
            return WeddingError.s_wedding_result_invalid_divorce_agree_user;
        }
        if (!IsFiled(status)) {
            return WeddingError.s_wedding_result_invaild_state_divorce;
        }
        if (CoolingOffOver(status, filingTime, coolingOffDays, now)) {
            return WeddingError.s_wedding_result_late_divorce_agree;
        }
        return WeddingError.none;
    }

    // Taking a filed divorce back (function individualcancel). Client check at weddingproxy.cpp:622: only the
    // filer, and only before the cooling off ends (0x37 for both).
    public static WeddingError CheckCancel(MaritalStatus status, bool isFiler, long filingTime, int coolingOffDays, long now) {
        if (!isFiler || !IsFiled(status) || now > CoolingOffEnd(filingTime, coolingOffDays)) {
            return WeddingError.s_wedding_result_divorcecancel;
        }
        return WeddingError.none;
    }

    // A mutual divorce (function mutual and its answer). Client check at weddingproxy.cpp:632: married (0x32).
    public static WeddingError CheckMutual(MaritalStatus status) {
        return status == MaritalStatus.Married ? WeddingError.none : WeddingError.s_wedding_result_only_marriage_user;
    }

    // Wedding 7 from the wedding window (UIWeddingDialog::SendWedding, uiweddingdialog.cpp:278): the client
    // offers it to the partner who did not file, while a divorce is filed.
    public static WeddingError CheckVisit(MaritalStatus status, bool isFiler) {
        if (isFiler) {
            return WeddingError.s_wedding_result_invalid_divorce_agree_user;
        }
        return IsFiled(status) ? WeddingError.none : WeddingError.s_wedding_result_invaild_state_divorce;
    }

    // The move of Wedding 7 is allowed from where a map move is: not dead, not from a dungeon room and not
    // from a wedding hall room.
    public static bool CanVisitFrom(bool inField, bool dead, bool dungeonField, bool weddingHall) {
        return inField && !dead && !dungeonField && !weddingHall;
    }

    // A change of the stored state is one update that tests the state it starts from; it is stored when it
    // changed that one row. Of two requests for the same change the second changes none.
    public static bool Stored(int rows) {
        return rows == 1;
    }

    // The fee that is in the wallet after a filing that was not stored: all of it back.
    public static long MesoAfterFiling(long meso, long fee, bool stored) {
        return stored ? meso : meso + fee;
    }

    // How long an ask for a mutual divorce waits for its answer. The server's own time; the client has none.
    public const long AskTimeoutMs = 30000;

    // An ask waits on the asked player: someone asked and its time is not over.
    public static bool AskOpen(long askFrom, long expireTick, long nowTick) {
        return askFrom != 0 && nowTick < expireTick;
    }

    // The answer (Wedding 9) holds for an ask that still waits, came from the partner, and whose asker is
    // still in the field of the asked player.
    public static bool CanAnswerAsk(long askFrom, long expireTick, long nowTick, long partnerId, bool askerInField) {
        return AskOpen(askFrom, expireTick, nowTick) && askFrom == partnerId && askerInField;
    }

    // A proposal by someone in this state: refused while a divorce is filed (0x19) and during the propose
    // cooltime after one (0x1a).
    public static WeddingError ProposeFrom(MaritalStatus status) {
        return status switch {
            MaritalStatus.Engaged => WeddingError.s_wedding_result_err_propose_state_to,
            MaritalStatus.Married => WeddingError.s_wedding_result_err_marriage_state_to,
            MaritalStatus.DivorceFiledByGroom or MaritalStatus.DivorceFiledByBride => WeddingError.s_wedding_result_err_coolingoff_state_to,
            MaritalStatus.Divorced => WeddingError.s_wedding_result_err_prpose_cooltime_state_to,
            _ => WeddingError.none,
        };
    }

    // A proposal to someone in this state (0x1b to 0x1e).
    public static WeddingError ProposeTo(MaritalStatus status) {
        return status switch {
            MaritalStatus.Engaged => WeddingError.s_wedding_result_err_propose_state_from,
            MaritalStatus.Married => WeddingError.s_wedding_result_err_mareriage_state_from,
            MaritalStatus.DivorceFiledByGroom or MaritalStatus.DivorceFiledByBride => WeddingError.s_wedding_result_err_coolingoff_state_from,
            MaritalStatus.Divorced => WeddingError.s_wedding_result_err_propose_cooltime_state_from,
            _ => WeddingError.none,
        };
    }

    // The wedding tests of an npc script condition other than the hall's: weddingState (null is no test),
    // marriageDate (0 is no test; married for more than that many days) and coolingOff (request is the filer's
    // script, the one that leads to the cancel function; response is the other partner's, the one that leads to
    // the agree function; anything else is no test).
    public static bool ScriptCondition(MaritalStatus? userState, int marriageDays, string? coolingOff, MaritalStatus status, bool isFiler, long marriageTime, long now) {
        if (userState != null && userState != status) {
            return false;
        }
        if (marriageDays > 0 && !(marriageTime > 0 && OlderThanDays(marriageTime, marriageDays, now))) {
            return false;
        }
        return coolingOff switch {
            CoolingOffRequest => IsFiled(status) && isFiler,
            CoolingOffResponse => IsFiled(status) && !isFiler,
            _ => true,
        };
    }
}
