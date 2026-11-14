using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The trophies of a world champion season count the damage dealt to its npcs: KR worldchampion_damage code 1 (the
// season) value 1500000 and 4000000, code 5 value 750000 and 2000000. The damage of season 1 counts for code 1 only.
public class WorldChampionConditionTests {
    private static ConditionMetadata Damage(string season) =>
        new(ConditionType.worldchampion_damage, 1500000, new[] { season }.ConvertCodes(), new string[0].ConvertCodes(), 0, 0);

    [Test]
    public void Damage_CodeIsTheSeason() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        Assert.Multiple(() => {
            Assert.That(Damage("1").Check(session, codeLong: 1), Is.True);
            Assert.That(Damage("1").Check(session, codeLong: 5), Is.False);
            Assert.That(Damage("5").Check(session, codeLong: 5), Is.True);
        });
    }
}
