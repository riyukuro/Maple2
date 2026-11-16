using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.AI;

namespace Maple2.Server.Tests.Ingest;

// KR AI_Balrog_Chaos.xml: 3 ShowVibrate nodes with cooltime="82000" under a select. The node keeps its cooltime.
public class AiShowVibrateTests {
    [Test]
    public void CooltimeIsKept() {
        var node = new ShowVibrateNode { name = "ShowVibrate", groupID = 2, cooltime = 82000 };
        Maple2.Model.Metadata.AiMetadata.ShowVibrateNode mapped = AiMapper.MapShowVibrate(node.name, [], node);
        Assert.That(mapped.GroupId, Is.EqualTo(2));
        Assert.That(mapped.Cooltime, Is.EqualTo(82000));
    }
}
