using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Extensions;
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
    // A name of its own per run: the navmesh file sits in the shared build output, where another test run may delete it.
    private static readonly string XBlock = $"free_air_taxi_test_{Guid.NewGuid():N}";
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

    private static (long Paid, long Fare) Ride(DateTime? eventEnd) => Ride(eventEnd, 3, true).Paid;

    // Mode 3 pays in meso; mode 4 (the meret air taxi) pays in meret. Returns what each wallet lost.
    private static ((long Paid, long Fare) Paid, long MeretPaid) Ride(DateTime? eventEnd, byte mode, bool meretTaxiOn,
                                                                    bool premium = false, bool vipTaxiOn = true, bool visited = true) {
        var test = new TestGameSession();
        var off = new List<string>();
        if (!meretTaxiOn) {
            off.Add(TaxiHandler.MeretAirTaxiFeature);
        }
        if (!vipTaxiOn) {
            off.Add(TaxiHandler.VipAirTaxiFeature);
        }
        if (off.Count > 0) {
            test.SetFeaturesOff(off.ToArray());
        }
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
        session.Player.Value.Account.PremiumTime = premium ? DateTime.Now.AddDays(30).ToEpochSeconds() : 0;
        if (visited) {
            session.Player.Value.Unlock.Maps.Add(To);
        }
        session.Currency.Meso = Meso;
        session.Currency.Meret = Meso;
        new TaxiHandler {
            MapMetadata = test.MapMetadata,
            EntityMetadata = null!,
            TaxiRoute = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            writer.WriteInt(To);
        }));
        long fare = TaxiRules.AirFare(GameLua.CalcAirTaxiCharge(session.Player.Value.Character.Level), 0f);
        return ((Meso - session.Currency.Meso, fare), Meso - session.Currency.Meret);
    }

    // The client sends mode 4 only with MeratAirTaxi on (cashcallmanager.cpp:123): with it off no meret is taken.
    [TestCase(true, ExpectedResult = true)]
    [TestCase(false, ExpectedResult = false)]
    public bool MeretAirTaxi_NeedsTheFeature(bool on) => Ride(null, 4, on).MeretPaid > 0;

    // A premium club member rides free (and to a field never visited) only with VIPAirTaxi on (cashcallmanager.cpp:85);
    // with it off the client sends mode 3 with the meso fare, and only to a visited field.
    [TestCase(true, true, ExpectedResult = 0L)]
    [TestCase(true, false, ExpectedResult = 0L)]
    [TestCase(false, true, ExpectedResult = -1L)]
    [TestCase(false, false, ExpectedResult = 0L)]
    public long PremiumRide_IsFreeOnlyWithVipAirTaxi(bool vipTaxiOn, bool visited) {
        ((long paid, long fare), _) = Ride(null, 3, true, premium: true, vipTaxiOn: vipTaxiOn, visited: visited);
        Assert.That(fare, Is.GreaterThan(0));
        return paid == fare ? -1 : paid;
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
