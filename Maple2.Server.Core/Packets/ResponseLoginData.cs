using Maple2.PacketLib.Tools;

namespace Maple2.Server.Core.Packets;

/// <summary>
/// The body of c2s ResponseLogin as the client writes it (WriteLoginA, commonfiles/classobject.cpp:112): byte type,
/// string id, string password, short login type (0 from the login dialog, the channel from the server list),
/// 16 bytes machine id, int machine value, int client value, narrow string language.
/// </summary>
public readonly record struct ResponseLoginData(byte Type, string User, string Password, short LoginType, Guid MachineId,
    int MachineValue, int ClientValue, string Language) {
    public static ResponseLoginData Read(IByteReader packet) {
        byte type = packet.ReadByte();
        string user = packet.ReadUnicodeString();
        string password = packet.ReadUnicodeString();
        short loginType = packet.ReadShort();
        var machineId = new Guid(packet.ReadBytes(16));
        int machineValue = packet.ReadInt();
        int clientValue = packet.ReadInt();
        string language = packet.ReadString();

        return new ResponseLoginData(type, user, password, loginType, machineId, machineValue, clientValue, language);
    }
}
