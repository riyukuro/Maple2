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

    // Moves the players of one role to a random portal of the list, in this hall (the hall scripts' own note on
    // the action; a move to another map is left without effect). Guest: the players in the box who are not the
    // couple. Groom and Bride: that one of the couple, when in the box. The groom is the one who reserved the
    // hall (WeddingCeremony.GroomId), as in the invitation mail.
    public void WeddingMoveUser(string type, int mapId, int[] portalIds, int boxId) {
        DebugLog("[WeddingMoveUser] type:{Type}, mapId:{MapId}, boxId:{BoxId}", type, mapId, boxId);
        WeddingCeremony? ceremony = Field.Wedding;
        if (ceremony == null || portalIds.Length == 0 || (mapId != 0 && mapId != Field.MapId)) {
            return;
        }

        IEnumerable<FieldPlayer> players = type == WeddingGuest
            ? WeddingGuests(PlayersInBox(boxId), ceremony, player => player.Session.CharacterId)
            : WeddingRole(PlayersInBox(boxId), ceremony, type, player => player.Session.CharacterId);
        foreach (FieldPlayer player in players.ToList()) {
            if (Field.TryGetPortal(portalIds[Random.Shared.Next(portalIds.Length)], out FieldPortal? portal)) {
                player.MoveToPortal(portal);
            }
        }
    }

    public const string WeddingGuest = "Guest";

    public static IEnumerable<T> WeddingGuests<T>(IEnumerable<T> players, WeddingCeremony ceremony, Func<T, long> characterId) =>
        players.Where(player => !ceremony.IsCouple(characterId(player)));

    // The player of a role (Groom or Bride) among players: none for another type or an unknown groom.
    public static IEnumerable<T> WeddingRole<T>(IEnumerable<T> players, WeddingCeremony ceremony, string type, Func<T, long> characterId) {
        long roleId = ceremony.RoleId(type);
        return players.Where(player => roleId != 0 && characterId(player) == roleId);
    }

    private FieldPlayer? WeddingRolePlayer(string type) {
        long roleId = Field.Wedding?.RoleId(type) ?? 0;
        return roleId != 0 && Field.TryGetPlayerById(roleId, out FieldPlayer? player) ? player : null;
    }

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

    // The groom or the bride plays a weddingemotion row (Wedding 0x27). The client puts its own player in the
    // emotion state and sends that to the others, so only the one of the role gets it.
    public void WeddingSetUserEmotion(string type, int id) {
        DebugLog("[WeddingSetUserEmotion] type:{Type}, id:{Id}", type, id);
        WeddingRolePlayer(type)?.Session.Send(WeddingPacket.Emotion(id));
    }

    // The groom or the bride looks at the other role's player (trigger Ui 0xb on the client's own player).
    public void WeddingSetUserLookAt(string type, string lookAtType, bool immediate) {
        DebugLog("[WeddingSetUserLookAt] type:{Type}, lookAtType:{LookAtType}", type, lookAtType);
        FieldPlayer? player = WeddingRolePlayer(type);
        FieldPlayer? target = WeddingRolePlayer(lookAtType);
        if (player == null || target == null || player == target) {
            return;
        }

        player.Session.Send(TriggerPacket.UiLookAt(target.Position, immediate));
    }

    // The groom or the bride turns to rotation (trigger Ui 10 on the client's own player).
    public void WeddingSetUserRotation(string type, Vector3 rotation, bool immediate) {
        DebugLog("[WeddingSetUserRotation] type:{Type}, rotation:{Rotation}", type, rotation);
        WeddingRolePlayer(type)?.Session.Send(TriggerPacket.UiPlayerRotation(rotation, immediate));
    }

    // The groom or the bride walks the patrol path, the way MoveUserPath moves a player. patrolIndex is the
    // hall scripts' index of the path (1 groom, 2 bride) and names nothing more.
    public void WeddingUserToPatrol(string patrolName, string type, int patrolIndex) {
        DebugLog("[WeddingUserToPatrol] patrol:{Patrol}, type:{Type}", patrolName, type);
        long roleId = Field.Wedding?.RoleId(type) ?? 0;
        if (roleId == 0) {
            return;
        }

        Field.MovePlayerAlongPath(patrolName, player => player.Session.CharacterId == roleId);
    }

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
        // The hall was held: its ticket is taken back from the guests in the room (Wedding 25).
        foreach (FieldPlayer player in Field.Players.Values) {
            player.Session.Marriage.TakeBackTicket(ceremony.MarriageId);
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
