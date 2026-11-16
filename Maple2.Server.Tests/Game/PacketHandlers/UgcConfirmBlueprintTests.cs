using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Ugc 3 (the upload's confirmation) for a layout blueprint's preview: the staged blueprint takes the stored picture,
// and the client is sent Ugc 0x10 (i32 object id, i64 blueprint uid, i64 item uid, i32 item id, wstr name, the
// look) and then Ugc 4 (the stored path). Another upload's uid is refused.
public class UgcConfirmBlueprintTests {
    private TestGameSession test = null!;
    private GameSession Session => test.Session;
    private WebStorage web = null!;
    private Item blueprint = null!;
    private long ugcUid;

    private void Start() {
        test = new TestGameSession();
        blueprint = test.Store(TestGameSession.Metadata(Constant.BlueprintId, type: 22));
        test.Start();
        web = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options, NullLogger<WebStorage>.Instance);
        using (WebStorage.Request request = web.Context()) {
            ugcUid = request.CreateUgc(UgcType.LayoutBlueprint, Session.CharacterId, Constant.BlueprintId, System.DateTime.UtcNow.AddMinutes(5))!.Id;
        }
        blueprint = Session.Item.Inventory.Find(Constant.BlueprintId).Single();
        blueprint.Blueprint = new ItemBlueprint { BlueprintUid = 4242 };
        blueprint.Template = new UgcItemLook { Id = ugcUid, Name = "my home", CharacterId = Session.CharacterId, AccountId = Session.AccountId };
        Session.StagedUgcItem = blueprint;
        test.TakeSent();
    }

    private void Confirm(long uid) {
        new UgcHandler { WebStorage = web, ItemMetadata = Session.ItemMetadata, TableMetadata = test.TableMetadata, World = null! }
            .Handle(Session, TestGameSession.Packet(writer => {
                writer.WriteByte(3);
                writer.WriteByte((byte) UgcType.LayoutBlueprint);
                writer.WriteShort(0);
                writer.WriteInt(0);
                writer.WriteLong(Session.AccountId);
                writer.WriteLong(Session.CharacterId);
                writer.WriteUnicodeString("");
                writer.WriteUnicodeString("");
                writer.WriteLong(uid);
                writer.WriteUnicodeString("");
                writer.WriteByte(0);
                writer.WriteByte(0);
                writer.WriteUnicodeString("");
            }));
    }

    private List<byte[]> Ugc(byte mode) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Ugc && packet[2] == mode).ToList();
    }

    [Test]
    public void TheStagedPreview_UpdatesTheBlueprint() {
        Start();

        Confirm(ugcUid);

        List<byte[]> sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Ugc).ToList();
        Assert.That(sent.Select(packet => packet[2]), Is.EqualTo(new byte[] { 16, 4 }));
        var reader = new ByteReader(sent[0], 3);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(Session.Player.ObjectId));
            Assert.That(reader.ReadLong(), Is.EqualTo(4242));
            Assert.That(reader.ReadLong(), Is.EqualTo(blueprint.Uid));
            Assert.That(reader.ReadInt(), Is.EqualTo(Constant.BlueprintId));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("my home"));
            Assert.That(reader.ReadClassWithNew<UgcItemLook>().Id, Is.EqualTo(ugcUid));
            Assert.That(reader.Available, Is.EqualTo(0));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A preview this session did not stage is not made the blueprint's picture: no Ugc 0x10.
    [Test]
    public void AnotherUpload_IsNotTaken() {
        Start();
        long other;
        using (WebStorage.Request request = web.Context()) {
            other = request.CreateUgc(UgcType.LayoutBlueprint, Session.CharacterId, Constant.BlueprintId, System.DateTime.UtcNow.AddMinutes(5))!.Id;
        }

        Confirm(other);

        Assert.That(Ugc(16), Is.Empty);
        Assert.That(blueprint.Template!.Id, Is.EqualTo(ugcUid));
    }
}
