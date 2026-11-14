using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using DotRecast.Recast.Toolset.Builder;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// The wedding hall and mini game rows of the trigger scripts, on a field with a flat test navmesh: the moves check
// the target against the navmesh, as a field made from a map's own navmesh does. Made-up ids.
public class TriggerHallGameTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long PartnerId = 990001;

    // Box 1 holds the player at the origin, box 2 is far off.
    private static readonly Ms2TriggerBox Near = new(Vector3.Zero, new Vector3(300, 300, 300), 1, true);
    private static readonly Ms2TriggerBox Far = new(new Vector3(5000, 0, 0), new Vector3(300, 300, 300), 2, true);

    private long CharacterId => test.Session.CharacterId;

    private void StartHall() {
        Start(Near, Far);
        test.Session.Player.Position = Vector3.Zero;
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", Any)!;
        portals.SetValue(Field, Activator.CreateInstance(portals.FieldType));
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, FlatNavigation());
        // The player stands in this field (a move checks the position against its navmesh).
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        // An empty exp table: the rounds below give no exp, only meso.
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
    }

    // One flat poly over map x and y from -2000 to 2000 at height 0 (navmesh units are map units / 100, the map's z
    // is the navmesh's y). A point off it is not a valid position.
    internal static Navigation FlatNavigation() {
        var option = new DtNavMeshCreateParams {
            verts = [0, 0, 0, 0, 0, 400, 400, 0, 400, 400, 0, 0],
            vertCount = 4,
            polys = [0, 1, 2, 3, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff],
            polyAreas = [0],
            polyFlags = [1],
            polyCount = 1,
            nvp = 6,
            walkableHeight = 2,
            walkableRadius = 0.5f,
            walkableClimb = 0.5f,
            bmin = new RcVec3f(-20, 0, -20),
            bmax = new RcVec3f(20, 1, 20),
            cs = 0.1f,
            ch = 0.1f,
            buildBvTree = true,
            offMeshConVerts = [],
            offMeshConRad = [],
            offMeshConDir = [],
            offMeshConAreas = [],
            offMeshConFlags = [],
            offMeshConUserID = [],
        };
        DtMeshData data = DtNavMeshBuilder.CreateNavMeshData(option)!;
        var navMesh = new DtNavMesh();
        Assert.That(navMesh.Init(data, 6, 0).Succeeded(), Is.True);

        var navigation = (Navigation) RuntimeHelpers.GetUninitializedObject(typeof(Navigation));
        typeof(Navigation).GetField("navMesh", Any)!.SetValue(navigation, navMesh);
        typeof(Navigation).GetField("navMeshQuery", Any)!.SetValue(navigation, new DtNavMeshQuery(navMesh));
        typeof(Navigation).GetField("<Crowd>k__BackingField", Any)!.SetValue(navigation, new DtCrowd(new DtCrowdConfig(maxAgentRadius: 0.3f), navMesh,
            _ => new DtQueryDefaultFilter(SampleAreaModifications.SAMPLE_POLYFLAGS_ALL, SampleAreaModifications.SAMPLE_POLYFLAGS_DISABLED,
                [1f, 10f, 1f, 1f, 2f, 1.5f])));
        // An npc spawned on it takes its agent settings from here (AddAgent).
        typeof(Navigation).GetField("crowdAgentConfig", Any)!.SetValue(navigation, new DtCrowdAgentConfig());
        return navigation;
    }

    private FieldPortal Portal(int id, Vector3 position) {
        return Field.SpawnPortal(new Portal(id, 0, 0, PortalType.Field, PortalActionType.Touch, position, Vector3.Zero,
            new Vector3(100, 100, 100), 0, 0, false, false, false));
    }

    // The UserMoveByPortal packets: the object id and the position.
    private (int ObjectId, Vector3 Position)[] Moves() {
        return Sent(SendOp.UserMoveByPortal).Select(reader => (reader.ReadInt(), reader.Read<Vector3>())).ToArray();
    }

    // move_user_to_pos (64): every player of the field goes to the position; a position off the navmesh moves nobody.
    // The move packet lifts the position by 25, as for a portal.
    [Test]
    public void MoveUserToPos_OnTheNavMesh() {
        StartHall();

        Action("""<action name="move_user_to_pos" pos="500,600,0" rot="0,0,90" />""");
        Assert.That(Moves(), Is.EqualTo(new[] { (test.Session.Player.ObjectId, new Vector3(500, 600, 25)) }));

        Action("""<action name="move_user_to_pos" pos="9000,0,0" rot="0,0,90" />""");
        Assert.That(Moves(), Is.Empty);
    }

    // wedding_move_user (108), Guest: the player in the box who is not one of the couple goes to a portal of the
    // list. KR scripts give the hall's own map id.
    [Test]
    public void WeddingMoveUser_GuestInTheBox() {
        StartHall();
        Field.Wedding = new WeddingCeremony(7001, PartnerId, PartnerId + 1, PartnerId);
        Portal(22, new Vector3(1000, 0, 0));
        test.TakeSent();

        Action($"""<action name="wedding_move_user" entry_type="Guest" map_id="{Field.MapId}" portal_ids="22" box_id="2" />""");
        Assert.That(Moves(), Is.Empty);

        Action($"""<action name="wedding_move_user" entry_type="Guest" map_id="{Field.MapId}" portal_ids="22" box_id="1" />""");
        Assert.That(Moves(), Is.EqualTo(new[] { (test.Session.Player.ObjectId, new Vector3(1000, 0, 25)) }));

        // Another map, or a room without a ceremony: nothing moves.
        Action("""<action name="wedding_move_user" entry_type="Guest" map_id="1" portal_ids="22" box_id="1" />""");
        Assert.That(Moves(), Is.Empty);
        Field.Wedding = null;
        Action($"""<action name="wedding_move_user" entry_type="Guest" map_id="{Field.MapId}" portal_ids="22" box_id="1" />""");
        Assert.That(Moves(), Is.Empty);
    }

    // Groom and Bride move that one of the couple: the player is the groom here, so Bride moves nobody.
    [Test]
    public void WeddingMoveUser_Role() {
        StartHall();
        Field.Wedding = new WeddingCeremony(7001, CharacterId, PartnerId, CharacterId);
        Portal(22, new Vector3(1000, 0, 0));
        test.TakeSent();

        Action("""<action name="wedding_move_user" entry_type="Guest" portal_ids="22" box_id="1" />""");
        Assert.That(Moves(), Is.Empty);
        Action("""<action name="wedding_move_user" entry_type="Bride" portal_ids="22" box_id="1" />""");
        Assert.That(Moves(), Is.Empty);
        Action("""<action name="wedding_move_user" entry_type="Groom" portal_ids="22" box_id="1" />""");
        Assert.That(Moves(), Has.Length.EqualTo(1));
    }

    // wedding_vow_complete (12 KR uses, no attributes): with the couple in the room the engaged marriage is stored as
    // married and the couple's own marriage turns married; a second vow, or one without the couple, changes nothing.
    // The test field holds one player, so the couple here is that player twice.
    [Test]
    public void WeddingVowComplete_MarriesTheCouple() {
        StartHall();
        long marriageId;
        using (GameStorage.Request db = test.Storage.Context()) {
            Account account = db.CreateAccount(new Account { Username = "partner" }, "pw");
            var partner = new Character { AccountId = account.Id, Name = "partner", Mastery = new Mastery() };
            partner.ReturnMaps.Push(2000062);
            long partnerId = db.CreateCharacter(partner)!.Id;
            marriageId = db.CreateMarriage(CharacterId, partnerId)!.Id;
            test.Session.Marriage.Marriage = db.GetMarriage(CharacterId)!;
        }

        Field.Wedding = new WeddingCeremony(marriageId, CharacterId, PartnerId, CharacterId);
        Action("""<action name="wedding_vow_complete" />""");
        Assert.That(Field.Wedding.Complete, Is.False);

        Field.Wedding = new WeddingCeremony(marriageId, CharacterId, CharacterId, CharacterId);
        Action("""<action name="wedding_vow_complete" />""");
        Assert.That(Field.Wedding.Complete, Is.True);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetMarriage(CharacterId)!.Status, Is.EqualTo(MaritalStatus.Married));
        }
        Assert.That((test.Session.Marriage.Marriage.Status, test.Session.Player.Value.Character.MarriageInfo.Status),
            Is.EqualTo((MaritalStatus.Married, MaritalStatus.Married)));
        // The couple's gift mail is stored with the marriage (here the one player, once for each of the couple).
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            Assert.That(context.Mail.Count(mail => mail.ReceiverId == CharacterId), Is.EqualTo(2));
        }
    }

    // The vow writes the marriageRegister row's exp (KR 100, month) to the log as its wedding line (type 8,
    // s_wedding_dialog_exp_8), stored in the vow's transaction; the couple's marriage is read again with it. Before,
    // the vow wrote no exp.
    [Test]
    public void WeddingVowComplete_WritesTheWeddingExpLine() {
        StartHall();
        test.SetTable(new WeddingTable(new Dictionary<MarriageExpType, WeddingReward> {
            [MarriageExpType.marriageRegister] = new(MarriageExpType.marriageRegister, 100, MarriageExpLimit.month),
        }, new Dictionary<int, WeddingPackage>()));
        long marriageId;
        using (GameStorage.Request db = test.Storage.Context()) {
            Account account = db.CreateAccount(new Account { Username = "partner" }, "pw");
            var partner = new Character { AccountId = account.Id, Name = "partner", Mastery = new Mastery() };
            partner.ReturnMaps.Push(2000062);
            long partnerId = db.CreateCharacter(partner)!.Id;
            marriageId = db.CreateMarriage(CharacterId, partnerId)!.Id;
            test.Session.Marriage.Marriage = db.GetMarriage(CharacterId)!;
        }

        Field.Wedding = new WeddingCeremony(marriageId, CharacterId, CharacterId, CharacterId);
        Action("""<action name="wedding_vow_complete" />""");
        Assert.That(Field.Wedding.Complete, Is.True);
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            Assert.That(context.Marriage.Single(row => row.Id == marriageId).ExpHistory.Select(exp => (exp.Type, exp.Amount)),
                Is.EqualTo(new[] { (MarriageExpType.Wedding, 100L) }));
        }
        Assert.That(test.Session.Marriage.Marriage.Exp, Is.EqualTo(100));
    }

    // A gift mail that cannot be stored fails the vow's transaction: the ceremony is not complete (a later vow may try
    // again) and the couple's marriage is not turned married. The in-memory store keeps no rollback, so the stored row
    // is not checked here.
    [Test]
    public void WeddingVowComplete_AFailedGiftMailMarriesNobody() {
        StartHall();
        long marriageId;
        using (GameStorage.Request db = test.Storage.Context()) {
            Account account = db.CreateAccount(new Account { Username = "partner" }, "pw");
            var partner = new Character { AccountId = account.Id, Name = "partner", Mastery = new Mastery() };
            partner.ReturnMaps.Push(2000062);
            long partnerId = db.CreateCharacter(partner)!.Id;
            marriageId = db.CreateMarriage(CharacterId, partnerId)!.Id;
            test.Session.Marriage.Marriage = db.GetMarriage(CharacterId)!;
        }
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Mail>().Any();

        Field.Wedding = new WeddingCeremony(marriageId, CharacterId, CharacterId, CharacterId);
        Action("""<action name="wedding_vow_complete" />""");
        test.FailSave = null;

        Assert.That(Field.Wedding.Complete, Is.False);
        Assert.That(test.Session.Marriage.Marriage.Status, Is.Not.EqualTo(MaritalStatus.Married));
    }

    // An exception while the vow's store runs (here the hall's gift list throws once the gift mail is created) ends in
    // the vow's own catch: the transaction is rolled back, the ceremony is not complete, the couple is not married and
    // no mail notice goes out, so a later vow may try again. Before, the exception left the vow and the ceremony
    // stayed complete with nothing stored, so no vow could be made again. The in-memory store keeps no rollback (its
    // marriage row is married and its mail row stays), so the stored rows and a second vow are not checked here.
    [Test]
    public void WeddingVowComplete_AnExceptionInTheStoreUncompletes() {
        StartHall();
        long marriageId;
        using (GameStorage.Request db = test.Storage.Context()) {
            Account account = db.CreateAccount(new Account { Username = "partner" }, "pw");
            var partner = new Character { AccountId = account.Id, Name = "partner", Mastery = new Mastery() };
            partner.ReturnMaps.Push(2000062);
            long partnerId = db.CreateCharacter(partner)!.Id;
            marriageId = db.CreateMarriage(CharacterId, partnerId)!.Id;
            test.Session.Marriage.Marriage = db.GetMarriage(CharacterId)!;
        }

        WeddingCeremony ceremony = Field.Wedding = new WeddingCeremony(marriageId, CharacterId, CharacterId, CharacterId) {
            Gifts = new ThrowingGifts(),
        };
        Assert.DoesNotThrow(() => Action("""<action name="wedding_vow_complete" />"""));

        Assert.Multiple(() => {
            Assert.That(ceremony.Complete, Is.False);
            Assert.That(test.Session.Marriage.Marriage.Status, Is.Not.EqualTo(MaritalStatus.Married));
            Assert.That(test.World.MailNotifications, Is.Empty);
        });
    }

    private sealed class ThrowingGifts : IReadOnlyList<WeddingPackage.HallData.Item> {
        public int Count => 1;
        public WeddingPackage.HallData.Item this[int index] => throw new InvalidOperationException("gift list");
        public IEnumerator<WeddingPackage.HallData.Item> GetEnumerator() => throw new InvalidOperationException("gift list");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // wedding_entry_in_field (60): GroomBride with is_in_field 1 holds while both of the couple are in the room, 0
    // while one is missing. Other entry types and a room without a ceremony are false either way.
    [Test]
    public void WeddingEntryInField_TheCouple() {
        StartHall();
        Assert.That(Condition("""<condition name="wedding_entry_in_field" entry_type="GroomBride" is_in_field="0" />"""), Is.False);

        Field.Wedding = new WeddingCeremony(7001, CharacterId, PartnerId, CharacterId);
        Assert.That(Condition("""<condition name="wedding_entry_in_field" entry_type="GroomBride" is_in_field="1" />"""), Is.False);
        Assert.That(Condition("""<condition name="wedding_entry_in_field" entry_type="GroomBride" is_in_field="0" />"""), Is.True);
        Assert.That(Condition("""<condition name="wedding_entry_in_field" entry_type="Groom" is_in_field="1" />"""), Is.False);
        Assert.That(Condition("""<condition name="wedding_entry_in_field" entry_type="Groom" is_in_field="0" />"""), Is.False);
    }

    // wedding_mutual_agree_result (72): after wedding_mutual_agree asks the couple, success 0 holds once one of them
    // refused and success 1 once both accepted; with an answer missing neither holds. Cancel drops the question.
    [Test]
    public void WeddingMutualAgreeResult_Answers() {
        StartHall();
        WeddingCeremony ceremony = Field.Wedding = new WeddingCeremony(7001, CharacterId, PartnerId, CharacterId);
        const string accepted = """<condition name="wedding_mutual_agree_result" agree_type="startActing" success="1" />""";
        const string refused = """<condition name="wedding_mutual_agree_result" agree_type="startActing" success="0" />""";
        Assert.That((Condition(accepted), Condition(refused)), Is.EqualTo((false, false)));

        Action("""<action name="wedding_mutual_agree" agree_type="startActing" />""");
        ceremony.Answer(CharacterId, WeddingAgreeType.StartActing, true);
        Assert.That((Condition(accepted), Condition(refused)), Is.EqualTo((false, false)));
        ceremony.Answer(PartnerId, WeddingAgreeType.StartActing, true);
        Assert.That((Condition(accepted), Condition(refused)), Is.EqualTo((true, false)));
        ceremony.Answer(PartnerId, WeddingAgreeType.StartActing, false);
        Assert.That((Condition(accepted), Condition(refused)), Is.EqualTo((false, true)));
        // Asking again clears the earlier answers.
        Action("""<action name="wedding_mutual_agree" agree_type="startActing" />""");
        Assert.That((Condition(accepted), Condition(refused)), Is.EqualTo((false, false)));
        ceremony.Answer(PartnerId, WeddingAgreeType.StartActing, false);
        Assert.That((Condition(accepted), Condition(refused)), Is.EqualTo((false, true)));
        // Another question's result is not this one's.
        Assert.That(Condition("""<condition name="wedding_mutual_agree_result" agree_type="endActing" success="0" />"""), Is.False);

        Action("""<action name="wedding_mutual_cancel" agree_type="startActing" />""");
        Assert.That((Condition(accepted), Condition(refused)), Is.EqualTo((false, false)));
    }

    private void StartRecord() {
        test.Session.MiniGameRecord = new MiniGameUserRecord(CharacterId) { TotalRounds = 5, ShowResult = true, MinRound = 1 };
    }

    // end_mini_game_round (78): a player in the winner box cleared the round and gets the meso; the game goes on.
    [Test]
    public void EndMiniGameRound_Winner() {
        StartHall();
        StartRecord();
        long meso = test.Session.Currency.Meso;

        Action("""<action name="end_mini_game_round" winner_box_id="1" exp_rate="0.1" meso="300" />""");

        MiniGameUserRecord? record = test.Session.MiniGameRecord;
        Assert.That(record, Is.Not.Null);
        Assert.That(record!.ClearedRounds, Is.EqualTo(1));
        Assert.That(test.Session.Currency.Meso, Is.EqualTo(meso + 300));
        Assert.That(record.Rewards[DungeonRewardType.Meso], Is.EqualTo(300));
        Assert.That(Sent(SendOp.DungeonReward), Is.Empty);
    }

    // A player outside the winner box lost: the game ends for them with the result. With is_only_winner the losers
    // are left as they are (the scripts that set it end the game next).
    [Test]
    public void EndMiniGameRound_Loser() {
        StartHall();
        StartRecord();
        Action("""<action name="end_mini_game_round" winner_box_id="2" exp_rate="0.1" is_only_winner="1" />""");
        Assert.That(test.Session.MiniGameRecord, Is.Not.Null);
        Assert.That(Sent(SendOp.DungeonReward), Is.Empty);

        Action("""<action name="end_mini_game_round" winner_box_id="2" exp_rate="0.1" />""");
        Assert.That(test.Session.MiniGameRecord, Is.Null);
        Assert.That(Sent(SendOp.DungeonReward), Has.Count.EqualTo(1));
    }

    // The goal round of the user-opened Escape (81000001 goal.xml) ends with no game_name at exp rate 1: the winner
    // and, with is_gain_loser_bonus, the loser get the started game's userMiniGame exp (KR factor 1.5), not the mass
    // event's miniGame row (25). Exp base 1000 at every level.
    [Test]
    public void EndMiniGameRound_Unnamed_TakesTheStartedGamesExp() {
        StartHall();
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> {
            [ExpType.miniGame] = new(1, 25f),
            [ExpType.userMiniGame] = new(1, 1.5f),
        }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1000L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));

        Action("""<action name="start_mini_game" round="5" box_id="1" game_name="UserMassive_Escape" />""");
        long exp = test.Session.Player.Value.Character.Exp;
        Action("""<action name="end_mini_game_round" winner_box_id="1" exp_rate="1" />""");
        Assert.That(test.Session.Player.Value.Character.Exp - exp, Is.EqualTo(1500));
        Assert.That(test.Session.MiniGameRecord!.Rewards[DungeonRewardType.Exp], Is.EqualTo(1500));

        Action("""<action name="start_mini_game" round="5" box_id="1" game_name="UserMassive_Escape" />""");
        exp = test.Session.Player.Value.Character.Exp;
        Action("""<action name="end_mini_game_round" winner_box_id="2" exp_rate="1" is_gain_loser_bonus="1" />""");
        Assert.That(test.Session.Player.Value.Character.Exp - exp, Is.EqualTo(1500));
        Assert.That(test.Session.MiniGameRecord, Is.Null);
    }

    // start_mini_game_round (55) sends nothing and leaves the records as they are.
    [Test]
    public void StartMiniGameRound_SendsNothing() {
        StartHall();
        StartRecord();
        Action("""<action name="start_mini_game_round" box_id="1" round="2" />""");
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(test.Session.MiniGameRecord!.ClearedRounds, Is.EqualTo(0));
    }
    // move_to_portal (19): the players in the box with the user tag (or any tag when none is named) go to the
    // portal; another box, another tag or a portal the field lacks moves nobody.
    [Test]
    public void MoveToPortal_TheTaggedPlayersInTheBox() {
        StartHall();
        test.Session.Player.TagId = 2;
        Portal(3, new Vector3(1000, 0, 0));
        test.TakeSent();

        Action("""<action name="move_to_portal" user_tag_id="2" portal_id="3" box_id="2" />""");
        Action("""<action name="move_to_portal" user_tag_id="1" portal_id="3" box_id="1" />""");
        Action("""<action name="move_to_portal" user_tag_id="2" portal_id="4" box_id="1" />""");
        Assert.That(Moves(), Is.Empty);

        Action("""<action name="move_to_portal" user_tag_id="2" portal_id="3" box_id="1" />""");
        Assert.That(Moves(), Is.EqualTo(new[] { (test.Session.Player.ObjectId, new Vector3(1000, 0, 25)) }));
        Action("""<action name="move_to_portal" portal_id="3" box_id="1" />""");
        Assert.That(Moves(), Is.EqualTo(new[] { (test.Session.Player.ObjectId, new Vector3(1000, 0, 25)) }));
    }

    // start_mini_game (16): each player in the box gets a fresh record of the game's rounds; a player outside it
    // gets none.
    [Test]
    public void StartMiniGame_RecordsThePlayersInTheBox() {
        StartHall();
        Action("""<action name="start_mini_game" round="5" box_id="2" game_name="springbeach" />""");
        Assert.That(test.Session.MiniGameRecord, Is.Null);

        Action("""<action name="start_mini_game" round="5" box_id="1" game_name="springbeach" />""");
        MiniGameUserRecord? record = test.Session.MiniGameRecord;
        Assert.That(record, Is.Not.Null);
        Assert.That((record!.CharacterId, record.TotalRounds, record.MinRound, record.ClearedRounds), Is.EqualTo((CharacterId, 5, 1, 0)));
        Assert.That(record.GameName, Is.EqualTo("springbeach"));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The result window shows unless is_show_result_ui says no: the trigger definition's default is True, and the KR
    // scripts write "false" only on the games without one (UserMassive_Springbeach next to the plain springbeach).
    [Test]
    public void StartMiniGame_ShowsTheResultUnlessTold() {
        StartHall();
        Action("""<action name="start_mini_game" round="5" box_id="1" game_name="springbeach" />""");
        Assert.That(test.Session.MiniGameRecord!.ShowResult, Is.True);

        Action("""<action name="start_mini_game" round="1" box_id="9001" is_show_result_ui="False" game_name="PinataWD" />""");
        Assert.That(test.Session.MiniGameRecord!.ShowResult, Is.True);
        Action("""<action name="start_mini_game" round="1" box_id="1" is_show_result_ui="False" game_name="PinataWD" />""");
        Assert.That(test.Session.MiniGameRecord!.ShowResult, Is.False);
        Action("""<action name="start_mini_game" round="10" box_id="1" is_show_result_ui="1" game_name="oxquiz_ugc" />""");
        Assert.That(test.Session.MiniGameRecord!.ShowResult, Is.True);
    }
}
