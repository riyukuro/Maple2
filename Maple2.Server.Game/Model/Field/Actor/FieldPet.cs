using System.Numerics;
using DotRecast.Detour.Crowd;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Enum;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model;

public sealed class FieldPet : FieldNpc {
    private readonly FieldPlayer? owner;
    public readonly Item Pet;
    public readonly PetMetadata Metadata;

    public int OwnerId => owner?.ObjectId ?? 0;
    // The player who summoned the pet; null for a wild pet.
    public FieldPlayer? OwnerPlayer => owner;
    public int SkinId { get; private set; }

    public readonly float Scale = 1f;

    public short TamingRank; // (0-70=green, 70-95=yellow, 95-100=red)
    public int TamingPoint;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => Field.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public FieldPet(FieldManager field, int objectId, DtCrowdAgent agent, Npc npc, Item pet, PetMetadata petMetadata, string aiPath, FieldPlayer? owner = null) : base(field, objectId, agent, npc, aiPath) {
        this.owner = owner;
        Pet = pet;

        Metadata = petMetadata;

        // Wild pets need a TamingRank.
        if (owner == null) {
            TamingRank = (short) (Random.Shared.Next(100) + 1);
        }

        // Use Badge for SkinId if equipped.
        if (owner != null && owner.Session.Item.Equips.Badge.TryGetValue(BadgeType.PetSkin, out Item? value)) {
            SkinId = value.Badge?.PetSkinId ?? Value.Id;
        } else {
            SkinId = Value.Id;
        }
    }

    private long reviveHits; // pet hits taken since the pet entered its revive state
    private long reviveEnterTick; // 0 while the pet is out of its revive state

    // Called each update after the revive state is set: the tick the pet entered SummonRevive, kept while it stays.
    public void NoteReviveState(long tickCount) {
        if (MovementState.State != ActorState.SummonRevive) {
            reviveEnterTick = 0;
            reviveHits = 0;
        } else if (reviveEnterTick == 0) {
            reviveEnterTick = tickCount;
            reviveHits = 0;
        }
    }

    // The owner lies dead in this field with a tombstone: the pet stands in its revive state (FieldNpc.Update).
    public bool OwnerDown => owner != null && owner.Field == Field && TombstoneRules.PetHolds(owner.IsDead, owner.Tombstone != null);

    // Revival 3 from the owner's client: one hit on the owner's tombstone, at most one per the client's interval.
    public bool TryReviveHit(long tickCount, out string refusal) {
        refusal = string.Empty;
        if (owner?.Tombstone is not { } tombstone || owner.Field != Field) {
            refusal = "owner not down here";
            return false;
        }
        if (!TombstoneRules.PetHelps(owner.IsDead, true, owner.DeathState, BuffManager.BlocksRevival(owner.Buffs.EnumerateBuffs()))) {
            refusal = "revival blocked";
            return false;
        }
        // The client sends only from StateSummonRevive (statesummon.cpp:787): not from a dead, stunned or hit pet.
        if (IsDead || MovementState.State != ActorState.SummonRevive || reviveEnterTick == 0) {
            refusal = "pet not in its revive state";
            return false;
        }
        reviveHits = TombstoneRules.PetHitsCounted(tickCount, reviveEnterTick, reviveHits);
        if (!TombstoneRules.PetHitDue(tickCount, reviveEnterTick, reviveHits)) {
            refusal = "too soon";
            return false;
        }
        if (!TombstoneRules.CanHit(tombstone.HitsRemaining)) {
            refusal = "tombstone broken";
            return false;
        }

        reviveHits++;
        tombstone.Hit(byPet: true);
        return true;
    }

    public override void Update(long tickCount) {
        base.Update(tickCount);
    }

    // A summoned pet holds its pet effects (the pet's <effect> rows: KR 18 pets, one each, e.g. 61100010) while it is
    // out. They are event effects: each fires on the pet's own landed hits (OnOwnerAttackHit) and gives the hit target
    // a debuff or the owner a buff (PetOwner). The client applies none of them itself (no reader of the pet's effect
    // list), so the server holds them and sends their buffs. A wild pet gets none.
    public void AddPetEffects() {
        if (owner == null) {
            return;
        }
        foreach (PetMetadataEffect effect in Metadata.Effect) {
            Buffs.AddBuff(this, this, effect.Id, effect.Level, Field.FieldTick);
        }
    }

