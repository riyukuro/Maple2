using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Helpers;

namespace Maple2.Server.Tests.Ingest;

// The box the played client's item data names for an item box (ClientItemBoxes): the item's OpenItemBox opens it
// when the data pack's function names another box or none. Shapes from the KR data.
public class ClientItemBoxesTests {
    // 30000928 Level Up Mission Box: the client's param1 30003 (a global box); 30000929 an individual box; 30000940 a
    // required amount; 30000950 two environments that disagree; 30000960 a box only for another locale; 30000970
    // another function.
    private const string Client = """
        <ms2>
          <item id="30000928"><environment><function name="OpenItemBox" param1="30003" /></environment></item>
          <item id="30000929"><environment><function name="OpenItemBox" param1="330001648" /></environment></item>
          <item id="30000940"><environment><function name="OpenItemBox" param1="330001700" param2="5" /></environment></item>
          <item id="30000950">
            <environment><function name="OpenItemBox" param1="330001701" /></environment>
            <environment><function name="OpenItemBox" param1="330001702" /></environment>
          </item>
          <item id="30000960">
            <environment locale="CN"><function name="OpenItemBox" param1="330001703" /></environment>
            <environment><function name="OpenItemBox" param1="330001704" /></environment>
          </item>
          <item id="30000970"><environment><function name="SelectItemBox" parameter="1,330001705" /></environment></item>
        </ms2>
        """;

    private static Dictionary<int, ClientItemBoxes.Box> Read() {
        var doc = new XmlDocument();
        doc.LoadXml(Client);
        return ClientItemBoxes.Read([doc], element => element.GetAttribute("locale") is "" or "KR");
    }

    [Test]
    public void Read_TakesTheAvailableBoxTheEnvironmentsAgreeOn() {
        Assert.That(Read(), Is.EqualTo(new Dictionary<int, ClientItemBoxes.Box> {
            [30000928] = new(30003, 0),
            [30000929] = new(330001648, 0),
            [30000940] = new(330001700, 5),
            [30000960] = new(330001704, 0),
        }));
    }

    // The data pack's function opens the client's box already (global or individual place, comma or xml form), or not.
    [TestCase("OpenItemBox", "0,1,30000301,0", 30003, false)]
    [TestCase("OpenItemBox", "53,915,30000201,0", 330000420, false)]
    [TestCase("OpenItemBox", "0,1,0,330001854", 330001418, false)]
    [TestCase("OpenItemBox", "0,1,0,330001648", 330001648, true)]
    [TestCase("OpenItemBox", "30003,1,0,0", 30003, true)]
    [TestCase("OpenItemBox", "<v globalDropBoxId=\"0\" individualDropBoxId=\"330002090\" />", 330002090, true)]
    [TestCase("SelectItemBox", "1,330002718", 320000362, false)]
    [TestCase("SurvivalLevelExp", "<v SurvivalExp=\"100000\" />", 320000546, false)]
    public void Names_TheBoxThePackAlreadyOpens(string name, string parameter, int box, bool names) {
        Assert.That(ClientItemBoxes.Names(name, parameter, box), Is.EqualTo(names));
    }

    // A global box in OpenItemBox's global place, any other in its individual place; a required amount after them.
    [TestCase(30003, 0, true, "30003,1,0,0")]
    [TestCase(330001648, 0, false, "0,1,0,330001648")]
    [TestCase(330001700, 5, false, "0,1,0,330001700,5")]
    public void Parameter_PutsTheBoxInItsPlace(int box, int amount, bool global, string parameter) {
        Assert.That(ClientItemBoxes.Parameter(new ClientItemBoxes.Box(box, amount), global), Is.EqualTo(parameter));
    }
}
