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
// ended; until now only TaxiRules.MesoFare was tested, not the handler's use of the server's events. The ride leaves
// as a move to another server (TestGameSession.EnterMovingOut) unless a test refuses it. The field ids are made up.
public class FreeAirTaxiTests {
    // How the ride's field change ends: it goes (a move to another server), the World refuses that move, or this
    // server refuses the change (the target is a field the played client lacks).
    public enum Outcome { Went, WorldRefused, RefusedHere }

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

    private static ((long Paid, long Fare) Paid, long MeretPaid) Ride(DateTime? eventEnd, byte mode, bool meretTaxiOn,
                                                                    bool premium = false, bool vipTaxiOn = true, bool visited = true) =>
        Ride(eventEnd, mode, meretTaxiOn, Outcome.Went, premium, vipTaxiOn, visited).Paid;

    // Mode 3 pays in meso; mode 4 (the meret air taxi) pays in meret. Returns what each wallet lost, and the session.
    private static (((long Paid, long Fare) Paid, long MeretPaid) Paid, TestGameSession Test) Ride(DateTime? eventEnd, byte mode,
            bool meretTaxiOn, Outcome outcome, bool premium = false, bool vipTaxiOn = true, bool visited = true) {
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
        test.AddMap(from, Field(To, notInPlayedClient: outcome == Outcome.RefusedHere));
        GameSession session = test.Start();
        if (outcome == Outcome.RefusedHere) {
            test.EnterMap(from);
        } else {
            test.EnterMovingOut(from);
            if (outcome == Outcome.WorldRefused) {
                test.World.MoveAddress = "";
            }
        }
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
        return (((Meso - session.Currency.Meso, fare), Meso - session.Currency.Meret), test);
    }

    // The fare (meso, mode 3) or the merets (mode 4) are kept only for a ride that went: a ride refused here or by the
    // World gives them back. Before, both were taken ahead of the field change and kept whatever its end.
    [TestCase((byte) 3, Outcome.Went, ExpectedResult = true)]
    [TestCase((byte) 3, Outcome.WorldRefused, ExpectedResult = false)]
    [TestCase((byte) 3, Outcome.RefusedHere, ExpectedResult = false)]
    [TestCase((byte) 4, Outcome.Went, ExpectedResult = true)]
    [TestCase((byte) 4, Outcome.WorldRefused, ExpectedResult = false)]
    [TestCase((byte) 4, Outcome.RefusedHere, ExpectedResult = false)]
    public bool TheFare_IsKeptOnlyForARideThatWent(byte mode, Outcome outcome) {
        (((long paid, long fare), long meretPaid), TestGameSession test) = Ride(null, mode, true, outcome);
        Assert.That(fare, Is.GreaterThan(0));
        long taken = mode == 3 ? paid : meretPaid;
        Assert.That(mode == 3 ? meretPaid : paid, Is.Zero, "the other wallet");
        Assert.That(test.World.Moves, Has.Count.EqualTo(outcome == Outcome.RefusedHere ? 0 : 1), "the moves asked for");
        if (outcome != Outcome.Went) {
            Assert.That(taken, Is.Zero);
        }
        return taken > 0;
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
