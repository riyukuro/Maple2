using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;


namespace Maple2.Server.Game.PacketHandlers;

public class HomeHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.RequestHome;

    private enum Command : byte {
        Accept = 0,
        Invite = 1,
        Warp = 3,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required MapMetadataStorage MapMetadataStorage { private get; init; }
    public required WorldClient World { private get; init; }

    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Accept:
                HandleAccept(session, packet);
                return;
            case Command.Invite:
                HandleInvite(session, packet);
                return;
            case Command.Warp:
                HandleWarp(session, packet);
                return;
        }
    }

    private void HandleAccept(GameSession session, IByteReader packet) {
        string name = packet.ReadUnicodeString(); // the inviter
        byte answer = packet.ReadByte(); // 1 accept, 2 decline, 5 invites blocked in the options, 6 timed out

        // The client shows the inviter one notice per answer (CSceneStateGame::OnHomeInviteResult).
        if (answer is not (1 or 2 or 5 or 6) || string.IsNullOrEmpty(name)) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long inviterId = db.GetCharacterId(name);
        if (inviterId == 0 || inviterId == session.CharacterId) {
            return;
        }
        // Only an answer to a live invite from that player is passed on; the entry is used up by any answer.
        // Answer 1 grants nothing here: the invited client enters through RequestMoveField (visit home) on its own.
        if (session.FindSession(inviterId, out GameSession? inviter) && inviter.HomeInvites.TryAnswer(session.CharacterId, Environment.TickCount64)) {
            inviter.Send(HomeInvitePacket.Result((HomeInvitePacket.InviteResult) answer, session.Player.Value.Character.Name));
        }
    }

    private void HandleInvite(GameSession session, IByteReader packet) {
        string playerName = packet.ReadUnicodeString();

        if (string.IsNullOrEmpty(playerName)) {
            session.Send(NoticePacket.MessageBox(StringCode.s_fail_enterfield_invaliduser));
            return;
        }

        if (string.Equals(playerName, session.Player.Value.Character.Name, StringComparison.CurrentCultureIgnoreCase)) {
            session.Send(HomeInvitePacket.Result(HomeInvitePacket.InviteResult.Self, string.Empty));
            return;
        }

        if (!session.Player.Value.Home.IsHomeSetup) {
            session.Send(NoticePacket.MessageBox(StringCode.s_home_returnable_invalid_state));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(playerName);
        if (characterId == 0) {
            session.Send(NoticePacket.MessageBox(StringCode.s_fail_enterfield_invaliduser));
            return;
        }

        session.FindSession(characterId, out GameSession? targetSession);
        if (targetSession is null) {
            session.Send(HomeInvitePacket.Result(HomeInvitePacket.InviteResult.Offline, playerName));
            return;
        }

        session.HomeInvites.Add(characterId, Environment.TickCount64);
        targetSession.Send(HomeInvitePacket.Invite(session.Player.Value.Character));
        // KMS2 sends the inviter nothing for a delivered invite; the next packet is the accept or decline result.
    }

    private void HandleWarp(GameSession session, IByteReader packet) {
        if (session.Field?.Metadata.Property.HomeReturnable != true) {
            session.Send(NoticePacket.MessageBox(StringCode.s_home_returnable_forbidden));
            return;
        }

        Home home = session.Player.Value.Home;
        int homeMapId = home.Indoor.MapId;
        if (session.Field.MapId == homeMapId) {
            session.Send(NoticePacket.MessageBox(StringCode.s_home_returnable_forbidden_to_sameplace));
            return;
        }

        // If the home has no name, initialize home
        if (string.IsNullOrEmpty(home.Indoor.Name)) {
            int templateId = packet.ReadInt(); // -1 = none

            if (templateId is < -1 or > 2) {
                Logger.Warning("Invalid template id {templateId} for initializing home for {CharacterName}", templateId, session.Player.Value.Character.Name);
                return;
            }

            // Template ids in XML are 1-based, but the client uses 0-based
            templateId++;

            MapMetadataStorage.TryGetExportedUgc(templateId.ToString(), out ExportedUgcMapMetadata? exportedUgcMap);

            session.Housing.InitNewHome(session.Player.Value.Character.Name, exportedUgcMap);
        }

        // home_goto (trophy 23500019): KMS2 sends its update, up by 1, just before the migrate packet to the own home.
        // A refused move counts nothing.
        session.Migrate(home.Indoor.MapId, home.Indoor.OwnerId, () => session.ConditionUpdate(ConditionType.home_goto));
    }
}
