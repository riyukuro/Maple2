using System.Collections.Generic;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game.Util;

// add_effect_nif keeps its effect by npc object id; a player entering later gets the effects of npcs still in
// the field. Made-up values.
public class TriggerEffectNifTests {
    [Test]
    public void LiveNpcEffects_SkipsRemovedNpcs() {
        var effects = new Dictionary<int, (string NifPath, bool IsOutline, float Scale, float RotateZ)> {
            [302] = ("b.nif", false, 1f, 0f),
            [301] = ("a.nif", true, 1.2f, 225f),
            [303] = ("c.nif", true, 1.2f, 90f),
        };
        var live = FieldManager.LiveNpcEffects(effects, objectId => objectId != 303);
        Assert.That(live, Is.EqualTo(new[] {
            (301, "a.nif", true, 1.2f, 225f),
            (302, "b.nif", false, 1f, 0f),
        }));
        Assert.That(FieldManager.LiveNpcEffects(effects, _ => false), Is.Empty);
    }
}
