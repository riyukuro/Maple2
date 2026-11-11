namespace Maple2.Model.Validators;

// Microgame (0x00A8 / 0x0114), the rock-paper-scissors game of the RPS event. The client side is
// client/ms2/microgamemanagercli.cpp (packet handler), statemicrogame.cpp (the game state),
// uibannereventlistener.cpp (invite), uimessagebox.cpp (accept / decline), uimicrogamerps.cpp (card) and
// uirpseventdialog.cpp (banner, rewards).
public static class RpsRules {
    // Client -> server modes.
    public const short OpenBanner = 0;
    public const short Invite = 1;
    public const short Accept = 2;
    public const short Decline = 3;
    public const short Cancel = 5;      // the inviter's waiting box: cancel button or time out
    public const short AutoReject = 6;  // the invitee cannot show the request box
    public const short PlayCard = 7;
    public const short ClaimReward = 10;
    public const short InviteAck = 0xC; // the invitee shows the request box

    // Server -> client modes.
    public const short SBanner = 0;
    public const short SInvite = 1;
    public const short SAccepted = 2;
    public const short SDeclined = 3;
    public const short SRound = 4;
    public const short SCancelled = 5;
    public const short SFailed = 6;
    public const short SRewards = 10;
    public const short SResult = 0xB;
    public const short SInviteSent = 0xC;

    // The reason byte of mode 6 (statemicrogame.cpp FUN_140b887d0): 0 request failed (peer in the wrong state),
    // 1 request blocked in this field, 2 accept failed (peer in the wrong state), 3 banner blocked in this field,
    // anything else "Failed to start".
    public const byte ReasonPeerState = 0;
    public const byte ReasonAcceptPeerState = 2;
    public const byte ReasonFailed = 4;

    // Client::Constants MicroGameMaxDistance (IsWithinMicroGameDistance, statemicrogame.cpp).
    public const float MaxDistance = 300f;

    public const int Rock = 0;
    public const int Paper = 1;
    public const int Scissors = 2;

    // The result mode of mode 0xB, also the order of rps_game's result nodes (draw, win, lose).
    public const byte Draw = 0;
    public const byte Win = 1;
    public const byte Lose = 2;

    public static bool IsCard(int card) => card is >= Rock and <= Scissors;

    // The result for the player who played `mine`: paper beats rock, scissors beat paper, rock beats scissors.
    public static byte Result(int mine, int theirs) {
        return ((mine - theirs + 3) % 3) switch {
            0 => Draw,
            1 => Win,
            _ => Lose,
        };
    }

    // The play_rps_* condition of a result.
    public static Enum.ConditionType ResultCondition(byte result) {
        return result switch {
            Win => Enum.ConditionType.play_rps_win,
            Lose => Enum.ConditionType.play_rps_lose,
            _ => Enum.ConditionType.play_rps_draw,
        };
    }

    // User value 1801: the claimed reward indices as the client reads them, a comma list
    // (UIRpsEventDialog FUN_140e83200, ParseIntArray with ",").
    public static int[] ParseClaimed(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }
        var list = new List<int>();
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (int.TryParse(part, out int index)) {
                list.Add(index);
            }
        }
        return list.ToArray();
    }

    public static string AddClaimed(string? value, int index) {
        return string.Join(",", ParseClaimed(value).Append(index));
    }

    // Mode 10: the index is a reward of the event (in the order the server wrote them), the play count reaches
    // its condPlayCount (the client sends it only then, UIRpsEventDialog::RequestReward) and it is not claimed yet.
    public static bool CanClaim(int index, int[] condPlayCounts, int playCount, string? claimed) {
        if (index < 0 || index >= condPlayCounts.Length) {
            return false;
        }
        return condPlayCounts[index] <= playCount && !ParseClaimed(claimed).Contains(index);
    }
}

// The pending invites and running games of one field, keyed by character id. One invite or game per player:
// the client keeps one request box and one game state.
public sealed class RpsMatchTable {
    private sealed class Match {
        public long Inviter { get; init; }
        public long Invitee { get; init; }
        public bool Started { get; set; }
        public int InviterCard { get; set; } = -1;
        public int InviteeCard { get; set; } = -1;

        public long Partner(long characterId) => characterId == Inviter ? Invitee : Inviter;
    }

    // Both cards of a finished game.
    public readonly record struct Played(long Inviter, int InviterCard, long Invitee, int InviteeCard);

    private readonly Dictionary<long, Match> matches = new();
    private readonly object guard = new();

    public bool IsBusy(long characterId) {
        lock (guard) {
            return matches.ContainsKey(characterId);
        }
    }

    // A new invite; false when either player already has an invite or a game.
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

    // Accept: the pending invite becomes a game.
    public bool Start(long inviter, long invitee) {
        lock (guard) {
            if (!matches.TryGetValue(invitee, out Match? match) || match.Started || match.Inviter != inviter || match.Invitee != invitee) {
                return false;
            }
            match.Started = true;
            return true;
        }
    }

    // Drops the player's invite or game; the partner's id, or 0 when there was none.
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

    // The player's partner in a running game, 0 when there is none.
    public long GamePartner(long characterId) {
        lock (guard) {
            return matches.TryGetValue(characterId, out Match? match) && match.Started ? match.Partner(characterId) : 0;
        }
    }

    // A card of a running game; one card per player. When both are in, the game ends and both cards are returned.
    public bool Play(long characterId, int card, out Played? played) {
        played = null;
        lock (guard) {
            if (!RpsRules.IsCard(card) || !matches.TryGetValue(characterId, out Match? match) || !match.Started) {
                return false;
            }
            if (characterId == match.Inviter) {
                if (match.InviterCard >= 0) return false;
                match.InviterCard = card;
            } else {
                if (match.InviteeCard >= 0) return false;
                match.InviteeCard = card;
            }
            if (match.InviterCard >= 0 && match.InviteeCard >= 0) {
                played = new Played(match.Inviter, match.InviterCard, match.Invitee, match.InviteeCard);
                matches.Remove(match.Inviter);
                matches.Remove(match.Invitee);
            }
            return true;
        }
    }
}
