using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Red envelope: the click on the envelope's chat link.
public static class HongBaoRules {
    // ItemBillboard mode 1: i32 envelope id, the id of the link (uimessenger.cpp:4739-4749).
    public static int ReadClaim(IByteReader packet) {
        return packet.ReadInt();
    }

    public enum ClaimStep {
        Refuse,
        Give,
    }

    // An envelope gives one share to each of its first maxUsers players, the opener included, until
    // its durationSec is over. The client has one refusal line (s_special_item_hongbao_cantget), so a
    // gone, an expired, an emptied and an already taken envelope all get it.
    public static ClaimStep Claim(bool found, bool expired, bool alreadyClaimed, int claimedUsers, int maxUsers) {
        if (!found || expired || alreadyClaimed || claimedUsers >= maxUsers) {
            return ClaimStep.Refuse;
        }
        return ClaimStep.Give;
    }
}

// The shares of one envelope. Claims come from several session threads, so the test, the entry and
// the pick of the share are one step under the lock: each share goes out at most once, and no more
// characters are entered than there are shares. Keyed by character id, which stays the same when a
// player leaves the field and comes back.
public sealed class HongBaoShares {
    private readonly object sync = new();
    private readonly System.Collections.Generic.HashSet<long> claimed = [];
    private readonly int[] amounts;
    private readonly int maxUsers;

    public HongBaoShares(int[] amounts, int maxUsers) {
        this.amounts = amounts;
        this.maxUsers = maxUsers;
    }

    public int Count {
        get {
            lock (sync) {
                return claimed.Count;
            }
        }
    }

    public bool TryTake(long characterId, out int amount) {
        lock (sync) {
            amount = 0;
            if (HongBaoRules.Claim(true, false, claimed.Contains(characterId), claimed.Count, maxUsers) == HongBaoRules.ClaimStep.Refuse) {
                return false;
            }
            if (claimed.Count >= amounts.Length) {
                return false;
            }

            amount = amounts[claimed.Count];
            claimed.Add(characterId);
            return true;
        }
    }
}
