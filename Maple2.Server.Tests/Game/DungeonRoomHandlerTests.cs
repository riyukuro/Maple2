using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.World.Service;
using Maple2.Model.Error;
using Maple2.Model.Game;

namespace Maple2.Server.Tests.Game;

// The dungeon room window's requests as the client sends them (RoomDungeon, 0x0094 bytes): the favorite mark, the
// expert (veteran) change and the helper and member recruit, each answered with what the client's room dialog shows.
public class DungeonRoomHandlerTests {
    private const int Room = 23050003;
    private TestGameSession test = null!;
    private DungeonRoomHandler handler = null!;

    private void Setup(int helperClears = 5, bool disabledFindHelper = false) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 50000016) with {
            HelperRequireClearCount = helperClears,
            DisabledFindHelper = disabledFindHelper,
            Level = 50,
        };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.Start();
        test.Session.Dungeon.Records = new Dictionary<int, DungeonRecord>();
        handler = new DungeonRoomHandler { TableMetadata = test.TableMetadata, World = test.World };
        test.TakeSent();
    }

    private void Send(byte command, int dungeonId, params byte[] rest) {
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteInt(dungeonId);
            foreach (byte b in rest) {
                writer.WriteByte(b);
            }
        }));
    }

    [Test]
    public void Favorite_MarksTheRecordAndSendsItBack() {
        Setup();
        test.Session.Dungeon.Records[Room] = new DungeonRecord(Room);

        Send(24, Room, 1);

        Assert.That(test.Session.Dungeon.Records[Room].Flag.HasFlag(DungeonRecordFlag.Favorite), Is.True);
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Contain(SendOp.RoomDungeon));

        Send(24, Room, 0);

        Assert.That(test.Session.Dungeon.Records[Room].Flag.HasFlag(DungeonRecordFlag.Favorite), Is.False);
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Contain(SendOp.RoomDungeon));
    }

    [Test]
    public void Favorite_WithoutARecordSendsNothing() {
        Setup();

        Send(24, Room, 1);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void ChangeExpert_NeedsTheClearCount() {
        Setup(helperClears: 5);
        test.Session.Dungeon.Records[Room] = new DungeonRecord(Room) { TotalClears = 4 };

        Send(17, Room);

        Assert.That(test.Session.Dungeon.Records[Room].Flag.HasFlag(DungeonRecordFlag.Veteran), Is.False);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void ChangeExpert_WithTheClearsMakesAVeteranOnce() {
        Setup(helperClears: 5);
        test.Session.Dungeon.Records[Room] = new DungeonRecord(Room) { TotalClears = 5 };

        Send(17, Room);

        Assert.That(test.Session.Dungeon.Records[Room].Flag.HasFlag(DungeonRecordFlag.Veteran), Is.True);
        Assert.That(test.TakeSent().Select(TestGameSession.Op).Count(op => op == SendOp.RoomDungeon), Is.EqualTo(2));

        Send(17, Room);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void ChangeExpert_RoomWithoutHelperCountIsIgnored() {
        Setup(helperClears: 0);
        test.Session.Dungeon.Records[Room] = new DungeonRecord(Room) { TotalClears = 99 };

        Send(17, Room);

        Assert.That(test.Session.Dungeon.Records[Room].Flag.HasFlag(DungeonRecordFlag.Veteran), Is.False);
    }

    [Test]
    public void FindHelper_FeatureOffSendsNothingAndAsksNoWorld() {
        Setup();
        test.SetFeaturesOff(DungeonRoomHandler.FindHelperFeature);
        bool asked = false;
        test.World.PartyWorld = _ => {
            asked = true;
            return new PartyResponse();
        };

        Send(15, Room);

        Assert.That(asked, Is.False);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void FindMember_RoomWithoutHelperCountGetsTheShutdownError() {
        Setup(helperClears: 0);

        Send(16, Room);

        Assert.That(test.TakeSent().Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.RoomDungeon }));
    }

    [Test]
    public void FindMember_AsksTheWorldWithTheRoomsLimits() {
        Setup();
        PartyRequest? seen = null;
        test.World.PartyWorld = request => {
            seen = request;
            return new PartyResponse();
        };

        Send(16, Room);

        Assert.That(seen, Is.Not.Null);
        Assert.That(seen!.RequestorId, Is.EqualTo(test.Session.CharacterId));
        Assert.That(seen.DungeonRecruit.Kind, Is.EqualTo((int) DungeonRecruitKind.Member));
        Assert.That(seen.DungeonRecruit.RoomId, Is.EqualTo(Room));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void FindHelper_InsideTheCooldownTellsTheRequester() {
        Setup();
        test.World.PartyWorld = _ => new PartyResponse { CooldownMilliseconds = 30000 };

        Send(15, Room);

        Assert.That(test.TakeSent().Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Party }));
    }

    [Test]
    public void FindMember_WorldErrorIsShownToTheRequester() {
        Setup();
        test.World.PartyWorld = _ => new PartyResponse { Error = (int) PartyError.s_party_err_not_found };

        Send(16, Room);

        Assert.That(test.TakeSent().Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Party }));
    }

    [Test]
    public void HallLobby_WithoutAHallMapDoesNothing() {
        Setup();

        Send(4, 0);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The chaos raid (4), reverse raid (12) and colosseum (13) hall buttons, a bare mode byte each
    // (UIDungeonSearchDialog::OnLoadComplete): pressed on the hall's own map, the client's already-there text (Error 18
    // with the code, arg 0); inside a dungeon room, its can't-enter-in-dungeon text (16). Neither moves the player.
    [TestCase((byte) 4, 63000047, 13)]
    [TestCase((byte) 12, 63000054, 13)]
    [TestCase((byte) 13, 83000001, 15)]
    public void HallLobby_OnTheHallsMapIsRefused(byte mode, int hall, int code) {
        Setup();
        SetHalls(hall);
        test.EnterMap(TestGameSession.Map(hall));

        handler.Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(mode)));

        Assert.That(test.TakeSent().Single(), Is.EqualTo(HallError(code)));
        Assert.That(test.Session.Field!.MapId, Is.EqualTo(hall));
    }

    [TestCase((byte) 4, 63000047)]
    [TestCase((byte) 12, 63000054)]
    [TestCase((byte) 13, 83000001)]
    public void HallLobby_InsideADungeonIsRefused(byte mode, int hall) {
        Setup();
        SetHalls(hall);
        test.EnterDungeon(TestGameSession.DungeonRoom(Room, 50000016));

        handler.Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(mode)));

        Assert.That(test.TakeSent().Single(), Is.EqualTo(HallError(16)));
        Assert.That(test.Session.Field!.MapId, Is.EqualTo(50000016));
    }

    private void SetHalls(int hall) {
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            ChaosDungeonHallFieldID = 63000047,
            ReverseRaidDungeonHallFieldID = 63000054,
            ColosseumDungeonHallFieldID = 83000001,
        });
        test.AddMap(TestGameSession.Map(hall));
    }

    private static byte[] HallError(int code) {
        var bytes = new List<byte> { 0x1A, 0x00, 18 };
        bytes.AddRange(System.BitConverter.GetBytes(code));
        bytes.AddRange(System.BitConverter.GetBytes(0));
        return bytes.ToArray();
    }
}
