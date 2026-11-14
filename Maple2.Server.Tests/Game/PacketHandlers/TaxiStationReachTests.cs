using System.Numerics;
using System.Reflection;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A car taxi ride (RequestTaxi 1) is refused with no answer unless the character is alive and near its field's taxi
// station: the client sends it only from the world map's TakeTaxi (uiworldmapdialog.cpp:1369), which needs both. The
// server took a paid ride from anywhere, also when dead. The test field refuses departures, so a ride that gets past
// the station check is answered with that refusal. The field id is made up.
public class TaxiStationReachTests {
    private TestGameSession test = null!;

    private GameSession Start(Vector3 player, bool station = true) {
        test = new TestGameSession();
        GameSession session = test.Start();
        MapMetadata map = TestGameSession.Map(2000913) with { CashCall = new MapMetadataCashCall(false, false, false, false, false) };
        test.EnterMap(map);
        var entities = (MapEntityMetadata) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        if (station) {
            typeof(MapEntityMetadata).GetField("<Taxi>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(entities, new TaxiStation(new Vector3(1000, 0, 0), Vector3.Zero));
        }
        typeof(FieldManager).GetField("<Entities>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Field, entities);
        session.Player.Position = player;
        test.TakeSent();
        return session;
    }

    private int Ride(GameSession session) {
        new TaxiHandler {
            MapMetadata = test.MapMetadata,
            EntityMetadata = null!,
            TaxiRoute = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(2000914);
        }));
        return test.TakeSent().Count;
    }

    [Test]
    public void AtTheStation_GetsPastTheCheck() {
        Assert.That(Ride(Start(new Vector3(1150, 0, 0))), Is.EqualTo(1));
    }

    [Test]
    public void AwayFromTheStation_GetsNoAnswer() {
        Assert.That(Ride(Start(Vector3.Zero)), Is.Zero);
    }

    [Test]
    public void InAFieldWithNoStation_GetsNoAnswer() {
        Assert.That(Ride(Start(Vector3.Zero, station: false)), Is.Zero);
    }

    [Test]
    public void WhenDead_GetsNoAnswer() {
        GameSession session = Start(new Vector3(1000, 0, 0));
        typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Player, true);
        Assert.That(Ride(session), Is.Zero);
    }

    [Test]
    public void Reach_IsTheConstant() {
        Assert.Multiple(() => {
            Assert.That(TaxiRules.StationReach(0), Is.EqualTo(200));
            Assert.That(TaxiRules.StationReach(300), Is.EqualTo(300));
        });
    }
}
