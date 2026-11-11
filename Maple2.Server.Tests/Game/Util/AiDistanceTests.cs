using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class AiDistanceTests {
    private static readonly Vector3 Self = new(100, 200, 300);

    private static Vector3 Away(float distance) => Self + Vector3.Normalize(new Vector3(3, 4, 0)) * distance;

    // AI_DefaultPetTaming traceAndRun: over 400 traces, less than 400 runs away, exactly 400 stands by.
    [TestCase(1000f, "trace")]
    [TestCase(200f, "runaway")]
    [TestCase(400f, "standby")]
    public void PetTamingTracesFarAndRunsFromNear(float distance, string expected) {
        Vector3 target = Away(distance);
        string action = AiDistance.Over(Self, target, 400) ? "trace" : AiDistance.Less(Self, target, 400) ? "runaway" : "standby";
        Assert.That(action, Is.EqualTo(expected));
    }

    // AI_Primadonna's rush: the first DistanceLess that passes picks the skills for that bracket.
    [TestCase(100f, 300)]
    [TestCase(450f, 600)]
    [TestCase(1700f, 1800)]
    [TestCase(2000f, -1)]
    public void PrimadonnaRushTakesTheNearestBracket(float distance, int expected) {
        int[] brackets = [300, 600, 900, 1200, 1500, 1800];
        Vector3 target = Away(distance);
        int picked = brackets.Cast<int?>().FirstOrDefault(b => AiDistance.Less(Self, target, b!.Value)) ?? -1;
        Assert.That(picked, Is.EqualTo(expected));
    }
}
