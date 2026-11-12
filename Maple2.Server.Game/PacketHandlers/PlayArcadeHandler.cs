using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// Client senders, the arcade lobby (client/ms2/uiarcademaindialog.cpp): mode 9 when the lobby opens and its season
// is not the current arcade season (:307, Encode4(9), so the mode byte and three zero bytes), mode 10 with an int
// arcade id from a game button (:559, after the client's own checks: not dead, not already in the game's field).
public class PlayArcadeHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.PlayArcade;

    public const byte Scores = 9;
    public const byte Enter = 10;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required GameStorage GameStorage { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        byte mode = packet.ReadByte();
        switch (mode) {
            case Scores:
                HandleScores(session);
                return;
            case Enter:
                HandleEnter(session, packet.ReadInt());
                return;
        }
    }

    // The current arcade season (0 without one) and the player's best of each game in it.
    private void HandleScores(GameSession session) {
        int seasonId = ArcadeBest.SeasonAt(session.TableMetadata.SeasonDataTable.Arcade, DateTime.Now);
        IList<ArcadeBest> bests;
        using (GameStorage.Request db = GameStorage.Context()) {
            bests = seasonId == 0 ? [] : db.GetArcadeBests(session.CharacterId, seasonId);
        }
        session.Send(ArcadePacket.Scores(seasonId, bests));
    }

    // The game's field (arcadegame.xml targetField). A game the table does not list, one without a field and an
    // event arcade (open only while an arcade event names it, which our server does not run) are dropped, as are a
    // dead player and one already in the field.
    private static void HandleEnter(GameSession session, int arcadeId) {
        if (session.Field == null
            || !session.TableMetadata.ArcadeGameTable.Entries.TryGetValue(arcadeId, out ArcadeGameTable.Entry? game)
            || game.TargetField == 0 || game.IsEventArcade
            || session.Player.IsDead || session.Field.MapId == game.TargetField) {
            return;
        }
        session.Send(session.PrepareField(game.TargetField)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }
}
