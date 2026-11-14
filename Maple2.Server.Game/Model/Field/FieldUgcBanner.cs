using Maple2.Model.Metadata;
using Maple2.Model.Game.Ugc;
using Maple2.Model.Validators;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Game.Model.Field;

public class FieldUgcBanner : UgcBanner, IUpdatable {
    private readonly FieldManager field;
    public FieldUgcBanner(FieldManager field, long id, int mapId, List<BannerSlot> slots) : base(id, mapId, slots) {
        this.field = field;
    }

    public void Update(long tickCount) {
        DateTimeOffset dateTimeOffset = DateTimeOffset.UtcNow;

        lock (Slots) {
            DeleteOldBannerSlots(dateTimeOffset);

            if (!UgcRules.SetLiveBannerSlot(Slots, dateTimeOffset)) {
                return;
            }
        }
        field.Broadcast(UgcPacket.ActivateBanner(this));
    }

    // Sends the live slot to the field again (a design confirmed during its live hour).
    public void Show() {
        field.Broadcast(UgcPacket.ActivateBanner(this));
    }

    private void DeleteOldBannerSlots(DateTimeOffset dateTimeOffset) {
        foreach (BannerSlot bannerSlot in Slots) {
            // check if the banner is expired
            DateTimeOffset expireTimeStamp = dateTimeOffset.Subtract(Constant.FieldUgcBannerRemoveAfter);
            if (bannerSlot.ActivateTime >= expireTimeStamp) {
                continue;
            }

            bannerSlot.Expired = true;
        }
    }
}
