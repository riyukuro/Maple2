using System.Linq;
using System.Xml;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using TriggerActions = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// wedding_move_user with the Guest type: the players in the box who are not the couple move. Made-up ids.
public class WeddingGuestMoveTests {
    [Test]
    public void Action_Parses() {
        var document = new XmlDocument();
        document.LoadXml("""<action name="wedding_move_user" entry_type="Guest" map_id="89900001" portal_ids="91,92" box_id="901" />""");
        XmlElement node = document.DocumentElement!;
        Assert.That(TriggerFunctionMapping.ActionMap[node.GetAttribute("name")](node.Attributes), Is.TypeOf<TriggerActions.WeddingMoveUser>());
    }

    [Test]
    public void Guests_AreThePlayersWhoAreNotTheCouple() {
        var ceremony = new WeddingCeremony(7001, 101, 102);
        long[] inBox = [101, 103, 102, 104];
        Assert.That(TriggerContext.WeddingGuests(inBox, ceremony, id => id).ToArray(), Is.EqualTo(new long[] { 103, 104 }));
        Assert.That(TriggerContext.WeddingGuests(new long[] { 101, 102 }, ceremony, id => id), Is.Empty);
    }

    [Test]
    public void GuestType_IsTheScriptsName() {
        Assert.That(TriggerContext.WeddingGuest, Is.EqualTo("Guest"));
    }
}
