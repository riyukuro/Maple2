using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class DynamicBanwordHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.DynamicBanword;

    public override void Handle(GameSession session, IByteReader packet) {
        // CSceneStateGame::OnServerEnter sends the version of the client's ban-word list (0 when it has none). The
        // reply (DynamicBanwordPacket) brings it to the GM-kept list (banword command, DynamicBanWordRules): the whole
        // list when the versions differ, a clear when the list is empty, nothing when the client holds it already.
        long version = packet.ReadLong();
        IList<DynamicBanWord> words;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            words = db.GetDynamicBanWords();
        }
        ByteWriter? reply = DynamicBanwordPacket.For(version, words);
        if (reply != null) {
            session.Send(reply);
        }
    }
}
