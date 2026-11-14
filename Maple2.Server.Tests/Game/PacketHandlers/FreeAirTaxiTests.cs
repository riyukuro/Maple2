using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using GameLua = Maple2.Server.Game.LuaFunctions.Lua;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestTaxi mode 3 (the meso air taxi) takes no fare while a FreeAirTaxi event runs, and the full fare once it has
// ended; until now only TaxiRules.MesoFare was tested, not the handler's use of the server's events. The ride reaches
// PrepareField, which stops at the test field (one the played client lacks). The field ids are made up.
public class FreeAirTaxiTests {
    private const int From = 2000911, To = 2000912;
    private const string XBlock = "free_air_taxi_test";
    private const long Meso = 1_000_000;
    private static string MeshPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Navmeshes", $"{XBlock}.navmesh");
    private bool madeMesh;

    [SetUp]
    public void MakeMesh() {
        // CheckMapCashCall refuses a field without a navmesh file; only the file's presence is asked.
        Directory.CreateDirectory(Path.GetDirectoryName(MeshPath)!);
        if (!System.IO.File.Exists(MeshPath)) {
            System.IO.File.WriteAllBytes(MeshPath, []);
            madeMesh = true;
        }
    }

    [TearDown]
    public void RemoveMesh() {
        if (madeMesh) {
            System.IO.File.Delete(MeshPath);
            madeMesh = false;
        }
    }

    private static MapMetadata Field(int id, bool notInPlayedClient = false) {
        MapMetadata map = TestGameSession.Map(id, notInPlayedClient: notInPlayedClient);
        return map with {
            XBlock = XBlock,
            CashCall = new MapMetadataCashCall(true, true, false, false, false),
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], false, false, false, false, false, false, false),
        };
    }

    private static (long Paid, long Fare) Ride(DateTime? eventEnd) {
        var test = new TestGameSession();
        if (eventEnd != null) {
            test.AddEvent(1, GameEventType.FreeAirTaxi, new FreeAirTaxi(), DateTime.Now.AddDays(-3), eventEnd);
        }
        MapMetadata from = Field(From);
        test.AddMap(from, Field(To, notInPlayedClient: true));
        GameSession session = test.Start();
        test.EnterMap(from);
        // The premium club check (ConfigManager.IsPremiumClubActive) reads only the player; no premium time.
        var config = (ConfigManager) RuntimeHelpers.GetUninitializedObject(typeof(ConfigManager));
        typeof(ConfigManager).GetField("session", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(config, session);
        session.Config = config;
        session.Player.Value.Account.PremiumTime = 0;
        session.Player.Value.Unlock.Maps.Add(To);
        session.Currency.Meso = Meso;
        new TaxiHandler {
            MapMetadata = test.MapMetadata,
            EntityMetadata = null!,
            TaxiRoute = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(To);
        }));
        long fare = TaxiRules.AirFare(GameLua.CalcAirTaxiCharge(session.Player.Value.Character.Level), 0f);
        return (Meso - session.Currency.Meso, fare);
    }

    [Test]
    public void WhileTheEventRuns_TheRideIsFree() {
        (long paid, long fare) = Ride(DateTime.Now.AddDays(30));
        Assert.Multiple(() => {
            Assert.That(paid, Is.Zero);
            Assert.That(fare, Is.GreaterThan(0));
        });
    }

    [Test]
    public void AfterTheEventEnded_TheFullFareIsTaken() {
        (long paid, long fare) = Ride(DateTime.Now.AddMinutes(-1));
        (long paidWithout, long fareWithout) = Ride(null);
        Assert.Multiple(() => {
            Assert.That(fare, Is.GreaterThan(0));
            Assert.That(paid, Is.EqualTo(fare));
            Assert.That(paidWithout, Is.EqualTo(fareWithout));
        });
    }
}
