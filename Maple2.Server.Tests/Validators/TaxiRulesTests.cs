using System.Collections.Generic;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class TaxiRulesTests {
    [TestCase(0, true)]
    [TestCase(5, true)]
    [TestCase(1, false)]
    [TestCase(3, false)]
    public void DiscoverModes(byte mode, bool expected) {
        Assert.That(TaxiRules.IsDiscover(mode), Is.EqualTo(expected));
    }

    [TestCase(false, true, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    public void Discover(bool known, bool station, bool expected) {
        Assert.That(TaxiRules.CanDiscover(known, station), Is.EqualTo(expected));
    }

    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    public void MesoRide(bool visited, bool premium, bool expected) {
        Assert.That(TaxiRules.CanMesoRide(visited, premium), Is.EqualTo(expected));
    }

    [TestCase(false, true, 1)]
    [TestCase(true, true, 1)]
    [TestCase(false, false, 0)]
    [TestCase(true, false, 3)]
    public void Tickets(bool advanced, bool visited, int expected) {
        Assert.That(TaxiRules.TicketCost(advanced, visited), Is.EqualTo(expected));
    }

    [TestCase(31500, 0f, 31500)]
    [TestCase(31500, 0.1f, 28349)] // 1 - 0.1f is 0.89999998 as a float: the client truncates to 28349
    [TestCase(15, 0.5f, 7)]
    [TestCase(100, 1.5f, 0)]
    public void Fare(long price, float rate, long expected) {
        Assert.That(TaxiRules.AirFare(price, rate), Is.EqualTo(expected));
    }

    // Made-up fields: 1 -> 2 -> 3 -> 4, a short cut 1 -> 5 -> 4, 4 -> 1 back only, 6 cut off, 7 not enabled.
    private static readonly Dictionary<int, IReadOnlyList<int>> Portals = new() {
        [1] = [0, 1, 2, 5, 7],
        [2] = [3, 1],
        [3] = [4],
        [4] = [1],
        [5] = [4, 4],
        [6] = [1],
        [7] = [4],
        [8] = [7, 1],
    };

    [TestCase(1, 1, 0)]
    [TestCase(1, 2, 1)]
    [TestCase(1, 3, 2)]
    [TestCase(1, 4, 2)] // through 5, not 2 and 3
    [TestCase(4, 3, 3)] // portals are one way: 4 -> 1 -> 2 -> 3
    [TestCase(1, 6, -1)] // nothing leads to 6
    [TestCase(1, 9, -1)] // 9 has no portal list
    [TestCase(8, 4, 3)] // 8 -> 7 is not loaded: 8 -> 1 -> 5 -> 4
    [TestCase(7, 4, -1)] // a field that is not enabled has no portals
    public void PortalHops(int from, int to, int expected) {
        Assert.That(TaxiRules.PortalHops(from, to, Portals, field => field != 7), Is.EqualTo(expected));
    }
}
