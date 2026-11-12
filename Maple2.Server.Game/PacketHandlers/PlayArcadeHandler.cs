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
// The Three Two One direction buttons (client/ms2/uimicrogamecoupledance.cpp, the dialog PlayArcade 7 shows): mode 11
// with an int direction, 2, 1 or 3 for the dialog's first, second and third button (SendPlayArcade).
public class PlayArcadeHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.PlayArcade;

    public const byte Scores = 9;
    public const byte Enter = 10;
    public const byte Pick = 11;

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
            case Pick:
                session.Field?.ThreeTwoOnePicked(session.Player, packet.ReadInt());
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
    // dead player, a player in battle (the lobby's own check) and one already in the field.
    private static void HandleEnter(GameSession session, int arcadeId) {
        if (session.Field == null
            || !session.TableMetadata.ArcadeGameTable.Entries.TryGetValue(arcadeId, out ArcadeGameTable.Entry? game)
            || game.TargetField == 0 || game.IsEventArcade
            || session.Player.IsDead || session.Player.InBattle || session.Field.MapId == game.TargetField) {
            return;
        }
        session.Send(session.PrepareField(game.TargetField)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }
}
