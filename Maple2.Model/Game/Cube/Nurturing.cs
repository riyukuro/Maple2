using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

// One entry of the daily record: the owner's account, the pet item and the uid of its placed cube (0: not placed).
public readonly record struct NurturingRecord(long OwnerId, int ItemId, long CubeId) {
    // The cube uids the client's daily list holds. The pet in use is listed with the cube that was used, so the
    // client finds it (MyPC list lookup by cube uid, mypc.cpp:4038); a pet with no placed cube has no uid to list.
    // An entry stays per feeding: the client counts the fed list (CList, mypc.cpp:3978), so a pet fed twice is
    // listed twice.
    public static List<long> CubeIds(IEnumerable<NurturingRecord> records, long ownerId, int itemId, long cubeId) {
        var ids = new List<long>();
        foreach (NurturingRecord record in records) {
            long id = record.OwnerId == ownerId && record.ItemId == itemId ? cubeId : record.CubeId;
            if (id != 0) {
                ids.Add(id);
            }
        }

        return ids;
    }
}

public class Nurturing : IByteSerializable {
    public long Exp { get; set; }
    public DateTimeOffset LastFeedTime { get; set; }

    public short Stage { get; private set; }
    public short ClaimedGiftForStage { get; set; }
    public List<long> PlayedBy { get; set; }
    private DateTimeOffset CreationTime { get; set; }

    public readonly FunctionCubeMetadata.NurturingData NurturingMetadata;

    public Nurturing(FunctionCubeMetadata.NurturingData metadata) {
        CreationTime = DateTimeOffset.Now;
        NurturingMetadata = metadata;
        Stage = 1;
        ClaimedGiftForStage = 1;
        PlayedBy = [];
    }

    public Nurturing(long exp, short claimedGiftForStage, long[] playedBy, DateTimeOffset creationTime, DateTimeOffset lastFeedTime, FunctionCubeMetadata.NurturingData? metadata) {
        NurturingMetadata = metadata ?? throw new ArgumentException("FunctionCubeMetadata does not have a Nurturing metadata.");

        Exp = exp;
        Stage = 1;
        ClaimedGiftForStage = claimedGiftForStage;
        PlayedBy = playedBy.ToList();
        CreationTime = creationTime;
        LastFeedTime = lastFeedTime;

        FunctionCubeMetadata.NurturingData.Growth[] requiredGrowth = metadata.RequiredGrowth;
        if (exp >= requiredGrowth.Last().Exp) {
            Stage = requiredGrowth.Last().Stage;
            return;
        }

        for (short i = 0; i < requiredGrowth.Length; i++) {
            FunctionCubeMetadata.NurturingData.Growth growth = requiredGrowth[i];
            if (exp < growth.Exp) {
                Stage = (short) (i + 1);
                break;
            }
        }
    }

    public void Feed(int nurturingEatGrowth) {
        if (Exp >= NurturingMetadata.RequiredGrowth.Last().Exp) {
            return;
        }

        Exp += nurturingEatGrowth;
        if (Exp >= NurturingMetadata.RequiredGrowth.First(x => x.Stage == Stage).Exp) {
            Stage++;
        }
        LastFeedTime = DateTimeOffset.Now;
    }

    // False when the account already played with this pet today. `maxGrowthPlays` (NurturingPlayMaxGrowth): only
    // that many plays a day make the pet grow; a later play still counts for the visitor. The client does not
    // read that constant and has no error for it, so the play is never refused for it.
    public bool Play(long accountId, int playGrowth, int maxGrowthPlays) {
        if (PlayedBy.Contains(accountId)) {
            return false;
        }

        if (PlayedBy.Count < maxGrowthPlays) {
            Feed(playGrowth);
        }
        PlayedBy.Add(accountId);
        return true;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(CreationTime.ToUnixTimeSeconds());
        writer.WriteLong(Exp);
        writer.WriteShort(Stage);
        writer.WriteShort(ClaimedGiftForStage);
        writer.WriteShort(); // Unknown
        writer.WriteLong(LastFeedTime.ToUnixTimeSeconds());
    }

    // The system mail (systemmailcontent id) that carries the feeding gift to the owner when a visitor plays with the
    // pet; null for a pet whose event has no such mail. 180524005 is the Rainbow Slime nest's KR mail (its 2 rainbow
    // coins are the nest's rewardItemByFeeding, 30001135 x2).
    public static string? OwnerMailContent(string questTag) => questTag switch {
        "20180524_RainbowSlime" => "180524005",
        "NurturingPumpkinDevil" => "18101804",
        "NurturingGhostCats" => "19101804",
        _ => null,
    };
}
