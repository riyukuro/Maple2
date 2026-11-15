using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The maid dialog's requests (Maid, UIMaidDialog::OnMessage, uimaiddialog.cpp:378-402): mode 4 saves the profile the
// owner filled in (uid, npc id, the 13 profile texts); modes 5-10 are the dialog's debug buttons, which the server
// takes from a debug account only. The ids are made up.
[NonParallelizable]
public class MaidHandlerTests {
    private const int MaidCube = 50400001;
    private const int NpcObjectId = 777;
    private const int TrophyId = 9301;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private long maidUid;
    private Maid maid = null!;

    private void Login(long ownerOffset = 0, string name = "", int nameMax = 12, int descMax = 16, params string[] banned) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(MaidCube));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MaidNameLengthMax = nameMax, MaidDescLengthMax = descMax, MaidAffinityMax = 3 });
        test.SetTable(new MaidTable(new Dictionary<int, MaidTable.Entry>(), new Dictionary<int, int> { [1] = 100, [2] = 200, [3] = 300 },
            new Dictionary<int, MaidTable.Recipe>(), new Dictionary<int, IReadOnlyList<MaidTable.RecipeLevel>>()));
        test.AddAchievement(TestGameSession.Trophy(TrophyId, ConditionType.maid_profile, [1, 2]));
        test.Start();
        Plot plot = test.EnterHome();
        using (GameStorage.Request db = test.Storage.Context()) {
            session.ItemMetadata.TryGet(MaidCube, out ItemMetadata? cubeMetadata);
            PlotCube? cube = db.CreateCube(plot, new PlotCube(cubeMetadata!) { Type = PlotCube.CubeType.Construction });
            Assert.That(cube, Is.Not.Null);
            maidUid = cube!.Id;
            maid = db.CreateMaid(new Maid {
                Uid = maidUid, OwnerId = session.AccountId + ownerOffset, ItemId = MaidCube, MaidId = 3, CreationTime = 1700000000,
                ExpiryTime = 2000000000, Mood = 50, Profile = new MaidProfile { Name = name, Specialty = "kept" },
            })!;
        }
        FieldManager field = session.Field!;
        Type slotType = typeof(FieldManager).GetNestedType("MaidSlot", BindingFlags.NonPublic)!;
        object slot = Activator.CreateInstance(slotType, maid)!;
        object maids = typeof(FieldManager).GetField("maids", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(field)!;
        maids.GetType().GetMethod("TryAdd")!.Invoke(maids, [maid.Uid, slot]);
        banWords = TestGameSession.BanWords(banned);
        test.TakeSent();
    }

    private BanWordStorage banWords = null!;

    private MaidHandler Handler() => new() {
        TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata, BanWordStorage = banWords,
    };

    private List<byte[]> SendProfile(long uid, string name = "Mia", string like = "tea", string specialty = "ignored", int npc = NpcObjectId) {
        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteLong(uid);
            writer.WriteInt(npc);
            foreach (string text in new[] { "Title", name, "0101", "Leo", "160", "50", specialty, "detail", like, "bugs", "reading", "Owner", "Home" }) {
                writer.WriteUnicodeString(text);
            }
        }));
        test.RunFieldQueue();
        return test.TakeSent();
    }

    private List<byte[]> SendDebug(byte command, byte flag = 0, long seconds = 0) {
        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(maidUid);
            if (command == 10) {
                writer.WriteByte(flag);
                writer.WriteLong(seconds);
            }
        }));
        test.RunFieldQueue();
        return test.TakeSent();
    }

    private Maid Stored() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetMaid(maidUid)!;
    }

    [Test]
    public void Profile_OwnerOfUnnamedMaid_SavesIt() {
        Login();

        List<byte[]> sent = SendProfile(maidUid);

        Maid stored = Stored();
        Assert.Multiple(() => {
            Assert.That(stored.Profile.Name, Is.EqualTo("Mia"));
            Assert.That(stored.Profile.Title, Is.EqualTo("Title"));
            Assert.That(stored.Profile.Like, Is.EqualTo("tea"));
            Assert.That(stored.Profile.BelongTo, Is.EqualTo("Home"));
            Assert.That(maid.Profile.Name, Is.EqualTo("Mia"));
            // The dialog has no specialty field, so the maid keeps hers.
            Assert.That(stored.Profile.Specialty, Is.EqualTo("kept"));
            Assert.That(sent.Select(TestGameSession.Op), Does.Contain(SendOp.UserMaid));
            Assert.That(test.TrophyCounter(TrophyId), Is.EqualTo(1));
        });
    }

    // The maid's own refresh: UserMaid Update (3) with the saved maid, then Property (4) with the npc's object id (the
    // npc is not spawned here, so the id the dialog sent).
    [Test]
    public void Profile_AnswersUpdateThenProperty() {
        Login();

        List<byte[]> sent = SendProfile(maidUid);

        List<byte[]> maidPackets = sent.Where(packet => TestGameSession.Op(packet) == SendOp.UserMaid).ToList();
        Assert.That(maidPackets, Has.Count.EqualTo(2));
        Assert.Multiple(() => {
            Assert.That(maidPackets[0][2], Is.EqualTo(3));
            Assert.That(Convert.ToHexString(maidPackets[1]), Is.EqualTo(Convert.ToHexString(Maple2.Server.Game.Packets.UserMaidPacket.Property(NpcObjectId).ToArray())));
        });
    }

    // Profile changed once per fill-in: a named maid cannot be edited again by its owner (only by a debug account).
    [Test]
    public void Profile_NamedMaid_IsKept() {
        Login(name: "Old");

        List<byte[]> sent = SendProfile(maidUid);

        Assert.Multiple(() => {
            Assert.That(Stored().Profile.Name, Is.EqualTo("Old"));
            Assert.That(sent, Is.Empty);
            Assert.That(test.TrophyCounter(TrophyId), Is.Null);
        });
    }

    [Test]
    public void Profile_NamedMaid_DebugAccountCanEdit() {
        Login(name: "Old");
        session.Player.AdminPermissions = AdminPermissions.Debug;

        SendProfile(maidUid);

        Assert.That(Stored().Profile.Name, Is.EqualTo("Mia"));
    }

    [Test]
    public void Profile_OtherAccountsMaid_IsRefused() {
        Login(ownerOffset: 5);

        List<byte[]> sent = SendProfile(maidUid);

        Assert.Multiple(() => {
            Assert.That(Stored().Profile.Name, Is.Empty);
            Assert.That(sent, Is.Empty);
        });
    }

    [Test]
    public void Profile_OtherAccountsMaid_DebugAccountStillRefused() {
        Login(ownerOffset: 5);
        session.Player.AdminPermissions = AdminPermissions.Debug;

        SendProfile(maidUid);

        Assert.That(Stored().Profile.Name, Is.Empty);
    }

    [Test]
    public void Profile_UnknownMaid_SendsNothing() {
        Login();

        List<byte[]> sent = SendProfile(maidUid + 999);

        Assert.Multiple(() => {
            Assert.That(sent, Is.Empty);
            Assert.That(Stored().Profile.Name, Is.Empty);
        });
    }

    // The limits are the dialog's two input limits (12 and 16); every edited text is held to the larger.
    [TestCase(16, true)]
    [TestCase(17, false)]
    public void Profile_TextLength_IsHeldToTheLargerLimit(int length, bool saved) {
        Login();

        SendProfile(maidUid, like: new string('a', length));

        Assert.That(Stored().Profile.Like, Is.EqualTo(saved ? new string('a', length) : string.Empty));
    }

    [Test]
    public void Profile_LimitsNotIngested_LeavesLengthOpen() {
        Login(nameMax: 0, descMax: 0);

        SendProfile(maidUid, like: new string('a', 200));

        Assert.That(Stored().Profile.Like, Has.Length.EqualTo(200));
    }

    [Test]
    public void Profile_BannedWord_IsRefused() {
        Login(banned: ["badword"]);

        List<byte[]> sent = SendProfile(maidUid, like: "xx badword xx");

        Assert.Multiple(() => {
            Assert.That(Stored().Profile.Name, Is.Empty);
            Assert.That(sent, Is.Empty);
        });
    }

    // The ban check runs over the specialty too (it is one of the 13 read texts), although the server then keeps its own.
    [Test]
    public void Profile_BannedWordInSpecialty_IsRefused() {
        Login(banned: ["badword"]);

        SendProfile(maidUid, specialty: "badword");

        Assert.That(Stored().Profile.Name, Is.Empty);
    }

    [Test]
    public void Profile_MaidRemovedBeforeTheFieldThreadRuns_IsLeftAlone() {
        Login();
        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteLong(maidUid);
            writer.WriteInt(NpcObjectId);
            for (int i = 0; i < 13; i++) {
                writer.WriteUnicodeString("x");
            }
        }));
        session.Field!.RemoveMaid(maidUid);
        test.RunFieldQueue();

        Assert.That(Stored().Profile.Name, Is.Empty);
    }

    [TestCase((byte) 5)]
    [TestCase((byte) 6)]
    [TestCase((byte) 7)]
    [TestCase((byte) 8)]
    [TestCase((byte) 9)]
    [TestCase((byte) 10)]
    public void Debug_PlainAccount_ChangesNothing(byte command) {
        Login();

        SendDebug(command, 1, 3600);

        Maid stored = Stored();
        Assert.Multiple(() => {
            Assert.That(stored.Mood, Is.EqualTo(50));
            Assert.That(stored.Level, Is.EqualTo(1));
            Assert.That(stored.ExpiryTime, Is.EqualTo(2000000000));
        });
    }

    [Test]
    public void Debug_MoodButtons_MoveByTen() {
        Login();
        session.Player.AdminPermissions = AdminPermissions.Debug;

        SendDebug(8);
        Assert.That(Stored().Mood, Is.EqualTo(60));
        SendDebug(9);
        SendDebug(9);
        Assert.That(Stored().Mood, Is.EqualTo(40));
    }

    [Test]
    public void Debug_AffinityButtons() {
        Login();
        session.Player.AdminPermissions = AdminPermissions.Debug;

        SendDebug(6); // exp of level 1: a full level
        Assert.That((Stored().Level, Stored().Exp), Is.EqualTo((2, 0)));
        SendDebug(5); // a level at once: the top (3) clears the exp
        Assert.That((Stored().Level, Stored().Exp), Is.EqualTo((3, 0)));
        SendDebug(7);
        Assert.That((Stored().Level, Stored().Exp), Is.EqualTo((1, 0)));
    }

    [TestCase((byte) 0, 1999996400L)]
    [TestCase((byte) 1, 2000003600L)]
    public void Debug_Expiry_MovesTheContractEnd(byte flag, long expected) {
        Login();
        session.Player.AdminPermissions = AdminPermissions.Debug;

        List<byte[]> sent = SendDebug(10, flag, 3600);

        Assert.Multiple(() => {
            Assert.That(Stored().ExpiryTime, Is.EqualTo(expected));
            // The save sends the maid again to its owner (UserMaid Update 3).
            Assert.That(sent.Any(packet => TestGameSession.Op(packet) == SendOp.UserMaid && packet[2] == 3), Is.True);
        });
    }

    [Test]
    public void Debug_Expiry_NegativeSecondsDoNotReverseTheDirection() {
        Login();
        session.Player.AdminPermissions = AdminPermissions.Debug;

        SendDebug(10, 0, -3600);

        Assert.That(Stored().ExpiryTime, Is.EqualTo(2000000000));
    }

    [Test]
    public void UnknownCommand_IsIgnored() {
        Login();

        Assert.DoesNotThrow(() => SendDebug(3));
        Assert.That(Stored().Mood, Is.EqualTo(50));
    }
}
