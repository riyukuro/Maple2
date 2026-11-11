using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class StateFishingPacket {
    // State numbers of the client's player state machine.
    public const int STATE_IDLE = 1;
    public const int STATE_FISHING = 45;

    // A player whose guide object is the fishing one is fishing: a player entering the field is sent state 45
    // for them after their AddPlayer (the client looks the player up by object id and drops the packet otherwise).
    public static bool IsFishing(GuideObjectType guideType) {
        return guideType == GuideObjectType.Fishing;
    }

    // Client: CSceneStateGame::OnStateFishingData (scenestategame.cpp:8510): int player object id, int state,
    // int sub state. The client puts that player in the state, with the fishing state data attached. A fishing
    // player enters state 45 with sub state 0 (fishingmanager.cpp:1306) and leaves to state 1 (:1336).
    public static ByteWriter State(int objectId, int state, int subState = 0) {
        var pWriter = Packet.Of(SendOp.StateFishingData);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(state);
        pWriter.WriteInt(subState);

        return pWriter;
    }
}
