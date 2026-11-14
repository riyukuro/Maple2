using System.Linq;
using System.Xml;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// KR destroy_monster spells its every-spawn id list both "all" (111 actions) and "All" (4 actions in 52010055_qd,
// 52100043_qd and 52100060_qd); the second threw in int.Parse, so the whole script failed to load.
public class TriggerAllIdsParseTests {
    [Test]
    public void ParseIntArray_AllInAnyCase() {
        Assert.That(TriggerFunctionMapping.ParseIntArray("all"), Is.EqualTo(new[] { -1 }));
        Assert.That(TriggerFunctionMapping.ParseIntArray("All"), Is.EqualTo(new[] { -1 }));
    }

    [Test]
    public void DestroyMonster_AllLoadsTheScript() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2><state name="End"><onEnter>
              <action name="destroy_monster" spawn_ids="All" />
            </onEnter></state></ms2>
            """);
        Trigger trigger = TriggerCache.ParseTrigger("52100060_qd", "main", document);
        Assert.That(trigger.States.Single().Enter!.Actions.Single(), Is.InstanceOf<Trigger.DestroyMonster>());
    }
}
