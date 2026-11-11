using System.Collections.Generic;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class GuideRecordRulesTests {
    private static ByteReader Body(int count, params (int Id, int Step)[] records) {
        var writer = new ByteWriter();
        writer.WriteInt(count);
        foreach ((int id, int step) in records) {
            writer.WriteInt(id);
            writer.WriteInt(step);
        }
        return new ByteReader(writer.ToArray(), 0);
    }

    // i32 count, then i32 id, i32 step per record. Id 0 is not kept.
    [Test]
    public void Bytes() {
        ByteReader reader = Body(3, (10001, 2), (0, 9), (10002, 1));
        var records = new Dictionary<int, int> { [10001] = 1 };
        Assert.That(GuideRecordRules.Read(reader, records), Is.True);
        Assert.That(reader.Available, Is.EqualTo(0));
        Assert.That(records, Is.EquivalentTo(new Dictionary<int, int> { [10001] = 2, [10002] = 1 }));
    }

    // A count the body does not hold changes nothing.
    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(int.MaxValue)]
    public void BadCount(int count) {
        ByteReader reader = Body(count, (10001, 2));
        var records = new Dictionary<int, int>();
        Assert.That(GuideRecordRules.Read(reader, records), Is.False);
        Assert.That(records, Is.Empty);
    }

    // A full list still takes a new step for an id it holds, and no new id.
    [Test]
    public void Full() {
        var records = new Dictionary<int, int>();
        for (int i = 1; i <= GuideRecordRules.MaxRecords; i++) {
            records[i] = 1;
        }
        ByteReader reader = Body(2, (5, 7), (GuideRecordRules.MaxRecords + 1, 1));
        Assert.That(GuideRecordRules.Read(reader, records), Is.True);
        Assert.That(reader.Available, Is.EqualTo(0));
        Assert.That(records, Has.Count.EqualTo(GuideRecordRules.MaxRecords));
        Assert.That(records[5], Is.EqualTo(7));
    }
}
