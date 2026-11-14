using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// Login into a saved field that cannot be made (no navmesh file): which fields are tried instead.
public class FieldEntryRulesTests {
    [Test]
    public void ReturnField_ThenDefault() {
        Assert.That(FieldEntryRules.Fallbacks(2000064, 2000001, 2000062).ToArray(), Is.EqualTo(new[] { 2000001, 2000062 }));
    }

    [Test]
    public void FailedFieldAndRepeats_AreLeftOut() {
        Assert.That(FieldEntryRules.Fallbacks(2000064, 2000064, 2000062).ToArray(), Is.EqualTo(new[] { 2000062 }));
        Assert.That(FieldEntryRules.Fallbacks(2000064, 2000062, 2000062).ToArray(), Is.EqualTo(new[] { 2000062 }));
        Assert.That(FieldEntryRules.Fallbacks(2000062, 2000062, 2000062).ToArray(), Is.Empty);
        Assert.That(FieldEntryRules.Fallbacks(2000064, 0, 2000062).ToArray(), Is.EqualTo(new[] { 2000062 }));
    }

    [Test]
    public void ReturnMap_GmOnlyFieldForPlainAccount_IsDefault() {
        Assert.That(FieldEntryRules.ReturnMap(2000064, true, 50, false, 2000062), Is.EqualTo(2000062));
        Assert.That(FieldEntryRules.ReturnMap(2000064, true, 50, true, 2000062), Is.EqualTo(2000064));
        Assert.That(FieldEntryRules.ReturnMap(2000001, true, 0, false, 2000062), Is.EqualTo(2000001));
    }

    // A map the played client does not have is no return field, whoever asks.
    [Test]
    public void ReturnMap_NotInPlayedClient_IsDefault() {
        Assert.That(FieldEntryRules.ReturnMap(900001, true, 0, true, 900009, notInPlayedClient: true), Is.EqualTo(900009));
        Assert.That(FieldEntryRules.ReturnMap(900001, true, 0, true, 900009, notInPlayedClient: false), Is.EqualTo(900001));
    }

    // Arriving (login, channel change) on a map the played client does not have: the return field, then the
    // default one, whichever the client has; an unmarked map is entered as it is.
    [Test]
    public void ArrivalMap_SkipsMapsThePlayedClientLacks() {
        Assert.That(FieldEntryRules.ArrivalMap(900001, false, 900002, 900009, _ => false), Is.EqualTo(900001));
        Assert.That(FieldEntryRules.ArrivalMap(900001, true, 900002, 900009, _ => true), Is.EqualTo(900002));
        Assert.That(FieldEntryRules.ArrivalMap(900001, true, 900002, 900009, id => id != 900002), Is.EqualTo(900009));
        Assert.That(FieldEntryRules.ArrivalMap(900001, true, 900001, 900009, _ => true), Is.EqualTo(900009), "the return field is the same map");
        Assert.That(FieldEntryRules.ArrivalMap(900001, true, 900002, 900009, _ => false), Is.EqualTo(0), "nothing usable");
    }

    [Test]
    public void NotInPlayedClientNotice_NamesTheMap() {
        Assert.That(FieldEntryRules.NotInPlayedClientNotice(900001, "Test Park"), Is.EqualTo("Map Test Park (900001) is not in this client version. Entry refused."));
        Assert.That(FieldEntryRules.NotInPlayedClientNotice(900001, null), Is.EqualTo("Map 900001 is not in this client version. Entry refused."));
    }

    [Test]
    public void ReturnMap_UnknownField_IsDefault() {
        Assert.That(FieldEntryRules.ReturnMap(123, false, 0, true, 2000062), Is.EqualTo(2000062));
    }

    // "Enter Dungeon" on the party panel after the room was closed: the client gets error 0x1D on GameToGame
    // (a message box, it stays in the field) and is not told to move.
    [Test]
    public void DungeonRoomGone_IsError1D() {
        Assert.That((byte) FieldEntryRules.DungeonRoomError(false), Is.EqualTo(0x1D));
        Assert.That(FieldEntryRules.DungeonRoomError(true), Is.EqualTo(MigrationError.ok));
        Assert.That(MigrationPacket.GameToGameError(FieldEntryRules.DungeonRoomError(false)).ToArray(),
            Is.EqualTo(new byte[] { (byte) SendOp.GameToGame, 0x00, 0x1D }));
    }

