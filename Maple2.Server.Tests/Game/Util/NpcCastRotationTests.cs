using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class NpcCastRotationTests {
    [Test]
    public void CastWithoutRotationCarriesTheCastersFacing() {
        var facing = new Vector3(0, 0, 135);
        Assert.That(SkillCastRules.CastRotation(default, facing), Is.EqualTo(facing));
    }

    [Test]
    public void CastKeepsTheRotationGivenWithIt() {
        var given = new Vector3(0, 0, -90);
        Assert.That(SkillCastRules.CastRotation(given, new Vector3(0, 0, 135)), Is.EqualTo(given));
        Assert.That(SkillCastRules.CastRotation(given, default), Is.EqualTo(given));
    }
}
