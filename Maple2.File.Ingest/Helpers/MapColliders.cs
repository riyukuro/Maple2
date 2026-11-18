using Maple2.File.Flat;
using Maple2.File.Flat.maplestory2library;
using Maple2.File.Flat.physxmodellibrary;
using Maple2.File.Flat.standardmodellibrary;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;
using Maple2.Tools.VectorMath;
using System.Numerics;

namespace Maple2.File.Ingest.Helpers;

/// <summary>
/// The collision a map entity makes, as a <see cref="FieldEntity"/>. Shared by <c>MapDataMapper</c> (the field's
/// collision data) and <see cref="SpawnGround"/> (the ground under a spawn point), so both read the same colliders.
/// </summary>
public static class MapColliders {
    private const float BLOCK_SIZE = (float) Constant.BlockSize;
    private const float HALF_BLOCK = 0.5f * BLOCK_SIZE;
    // The client's PhysX groups for box colliders: a whitebox defaults to 3 unless its PropCollisionGroup says otherwise
    // (WorldService::OnPhysXProp, worldservice_1.cpp:3748-3756), a cube's GeneratePhysX box is always 9
    // (CubeMapInformationService::GeneratePhysX, cubemapinformationservice_3.cpp:4342).
    private const ushort WHITEBOX_GROUP = 3;
    private const ushort GENERATE_PHYSX_GROUP = 9;

    /// <summary>The entity's world transform: rotation and scale about its position.</summary>
    public static Matrix4x4 EntityTransform(IPlaceable placeable, Transform transform) {
        transform.Transformation = Matrix4x4.Identity;
        transform.Position = placeable.Position;
        transform.RotationAnglesDegrees = placeable.Rotation;
        transform.Scale = placeable.Scale;
        return transform.Transformation;
    }

