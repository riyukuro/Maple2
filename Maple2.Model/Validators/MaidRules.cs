using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// Home maids: a furnishing with <maid maidID> places the maid of that row of table/maidproperty.xml.
public static class MaidRules {
    // The maid id of a cube that places a maid, 0 for other cubes and for a planner plot (its cubes are a preview).
    public static int MaidId(ItemMetadata metadata, bool planner) {
        return planner ? 0 : metadata.Housing?.MaidId ?? 0;
    }

    // The maid record of a maid cube placed at now (epoch seconds): affinity level 1, mood 0 (a new CMaid's
    // values), hired for PeriodOfMaidEmployment days.
    public static Maid Hire(PlotCube cube, int maidId, long ownerId, long now, int periodDays) {
        return new Maid {
            Uid = cube.Id,
            OwnerId = ownerId,
            ItemId = cube.ItemId,
            MaidId = maidId,
            CreationTime = now,
            ExpiryTime = now + (long) Math.Max(periodDays, 0) * 24 * 60 * 60,
        };
    }
}
