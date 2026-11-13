namespace Maple2.Model.Validators;

// RecvOp Tutorial (no body) comes from the client's CGuideActionClearTutorial, which only guide_tutorial_100.xml
// event 270 runs: the "use the portal" step in start field 52000065. The event is isNoSave, so it can come again.
// job.xml names what the clear opens: tutorialClearOpenMaps (added to the visited maps, the list ServerEnter sends
// and FindFields 0x81 extends) and tutorialClearOpenTaxis (the known taxi stations). Chosen here: the clear only
// counts in the job's own tutorial start field (the client sends it from no other map).
public static class TutorialRules {
    // Adds the opened maps and taxis to the sets and returns the maps that were not there yet, in table order.
    public static List<int> Clear(int startField, int mapId, IEnumerable<int> openMaps, IEnumerable<int> openTaxis,
                                  ISet<int> maps, ISet<int> taxis) {
        var added = new List<int>();
        if (startField == 0 || mapId != startField) {
            return added;
        }
        foreach (int map in openMaps) {
            if (maps.Add(map)) {
                added.Add(map);
            }
        }
        taxis.UnionWith(openTaxis);
        return added;
    }
}