    // An open field and a saveField instance save their enterReturnId; a dungeon room on an open map (instance type
    // none, as Zakum's room field 02000426) and a dungeon lobby do not, so leaving the room goes back to the field
    // the player came from. A field without an enterReturnId saves nothing.
    [Test]
    public void SavesReturnMap_NotInDungeonFields() {
        Assert.That(FieldEntryRules.SavesReturnMap(InstanceType.none, false, false, 2000426), Is.True);
        Assert.That(FieldEntryRules.SavesReturnMap(InstanceType.solo, true, false, 2000062), Is.True);
        Assert.That(FieldEntryRules.SavesReturnMap(InstanceType.solo, false, false, 2000062), Is.False);
        Assert.That(FieldEntryRules.SavesReturnMap(InstanceType.none, false, true, 2000426), Is.False);
        Assert.That(FieldEntryRules.SavesReturnMap(InstanceType.DungeonLobby, true, true, 2000062), Is.False);
        Assert.That(FieldEntryRules.SavesReturnMap(InstanceType.none, false, false, 0), Is.False);
    }

    // A login on a map of a running room the character entered goes back into that room's field (the lobby on the
    // lobby map); a room the character never entered, or a map that is none of the room's, gives null. Of two rooms
    // on the same map the newest is taken.
    [Test]
    public void RejoinedField_RunningRoomOfTheCharacter() {
        var rooms = new[] {
            new FieldEntryRules.RunningRoom<string>(10000003, true, 50000013, "old lobby", new Dictionary<int, string> { [2000426] = "old room" }),
            new FieldEntryRules.RunningRoom<string>(10000009, true, 50000013, "lobby", new Dictionary<int, string> { [2000426] = "room", [2000427] = "room 2" }),
            new FieldEntryRules.RunningRoom<string>(10000013, false, 50000001, "other lobby", new Dictionary<int, string> { [2000500] = "other room" }),
        };
        Assert.That(FieldEntryRules.RejoinedField(rooms, 2000426), Is.EqualTo("room"));
        Assert.That(FieldEntryRules.RejoinedField(rooms, 2000427), Is.EqualTo("room 2"));
        Assert.That(FieldEntryRules.RejoinedField(rooms, 50000013), Is.EqualTo("lobby"));
        Assert.That(FieldEntryRules.RejoinedField(rooms, 2000500), Is.Null);
        Assert.That(FieldEntryRules.RejoinedField(rooms, 50000001), Is.Null);
        Assert.That(FieldEntryRules.RejoinedField(rooms, 2000062), Is.Null);
    }

    // A dungeon arrival whose room is gone enters the return field; every other arrival is left alone.
    [Test]
    public void DungeonArrival_RoomGone_EntersReturnField() {
        Assert.That(FieldEntryRules.ArrivesInReturnField(MigrationType.Dungeon, false), Is.True);
        Assert.That(FieldEntryRules.ArrivesInReturnField(MigrationType.Dungeon, true), Is.False);
        Assert.That(FieldEntryRules.ArrivesInReturnField(MigrationType.Normal, false), Is.False);
    }

    // The same for a rank duel arrival whose match field is gone, and for a login on a rank duel map.
    [Test]
    public void RankDuelArrival_FieldGone_EntersReturnField() {
        Assert.That(FieldEntryRules.ArrivesInReturnField(MigrationType.PvpMatch, false), Is.True);
        Assert.That(FieldEntryRules.ArrivesInReturnField(MigrationType.PvpMatch, true), Is.False);
        Assert.That(FieldEntryRules.RankDuelMapWithoutMatch(65010001, RankDuelField.Maps), Is.True);
        Assert.That(FieldEntryRules.RankDuelMapWithoutMatch(65010010, RankDuelField.Maps), Is.True);
        Assert.That(FieldEntryRules.RankDuelMapWithoutMatch(65000001, RankDuelField.Maps), Is.False);
        Assert.That(FieldEntryRules.RankDuelMapWithoutMatch(2000062, RankDuelField.Maps), Is.False);
    }

