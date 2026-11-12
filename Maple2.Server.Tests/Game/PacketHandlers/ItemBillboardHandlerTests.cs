using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemBillboard 8, the staff's remove request: u8 8, i64 board id. Only a GM account removes a board.
public class ItemBillboardHandlerTests {
    private const long BoardUid = 4242;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private FieldInteract board = null!;
    private readonly ItemBillboardHandler handler = new();

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        var metadata = new InteractObjectMetadata(1, InteractType.BillBoard, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0,
            WeaponItemId: 0, new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
        board = test.AddBillboard(metadata, new InteractBillBoardObject("BillBoard_x", new Ms2InteractMesh(1, default, default),
            new Character { Id = 77, Name = "Owner", Mastery = new Mastery() }) {
            BoardUid = BoardUid,
            ExpirationTime = long.MaxValue,
        });
        test.TakeSent();
    }

    private void Remove(long boardUid) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteLong(boardUid);
        }));
        test.RunFieldQueue();
    }

    private bool Stands => session.Field!.FindBillboard(BoardUid) != null;

    [Test]
    public void APlayerWithoutTheGmPermissionRemovesNothing() {
        Remove(BoardUid);

        Assert.That(Stands, Is.True);
        Assert.That(board.State, Is.EqualTo(InteractState.Reactable));
        Assert.That(test.TakeSent().Any(packet => TestGameSession.Op(packet) == SendOp.InteractObject), Is.False);
    }

    [Test]
    public void AGmRemovesTheBoardWithTheId() {
        session.Player.AdminPermissions = AdminPermissions.GameMaster;

        Remove(BoardUid);

        Assert.That(Stands, Is.False);
        Assert.That(board.State, Is.EqualTo(InteractState.Hidden));
        // The board is hidden, then taken out of the field.
        Assert.That(test.TakeSent().Count(packet => TestGameSession.Op(packet) == SendOp.InteractObject), Is.EqualTo(2));
    }

    [TestCase(0)]
    [TestCase(BoardUid + 1)]
    public void AGmsRequestForNoBoardOfTheFieldIsDropped(long boardUid) {
        session.Player.AdminPermissions = AdminPermissions.GameMaster;

        Remove(boardUid);

        Assert.That(Stands, Is.True);
        Assert.That(test.TakeSent().Any(packet => TestGameSession.Op(packet) == SendOp.InteractObject), Is.False);
    }
}
