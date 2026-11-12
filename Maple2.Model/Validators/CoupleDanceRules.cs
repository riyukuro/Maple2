namespace Maple2.Model.Validators;

// Microgame (0x00A8 / 0x0114), the couple dance of the CoupleDance event. Every packet has the event id after the
// mode. The client side is client/ms2/microgamemanagercli.cpp (packet handler), statemicrogame.cpp (the dance state),
// uibannereventlistener.cpp (invite, waiting box), uimessagebox.cpp (accept / decline), uimicrogamecoupledance.cpp
// (the dance choice) and uicoupledanceeventdialog.cpp (banner, rewards).
public static class CoupleDanceRules {
    // Client -> server modes.
    public const short OpenBanner = 0xD;
    public const short Invite = 0xE;
    public const short Accept = 0xF;
    public const short InviteAck = 0x10;   // the invitee shows the request box
    public const short Decline = 0x11;
    public const short Cancel = 0x13;      // the inviter's waiting box: cancel button or time out
    public const short AutoReject = 0x14;  // the invitee cannot show the request box
    public const short Answer = 0x15;      // the round and the chosen dance, sent when the choice time is up
    public const short ClaimReward = 0x19;

    // Server -> client modes.
    public const short SBanner = 0xD;
    public const short SInvite = 0xE;
    public const short SAccepted = 0xF;
    public const short SInviteSent = 0x10;
    public const short SDeclined = 0x11;
    public const short SStart = 0x12;
    public const short SCancelled = 0x13;
    public const short SFailed = 0x14;
    public const short SResult = 0x16;

    // The reason byte of mode 0x14 (GetCoupleDanceFailMessage, microgamemanagercli.cpp), as for the RPS game:
    // 0 request failed (peer in the wrong state), 2 accept failed (peer in the wrong state), else "Failed to start".
    public const byte ReasonPeerState = 0;
    public const byte ReasonAcceptPeerState = 2;
    public const byte ReasonFailed = 4;

    // User value 2101 (CoupleDanceRewardState), one entry per daily reward (UICoupleDanceEventDialog).
    public const int StateNone = 0;
    public const int StateCompleted = 1;
    public const int StateCompletedBonus = 2;
    public const int StateReceived = 3;
    public const int StateReceivedBonus = 4;

    // A round is good when both players chose the same dance.
    public static bool IsGood(int dance, int partnerDance) => dance == partnerDance;

    // The game ends well when the good rounds reach goodEndBonus count (FinishDance, statemicrogame.cpp).
    public static bool IsGoodEnd(int score, int goodEndCount) => score >= goodEndCount;

    // Mode 0x12: one dance index per round. The client puts it among the three dances it offers each round
    // (UIMicroGameCoupleDance FUN_140e64270), so both players always see it.
    public static short[] PickStages(int danceCount, int maxRound, Random random) {
        var stages = new short[Math.Max(0, maxRound)];
        for (int i = 0; i < stages.Length; i++) {
            stages[i] = (short) random.Next(danceCount);
        }
        return stages;
    }

    // The 2101 value as the client reads it (XmlSplit with ","), one entry per daily reward; missing or bad
    // entries are 0.
    public static int[] ParseStates(string? value, int count) {
        var states = new int[Math.Max(0, count)];
        if (string.IsNullOrWhiteSpace(value)) {
            return states;
        }
        string[] parts = value.Split(',');
        for (int i = 0; i < states.Length && i < parts.Length; i++) {
            states[i] = int.TryParse(parts[i].Trim(), out int state) ? state : StateNone;
        }
        return states;
    }

    public static string FormatStates(int[] states) => string.Join(",", states);

    // A finished dance takes the first daily reward that is still open: 2 for a good end, else 1. Null when all are
    // taken today.
    public static string? Complete(string? value, int count, bool goodEnd) {
        int[] states = ParseStates(value, count);
        int step = Array.IndexOf(states, StateNone);
        if (step < 0) {
            return null;
        }
        states[step] = goodEnd ? StateCompletedBonus : StateCompleted;
        return FormatStates(states);
    }

    // Mode 0x19: the reward of a completed step (the client sends it only for state 1 or 2). The new value and
    // whether the good-end bonus comes with it; null when the step cannot be claimed.
    public static (string Value, bool Bonus)? Claim(string? value, int count, int step) {
        if (step < 0 || step >= count) {
            return null;
        }
        int[] states = ParseStates(value, count);
        switch (states[step]) {
            case StateCompleted:
                states[step] = StateReceived;
                return (FormatStates(states), false);
            case StateCompletedBonus:
                states[step] = StateReceivedBonus;
                return (FormatStates(states), true);
            default:
                return null;
        }
    }
}