    // A login on Demon Tree's field 02000207 (dungeon 20007001) or its lobby 50000004 after a restart, with no room
    // running: the return field, unless that is itself a room map or a map the client cannot enter, then the default.
    [Test]
    public void DungeonRoomMapLogin_WithoutItsRoom_EntersTheReturnField() {
        var roomMaps = new HashSet<int> { 2000207, 50000004 };
        Assert.That(FieldEntryRules.DungeonRoomMapWithoutRoom(2000207, roomMaps), Is.True);
        Assert.That(FieldEntryRules.DungeonRoomMapWithoutRoom(50000004, roomMaps), Is.True);
        Assert.That(FieldEntryRules.DungeonRoomMapWithoutRoom(2000062, roomMaps), Is.False);

        Func<int, bool> usable = id => id != 9999999;
        Assert.That(FieldEntryRules.DungeonLoginReturnField(2000062, roomMaps, 2000001, usable), Is.EqualTo(2000062));
        Assert.That(FieldEntryRules.DungeonLoginReturnField(2000207, roomMaps, 2000001, usable), Is.EqualTo(2000001));
        Assert.That(FieldEntryRules.DungeonLoginReturnField(9999999, roomMaps, 2000001, usable), Is.EqualTo(2000001));
        Assert.That(FieldEntryRules.DungeonLoginReturnField(0, roomMaps, 2000001, usable), Is.EqualTo(2000001));
    }

    // A rank duel arrival whose field cannot be made goes to the fallback fields, as a plain entry does; a dungeon
    // arrival does not (it has its own return path).
    [Test]
    public void RankDuelArrival_TakesTheFallback() {
        Assert.That(FieldEntryRules.TakesFallback(MigrationType.Normal), Is.True);
        Assert.That(FieldEntryRules.TakesFallback(MigrationType.PvpMatch), Is.True);
        Assert.That(FieldEntryRules.TakesFallback(MigrationType.Dungeon), Is.False);
        Assert.That(FieldEntryRules.TakesFallback(MigrationType.DecorPlanner), Is.False);
    }

    [Test]
    public void RankDuel_RefusedInstanceTypes() {
        foreach (InstanceType type in new[] { InstanceType.solo, InstanceType.DungeonLobby, InstanceType.massiveEvent, InstanceType.GuildPvp,
                     InstanceType.RankingPvp, InstanceType.MapleSurvival, InstanceType.MapleSurvivalSquad, InstanceType.FieldWar }) {
            Assert.That(RankDuelField.RefusesInstance(type), Is.True, type.ToString());
        }
        foreach (InstanceType type in new[] { InstanceType.none, InstanceType.channelScale, InstanceType.ugcMap, InstanceType.GuildHouse, InstanceType.WeddingHall }) {
            Assert.That(RankDuelField.RefusesInstance(type), Is.False, type.ToString());
        }
    }

    // A rank duel room that is gone is not made again as a plain field; another map's room is.
    [Test]
    public void MissingRoom_NotMadeOnARankDuelMap() {
        Assert.That(FieldEntryRules.MakesMissingRoom(65010004, RankDuelField.Maps), Is.False);
        Assert.That(FieldEntryRules.MakesMissingRoom(2000062, RankDuelField.Maps), Is.True);
    }

    // An OX quiz stadium room that is gone is not made again either: a stadium without its event runs no game, and
    // a login on the stadium goes to the return field.
    [Test]
    public void MissingRoom_NotMadeOnTheOxStadium() {
        Assert.That(FieldEntryRules.MakesMissingRoom(UgcOxRules.StadiumMapId, RankDuelField.Maps), Is.False);
        Assert.That(FieldEntryRules.UgcOxStadiumWithoutEvent(UgcOxRules.StadiumMapId), Is.True);
        Assert.That(FieldEntryRules.UgcOxStadiumWithoutEvent(2000062), Is.False);
    }

    // A rank duel field takes its duelists until it has sent them out.
    [Test]
    public void RankDuelField_RunsUntilClosed() {
        Assert.That(RankDuelField.IsRunning(Maple2.Model.Game.Pvp.RankDuelPhase.Ended), Is.True);
        Assert.That(RankDuelField.IsRunning(Maple2.Model.Game.Pvp.RankDuelPhase.Closed), Is.False);
    }

    [Test]
    public void RankDuel_LeavesOnlyFromAPlainField() {
        Assert.That(RankDuelField.CanLeaveFor(dead: false, dungeon: false, instanced: false, rankDuel: false), Is.True);
        Assert.That(RankDuelField.CanLeaveFor(dead: true, dungeon: false, instanced: false, rankDuel: false), Is.False);
        Assert.That(RankDuelField.CanLeaveFor(dead: false, dungeon: true, instanced: false, rankDuel: false), Is.False);
        Assert.That(RankDuelField.CanLeaveFor(dead: false, dungeon: false, instanced: true, rankDuel: false), Is.False);
        Assert.That(RankDuelField.CanLeaveFor(dead: false, dungeon: false, instanced: false, rankDuel: true), Is.False);
    }

