using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Tools;

namespace Maple2.Server.Game.Model;

public class Tombstone : IByteSerializable {
    public readonly FieldPlayer Owner;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => Owner.Session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public int ObjectId => Owner.ObjectId;
    private byte hitsRemaining;
    public byte HitsRemaining {
        get => hitsRemaining;
        set {
            if (hitsRemaining == 0) {
                return;
            }
            hitsRemaining = Math.Clamp(value, (byte) 0, TotalHitCount);

            Owner.Field.Broadcast(RevivalPacket.Tombstone(this));
        }
    }
    public byte TotalHitCount { get; }
    // 1 while the packet reports a hit by another player, 0 for the first packet and for a revive. With
    // nothing left and this at 1 the client plays the tombstone hit effect and, for its own player, the
    // event voice (PC::FUN_14165d300); KMS2 sends 0 when the player revives by the dialog.
    public int Hitter { get; private set; }
    public bool Unknown2 { get; }

    public Tombstone(FieldPlayer owner, int deathStreak) {
        Owner = owner;
        TotalHitCount = TombstoneRules.TotalHits(deathStreak, Constants.hitPerDeadCount, Constants.maxDeadCount);
        hitsRemaining = TotalHitCount;
    }

    // One hit by another player.
    public void Hit() {
        Hitter = 1;
        HitsRemaining = TombstoneRules.AfterHit(hitsRemaining);
    }

    // The owner revived without the last hit: the packet carries no hitter.
    public void Clear() {
        Hitter = 0;
        HitsRemaining = 0;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(ObjectId);
        writer.WriteByte(HitsRemaining);
        writer.WriteByte(TotalHitCount);
        writer.WriteInt(Hitter);
        writer.WriteBool(Unknown2); // true if revived by pet
    }
}