// The pending invites and running dances of one field, keyed by character id. One invite or dance per player: the
// client keeps one request box and one dance state.
public sealed class CoupleDanceMatchTable {
    private sealed class Match {
        public long Inviter { get; init; }
        public long Invitee { get; init; }
        public bool Started { get; set; }
        public short[] Stages { get; set; } = [];
        public int Round { get; set; }
        public int InviterDance { get; set; } = -1;
        public int InviteeDance { get; set; } = -1;
        public int Score { get; set; }

        public long Partner(long characterId) => characterId == Inviter ? Invitee : Inviter;
    }

    // A round with both answers: the dances, whether they matched, the good rounds so far and whether it was the last.
    public readonly record struct RoundResult(
        long Inviter, int InviterDance, long Invitee, int InviteeDance, int Round, bool Good, int Score, bool Finished);

    private readonly Dictionary<long, Match> matches = new();
    private readonly object guard = new();

    public bool IsBusy(long characterId) {
        lock (guard) {
            return matches.ContainsKey(characterId);
        }
    }

    // A new invite; false when either player already has an invite or a dance.
    public bool Invite(long inviter, long invitee) {
        lock (guard) {
            if (inviter == invitee || matches.ContainsKey(inviter) || matches.ContainsKey(invitee)) {
                return false;
            }
            var match = new Match { Inviter = inviter, Invitee = invitee };
            matches[inviter] = match;
            matches[invitee] = match;
            return true;
        }
    }

    // The invite from `inviter` to `invitee` is pending (not started).
    public bool IsPending(long inviter, long invitee) {
        lock (guard) {
            return matches.TryGetValue(invitee, out Match? match) && !match.Started
                && match.Inviter == inviter && match.Invitee == invitee;
        }
    }

    // Accept: the pending invite becomes a dance with these stages (one per round).
    public bool Start(long inviter, long invitee, short[] stages) {
        lock (guard) {
            if (stages.Length == 0 || !matches.TryGetValue(invitee, out Match? match) || match.Started
                || match.Inviter != inviter || match.Invitee != invitee) {
                return false;
            }
            match.Started = true;
            match.Stages = stages;
            return true;
        }
    }

    // Drops the player's invite or dance; the partner's id, or 0 when there was none.
    public long Remove(long characterId) {
        lock (guard) {
            if (!matches.TryGetValue(characterId, out Match? match)) {
                return 0;
            }
            matches.Remove(match.Inviter);
            matches.Remove(match.Invitee);
            return match.Partner(characterId);
        }
    }

    // Drops the invite between the two only while it is pending; false otherwise.
    public bool RemovePending(long inviter, long invitee) {
        lock (guard) {
            if (!matches.TryGetValue(invitee, out Match? match) || match.Started || match.Inviter != inviter || match.Invitee != invitee) {
                return false;
            }
            matches.Remove(inviter);
            matches.Remove(invitee);
            return true;
        }
    }

    // The player's partner in a running dance, 0 when there is none.
    public long GamePartner(long characterId) {
        lock (guard) {
            return matches.TryGetValue(characterId, out Match? match) && match.Started ? match.Partner(characterId) : 0;
        }
    }

    // An answer of the current round (the client sends GetRound, the 0-based round); one per player and round. A
    // dance index outside the event's dances, another round or a second answer is refused. When both answers are
    // in, the round ends: `result` holds both dances, and after the last round the dance is over and dropped.
    public bool Answer(long characterId, int round, int dance, int danceCount, out RoundResult? result) {
        result = null;
        lock (guard) {
            if (dance < 0 || dance >= danceCount || !matches.TryGetValue(characterId, out Match? match) || !match.Started
                || round != match.Round) {
                return false;
            }
            if (characterId == match.Inviter) {
                if (match.InviterDance >= 0) return false;
                match.InviterDance = dance;
            } else {
                if (match.InviteeDance >= 0) return false;
                match.InviteeDance = dance;
            }
            if (match.InviterDance < 0 || match.InviteeDance < 0) {
                return true;
            }

            bool good = CoupleDanceRules.IsGood(match.InviterDance, match.InviteeDance);
            if (good) {
                match.Score++;
            }
            bool finished = match.Round + 1 >= match.Stages.Length;
            result = new RoundResult(match.Inviter, match.InviterDance, match.Invitee, match.InviteeDance, match.Round, good, match.Score, finished);
            match.InviterDance = -1;
            match.InviteeDance = -1;
            match.Round++;
            if (finished) {
                matches.Remove(match.Inviter);
                matches.Remove(match.Invitee);
            }
            return true;
        }
    }
}
