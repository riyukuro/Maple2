using System.Numerics;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record NpcMetadata(
    int Id,
    string? Name,
    string AiPath,
    NpcMetadataModel Model,
    NpcMetadataStat Stat,
    NpcMetadataBasic Basic,
    NpcMetadataDistance Distance,
    NpcMetadataSkill Skill,
    NpcMetadataProperty Property,
    NpcMetadataDropInfo DropInfo,
    NpcMetadataAction Action,
    NpcMetadataDead Dead,
    NpcMetadataCorpse? Corpse,
    NpcMetadataLookAtTarget LookAtTarget,
    NpcMetadataInteract? Interact = null) : ISearchResult;

public record NpcMetadataModel(
    string Name,
    float Scale,
    float AniSpeed);

public record NpcMetadataStat(
    IReadOnlyDictionary<BasicAttribute, long> Stats,
    float[] ScaleStatRate,
    long[] ScaleBaseTap,
    long[] ScaleBaseDef,
    float[] ScaleBaseSpaRate);

public record NpcMetadataDistance(
    float Avoid,
    float Sight,
    float SightHeightUp,
    float SightHeightDown,
    float LastSightRadius,
    float LastSightHeightUp,
    float LastSightHeightDown);

public record NpcMetadataSkill(
    NpcMetadataSkill.Entry[] Entries,
    int Cooldown) {

    /// Prob is the npc's own weight for the skill (npc xml skill probs), used by AI select nodes with useNpcProb="1".
    public record Entry(
        int Id,
        short Level,
        int Prob = 0);
}

public record NpcMetadataBasic(
    int Friendly,
    int AttackGroup,
    int DefenseGroup,
    int Kind,
    int ShopId,
    int HitImmune,
    int AbnormalImmune,
    short Level,
    int Class,
    bool RotationDisabled,
    int MaxSpawnCount,
    int GroupSpawnCount,
    int RareDegree,
    int Difficulty,
    string[] MainTags,
    string[] SubTags,
    long CustomExp,
    // Names from the npc xml's propertyTags list. Null in rows ingested before the field existed.
    string[]? PropertyTags = null,
    // The aiInfo summonNpcIDs list: the npc ids an AI summon node's npcId (1-based) picks from. Null in rows
    // ingested before the field existed.
    int[]? SummonNpcIds = null,
    // The basic stringID (a boss name shared by its variants, read by npc_is_dead_by_string_id). Null when the
    // row has none or was ingested before the field existed.
    string? StringId = null,
    // The played client has no NPC data for this NPC (PlayedClientNpcs): the server does not spawn it. False
    // without a played client or in rows ingested before the field existed.
    bool NotInPlayedClient = false,
    // The basic raceString list (animal, plant, spirit...): the codes of the npc_race trophies. Null in rows
    // ingested before the field existed.
    string[]? RaceStrings = null) {
    public bool HasPropertyTag(string tag) => PropertyTags != null && PropertyTags.Contains(tag, StringComparer.OrdinalIgnoreCase);
}

public record NpcMetadataProperty(
    NpcMetadataBuff[] Buffs,
    NpcMetadataCapsule Capsule,
    NpcMetadataCollision? Collision);


public record NpcMetadataBuff(int Id, int Level);

public record NpcMetadataCapsule(float Radius, float Height);

public record NpcMetadataCollision(Vector3 Dimensions, Vector3 Offset);

public record NpcMetadataAction(
    float RotateSpeed,
    float WalkSpeed,
    float RunSpeed,
    NpcAction[] Actions,
    int MoveArea,
    string MaidExpired);

public record NpcAction(string Name, int Probability);

public record NpcMetadataDropInfo(
    float DropDistanceBase,
    int DropDistanceRandom,
    int[] GlobalDropBoxIds,
    int[] DeadGlobalDropBoxIds,
    int[] IndividualDropBoxIds,
    int[] GlobalHitDropBoxIds,
    int[] IndividualHitDropBoxIds);

public record NpcMetadataDead(
    float Time,
    int Revival,
    int Count,
    float LifeTime,
    int ExtendRoomTime);

public record NpcMetadataCorpse(
    float Width,
    float Height,
    float Depth,
    float Added,
    float OffsetNametag,
    string CorpseEffect,
    bool HitAble,
    Vector3 Rotation);

// The <interact> row of the npc data (CNpcData +0x458, commonfiles/npcdata.cpp:1595): the npc casts SkillId at
// SkillLevel when a player has cast at it for CastingTime ms, then is not talkable for CoolTime ms.
public record NpcMetadataInteract(
    int SkillId,
    short SkillLevel,
    int CastingTime,
    int CoolTime);

public record NpcMetadataLookAtTarget(
    string TargetDummy,
    bool LookAtMyPlayerWhenTalking,
    bool UseTalkMotion);

