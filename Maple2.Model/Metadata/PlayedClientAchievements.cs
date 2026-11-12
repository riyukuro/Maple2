using System.Xml;

namespace Maple2.Model.Metadata;

// The trophies the played client holds locked, read from that client's own achieve data (achieve/<id>.xml). The
// client reads the locking attribute of <achieves> (CAchieveData, commonfiles/achievedata.cpp:903) and shows a
// trophy with a non-zero value locked (UIAchieveDialog::IsAchieveLocked, uiachievedialog.cpp:1115). The data folder's
// pack can differ from the played client's here: it has trophies unlocked that the played client locks.
public static class PlayedClientAchievements {
    public const string AchieveFolder = "achieve/";

    public static bool IsAchieveFile(string name) => name.StartsWith(AchieveFolder, StringComparison.OrdinalIgnoreCase);

    // The ids whose <achieves> rows that pass the filter (feature and locale) are all locked. A trophy with one
    // unlocked row that passes stays countable.
    public static HashSet<int> LockedIds(IEnumerable<XmlDocument> achieveFiles, Func<XmlElement, bool> available) {
        return LockStates(achieveFiles, available).Where(state => state.Value).Select(state => state.Key).ToHashSet();
    }

    // Every trophy with a row that passes the filter, and whether the played client holds it locked. For a trophy
    // in here the played client's flag stands alone (Locked below); the data folder's flag is used only for a trophy
    // the played client has no row for.
    public static Dictionary<int, bool> LockStates(IEnumerable<XmlDocument> achieveFiles, Func<XmlElement, bool> available) {
        var locked = new HashSet<int>();
        var unlocked = new HashSet<int>();
        foreach (XmlDocument file in achieveFiles) {
            XmlNodeList? rows = file.DocumentElement?.SelectNodes("achieves");
            if (rows == null) {
                continue;
            }
            foreach (XmlNode row in rows) {
                if (row is not XmlElement element || !int.TryParse(element.GetAttribute("id"), out int id) || id == 0 || !available(element)) {
                    continue;
                }
                if (IsLocked(element.GetAttribute("locking"))) {
                    locked.Add(id);
                } else {
                    unlocked.Add(id);
                }
            }
        }
        var states = new Dictionary<int, bool>();
        foreach (int id in locked) {
            states[id] = true;
        }
        foreach (int id in unlocked) {
            states[id] = false;
        }
        return states;
    }

    // Whether a trophy is locked: the played client's flag when that client has the trophy, else the data folder's.
    public static bool Locked(int id, bool dataLocked, IReadOnlyDictionary<int, bool> playedStates) {
        return playedStates.TryGetValue(id, out bool playedLocked) ? playedLocked : dataLocked;
    }

    // The attribute is a number: empty or 0 is not locked.
    public static bool IsLocked(string locking) => int.TryParse(locking, out int value) && value != 0;
}
