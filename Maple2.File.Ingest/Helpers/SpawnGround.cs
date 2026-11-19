using Maple2.File.Flat;
using Maple2.File.Flat.maplestory2library;
using Maple2.File.Flat.standardmodellibrary;
using Maple2.File.IO.Nif;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Tools.VectorMath;
using System.Numerics;

namespace Maple2.File.Ingest.Helpers;

/// <summary>
/// The ground under a map's spawn points. The official server places an npc on the ground below its spawn point, 1 unit
/// down: the first collision surface met going down from half a cube above the point. The client draws a field npc at
/// the position it is sent, with no snap of its own, so a spawn point that sits above its floor makes a floating npc
/// unless the server moves it down.
/// </summary>
/// <remarks>
/// The colliders are the ones <see cref="MapColliders"/> makes for the map data, standing: PhysXWhitebox boxes,
/// GeneratePhysX boxes and the NIF PhysX meshes of mesh colliders, breakables, vibrate entities and sellable tiles
/// included, as the client collides with them until they break. Entities with DisableCollision set count too, with the
/// collider they would have without it: a spawn point over such a floor (a grating, an invisible box) stands on it.
/// Fluids are left out. A box is solid seen from outside; a mesh triangle counts only from its front,
/// (v1 - v0) x (v2 - v0), as no map mesh is cooked double-sided.
/// STILL-OPEN: which collision the official server reads (its own data, not the client's PhysX), and why a few points
/// stand on a surface other than the first one (a box the point starts inside, a vibrate prop passed through), and why
/// the official z is a whole number 1 lower than this on MS2CubeProp deform bridge and piece cubes (ground - 2) and 3
/// lower on vibrate steel stairs. The z placed here is not rounded.
/// </remarks>
public sealed class SpawnGround {
    /// <summary>How far above a spawn point the ground is looked for: half a cube.</summary>
    public const float REACH_UP = 75f;
    /// <summary>How far below a spawn point the ground is looked for; a point with no ground within it keeps its z.</summary>
    public const float REACH_DOWN = 1500f;
    /// <summary>How far under the ground the npc is placed.</summary>
    public const float BELOW_GROUND = 1f;

    private sealed record Box(IMapEntity Source, Matrix4x4 ToLocal, Vector3 Min, Vector3 Max, BoundingBox3 Bounds);
    private sealed record Mesh(IMapEntity Source, uint Llid, Matrix4x4 Transform, BoundingBox3 Bounds);

    private readonly List<Box> boxes = [];
    private readonly List<Mesh> meshes = [];
    private readonly Dictionary<Mesh, Vector3[]> posed = [];

    // Each NIF's PhysX triangles in model space (PhysXToWorldScale * ActorPose * ShapeLocalPose), and their bounds.
    private static readonly Dictionary<uint, (Vector3[] Triangles, BoundingBox3 Bounds)?> ModelTriangles = [];

    public SpawnGround(IEnumerable<IMapEntity> entities) {
        var transform = new Transform();
        foreach (IMapEntity entity in entities) {
            if (entity is not IPlaceable placeable) {
                continue;
            }

            Matrix4x4 transformation = MapColliders.EntityTransform(placeable, transform);
            switch (MapColliders.Create(entity, placeable, transformation, vibrateIndex: 0, standing: true,
                                         disabledCollision: true)) {
                case FieldBoxColliderEntity { IsFluid: false, IsWhiteBox: true } whitebox:
                    // A box of ShapeDimensions centred on the entity, turned and scaled with it.
                    AddBox(entity, transformation, -0.5f * whitebox.Size, 0.5f * whitebox.Size);
                    break;
                case FieldBoxColliderEntity { IsFluid: false } generated:
                    // A box of GeneratePhysXDimension standing on the entity position, turned about Z only, not scaled
                    // (CubeMapInformationService::GeneratePhysX, cubemapinformationservice_3.cpp:4307-4345).
                    Matrix4x4 standing = Matrix4x4.CreateRotationZ(placeable.Rotation.Z * MathF.PI / 180f)
                                         * Matrix4x4.CreateTranslation(placeable.Position);
                    Vector3 half = 0.5f * generated.Size;
                    AddBox(entity, standing, new Vector3(-half.X, -half.Y, 0), new Vector3(half.X, half.Y, generated.Size.Z));
                    break;
                case FieldMeshColliderEntity mesh:
                    if (Model(mesh.MeshLlid) is { } model) {
                        meshes.Add(new Mesh(entity, mesh.MeshLlid, transformation, BoundingBox3.Transform(model.Bounds, transformation)));
                    }
                    break;
            }
        }
    }

    private void AddBox(IMapEntity source, Matrix4x4 toWorld, Vector3 min, Vector3 max) {
        if (!Matrix4x4.Invert(toWorld, out Matrix4x4 toLocal)) {
            return;
        }
        boxes.Add(new Box(source, toLocal, min, max, BoundingBox3.Transform(new BoundingBox3(min, max), toWorld)));
    }

    /// <summary>The z of the first surface met going down from <see cref="REACH_UP"/> above the point to
    /// <see cref="REACH_DOWN"/> below it, or null when there is none.</summary>
    public float? Ground(Vector3 point) => Hit(point)?.Z;

