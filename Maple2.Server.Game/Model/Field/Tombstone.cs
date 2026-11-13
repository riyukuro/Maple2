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
    // Set when the hit was the owner's pet's (Revival 3): the client then puts the owner's pet in its idle state with
    // sub state 0x11 and plays the pet's sound (OnTombstone, scenestategame.cpp:9644).
    public bool ByPet { get; private set; }

    public Tombstone(FieldPlayer owner, int deathStreak) {
        Owner = owner;
        TotalHitCount = TombstoneRules.TotalHits(deathStreak, Constants.hitPerDeadCount, Constants.maxDeadCount);
        hitsRemaining = TotalHitCount;
    }

    // One hit by another player, or by the owner's pet.
    public void Hit(bool byPet = false) {
        Hitter = 1;
        ByPet = byPet;
        HitsRemaining = TombstoneRules.AfterHit(hitsRemaining);
    }

    // The owner revived without the last hit: the packet carries no hitter.
    public void Clear() {
        Hitter = 0;
        ByPet = false;
        HitsRemaining = 0;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(ObjectId);
        writer.WriteByte(HitsRemaining);
        writer.WriteByte(TotalHitCount);
        writer.WriteInt(Hitter);
        writer.WriteBool(ByPet);
    }
}
