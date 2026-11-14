using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model;

public enum UgcOxPhase {
    // The stadium is open: entrants come in and the host sees the welcome dialog with the start button.
    Welcome,
    // The host started the game: the rounds run.
    Running,
    // The script ran EndGame: the event is over and its stadium closes.
    Ended,
}

// The host's step in a round, as the script's HostUIChange names it.
public enum UgcOxHostStep {
    None,
    InputQuiz,
    Move,
    SelectAnswer,
    Judge,
}

public enum UgcOxEnter {
    // The host itself (it pays no entry fee).
    Host,
    // An entrant already in the list comes back before the start.
    Rejoin,
    // A new entrant.
    New,
    // The list holds the most entrants the host chose.
    Full,
    // The game started or is over: nobody new comes in.
    Closed,
}

public enum UgcOxFinish {
    Ok,
    // The client refuses a finish before round 2 (s_ugcox_finish_fail_by_round); the server does the same.
    TooEarly,
    // The game is not running (a finish at the welcome cancels instead).
    NotRunning,
}

public enum UgcOxPromote {
    Ok,
    // All promote lines are used up.
    Spent,
    // The last line is less than PromoteCooldownMs old.
    Cooldown,
}

/// <summary>
/// One home OX quiz event (UGC OX): the host's settings, its stadium room and the game's state, which the stadium
/// script reads through the OxQuizUGC widget. Every member takes Lock, so the host's session thread and the stadium's
/// field thread see one state.
/// </summary>
public class UgcOxEvent {
    public readonly object Lock = new();

    public long HostAccountId { get; }
    public long HostCharacterId { get; }
    public string HostName { get; }
    // The host's home (indoor map) and the stadium portal placed in it.
    public int HomeMapId { get; }
    public UgcOxRules.Settings Settings { get; }
    public int StadiumRoomId { get; set; }
    public int HomePortalId { get; set; }
    public int HomePortalObjectId { get; set; }

    public UgcOxPhase Phase { get; private set; } = UgcOxPhase.Welcome;
    // Set when the event stops before its end (too few entrants, the host left); the script then runs its cancel.
    public bool Canceled { get; private set; }
    // Set by the host's finish (round 2 on) or when the last round ended; the script then runs its game end.
    public bool Finished { get; private set; }
    public int Round { get; private set; }
    public UgcOxHostStep HostStep { get; private set; }
    public string? Question { get; private set; }
    public bool? Answer { get; private set; }
    public bool WallRemoved { get; private set; }
    public int PromoteCount { get; private set; }
    private long lastPromoteTick;
    private readonly HashSet<long> entrants = [];

    public UgcOxEvent(long hostAccountId, long hostCharacterId, string hostName, int homeMapId, UgcOxRules.Settings settings) {
        HostAccountId = hostAccountId;
        HostCharacterId = hostCharacterId;
        HostName = hostName;
        HomeMapId = homeMapId;
        Settings = settings;
    }

    public int EntrantCount {
        get {
            lock (Lock) {
                return entrants.Count;
            }
        }
    }

    public IReadOnlyCollection<long> Entrants() {
        lock (Lock) {
            return entrants.ToArray();
        }
    }

    public bool IsEntrant(long characterId) {
        lock (Lock) {
            return entrants.Contains(characterId);
        }
    }

    // Open: the event takes entrants and runs; false once it ended or was canceled.
    public bool Open {
        get {
            lock (Lock) {
                return Phase != UgcOxPhase.Ended && !Canceled;
            }
        }
    }

    public UgcOxEnter Enter(long characterId) {
        lock (Lock) {
            if (characterId == HostCharacterId) {
                return Phase == UgcOxPhase.Ended ? UgcOxEnter.Closed : UgcOxEnter.Host;
            }
            if (Phase != UgcOxPhase.Welcome || Canceled) {
                return UgcOxEnter.Closed;
            }
            if (entrants.Contains(characterId)) {
                return UgcOxEnter.Rejoin;
            }
            if (entrants.Count >= Settings.Max) {
                return UgcOxEnter.Full;
            }
            entrants.Add(characterId);
            return UgcOxEnter.New;
        }
    }

