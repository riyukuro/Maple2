using System.Linq;
using System.Xml;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

public class ReactorTableReaderTests {
    private const string Reactors = """
        <ms2>
            <reactor id="10000002" reactDistance="155" reactorStateList="[120, 121, 122]" gameEventId="900080" rewardItemListXml='&lt;items&gt;&#x0A;&lt;v itemID=&quot;20001150&quot; count=&quot;2&quot; grade=&quot;1&quot; /&gt;&#x0A;&lt;/items&gt;' reactTimeMSec="1500" locale="KR" />
            <reactor id="10000002" reactDistance="155" reactorStateList="[120, 121, 122]" gameEventId="900070" rewardItemListXml='&lt;items&gt;&lt;v itemID=&quot;20001151&quot; count=&quot;1&quot; grade=&quot;1&quot; /&gt;&lt;/items&gt;' locale="NA" />
            <reactor id="10000009" reactorStateList="[120, 999]" locale="KR" />
        </ms2>
        """;

    private const string States = """
        <ms2>
            <reactorState id="120" type="connect" expireTimeSec="60"><condItem code="30001288" consume="1" /></reactorState>
            <reactorState id="121" type="sleep" nextTimeSec="15" expireTimeSec="30"><condItem /></reactorState>
            <reactorState id="122" type="reward" nextTimeSec="240" expireTimeSec="240"><condItem /></reactorState>
        </ms2>
        """;

    private static ReactorTable Read(string locale) {
        var reactors = new XmlDocument();
        reactors.LoadXml(Reactors);
        var states = new XmlDocument();
        states.LoadXml(States);
        return ReactorTableReader.Read(reactors, states, node => node.GetAttribute("locale") == locale);
    }

    [Test]
    public void Read_KeepsTheLocalesRowAndJoinsTheStates() {
        ReactorTable table = Read("KR");

        // 10000009 lists a state that does not exist, so the row is dropped as in the client.
        Assert.That(table.Entries.Keys, Is.EquivalentTo(new[] { 10000002 }));
        ReactorTable.Entry entry = table.Entries[10000002];
        Assert.That(entry.GameEventId, Is.EqualTo(900080));
        Assert.That(entry.ReactDistance, Is.EqualTo(155f));
        Assert.That(entry.ReactTimeMs, Is.EqualTo(1500));
        Assert.That(entry.Rewards, Is.EqualTo(new[] { new ReactorTable.Reward(20001150, 1, 2) }));

        Assert.That(entry.States.Select(state => state.Type),
            Is.EqualTo(new[] { ReactorStateType.Connect, ReactorStateType.Sleep, ReactorStateType.Reward }));
        Assert.That(entry.States[0].ExpireTimeSec, Is.EqualTo(60));
        Assert.That(entry.States[0].ConditionItems, Is.EqualTo(new[] { new ReactorTable.ConditionItem(30001288, 1, 1, 0) }));
        // A condItem without a code is no condition.
        Assert.That(entry.States[1].ConditionItems, Is.Empty);
        Assert.That(entry.States[1].NextTimeSec, Is.EqualTo(15));
    }

    [Test]
    public void Read_OtherLocaleGetsItsOwnRow() {
        ReactorTable.Entry entry = Read("NA").Entries[10000002];
        Assert.That(entry.GameEventId, Is.EqualTo(900070));
        // reactTimeMSec defaults to 15000 in the client.
        Assert.That(entry.ReactTimeMs, Is.EqualTo(15000));
        Assert.That(entry.Rewards, Is.EqualTo(new[] { new ReactorTable.Reward(20001151, 1, 1) }));
    }
}
