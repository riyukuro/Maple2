using System.Globalization;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.File.Ingest.Helpers;

// The value1 of the DungeonBonusReward, CollectItemGroup and ArcadeOpen rows and the values of the TreeWatering rows,
// each an <ms2> element, and the value2 of the MiniGameBonusReward rows, an <items> element. Text that is not XML is
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

    // <ms2><info arcadeId rewardCountPerDay/><reward itemID count grade/>...</ms2> (GameEventArcadeOpen::vfunc_2,
    // commonfiles/gameevent.cpp:6657): the client reads nothing without the info element. A row without an arcade
    // id opens nothing and is not stored; a reward row without an item id or a count is skipped.
    public static ArcadeOpen? ArcadeOpen(string? value1) {
        XmlElement? root = Root(value1);
        if (root?["info"] is not { } info
            || !int.TryParse(info.GetAttribute("arcadeId"), out int arcadeId) || arcadeId <= 0) {
            return null;
        }
        int.TryParse(info.GetAttribute("rewardCountPerDay"), out int countPerDay);
        RewardItem[] rewards = root.ChildNodes.OfType<XmlElement>()
            .Where(element => element.Name == "reward")
            .Select(Item)
            .OfType<RewardItem>()
            .ToArray();
        return new ArcadeOpen(arcadeId, Math.Max(countPerDay, 0), rewards);
    }

    // The three values of a TreeWatering row (GameEventTreeWatering::ReadTreeWateringData,
    // commonfiles/gameevent.cpp:5664). The client keeps no event when one of them is not an <ms2> document; a
    // missing element or attribute leaves its fields 0 or empty. A step or an emotion day whose time is not
    // "yyyy-MM-dd-HH-mm" (or to the second) is left out: it could never be the current one. The client keeps one step
    // and one resource per step number (CAtlMap::SetAt), the last.
    public static TreeWatering? TreeWatering(string? value1, string? value2, string? value3) {
        XmlElement? times = Root(value1);
        XmlElement? rewards = Root(value2);
        XmlElement? resources = Root(value3);
        if (times == null || rewards == null || resources == null) {
            return null;
        }

        var steps = new Dictionary<int, TreeWatering.Step>();
        foreach (XmlElement element in Children(times["tree_step"], "step")) {
            int.TryParse(element.GetAttribute("step"), out int step);
            DateTime begin = AttendGiftReplay.ParseTime(element.GetAttribute("begin"));
            DateTime end = AttendGiftReplay.ParseTime(element.GetAttribute("end"));
            if (begin != DateTime.MinValue && end != DateTime.MinValue) {
                steps[step] = new TreeWatering.Step(step, begin, end);
            }
        }
        var days = new List<TreeWatering.Period>();
        foreach (XmlElement element in Children(times["emotion_day"], "time")) {
            DateTime begin = AttendGiftReplay.ParseTime(element.GetAttribute("begin"));
            DateTime end = AttendGiftReplay.ParseTime(element.GetAttribute("end"));
            if (begin != DateTime.MinValue && end != DateTime.MinValue) {
                days.Add(new TreeWatering.Period(begin, end));
            }
        }
        (int waterLimit, RewardItem waterReward) = LimitedReward(rewards["watering_reward"]);
        (int emotionLimit, RewardItem emotionReward) = LimitedReward(rewards["emotion_reward"]);

        XmlElement? resource = resources["resource"];
        var nifs = new Dictionary<int, TreeWatering.Resource>();
        foreach (XmlElement element in Children(resources["tree_resource"], "step")) {
            int.TryParse(element.GetAttribute("step"), out int step);
            nifs[step] = new TreeWatering.Resource(step, element.GetAttribute("nif_id"));
        }
        int.TryParse(resource?.GetAttribute("FieldID"), out int fieldId);
        int.TryParse(resource?.GetAttribute("WaterPotItemID"), out int potId);
        return new TreeWatering(
            Steps: steps.Values.ToArray(),
            EmotionDays: days.ToArray(),
            WaterLimit: waterLimit,
            WaterReward: waterReward,
            EmotionLimit: emotionLimit,
            EmotionReward: emotionReward,
            Resources: nifs.Values.ToArray(),
            FieldId: fieldId,
            TreeName: resource?.GetAttribute("TreeNameTag") ?? string.Empty,
            WaterPotItemId: potId,
            WaterAnimation: resource?.GetAttribute("WateringAnimation") ?? string.Empty,
            EmotionAnimation: resource?.GetAttribute("EmotionAnimation") ?? string.Empty,
            EmotionDayEffect: resource?.GetAttribute("EmotionDayEffect") ?? string.Empty);
    }

    // value1 a comma list of mini game names, value2 <items><v itemID grade count/>...</items>. A row without a name
    // or without an item gives nothing and is not stored; an item without an id or a count is skipped.
    public static MiniGameBonusReward? MiniGameBonusReward(string? value1, string? value2) {
        string[] names = (value1 ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToArray();
        XmlElement? items = Root(value2, "items");
        if (names.Length == 0 || items == null) {
            return null;
        }
        RewardItem[] rewards = Children(items, "v").Select(Item).OfType<RewardItem>().ToArray();
        return rewards.Length == 0 ? null : new MiniGameBonusReward(names, rewards);
    }

    private static IEnumerable<XmlElement> Children(XmlElement? parent, string name) {
        return parent?.ChildNodes.OfType<XmlElement>().Where(element => element.Name == name) ?? [];
    }

    // MaxRewardCountDaily, then the item: id, grade and count as the client reads them (0 when missing).
    private static (int Limit, RewardItem Item) LimitedReward(XmlElement? element) {
        if (element == null) {
            return (0, default);
        }
        int.TryParse(element.GetAttribute("MaxRewardCountDaily"), out int limit);
        int.TryParse(element.GetAttribute("itemID"), out int itemId);
        short.TryParse(element.GetAttribute("grade"), out short grade);
        int.TryParse(element.GetAttribute("count"), out int count);
        return (Math.Max(limit, 0), new RewardItem(Math.Max(itemId, 0), Math.Max(grade, (short) 0), Math.Max(count, 0)));
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

    private static XmlElement? Root(string? text, string name = "ms2") {
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }
        try {
            var document = new XmlDocument();
            document.LoadXml(text);
            return document.DocumentElement is { } root && root.Name == name ? root : null;
        } catch (XmlException) {
            return null;
        }
    }
}
