using System.Net;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;

namespace Maple2.Server.Core.Packets;

public static class ServerListPacket {
    private enum Command : byte {
        Error = 0,
        Load = 1,
    }

    public static ByteWriter Error() {
        var pWriter = Packet.Of(SendOp.ServerList);
        pWriter.WriteString("dev"); // the client reads this string before the result byte (as in Load)
        pWriter.Write<Command>(Command.Error);

        return pWriter;
    }

    // UIServerListDialog::ReceiveServerList (uiserverlistdialog.cpp:310): the login scene reads the byte after the
    // string as a result (above 0 opens the server list), the dialog as the number of servers. Per server: the id
    // (looked up in the client's server list table), the name, the account's character count on it (the list's
    // charCnt, uiserverlistdialog.cpp:483), the addresses, the traffic percent (trafficPercent, :484) and the channels.
    public static ByteWriter Load(string serverName, IList<IPEndPoint> serverIps, ICollection<int> channels, int characters, int online = 0) {
        var pWriter = Packet.Of(SendOp.ServerList);
        pWriter.WriteString("dev"); // env (Live, Staging, qa, dev)
        pWriter.Write<Command>(Command.Load); // one server
        pWriter.WriteInt(1); // server id
        pWriter.WriteUnicodeString(serverName);
        pWriter.WriteByte((byte) Math.Clamp(characters, 0, byte.MaxValue));
        pWriter.Write<ushort>((ushort) serverIps.Count);
        foreach (IPEndPoint endpoint in serverIps) {
            pWriter.WriteUnicodeString(endpoint.Address.ToString());
            pWriter.Write<ushort>((ushort) endpoint.Port);
        }
        pWriter.WriteInt(TrafficPercent(online, channels.Count, Target.ChannelCapacity));

        // Channels
        pWriter.Write<ushort>((ushort) channels.Count);
        // I think this should be sorted by population
        foreach (int channel in channels) {
            pWriter.WriteShort((short) channel);
        }

        return pWriter;
    }

    // The server's load: the characters online over what its channels hold, rounded up, 0 to 100. The list's
    // ServerRenderer.setData (uiserverlistdialog.gfx) draws 0 as comfort, 1-25 as normal, 26-90 as busy and above
    // 90 as full; the fixed 100 sent before showed every server as full.
    public static int TrafficPercent(int online, int channels, int capacity) {
        long room = (long) Math.Max(channels, 0) * Math.Max(capacity, 0);
        if (online <= 0 || room <= 0) {
            return 0;
        }
        return (int) Math.Min(100, (online * 100L + room - 1) / room);
    }
}
