using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;

namespace Maple2.Server.Core.PacketHandlers;

public abstract class ResponseVersionHandler<T> : PacketHandler<T> where T : Session {
    public override RecvOp OpCode => RecvOp.ResponseVersion;

    // The client's hello (CNetworkService::SendResponseVersion, WriteLoginHello): int version, short feature level of
    // its region, short region (GetRegion: KR 0, JP 1, NA 2, CN 3; KR unless the client was started with -locale).
    public override void Handle(T session, IByteReader packet) {
        uint version = packet.Read<uint>();
        packet.ReadShort(); // feature level
        short region = packet.ReadShort();

        if (version != Session.VERSION) {
            session.Disconnect();
            return;
        }
        if (!ClientRegion.Matches(region, Target.LOCALE)) {
            Logger.Warning("Client region {Region} differs from LOCALE {Locale}: the client gates features, items and texts by its own region",
                ClientRegion.Name(region), Target.LOCALE);
        }
    }
}