    /// <summary>
    /// The collider for <paramref name="entity"/>, or null when it makes none (breakables, disabled collision, a mesh
    /// without a known NIF). Its <c>Bounds</c> are in world space. A vibrate entity takes <paramref name="vibrateIndex"/>;
    /// the caller advances it when the result is a <see cref="FieldVibrateEntity"/>. Unknown NIF assets are reported once
    /// per set when the sets are given. With <paramref name="standing"/>, a breakable, a vibrate entity and a sellable
    /// tile make the box or mesh collider their map properties give, as any other entity does: the collision they have
    /// while they stand, before they break or are sold.
    /// </summary>
    public static FieldEntity? Create(IMapEntity entity, IPlaceable placeable, Matrix4x4 transformation, int vibrateIndex,
                                      ISet<string>? invalidLlids = null, ISet<uint>? missingLlids = null, bool standing = false) {
        var entityBounds = new BoundingBox3();

        bool isHexId = entity.EntityId.Length == 32;

        for (int i = 0; isHexId && i < entity.EntityId.Length; ++i) {
            isHexId = entity.EntityId[i].IsHexDigit();
        }

        FieldEntity? fieldEntity;
        var entityId = new FieldEntityId(0, 0, string.Empty);

        if (isHexId) {
            entityId = FieldEntityId.FromString(entity.EntityId);
        }

        switch (entity) {
            /*
                PhysXProp                               | WhiteboxCube
                PhysXProp, MS2MapProperties, MS2Vibrate | PhysXCube, DoesMakeTok
                MS2MapProperties                        | PhysXCube
                MS2MapProperties, MS2Vibrate            | None
                MS2MapProperties, MS2Breakable          | PhysXCube, NxCube, BothCube, OnlyNxCube
                MS2Breakable                            | NxCube
             */
            case IMS2Breakable when !standing:
                return null; // intentionally skip breakables. these are dynamic so should be handled at run time
            case IPhysXWhitebox whitebox:
                entityBounds.Min = -new Vector3(whitebox.ShapeDimensions.X, whitebox.ShapeDimensions.Y, 0.5f * whitebox.ShapeDimensions.Z);
                entityBounds.Max = new Vector3(whitebox.ShapeDimensions.X, whitebox.ShapeDimensions.Y, 0.5f * whitebox.ShapeDimensions.Z);
                fieldEntity = new FieldBoxColliderEntity(
                    Id: entityId,
                    Position: placeable.Position - new Vector3(0, 0, 0.5f * whitebox.ShapeDimensions.Z),
                    Rotation: placeable.Rotation,
                    Scale: placeable.Scale,
                    Bounds: BoundingBox3.Transform(entityBounds, transformation),
                    Size: whitebox.ShapeDimensions,
                    IsWhiteBox: true,
                    IsFluid: false,
                    MapAttribute: MapAttribute.none,
                    CollisionGroup: whitebox is IPhysXProp whiteboxProp ? whiteboxProp.PropCollisionGroup : WHITEBOX_GROUP);
                return fieldEntity;
            case IMesh mesh:
                if (!standing && entity is IMS2Vibrate { Enabled: true } vibrate) {
                    entityBounds.Min = -new Vector3(HALF_BLOCK, HALF_BLOCK, 0);
                    entityBounds.Max = new Vector3(HALF_BLOCK, HALF_BLOCK, BLOCK_SIZE);

                    fieldEntity = new FieldVibrateEntity(
                        Id: entityId,
                        Position: placeable.Position,
                        Rotation: placeable.Rotation,
                        Scale: placeable.Scale,
                        Bounds: BoundingBox3.Transform(entityBounds, transformation),
                        BreakDefense: vibrate.brokenDefence,
                        BreakTick: vibrate.brokenTick,
                        VibrateIndex: vibrateIndex,
                        HideGroupId: vibrate.hideGroupId,
                        MaterialTag: vibrate.materialTag ?? string.Empty,
                        ShapeDimensions: vibrate.ShapeDimensions);

                    return fieldEntity;
                }

                if (!standing && entity is IMS2CubeProp cube && cube.CubeSalableGroup != 0) {
                    entityBounds.Min = -new Vector3(HALF_BLOCK, HALF_BLOCK, 0);
                    entityBounds.Max = new Vector3(HALF_BLOCK, HALF_BLOCK, BLOCK_SIZE);
                    fieldEntity = new FieldSellableTile(
                        Id: entityId,
                        Position: placeable.Position,
                        Rotation: placeable.Rotation,
                        Scale: placeable.Scale,
                        Bounds: BoundingBox3.Transform(entityBounds, transformation),
                        SellableGroup: cube.CubeSalableGroup
                    );

                    return fieldEntity;
                }

                bool isFluid = false;
                var attribute = MapAttribute.none;
                if (entity is IMS2MapProperties meshMapProperties) {
                    if (meshMapProperties.DisableCollision) {
                        return null;
                    }

                    isFluid = meshMapProperties.CubeType == "Fluid";
                    attribute = Enum.TryParse(meshMapProperties.MapAttribute, out MapAttribute mapAttribute) ? mapAttribute : MapAttribute.none;
                    if (meshMapProperties.GeneratePhysX) {
                        var meshPhysXDimension = new Vector3(BLOCK_SIZE, BLOCK_SIZE, BLOCK_SIZE);

                        if (meshMapProperties.GeneratePhysXDimension != Vector3.Zero) {
                            meshPhysXDimension = meshMapProperties.GeneratePhysXDimension;
                        }

                        entityBounds.Min = -new Vector3(0.5f * meshPhysXDimension.X, 0.5f * meshPhysXDimension.Y, 0);
                        entityBounds.Max = new Vector3(0.5f * meshPhysXDimension.X, 0.5f * meshPhysXDimension.Y, meshPhysXDimension.Z);

                        fieldEntity = new FieldBoxColliderEntity(
                            Id: entityId,
                            Position: placeable.Position,
                            Rotation: placeable.Rotation,
                            Scale: placeable.Scale,
                            Bounds: BoundingBox3.Transform(entityBounds, transformation),
                            Size: meshPhysXDimension,
                            IsWhiteBox: false,
                            IsFluid: isFluid,
                            MapAttribute: attribute,
                            CollisionGroup: GENERATE_PHYSX_GROUP);

                        return fieldEntity;
                    }
                }

                if (mesh.NifAsset.Length < 9 || !mesh.NifAsset[..9].Equals("urn:llid:", StringComparison.CurrentCultureIgnoreCase)) {
                    if (invalidLlids?.Add(mesh.NifAsset) == true) {
                        Console.WriteLine($"Non llid NifAsset: '{mesh.NifAsset}'");
                    }

                    return null;
                }

                // require length of "urn:llid:XXXXXXXX"
                if (mesh.NifAsset.Length < 9 + 8) {
                    return null;
                }

                uint llid = Convert.ToUInt32(mesh.NifAsset.Substring(mesh.NifAsset.LastIndexOf(':') + 1, 8), 16);

                if (!NifParserHelper.nifBounds.TryGetValue(llid, out entityBounds)) {
                    if (missingLlids?.Add(llid) == true) {
                        Console.WriteLine($"NIF with LLID {llid:X} not found");
                    }

                    return null;
                }

                if (isFluid && mesh is IMS2MapProperties fluidMapProperties) {
                    fieldEntity = new FieldFluidEntity(
                        Id: entityId,
                        Position: placeable.Position,
                        Rotation: placeable.Rotation,
                        Scale: placeable.Scale,
                        LiquidType: Enum.TryParse(fluidMapProperties.MapAttribute, out LiquidType liquidType) ? liquidType : LiquidType.none,
                        Bounds: BoundingBox3.Transform(entityBounds, transformation),
                        MeshLlid: llid,
                        IsShallow: false,
                        IsSurface: true,
                        MapAttribute: attribute);
                    return fieldEntity;
                }

                fieldEntity = new FieldMeshColliderEntity(
                    Id: entityId,
                    Position: placeable.Position,
                    Rotation: placeable.Rotation,
                    Scale: placeable.Scale,
                    Bounds: BoundingBox3.Transform(entityBounds, transformation),
                    MeshLlid: llid,
                    MapAttribute: attribute);
                return fieldEntity;
            case IMS2MapProperties mapProperties: // GeneratePhysX
                if (mapProperties.DisableCollision) {
                    return null;
                }

                if (!mapProperties.GeneratePhysX) {
                    return null;
                }

                var physXDimension = new Vector3(BLOCK_SIZE, BLOCK_SIZE, BLOCK_SIZE);

                if (mapProperties.GeneratePhysXDimension != Vector3.Zero) {
                    physXDimension = mapProperties.GeneratePhysXDimension;
                }

                entityBounds.Min = -new Vector3(0.5f * physXDimension.X, 0.5f * physXDimension.Y, 0);
                entityBounds.Max = new Vector3(0.5f * physXDimension.X, 0.5f * physXDimension.Y, physXDimension.Z);

                fieldEntity = new FieldBoxColliderEntity(
                    Id: entityId,
                    Position: placeable.Position,
                    Rotation: placeable.Rotation,
                    Scale: placeable.Scale,
                    Bounds: BoundingBox3.Transform(entityBounds, transformation),
                    Size: physXDimension,
                    IsWhiteBox: false,
                    IsFluid: false,
                    MapAttribute: MapAttribute.none,
                    CollisionGroup: GENERATE_PHYSX_GROUP);
                return fieldEntity;
            default:
                return null;
        }

    }
}
