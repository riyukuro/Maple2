using System.Globalization;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.File.Ingest.Helpers;

// The value1 of the DungeonBonusReward and CollectItemGroup rows, both an <ms2> element. Text that is not XML is
// a row that cannot be read; it does not stop the ingest.
public static class EventXmlReader {
    // <ms2><v dungeonID groupType mesoRate expRate itemRolls customDropBoxIds/></ms2>: the client reads the root's
    // first element whatever its name (GameEventDungeonBonusReward::vfunc_2, commonfiles/gameevent.cpp:996, and
    // DungeonBonusRewardCtx::Load, gameeventcontext.cpp:751). groupType is the group's name, customDropBoxIds a
    // comma list. A row that names neither a room nor a group, or that adds nothing, is not stored.
    public static DungeonBonusReward? DungeonBonus(string? value1) {
        XmlElement? root = Root(value1);
        XmlElement? element = root?.ChildNodes.OfType<XmlElement>().FirstOrDefault();
        if (element == null) {
            return null;
        }

        int.TryParse(element.GetAttribute("dungeonID"), out int dungeonId);
        // A name only: Enum.TryParse would also take a number.
        string groupName = element.GetAttribute("groupType").Trim();
        DungeonGroupType groupType = DungeonGroupType.none;
        foreach (DungeonGroupType type in Enum.GetValues<DungeonGroupType>()) {
            if (string.Equals(type.ToString(), groupName, StringComparison.OrdinalIgnoreCase)) {
                groupType = type;
            }
        }
        int.TryParse(element.GetAttribute("itemRolls"), out int itemRolls);
        int[] boxIds = element.GetAttribute("customDropBoxIds")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => int.TryParse(text, out int boxId) ? boxId : 0)
            .Where(boxId => boxId > 0)
            .ToArray();

        var bonus = new DungeonBonusReward(
            DungeonId: Math.Max(dungeonId, 0),
            GroupType: groupType,
            MesoRate: Rate(element.GetAttribute("mesoRate")),
            ExpRate: Rate(element.GetAttribute("expRate")),
            ItemRolls: Math.Max(itemRolls, 0),
            DropBoxIds: boxIds);
        if (bonus.DungeonId == 0 && bonus.GroupType == DungeonGroupType.none) {
            return null;
        }
        if (bonus is { MesoRate: 0f, ExpRate: 0f, ItemRolls: 0, DropBoxIds.Length: 0 }) {
            return null;
        }
        return bonus;
    }

    // <ms2><info name/><result itemID count grade/><condition itemID count grade/>...</ms2>
    // (GameEventCollectItemGroup::vfunc_2, commonfiles/gameevent.cpp:6474; an item is ItemEntity::vfunc_2,
    // commonstruct_wa.cpp:60). The client reads nothing without the info or the result element, and its window
    // shows an event with 2 to 5 conditions only (UICollectItemGroupEventDialog::Refresh); other rows, and rows
    // with an item without an id or a count, are not stored.
    public static CollectItemGroup? CollectItemGroup(string? value1) {
        XmlElement? root = Root(value1);
        if (root?["info"] is not { } info || root["result"] is not { } resultElement) {
            return null;
        }
        if (Item(resultElement) is not { } result) {
            return null;
        }

        var conditions = new List<RewardItem>();
        foreach (XmlElement element in root.ChildNodes.OfType<XmlElement>().Where(element => element.Name == "condition")) {
            if (Item(element) is not { } condition) {
                return null;
            }
            conditions.Add(condition);
        }
        if (conditions.Count is < EventSaleRules.MinCollectConditions or > EventSaleRules.MaxCollectConditions) {
            return null;
        }
        return new CollectItemGroup(info.GetAttribute("name"), conditions.ToArray(), result);
    }

    private static RewardItem? Item(XmlElement element) {
        if (!int.TryParse(element.GetAttribute("itemID"), out int itemId) || itemId <= 0
            || !int.TryParse(element.GetAttribute("count"), out int count) || count <= 0) {
            return null;
        }
        // An item without a grade keeps the client's default rank 1 (ItemEntity, commonstruct_wa.cpp:41).
        if (!short.TryParse(element.GetAttribute("grade"), out short grade)) {
            grade = 1;
        }
        return new RewardItem(itemId, Math.Max(grade, (short) 0), count);
    }

    private static float Rate(string text) {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float rate) && float.IsFinite(rate) && rate > 0f ? rate : 0f;
    }

    private static XmlElement? Root(string? text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }
        try {
            var document = new XmlDocument();
            document.LoadXml(text);
            return document.DocumentElement is { Name: "ms2" } root ? root : null;
        } catch (XmlException) {
            return null;
        }
    }
}
