using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Login.PacketHandlers;

// The account delete button of the character list (onClickDeleteAccount, UICharacterListDialog::CheckDeleteAccount)
// sends CheckDeleteAccount with the account id; nothing answered it, so the click did nothing. The answer is the
// first character of the account that blocks the delete (a pending delete, a listing, a guild, a marriage, unread
// mail). The list's characters carry neither the guild (a GuildMember row) nor the marriage, so both are read per
// character. When nothing blocks, the answer is the identity-check failure, not 0: on 0 the client walks its terms
// boxes into the identity step, which does nothing outside CN (FUN_1418f2480), so the KR dialog would stop there
// and the delete is never sent. The account id of the packet is not used: the session's own account is checked.
public class CheckDeleteAccountHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.CheckDeleteAccount;

    public required GameStorage GameStorage { private get; init; }

    public override void Handle(LoginSession session, IByteReader packet) {
        packet.ReadLong(); // account id

        using GameStorage.Request db = GameStorage.Context();
        (_, IList<Character>? characters) = db.ListCharacters(session.AccountId);
        foreach (Character character in characters ?? []) {
            int block = DeleteAccountRules.Block(new DeleteAccountRules.Holdings(
                DeletePending: character.DeleteTime != default,
                BlackMarket: db.GetBlackMarketListings(character.Id).Any(),
                Guild: db.GetGuildId(character.Id) != 0,
                UgcMarket: db.GetUgcListingsByCharacterId(character.Id).Any(),
                Married: db.GetMarriageInfo(character.Id).Status != MaritalStatus.Single,
                UnreadMail: db.GetAllMail(character.Id).Any(mail => mail.ReadTime == 0),
                MesoMarket: db.GetMyMesoListingsByCharacterId(character.Id).Any()));
            if (block != DeleteAccountRules.Ok) {
                session.Send(DeleteAccountPacket.Check(block, character.Id));
                return;
            }
        }

        session.Send(DeleteAccountPacket.Check(DeleteAccountRules.CertQueryFail));
    }
}

// The delete itself (CN only on the client): it carries the platform's identity strings, which this server cannot
// verify, so it is refused with the client's identity-check text and nothing is deleted.
public class DeleteAccountHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.DeleteAccount;

    public override void Handle(LoginSession session, IByteReader packet) {
        session.Send(DeleteAccountPacket.Result(DeleteAccountRules.CertQueryFail));
    }
}
