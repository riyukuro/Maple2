using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class LoadUgcMapPacket {
    // The second int is the key the client finds a plot's owner by together with the plot number
    // (CubeMapInformationService f1b0, FindCubeOwner). Plot owners are sent keyed by plot number and apartment
    // number, and the server's apartment number is always 0 (ingest and home plots set it so), so 0 always finds them.
    public static ByteWriter Load(int cubeCount) {
        var pWriter = Packet.Of(SendOp.LoadUgcMap);
        pWriter.WriteInt(cubeCount);
        pWriter.WriteInt(); // apartment number
        pWriter.WriteBool(false);

        return pWriter;
    }

    public static ByteWriter LoadHome(int cubeCount, Home home) {
        var pWriter = Packet.Of(SendOp.LoadUgcMap);
        pWriter.WriteInt(cubeCount);
        pWriter.WriteInt(); // apartment number
        pWriter.WriteBool(true);
        pWriter.WriteClass<Home>(home);

        return pWriter;
    }
}
