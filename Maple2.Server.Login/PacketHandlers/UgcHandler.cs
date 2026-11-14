using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Login.PacketHandlers;

public class UgcHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.Ugc;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required GameStorage GameStorage { private get; init; }
    // ReSharper restore All
    #endregion

    private enum Command : byte {
        ProfilePicture = 11,
    }

    public override void Handle(LoginSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        if (session.LeftCharacterListRefuses($"Ugc {command}")) {
            return;
        }
        switch (command) {
            case Command.ProfilePicture:
                HandleProfilePicture(session, packet);
                break;
        }
    }

    // The character list sends mode 11 with the path the web server answered for a profile upload of one of the
    // account's characters (any of them, not only one made in this session): the path names the character, and it is
    // checked as the game server checks it (UgcRules.ProfilePathValid), since every player that sees the character
    // is shown it.
    private void HandleProfilePicture(LoginSession session, IByteReader packet) {
        string picture = packet.ReadUnicodeString();
        long characterId = UgcRules.ProfilePathCharacter(picture);
        if (!UgcRules.ProfilePathValid(picture, characterId)) {
            Logger.Warning("Refused profile picture path from account {AccountId}", session.AccountId);
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        Character? character = db.GetCharacter(characterId, session.AccountId);
        if (character == null) {
            Logger.Warning("Unable to locate character {CharacterId} for account {AccountId}", characterId, session.AccountId);
            return;
        }

        // Only the picture is written: a save of the whole character would also write the row's other columns as
        // the game character carries them.
        if (!db.SetCharacterPicture(character.Id, session.AccountId, picture)) {
            Logger.Warning("Unable to save the picture of character {CharacterId}", character.Id);
            return;
        }
        character.Picture = picture;
        session.Send(UgcPacket.ProfilePicture(character));
    }
}
