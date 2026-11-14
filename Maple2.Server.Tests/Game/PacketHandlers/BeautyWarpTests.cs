using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The move to a salon (Beauty 10, the hair salon 1, the face shop 3, the dye shop 5). The client refuses nothing
// before it sends; a dead player, one in combat and one already in the salon's field get the client's texts for it.
public class BeautyWarpTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int SalonMap = 52000065;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void Start() {
        test = new TestGameSession();
        test.Start();
        Salon(SalonMap);
    }

    private void Salon(int mapId) {
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { BeautyHairShopGotoFieldID = mapId, BeautyHairShopGotoPortalID = 1 });
        test.TakeSent();
    }

    private void Warp(short type) {
        new BeautyHandler {
            ItemMetadata = session.ItemMetadata,
            TableMetadata = test.TableMetadata,
            NpcMetadata = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(10);
                writer.WriteShort(type);
            }));
    }

    private static string Refused(StringCode code) =>
        Convert.ToHexString(NoticePacket.Message(code, NoticePacket.Flags.Message | NoticePacket.Flags.Alert).ToArray());

    [Test]
    public void TheRefusalsInOrder() {
        Assert.That(BeautyHandler.WarpRefusal(false, false, false), Is.Null);
        Assert.That(BeautyHandler.WarpRefusal(true, true, true), Is.EqualTo(StringCode.s_beauty_goto_map_invalid_dead));
        Assert.That(BeautyHandler.WarpRefusal(false, true, true), Is.EqualTo(StringCode.s_beauty_goto_map_invalid_battle));
        Assert.That(BeautyHandler.WarpRefusal(false, false, true), Is.EqualTo(StringCode.s_beauty_goto_map_invalid_samefield));
    }

    [Test]
    public void ADeadPlayerDoesNotMove() {
        typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(session.Player, true);

        Warp(1);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Refused(StringCode.s_beauty_goto_map_invalid_dead) }));
    }

    [Test]
    public void APlayerInCombatDoesNotMove() {
        session.Player.InBattle = true;
        test.TakeSent();

        Warp(1);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Refused(StringCode.s_beauty_goto_map_invalid_battle) }));
    }

    [Test]
    public void APlayerInTheSalonDoesNotMove() {
        Salon(session.Field!.MapId);

        Warp(1);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Refused(StringCode.s_beauty_goto_map_invalid_samefield) }));
    }

    // A type the client never sends (only 1, 3 and 5) is dropped; an allowed move goes on to the field move.
    [Test]
    public void OtherTypesAreDroppedAndAMoveGoesOn() {
        MapMetadata map = TestGameSession.Map(SalonMap, notInPlayedClient: true);
        test.AddMap(map with { Limit = (MapMetadataLimit) RuntimeHelpers.GetUninitializedObject(typeof(MapMetadataLimit)) });

        Warp(2);
        Assert.That(test.TakeSent(), Is.Empty);

        Warp(1);
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.RequestFieldEnter));
    }
}