    // A session whose character was never loaded (an arrival with a used token) has an empty log name
    // instead of a null reference.
    [Test]
    public void LogName_WithoutCharacter_IsEmpty() {
        Assert.That(FieldEntryRules.LogName(null), Is.EqualTo(string.Empty));
        Assert.That(FieldEntryRules.LogName("abc"), Is.EqualTo("abc"));
    }

    // A stale room id on one member's session must not clear the dungeon the party has now.
    [Test]
    public void OnlyThePartysOwnRoomClearsThePartyDungeon() {
        Assert.That(FieldEntryRules.ClearsPartyDungeon(7, 7), Is.True);
        Assert.That(FieldEntryRules.ClearsPartyDungeon(7, 9), Is.False);
        Assert.That(FieldEntryRules.ClearsPartyDungeon(7, 0), Is.False);
        Assert.That(FieldEntryRules.ClearsPartyDungeon(0, 0), Is.False);
    }

    // Guild 100: two guilds that open the same event do not meet in one room.
    [Test]
    public void GuildEventMap_HasOneRoomPerGuild() {
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.GuildEvent, 5, 0), Is.True);
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.GuildEvent, 0, 0), Is.False, "no owner");
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.GuildEvent, 5, 9), Is.False, "a named room");
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.solo, 5, 0), Is.False);
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.GuildHouse, 5, 0), Is.True, "a guild's house");
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.GuildHouse, 0, 0), Is.False);
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.ugcMap, 5, 0), Is.False, "homes have their own path");
    }

    // The exp for the first visit of a map: paid once, by the map's explore type. The session pays it
    // after the player is added to the field (the order itself is session code and has no test here).
    [Test]
    public void ExploreExp_FirstVisitOnly() {
        Assert.That(FieldEntryRules.ExploreExp(true, 1), Is.EqualTo(ExpType.mapCommon));
        Assert.That(FieldEntryRules.ExploreExp(true, 2), Is.EqualTo(ExpType.mapHidden));
        Assert.That(FieldEntryRules.ExploreExp(true, 0), Is.Null, "a map with no explore type pays none");
        Assert.That(FieldEntryRules.ExploreExp(false, 1), Is.Null, "a map seen before pays none");
    }

    // A dungeon room is made only when its lobby and every field have map data, a navmesh and map entities.
    // Made-up maps: 1 and 2 are complete, 3 has no navmesh, 4 no entities, 5 no map data.
    private static (int, FieldEntryRules.DungeonFieldProblem) Unmade(int lobby, params int[] fields) =>
        FieldEntryRules.UnmadeDungeonField(lobby, fields, id => id is >= 1 and <= 4 ? $"x{id}" : null,
            xblock => xblock != "x3", xblock => xblock != "x4");

    [Test]
    public void DungeonFields_AllPresent_AreMade() {
        Assert.That(Unmade(1, 2), Is.EqualTo((0, FieldEntryRules.DungeonFieldProblem.None)));
        Assert.That(Unmade(1), Is.EqualTo((0, FieldEntryRules.DungeonFieldProblem.None)));
    }

    [Test]
    public void DungeonFields_FirstMissingPiece_RefusesTheRoom() {
        Assert.Multiple(() => {
            Assert.That(Unmade(1, 2, 5), Is.EqualTo((5, FieldEntryRules.DungeonFieldProblem.NoMap)));
            Assert.That(Unmade(1, 3, 5), Is.EqualTo((3, FieldEntryRules.DungeonFieldProblem.NoNavmesh)));
            Assert.That(Unmade(1, 4), Is.EqualTo((4, FieldEntryRules.DungeonFieldProblem.NoEntities)));
            // The lobby is checked first; a reward-count row has lobby 0, which no map has.
            Assert.That(Unmade(3, 5), Is.EqualTo((3, FieldEntryRules.DungeonFieldProblem.NoNavmesh)));
            Assert.That(Unmade(0), Is.EqualTo((0, FieldEntryRules.DungeonFieldProblem.NoMap)));
        });
    }
}
