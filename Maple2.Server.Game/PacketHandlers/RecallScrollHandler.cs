using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

/// <summary>
/// The answer to a recall notice: UIMessageBox::HandleRecallscrollconfirmButton sends the notice's
/// type (u8), the caller's name and the map id when the player accepts.
/// </summary>
public class RecallScrollHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RecallScroll;

    public override void Handle(GameSession session, IByteReader packet) {
        byte type = packet.ReadByte();
        string callerName = packet.ReadUnicodeString();
        int mapId = packet.ReadInt();

        if (type >= session.Recalls.Length) {
            return;
        }

        PendingRecall? recall;
        lock (session.Recalls) {
            recall = session.Recalls[type];
            if (recall == null || recall.CallerName != callerName || recall.MapId != mapId) {
                return;
            }
            session.Recalls[type] = null;
        }

        if (recall.EndTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
            return;
        }

        switch ((RecallScrollType) type) {
            case RecallScrollType.Party:
                if (session.Party.Party?.Members.ContainsKey(recall.CallerId) != true) {
                    return;
                }
                break;
            default:
                return;
        }

        // The caller must still be on the map the notice named.
        if (!session.PlayerInfo.GetOrFetch(recall.CallerId, out PlayerInfo? callerInfo) || !callerInfo.Online || callerInfo.MapId != recall.MapId) {
            return;
        }

        if (!PlayerWarpUtil.GoToPlayer(session, callerInfo)) {
            Logger.Warning("Recall of {CharacterId} to {CallerId} failed", session.CharacterId, recall.CallerId);
        }
    }
}
