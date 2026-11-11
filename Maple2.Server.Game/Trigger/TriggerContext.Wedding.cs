using System.Numerics;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Trigger;

// The hall triggers (wedding hall maps, instance type WeddingHall). The state they drive is Field.Wedding, set
// when the room is entered through Wedding 0x1a; in a room without one (a GM who warped in) every condition is
// false and every action does nothing.
public partial class TriggerContext {
    // Nothing in the client or the hall triggers shows a state this changes: the action only stands between a
    // notice and moving everyone out. Left without effect.
    public void WeddingBroken() { }

    // The five below place and move the groom, the bride and the guests. Which of the couple is the groom is
    // not stored by the server, so they are left without effect.
    public void WeddingMoveUser(string type, int mapId, int[] portalIds, int boxId) { }

    // Asks the couple a question: Wedding 0x1e to each of the two in the room. The answers arrive as Wedding
    // 0x1e from the client and WeddingMutualAgreeResult reads them.
    public void WeddingMutualAgree(string type) {
        WeddingCeremony? ceremony = Field.Wedding;
        WeddingAgreeType agreeType = WeddingCeremony.ParseAgreeType(type);
        if (ceremony == null || !ceremony.Ask(agreeType)) {
            return;
        }

        foreach (long characterId in ceremony.Couple) {
            if (Field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                player.Session.Send(WeddingPacket.MutualAgree(agreeType, player.Session.Marriage.Partner.Name));
            }
        }
    }

    // Withdraws a question: Wedding 0x1f closes the client's box.
    public void WeddingMutualCancel(string type) {
        WeddingCeremony? ceremony = Field.Wedding;
        WeddingAgreeType agreeType = WeddingCeremony.ParseAgreeType(type);
        if (ceremony == null || !ceremony.Cancel(agreeType)) {
            return;
        }

        foreach (long characterId in ceremony.Couple) {
            if (Field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                player.Session.Send(WeddingPacket.MutualCancel(agreeType));
            }
        }
    }

    public void WeddingSetUserEmotion(string type, int id) { }

    public void WeddingSetUserLookAt(string type, string lookAtType, bool immediate) { }

    public void WeddingSetUserRotation(string type, Vector3 rotation, bool immediate) { }

    public void WeddingUserToPatrol(string patrolName, string type, int patrolIndex) { }

    // The vow is done: the engaged couple is married from now. Runs once per ceremony and only with both of the
    // couple in the room, which is the state the trigger reaches it in.
    public void WeddingVowComplete() {
        WeddingCeremony? ceremony = Field.Wedding;
        if (ceremony == null || !ceremony.CoupleInField(true, id => Field.TryGetPlayerById(id, out _)) || !ceremony.TryComplete()) {
            return;
        }

        DateTime now = DateTime.Now;
        using (GameStorage.Request db = Field.GameStorage.Context()) {
            if (!db.SetMarried(ceremony.MarriageId, now)) {
                logger.Error("Wedding vow complete: marriage {MarriageId} could not be stored as married", ceremony.MarriageId);
                ceremony.Uncomplete();
                return;
            }
        }

        long marriageTime = now.ToEpochSeconds();
        foreach (long characterId in ceremony.Couple) {
            if (Field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                player.Session.Marriage.SetMarried(marriageTime);
            }
        }

        foreach (long characterId in ceremony.Couple) {
            MailCompleteGifts(ceremony, characterId);
        }
    }

    // System mail 40000010 ("Wedding is complete", the gifts of the hall attached), one to each of the couple.
    private const int MailWeddingComplete = 40000010;

    private void MailCompleteGifts(WeddingCeremony ceremony, long characterId) {
        if (!Field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
            return;
        }

        using GameStorage.Request db = Field.GameStorage.Context();
        Mail? mail = db.CreateMail(new Mail {
            ReceiverId = characterId,
            SenderId = characterId,
            Type = MailType.System,
            Content = MailWeddingComplete.ToString(),
        });
        if (mail == null) {
            logger.Error("Wedding vow complete: gift mail for {CharacterId} could not be stored", characterId);
            return;
        }

        foreach (WeddingPackage.HallData.Item gift in ceremony.Gifts) {
            Item? item = Field.ItemDrop.CreateItem(gift.ItemId, gift.Rarity, gift.Amount);
            item = item == null ? null : db.CreateItem(mail.Id, item);
            if (item == null) {
                logger.Error("Wedding vow complete: gift {ItemId} for {CharacterId} could not be stored", gift.ItemId, characterId);
                continue;
            }
            mail.Items.Add(item);
        }

        try {
            player.Session.World.MailNotification(new MailNotificationRequest {
                CharacterId = characterId,
                MailId = mail.Id,
            });
        } catch { /* ignored */ }
    }

    #region Conditions
    // The hall triggers only test entry type GroomBride: with isInField both of the couple are in the room,
    // without it one of them is missing.
    public bool WeddingEntryInField(string entryType, bool isInField) {
        WeddingCeremony? ceremony = Field.Wedding;
        if (ceremony == null || entryType != WeddingCeremony.EntryGroomBride) {
            return false;
        }

        return ceremony.CoupleInField(isInField, id => Field.TryGetPlayerById(id, out _));
    }

    // The only state the hall triggers test is weddingComplete; success 0 is the test that it has not happened.
    public bool WeddingHallState(string state, bool success) {
        WeddingCeremony? ceremony = Field.Wedding;
        if (ceremony == null || state != WeddingCeremony.StateComplete) {
            return false;
        }

        return ceremony.Complete == success;
    }

    public bool WeddingMutualAgreeResult(string type, bool success) {
        WeddingCeremony? ceremony = Field.Wedding;
        return ceremony != null && ceremony.AgreeResult(WeddingCeremony.ParseAgreeType(type), success);
    }
    #endregion
}