    public override void ApplyDamage(IActor caster, DamageRecord damage, SkillMetadataAttack attack) {
        if (attack.Pet == null) {
            return;
        }

        if (BattleState.TargetId != 0 && BattleState.TargetId != caster.ObjectId && caster is FieldPlayer player) {
            player.Session.Send(NoticePacket.Notice(NoticePacket.Flags.Message | NoticePacket.Flags.Alert, new InterfaceText("s_err_taming_attack_other_user"))); // No string code for this?
            return;
        }

        var targetRecord = new DamageRecordTarget(this);
        int pointBefore = TamingPoint;
        int damageAmount = TamingPoint - Math.Min(TamingPoint + attack.Pet.TamingPoint, Constants.TamingPetMaxPoint);
        TamingPoint -= damageAmount;
        targetRecord.AddDamage(damageAmount == 0 ? DamageType.Miss : DamageType.Normal, damageAmount);
        if (damageAmount != 0) {
            // The bar shows only what SyncPetTamingPoint sends (CSceneStateGame::OnSyncPetTamingPoint sets the tamer and
            // the points); neither the client nor a table gives the points a rate to fall at, so they stay as fed.
            Field.Broadcast(PetPacket.SyncTaming(caster.ObjectId, this));
        }
        if (caster is FieldPlayer feeder && PetRules.Pacified(pointBefore, TamingPoint, Constants.TamingPetMaxPoint)) {
            // 90000792 names the pet's item id ("Pacify a hungry $item:61000002$").
            feeder.Session.ConditionUpdate(ConditionType.pet_taming, codeLong: Pet.Id);
        }

        if (attack.Pet.TrapLevel > 0) {
            if (attack.Pet.ForcedTaming) {
                IsDead = true;
                OnDeath();
                DropItem(caster);
            } else if (TamingPoint >= Constants.TamingPetMaxPoint) { // trap has chance to fail
                IsDead = true;
                OnDeath();
                DropItem(caster);
            }
        }

        damage.Targets.TryAdd(ObjectId, targetRecord);
    }

    private NpcTask? followWalk;

    /// <summary>
    /// The next idle task of a summoned pet: stay, run to the owner, or (far away, e.g. after a GM coordinate
    /// move) appear next to the owner. See <see cref="PetFollow"/>.
    /// </summary>
    public NpcTask? NextFollowTask() {
        if (owner == null || owner.Field != Field) {
            return MovementState.TryStandby(null, true);
        }

        switch (PetFollow.Decide(Position, owner.Position, Metadata.Distance, out Vector3 target)) {
            case PetFollow.Action.Warp:
                followWalk = null;
                WarpTo(target);
                return MovementState.TryStandby(null, true);
            case PetFollow.Action.Walk:
                if (followWalk is { Status: NpcTaskStatus.Cancelled }) {
                    // The last walk found no path (another floor, off the navmesh): put the pet with its owner.
                    followWalk = null;
                    WarpTo(owner.Position);
                    return MovementState.TryStandby(null, true);
                }
                followWalk = MovementState.TryMoveTo(target, false);
                return followWalk;
            default:
                followWalk = null;
                return MovementState.TryStandby(null, true);
        }
    }

    private void WarpTo(Vector3 position) {
        if (Navigation == null || !Navigation.UpdatePosition(position)) {
            Position = position;
        }
        SendControl = true;
        // KMS2 sends the owner ResponsePet 2 with the warp; the client plays the pet summon sound.
        owner?.Session.Send(PetPacket.WarpSound(OwnerId));
    }

    protected override void Remove(TimeSpan delay) => Field.RemovePet(ObjectId, delay);

    public void UpdateSkin(int skinId) {
        SkinId = skinId > 0 ? skinId : Value.Id;
    }

    private void DropItem(IActor capturer) {
        int rarity = PetRules.CaptureRarity(TamingRank);

        using GameStorage.Request db = Field.GameStorage.Context();
        Item? pet = Pet.Mutate(Pet.Metadata, rarity);
        pet.Stats = Field.ItemStatsCalc.GetStats(pet);

        pet = db.CreateItem(0, pet);
        if (pet == null) {
            return;
        }

        // The drop is the capturer's: only their client is told of it, and no one else may pick it up.
        long receiverId = capturer is FieldPlayer { Session.CharacterId: var id } ? id : 0;
        FieldItem fieldItem = Field.SpawnItem(this, pet, ItemDropKind.Pet, FieldItem.DefaultDurationMs, receiverId);
        if (capturer is FieldPlayer player) {
            // The capture counts for the pet catch conditions: the pet's item id (94000006 "Capture $item:61000153$").
            player.Session.ConditionUpdate(ConditionType.pet_catch_id, codeLong: pet.Id);
            // The pet's rarity (70600024 "Capture a normal pet" 1, 70600034 "Capture a rare pet" 2).
            player.Session.ConditionUpdate(ConditionType.pet_catch_grade, codeLong: pet.Rarity);
            // The pet's collection group (70600035 "Capture $petCollectGroup:2$ pets").
            player.Session.ConditionUpdate(ConditionType.pet_catch_category, codeLong: Metadata.CollectGroup);
            player.Session.Send(FieldPacket.DropItem(fieldItem));
            // The capture's exp: the common exp table's petTaming curve, named with the pet taming reason (3200).
            player.Session.Exp.AddExp(ExpType.petTaming);
            // The capture's pet taming mastery by the pet's rarity, with a running PetMastery event's capture rate,
            // named as a capture (3200) so the client shows the capture event's text.
            int captureRate = EventSaleRules.PetMasteryRates(player.Session.FindEvent(GameEventType.PetMastery)).Capture;
            player.Session.Mastery.Add(MasteryType.PetTaming,
                EventSaleRules.PetMasteryWithEvent(PetRules.CaptureMastery(pet.Rarity), captureRate), (int) ExpMessageCode.petTaming);
        } else {
            Field.Broadcast(FieldPacket.DropItem(fieldItem));
        }
    }
}
