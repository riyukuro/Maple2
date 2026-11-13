using Maple2.PacketLib.Tools;

namespace Maple2.Model.Game;

// The platform part of a user's data, after the mastery levels. The client reads it through the platform proxy it makes
// for its region (CreatePlatformProxy, the region it reports in ResponseVersion, so the server's LOCALE):
// KR and JP read a string and a long, NA a string and three longs, CN an int, a long, a byte and an int.
// The other LOCALE values have no platform proxy of their own; they get the KR layout.
public static class PlatformBlock {
    private static readonly string Current = Maple2.Tools.Locale.Read();

    public static void Write(IByteWriter writer) => Write(writer, Current);

    public static void Write(IByteWriter writer, string locale) {
        switch (locale) {
            case "NA":
                writer.WriteUnicodeString(); // login name
                writer.WriteLong();
                writer.WriteLong();
                writer.WriteLong();
                break;
            case "CN":
                writer.WriteInt();
                writer.WriteLong();
                writer.WriteByte();
                writer.WriteInt();
                break;
            default:
                writer.WriteUnicodeString(); // login name
                writer.WriteLong();
                break;
        }
    }
}
