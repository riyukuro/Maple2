using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class CheckCharacterNameHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.CheckCharName;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required GameStorage GameStorage { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        string characterName = packet.ReadUnicodeString();
        long itemUid = packet.ReadLong();

        // Counted the way the client counts for its region (Hangul and CJK count twice).
        int width = NameValidator.CharacterNameWidth(characterName, Target.LOCALE);
        bool blank = CharacterCreateRules.IsBlankName(characterName);
        bool banned = !blank && BanWordStorage.ContainsBannedWord(characterName);
        NameChangeRules.Refusal refusal = NameChangeRules.Check(blank, width, Constant.CharacterNameLengthMin, Constant.CharacterNameLengthMax, banned, taken: false);
        if (refusal != NameChangeRules.Refusal.None) {
            // The game scene reads no CharacterCreate packet; a message box is what it shows.
            (StringCode code, string[] args) = NameChangeRules.Text(refusal, characterName);
            session.Send(NoticePacket.MessageBox(new InterfaceText(code, args)));
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        long existingId = db.GetCharacterId(characterName);
        session.Send(CheckCharacterNamePacket.Result(existingId != 0, characterName, itemUid));
    }
}
