using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Tests.Core;

// The bytes are the client's sender: WriteLoginA (commonfiles/classobject.cpp:112), called by the login dialog
// (type 1) and the server list (type 2).
public class ResponseLoginDataTests {
    private static readonly byte[] MachineId = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    private static byte[] ClientBytes(byte type, string user, string password, short loginType, int machineValue, int clientValue, string language) {
        var writer = new ByteWriter();
        writer.WriteByte(type);
        writer.WriteUnicodeString(user);
        writer.WriteUnicodeString(password);
        writer.WriteShort(loginType);
        writer.WriteBytes(MachineId);
        writer.WriteInt(machineValue);
        writer.WriteInt(clientValue);
        writer.WriteString(language);
        return writer.Buffer.AsSpan(0, writer.Length).ToArray();
    }

    [TestCase("a", "b", "K", 38)]
    [TestCase("a", "b", "KR", 39)]
    [TestCase("", "", "", 33)]
    public void Read_ConsumesEveryByteTheClientWrites(string user, string password, string language, int size) {
        byte[] bytes = ClientBytes(2, user, password, 7, 0x11223344, 0x55667788, language);
        Assert.That(bytes, Has.Length.EqualTo(size));

        var reader = new ByteReader(bytes, 0);
        ResponseLoginData data = ResponseLoginData.Read(reader);

        Assert.That(reader.Position, Is.EqualTo(bytes.Length));
        Assert.That(data.Type, Is.EqualTo(2));
        Assert.That(data.User, Is.EqualTo(user));
        Assert.That(data.Password, Is.EqualTo(password));
        Assert.That(data.LoginType, Is.EqualTo(7));
        Assert.That(data.MachineId, Is.EqualTo(new Guid(MachineId)));
        Assert.That(data.MachineValue, Is.EqualTo(0x11223344));
        Assert.That(data.ClientValue, Is.EqualTo(0x55667788));
        Assert.That(data.Language, Is.EqualTo(language));
    }

    [Test]
    public void Read_KnownBytes() {
        byte[] bytes = [
            1, // type
            1, 0, (byte) 'a', 0, // id
            1, 0, (byte) 'b', 0, // password
            0, 0, // login type
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, // machine id
            9, 0, 0, 0, // machine value
            3, 0, 0, 0, // client value
            2, 0, (byte) 'K', (byte) 'R', // language
        ];
        var reader = new ByteReader(bytes, 0);
        ResponseLoginData data = ResponseLoginData.Read(reader);

        Assert.That(reader.Position, Is.EqualTo(39));
        Assert.That(data, Is.EqualTo(new ResponseLoginData(1, "a", "b", 0, new Guid(MachineId), 9, 3, "KR")));
    }
}
