using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// The client's GM NPC create window. GM only. The NPC is a normal field NPC: it lives in this field
// instance only and goes with it. The client has no reply of its own for this packet.
public class RequestNpcCreateHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestNpcCreate;

    // Server rules; the window has no limit of its own.
    private const int MaxCount = 50;
    private const float MaxDistance = 3000f;

    public override void Handle(GameSession session, IByteReader packet) {
        if (!GmCheck.Allow(session, OpCode)) {
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
        if (!IsFinite(request.Position) || !IsFinite(request.Rotation)
            || Vector3.Distance(request.Position, session.Player.Position) > MaxDistance) {
            return;
        }

        int count = Math.Clamp(request.Count, 1, MaxCount);
        for (int i = 0; i < count; i++) {
            Vector3 position = request.Position;
            if (i > 0) {
                // Keep the rest from standing inside the first one.
                position += new Vector3(Random.Shared.NextSingle() * 200 - 100, Random.Shared.NextSingle() * 200 - 100, 0);
            }

            FieldNpc? npc = session.Field.SpawnNpc(metadata, position, request.Rotation);
            if (npc == null) {
                continue;
            }

            session.Field.Broadcast(FieldPacket.AddNpc(npc));
            session.Field.Broadcast(ProxyObjectPacket.AddNpc(npc));
            npc.Update(Environment.TickCount64);
        }
    }

    private static bool IsFinite(Vector3 value) {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
