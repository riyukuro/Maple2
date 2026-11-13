using Maple2.Model;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class TutorialItemHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestTutorialItem;

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Player.Value.Character.Level > 1) {
            return;
        }

        var jobTable = session.TableMetadata.JobTable;

        var jobCode = session.Player.Value.Character.Job.Code();

        if (!jobTable.Entries.TryGetValue(jobCode, out var jobMetadata)) {
            return;
        }

        if (session.Field == null || jobMetadata.Tutorial.StartField != session.Field.MapId) {
            return;
        }

        // One inbox fetch for the request, not one per start item.
        List<IGrouping<int, JobTable.Item>> groups = jobMetadata.Tutorial.StartItem.GroupBy(item => item.Id).ToList();
        Dictionary<int, int> mailed = session.Mail.HeldAmounts(groups.Select(group => group.Key));
        foreach (IGrouping<int, JobTable.Item> tutorialItemGroup in groups) {
            // Amounts, not stacks: a stack of 10 counted as 1 and was given again on every request. What waits on a
            // mail counts too, so a start item mailed for a full bag is not mailed again on the next request.
            int owned = session.Item.Inventory.Find(tutorialItemGroup.Key).Sum(item => item.Amount)
                        + session.Item.Equips.Gear.Count(x => x.Value.Id == tutorialItemGroup.Key)
                        + mailed[tutorialItemGroup.Key];
            int missing = TutorialItemRules.Missing(owned, tutorialItemGroup.Select(item => item.Count));
            if (missing <= 0) {
                continue;
            }

            // One item with the missing amount: the same item object used to be added once per missing piece.
            Item? tutorialItem = session.Field?.ItemDrop.CreateItem(tutorialItemGroup.Key, amount: missing);
            if (tutorialItem == null) {
                continue;
            }

            // A full bag sends the item by mail (chosen here; the request, CGuideActionRequestTutorialItem::vfunc_3,
            // guideaction.cpp:1340, has no body and the client waits for no reply). The bag's refusal used to drop it,
            // and the guide asks only once.
            session.Item.AddOrMail(tutorialItem, true);
        }
    }
}
