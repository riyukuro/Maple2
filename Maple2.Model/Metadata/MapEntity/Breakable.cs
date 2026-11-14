using System.Numerics;

namespace Maple2.Model.Metadata;

public record Breakable(
    bool Visible,
    int Id,
    int HideTime,
    int ResetTime,
    int GlobalDropBoxId,
    float MoveTimeOffset,
    Vector3 Position,
    Vector3 Rotation,
    // moveDistance: how far the client moves the object from its position along its move direction
    // (CBreakableObject, breakableobject.cpp:424, :596-605); ShapeDimensions: the box its hits meet (m_ptSize).
    float MoveDistance = 0,
    Vector3 ShapeDimensions = default)
: MapBlock;

public record BreakableActor(
    bool Visible,
    int Id,
    int HideTime,
    int ResetTime,
    int GlobalDropBoxId,
    Vector3 Position,
    Vector3 Rotation,
    float MoveTimeOffset = 0,
    float MoveDistance = 0,
    Vector3 ShapeDimensions = default)
: MapBlock;
