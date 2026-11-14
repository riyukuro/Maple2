using System.Numerics;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record Portal(
    int Id,
    int TargetMapId,
    int TargetPortalId,
    PortalType Type,
    PortalActionType ActionType,
    Vector3 Position,
    Vector3 Rotation,
    Vector3 Dimension,
    float FrontOffset,
    int RandomRadius,
    bool Visible,
    bool MinimapVisible,
    bool Enable,
    // The xblock's PortalOffset: the client moves the portal's box centre by it, in world space (Unk_PortalBox,
    // portal.cpp:112). Optional, so a pack ingested before loads with none.
    Vector3 Offset = default
) : MapBlock;
