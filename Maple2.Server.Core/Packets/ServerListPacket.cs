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
    // charCnt, uiserverlistdialog.cpp:483), the addresses, the traffic percent and the channels.
    public static ByteWriter Load(string serverName, IList<IPEndPoint> serverIps, ICollection<int> channels, int characters) {
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
        pWriter.WriteInt(100); // Const

        // Channels
        pWriter.Write<ushort>((ushort) channels.Count);
        // I think this should be sorted by population
        foreach (int channel in channels) {
            pWriter.WriteShort((short) channel);
        }

        return pWriter;
    }
}
