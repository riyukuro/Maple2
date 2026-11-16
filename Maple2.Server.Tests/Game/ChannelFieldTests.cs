using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Game;
using Maple2.Server.Game.Service;

namespace Maple2.Server.Tests.Game;

// The channel's field call from the World: a request of no known kind answers empty, a dungeon room the table does
// not hold is not made, and a rank duel match that does not name two characters is not made.
public class ChannelFieldTests {
    private ChannelService service = null!;

    [SetUp]
    public void SetUp() {
        var test = new TestGameSession();
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata>()));
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        service = new ChannelService(server, null!, test.Storage, null!, test.TableMetadata, null!);
    }

    private FieldResponse Call(FieldRequest request) => service.Field(request, null!).GetAwaiter().GetResult();

    [Test]
    public void ARequestOfNoKind_AnswersEmpty() {
        FieldResponse response = Call(new FieldRequest());
        Assert.That(response.Error, Is.Zero);
        Assert.That(response.RoomId, Is.Zero);
    }

    [Test]
    public void ADungeonRoomNotInTheTable_IsNotMade() {
        FieldResponse response = Call(new FieldRequest { CreateDungeon = new FieldRequest.Types.CreateDungeon { DungeonId = 12345 } });
        Assert.That(response.Error, Is.EqualTo((int) MigrationError.s_move_err_dungeon_not_exist));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void ARankDuelWithoutTwoCharacters_IsNotMade(int count) {
        var create = new FieldRequest.Types.CreatePvpMatch { MatchId = 1 };
        for (int i = 0; i < count; i++) {
            create.CharacterIds.Add(100 + i);
        }
        FieldResponse response = Call(new FieldRequest { CreatePvpMatch = create });
        Assert.That(response.Error, Is.EqualTo((int) MigrationError.s_move_err_FailCreateDungeon));
    }
}