    /// <summary>The first surface met going down from <see cref="REACH_UP"/> above the point to <see cref="REACH_DOWN"/>
    /// below it: its z and the map entity it belongs to; null when there is none.</summary>
    public (float Z, IMapEntity Source)? Hit(Vector3 point) {
        float top = point.Z + REACH_UP;
        float bottom = point.Z - REACH_DOWN;
        (float Z, IMapEntity Source)? best = null;

        foreach (Box box in boxes) {
            if (!Under(box.Bounds, point, top, bottom)) {
                continue;
            }
            if (BoxEntry(box, new Vector3(point.X, point.Y, top), top - bottom) is float z && (best is null || z > best.Value.Z)) {
                best = (z, box.Source);
            }
        }

        foreach (Mesh mesh in meshes) {
            if (!Under(mesh.Bounds, point, top, bottom)) {
                continue;
            }
            Vector3[] triangles = Posed(mesh);
            for (int i = 0; i + 2 < triangles.Length; i += 3) {
                if (FrontFromAbove(triangles[i], triangles[i + 1], triangles[i + 2], point.X, point.Y) is float z
                    && z <= top && z >= bottom && (best is null || z > best.Value.Z)) {
                    best = (z, mesh.Source);
                }
            }
        }

        return best;
    }

    /// <summary>Where the server places an npc spawned at <paramref name="point"/>.</summary>
    public Vector3 Place(Vector3 point) {
        return Ground(point) is float z ? point with { Z = z - BELOW_GROUND } : point;
    }

    private static bool Under(BoundingBox3 bounds, Vector3 point, float top, float bottom) {
        return point.X >= bounds.Min.X && point.X <= bounds.Max.X && point.Y >= bounds.Min.Y && point.Y <= bounds.Max.Y
               && bounds.Max.Z >= bottom && bounds.Min.Z <= top;
    }

    // The point where a ray going straight down from `from` enters the box, as a world z; none when it starts inside.
    private static float? BoxEntry(Box box, Vector3 from, float length) {
        Vector3 origin = Vector3.Transform(from, box.ToLocal);
        Vector3 direction = Vector3.TransformNormal(new Vector3(0, 0, -length), box.ToLocal);
        float enter = float.NegativeInfinity, exit = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++) {
            float o = origin[axis], d = direction[axis], min = box.Min[axis], max = box.Max[axis];
            if (MathF.Abs(d) < 1e-9f) {
                if (o < min || o > max) {
                    return null;
                }
                continue;
            }
            float t0 = (min - o) / d, t1 = (max - o) / d;
            if (t0 > t1) {
                (t0, t1) = (t1, t0);
            }
            enter = MathF.Max(enter, t0);
            exit = MathF.Min(exit, t1);
        }
        if (enter < 0 || enter > 1 || enter > exit) {
            return null;
        }
        return from.Z - enter * length;
    }

    // The z where a vertical line through (x, y) meets the triangle, when the triangle faces up (its front seen from
    // above); none for a triangle facing down or on edge.
    private static float? FrontFromAbove(Vector3 a, Vector3 b, Vector3 c, float x, float y) {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.Z <= 0) {
            return null;
        }
        float d0 = Edge(a, b, x, y), d1 = Edge(b, c, x, y), d2 = Edge(c, a, x, y);
        if (d0 < 0 || d1 < 0 || d2 < 0) {
            return null;
        }
        return a.Z - (normal.X * (x - a.X) + normal.Y * (y - a.Y)) / normal.Z;
    }

    private static float Edge(Vector3 from, Vector3 to, float x, float y) {
        return (to.X - from.X) * (y - from.Y) - (to.Y - from.Y) * (x - from.X);
    }

    private Vector3[] Posed(Mesh mesh) {
        lock (posed) {
            if (posed.TryGetValue(mesh, out Vector3[]? cached)) {
                return cached;
            }
        }
        Vector3[] model = Model(mesh.Llid)!.Value.Triangles;
        var world = new Vector3[model.Length];
        for (int i = 0; i < model.Length; i++) {
            world[i] = Vector3.Transform(model[i], mesh.Transform);
        }
        lock (posed) {
            posed[mesh] = world;
        }
        return world;
    }

    private static (Vector3[] Triangles, BoundingBox3 Bounds)? Model(uint llid) {
        lock (ModelTriangles) {
            if (ModelTriangles.TryGetValue(llid, out (Vector3[], BoundingBox3)? cached)) {
                return cached;
            }
        }

        (Vector3[], BoundingBox3)? result = null;
        if (NifParserHelper.nifDocuments.TryGetValue(llid, out NifDocument? document)) {
            var triangles = new List<Vector3>();
            foreach (NiPhysXProp prop in document.PhysXProps) {
                if (prop.Snapshot is null) {
                    continue;
                }
                foreach (NiPhysXActorDesc actor in prop.Snapshot.Actors) {
                    foreach (NiPhysXShapeDesc shape in actor.ShapeDescriptions) {
                        if (shape.Mesh is null) {
                            continue;
                        }
                        var physXMesh = new PhysXMesh(shape.Mesh.MeshData);
                        Matrix4x4 pose = shape.LocalPose * actor.Poses[0] * Matrix4x4.CreateScale(prop.PhysXToWorldScale);
                        foreach (var face in physXMesh.Faces) {
                            triangles.Add(Vector3.Transform(physXMesh.Vertices[(int) face.Vert0], pose));
                            triangles.Add(Vector3.Transform(physXMesh.Vertices[(int) face.Vert1], pose));
                            triangles.Add(Vector3.Transform(physXMesh.Vertices[(int) face.Vert2], pose));
                        }
                    }
                }
            }
            if (triangles.Count > 0) {
                result = (triangles.ToArray(), BoundingBox3.Compute(triangles));
            }
        }

        lock (ModelTriangles) {
            ModelTriangles[llid] = result;
        }
        return result;
    }
}
