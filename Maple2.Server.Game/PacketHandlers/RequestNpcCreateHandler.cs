using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// The client's GM NPC create window. GM only. The NPC is a normal field NPC: it lives in this field
// instance only and goes with it. The client has no reply of its own for this packet.
public class RequestNpcCreateHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestNpcCreate;

    // Server rules; the window has no limit of its own.
    private const float MaxDistance = 3000f;

    public override void Handle(GameSession session, IByteReader packet) {
        if (!GmCheck.Allow(session, OpCode, GmCreateRules.NpcPermission)) {
            return;
        }

        GmNpcCreateRequest request = GmNpcCreateRequest.Read(packet);
        if (session.Field == null) {
            return;
        }
        if (request.Pet) {
            // The pet button: a pet needs its item, which the packet does not carry. Not handled.
            Logger.Information("RequestNpcCreate: pet summon of {NpcId} is not handled", request.NpcId);
            return;
        }
        if (!session.NpcMetadata.TryGet(request.NpcId, out NpcMetadata? metadata)) {
            Logger.Warning("RequestNpcCreate: unknown npc {NpcId} from account {AccountId}", request.NpcId, session.AccountId);
            return;
        }
        if (!PlayedClientNpcs.CanSpawn(metadata)) {
            Logger.Information("RequestNpcCreate: npc {NpcId} is not in the played client's NPC data, account {AccountId}", request.NpcId, session.AccountId);
            return;
        }
        if (!IsFinite(request.Position) || !IsFinite(request.Rotation)
            || Vector3.Distance(request.Position, session.Player.Position) > MaxDistance) {
            return;
        }

        int spawned = session.Field.SpawnGmNpcs(metadata, request.Count, i => {
            // Keep the rest from standing inside the first one.
            return i == 0 ? request.Position
                : request.Position + new Vector3(Random.Shared.NextSingle() * 200 - 100, Random.Shared.NextSingle() * 200 - 100, 0);
        }, request.Rotation);
        if (spawned == 0) {
            Logger.Warning("RequestNpcCreate: no npc {NpcId} spawned (field holds {Max} GM-spawned NPCs at most), account {AccountId}",
                request.NpcId, GmCreateRules.MaxNpcPerField, session.AccountId);
        }
    }

    private static bool IsFinite(Vector3 value) {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
