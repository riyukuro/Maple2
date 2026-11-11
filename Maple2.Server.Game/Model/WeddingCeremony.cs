namespace Maple2.Server.Game.Model;

// The agree boxes the hall trigger asks the couple (trigger attribute agreeType). The number is the byte of
// Wedding mode 0x1e in both directions: the client shows the start box for 1 and the end box for 2
// (WeddingManager::ShowStepResult) and the type-the-partner's-name steps for 3 (WeddingManager::OnMode1e), and
// answers with the same byte.
public enum WeddingAgreeType : byte {
    None = 0,
    StartActing = 1,
    EndActing = 2,
    PartnerName = 3,
}

// The state of one ceremony: the room of a wedding hall map that belongs to one marriage. The hall trigger
// drives it (TriggerContext.Wedding) and the couple's answers arrive from their sessions, so every member is
// taken under one lock.
public sealed class WeddingCeremony {
    // The client offers the way in for a ticket whose time is at most this far from now, before or after
    // (WeddingManager::CollectTickets).
    public const int EnterWindowSeconds = 1800;
    // Trigger and npc script attribute values.
    public const string EntryGroomBride = "GroomBride";
    public const string StateComplete = "weddingComplete";

    public readonly long MarriageId;
    private readonly long[] couple;
    private readonly Dictionary<WeddingAgreeType, Dictionary<long, bool>> asked = new();
    private bool complete;

    public WeddingCeremony(long marriageId, long partner1Id, long partner2Id) {
        MarriageId = marriageId;
        couple = [partner1Id, partner2Id];
    }

    public IReadOnlyList<long> Couple => couple;

    public bool IsCouple(long characterId) => characterId != 0 && Array.IndexOf(couple, characterId) >= 0;

    // The hall state weddingComplete: the vow was completed and the couple is married.
    public bool Complete {
        get {
            lock (asked) {
                return complete;
            }
        }
    }

    public static WeddingAgreeType ParseAgreeType(string type) {
        return type switch {
            "startActing" => WeddingAgreeType.StartActing,
            "endActing" => WeddingAgreeType.EndActing,
            "partnerName" => WeddingAgreeType.PartnerName,
            _ => WeddingAgreeType.None,
        };
    }

    // The trigger condition WeddingEntryInField for entry type GroomBride, the only one the hall triggers use:
    // with isInField both of the couple are in the room, without it at least one of them is not.
    public bool CoupleInField(bool isInField, Func<long, bool> inField) {
        bool both = inField(couple[0]) && inField(couple[1]);
        return isInField ? both : !both;
    }

    // WeddingMutualAgree: a new question; answers to an earlier one of the same type are dropped.
    public bool Ask(WeddingAgreeType type) {
        if (type == WeddingAgreeType.None) {
            return false;
        }

        lock (asked) {
            asked[type] = new Dictionary<long, bool>();
            return true;
        }
    }

    // WeddingMutualCancel: the question is withdrawn, so neither result holds any more.
    public bool Cancel(WeddingAgreeType type) {
        lock (asked) {
            return asked.Remove(type);
        }
    }

    // An answer of one of the couple (Wedding 0x1e from the client). Refused when nothing of that type is asked
    // or the sender is not one of the couple.
    public bool Answer(long characterId, WeddingAgreeType type, bool accept) {
        if (!IsCouple(characterId)) {
            return false;
        }

        lock (asked) {
            if (!asked.TryGetValue(type, out Dictionary<long, bool>? answers)) {
                return false;
            }

            answers[characterId] = accept;
            return true;
        }
    }

    // WeddingMutualAgreeResult. With success: both of the couple accepted. Without: one of them refused. While
    // an answer is missing and nobody refused, neither holds; the triggers leave that state by their own timer
    // or by the couple leaving.
    public bool AgreeResult(WeddingAgreeType type, bool success) {
        lock (asked) {
            if (!asked.TryGetValue(type, out Dictionary<long, bool>? answers)) {
                return false;
            }

            if (!success) {
                return answers.Values.Any(accept => !accept);
            }

            return couple.All(id => answers.TryGetValue(id, out bool accept) && accept);
        }
    }

    // WeddingVowComplete: once per ceremony. Returns false when the ceremony was already complete.
    public bool TryComplete() {
        lock (asked) {
            if (complete) {
                return false;
            }

            complete = true;
            return true;
        }
    }

    // The marriage could not be stored: the vow can be completed again.
    public void Uncomplete() {
        lock (asked) {
            complete = false;
        }
    }

    // The way in is open while the ceremony time is at most EnterWindowSeconds from now, the window in which
    // the client shows the ticket.
    public static bool InEnterWindow(long ceremonyTime, long now) {
        return Math.Abs(now - ceremonyTime) <= EnterWindowSeconds;
    }

    // The two wedding hall tests of an npc script condition (weddingHallState, weddingHallEntryType; value false
    // is the ! form). An empty or unknown key is no test. Outside a ceremony nothing is complete and nobody is
    // of the couple.
    public static bool ScriptCondition(WeddingCeremony? ceremony, long characterId, string? hallState, bool hallStateValue, string? entryType, bool entryTypeValue) {
        if (hallState == StateComplete && (ceremony?.Complete ?? false) != hallStateValue) {
            return false;
        }

        if (entryType == EntryGroomBride && (ceremony?.IsCouple(characterId) ?? false) != entryTypeValue) {
            return false;
        }

        return true;
    }

    // Who the room takes: the couple, anyone when the hall was reserved as public, else the guest list.
    public static bool CanEnter(bool couple, bool publicHall, bool guest) {
        return couple || publicHall || guest;
    }

    public static int HallMap(int mapId, int nightMapId, int hour) {
        return IsNight(hour) && nightMapId != 0 ? nightMapId : mapId;
    }

    // Client IsNightTime (weddingproxy.cpp:729): the hours 6 to 17 are day, the rest is night. A hall has one
    // map for each (weddingpackage fieldID and nightFieldID).
    public static bool IsNight(int hour) {
        return hour is < 6 or > 17;
    }
}