    // The host's start (c2s 7). With fewer than MinEntrants entrants the event is canceled instead; false then.
    public bool Start() {
        lock (Lock) {
            if (Phase != UgcOxPhase.Welcome || Canceled) {
                return false;
            }
            if (entrants.Count < UgcOxRules.MinEntrants) {
                Canceled = true;
                return false;
            }
            Phase = UgcOxPhase.Running;
            return true;
        }
    }

    // The host left (or asked to finish at the welcome): the script runs its cancel. Nothing once the game is over.
    public bool Cancel() {
        lock (Lock) {
            if (Phase == UgcOxPhase.Ended || Canceled || Finished) {
                return false;
            }
            Canceled = true;
            return true;
        }
    }

    // The host's finish (c2s 8): at the welcome it cancels, in the game it ends the game after the current round.
    public UgcOxFinish Finish() {
        lock (Lock) {
            if (Phase == UgcOxPhase.Welcome && !Canceled) {
                Canceled = true;
                return UgcOxFinish.Ok;
            }
            if (Phase != UgcOxPhase.Running || Canceled) {
                return UgcOxFinish.NotRunning;
            }
            if (Round < 2) {
                return UgcOxFinish.TooEarly;
            }
            Finished = true;
            return UgcOxFinish.Ok;
        }
    }

    // The script's StartRound: the next round, with no question, answer or wall removal yet.
    public int NextRound() {
        lock (Lock) {
            Round++;
            Question = null;
            Answer = null;
            WallRemoved = false;
            return Round;
        }
    }

    public void SetHostStep(UgcOxHostStep step) {
        lock (Lock) {
            HostStep = step;
        }
    }

    // The host's question (c2s 10), taken only while the host is asked for one.
    public bool SubmitQuestion(string text) {
        lock (Lock) {
            if (Phase != UgcOxPhase.Running || HostStep != UgcOxHostStep.InputQuiz || Question != null) {
                return false;
            }
            string? question = UgcOxRules.Question(text);
            if (question == null) {
                return false;
            }
            Question = question;
            return true;
        }
    }

    // The host's answer (c2s 11), taken only while the host is asked for one.
    public bool SubmitAnswer(bool isTrue) {
        lock (Lock) {
            if (Phase != UgcOxPhase.Running || HostStep != UgcOxHostStep.SelectAnswer || Answer != null) {
                return false;
            }
            Answer = isTrue;
            return true;
        }
    }

    // The host's remove_wall (c2s 17) while it is asked for the answer: the script shows the question again.
    public bool RemoveWall() {
        lock (Lock) {
            if (Phase != UgcOxPhase.Running || HostStep != UgcOxHostStep.SelectAnswer || Answer != null) {
                return false;
            }
            WallRemoved = true;
            return true;
        }
    }

    // The script's ShowQuiz runs again after a wall removal: the flag is spent.
    public void ClearWallRemoved() {
        lock (Lock) {
            WallRemoved = false;
        }
    }

    // The script's EndRound: the last round makes the game finished.
    public void EndRound() {
        lock (Lock) {
            HostStep = UgcOxHostStep.None;
            if (Round >= UgcOxRules.MaxRounds) {
                Finished = true;
            }
        }
    }

    // The script's EndGame. True once: the caller settles the event (layer 3 pays out there) only on true.
    public bool End() {
        lock (Lock) {
            if (Phase == UgcOxPhase.Ended) {
                return false;
            }
            Phase = UgcOxPhase.Ended;
            return true;
        }
    }

    // The host's promote line (c2s 1): at most PromoteTotal lines, PromoteCooldownMs apart.
    public UgcOxPromote Promote(long nowTick) {
        lock (Lock) {
            if (PromoteCount >= UgcOxRules.PromoteTotal) {
                return UgcOxPromote.Spent;
            }
            if (PromoteCount > 0 && nowTick - lastPromoteTick < UgcOxRules.PromoteCooldownMs) {
                return UgcOxPromote.Cooldown;
            }
            PromoteCount++;
            lastPromoteTick = nowTick;
            return UgcOxPromote.Ok;
        }
    }

    // What is left of the cooldown at nowTick, for the client's tooltip (s2c 1).
    public int PromoteCooldownLeft(long nowTick) {
        lock (Lock) {
            if (PromoteCount == 0) {
                return 0;
            }
            long left = UgcOxRules.PromoteCooldownMs - (nowTick - lastPromoteTick);
            return left > 0 ? (int) left : 0;
        }
    }
}
