using System.Xml;

namespace Maple2.File.Ingest.Helpers;

// The server pack's drop table holds some KR boxes with NA or CN rows only, so under LOCALE=KR the filter leaves them
// empty: opening one gives nothing, and a select box lists nothing (the client builds its dialog from its own copy of
// the table, table/individualitemdrop_final.xml, ItemDropTable). The played client's copy has KR rows for many of
// them. A box with no available row in the server's table is taken from the client's copy when the client has
// available rows for it. Some boxes keep KR rows for part of their groups only (the NA refill left one KR group of the
// adventure bonus box 330001968, Colorful Crystal Box x3, where the client lists 8 KR groups, and the tooltip shows
// them all). For a box an item use opens (itemBox, ItemBoxDropIds), the client's groups that have no available
// server group with the same rows (item ids and counts; the group ids differ between the two tables) or with any of
// their items (another version of the group: the server's stays) are added to it
// (Short): in place of the server's unavailable groups of that id, or under a free id when a kept server group has
// it. The server's available groups stay as they are, with their rates.
// A monster or other field box keeps only its server rows: the client's rows carry no drop chances, so its groups
// would drop on every kill.
//
// The client's rows have the item ids, counts, rank, enchant level, socket data and smart drop settings, but no
// weight, properJobWeight, imProperJobWeight, dropCount, dropCountProbability, grade, gradeProbability, isAnnounce,
// isBindCharacter, disableBreak, constraintsQuest, mapDependency, dropGroupMinLevel or serverDrop. Where the server
// has the same group with the same items (each item id matched in order), those attributes come from the server's
// rows. Otherwise the client group is kept as it is: without weights a plain group gives one of its items at an
// equal chance and a smart drop group gives the player's job item; no rate is made up.
public static class ClientDropBoxes {
    // Replaced: server boxes with no available row taken from the client's copy. Short: item boxes the client's groups
    // were added to, GroupsAdded of them; GroupsMatched client groups of those boxes had a server group with their rows,
    // GroupsOverlapping one with some of their items.
    public sealed record Counts(int Replaced, int Appended, int ServerGroups, int ClientGroups, int Short = 0, int GroupsAdded = 0,
        int GroupsMatched = 0, int GroupsOverlapping = 0);

    private static readonly HashSet<string> NotCopied = ["locale", "feature"];

    public static Counts Add(XmlDocument server, XmlDocument client, Func<XmlElement, bool> available, Func<string, bool>? itemBox = null) {
        XmlElement root = server.DocumentElement!;
        var serverBoxes = new Dictionary<string, XmlElement>();
        foreach (XmlElement box in Children(root, "dropBox")) {
            serverBoxes.TryAdd(box.GetAttribute("dropBoxID"), box);
        }

        int replaced = 0, appended = 0, serverGroups = 0, clientGroups = 0, shortBoxes = 0, groupsAdded = 0, groupsMatched = 0, groupsOverlapping = 0;
        foreach (XmlElement clientBox in Children(client.DocumentElement!, "dropBox")) {
            int clientCount = GroupsWithRows(clientBox, available);
            if (clientCount == 0) {
                continue;
            }
            string boxId = clientBox.GetAttribute("dropBoxID");
            serverBoxes.TryGetValue(boxId, out XmlElement? serverBox);
            int serverCount = serverBox == null ? 0 : GroupsWithRows(serverBox, available);
            if (serverBox != null && serverCount > 0) {
                if (serverCount >= clientCount || itemBox?.Invoke(boxId) != true) {
                    continue;
                }
                // The server's group ids need not be the client's (the server's KR group 3 of 330001968 is the
                // client's group 2), so a client group is matched to a kept server group by its rows.
                List<XmlElement> kept = Children(serverBox, "group").Where(group => HasRows(group, available)).ToList();
                var unmatched = new List<XmlElement>(kept);
                var keptIds = kept.Select(group => group.GetAttribute("dropGroupID")).ToHashSet();
                var keptItems = kept.SelectMany(group => Rows(group, available)).Select(v => v.GetAttribute("itemID")).ToHashSet();
                int nextId = Children(serverBox, "group").Concat(Children(clientBox, "group"))
                    .Select(group => int.TryParse(group.GetAttribute("dropGroupID"), out int id) ? id : 0).DefaultIfEmpty(0).Max() + 1;
                int added = 0;
                foreach (XmlElement clientGroup in Children(clientBox, "group")) {
                    if (!HasRows(clientGroup, available)) {
                        continue;
                    }
                    XmlElement? match = unmatched.FirstOrDefault(group => SameRows(group, clientGroup, available));
                    if (match != null) {
                        unmatched.Remove(match);
                        groupsMatched++;
                        continue;
                    }
                    // A kept server group with some of its items is the same group in another version (the server's
                    // 4 of the client's 8 rows of 330001417 group 2): the server's, with its weights, stays alone, so
                    // the box gives no second pick of those items.
                    if (Rows(clientGroup, available).Any(v => keptItems.Contains(v.GetAttribute("itemID")))) {
                        groupsOverlapping++;
                        continue;
                    }
                    var group = (XmlElement) server.ImportNode(clientGroup, true);
                    string groupId = group.GetAttribute("dropGroupID");
                    if (keptIds.Contains(groupId)) {
                        // The id is a kept server group's with other rows: a free id (groups are keyed by id).
                        group.SetAttribute("dropGroupID", (nextId++).ToString());
                        serverBox.AppendChild(group);
                        clientGroups++;
                    } else {
                        if (TakeServerValues(serverBox, group, available)) {
                            serverGroups++;
                        } else {
                            clientGroups++;
                        }
                        // The server's groups of this id have no available row (the NA refill's): the client's group
                        // takes their place.
                        List<XmlElement> sameId = Children(serverBox, "group").Where(g => g.GetAttribute("dropGroupID") == groupId).ToList();
                        if (sameId.Count > 0) {
                            serverBox.ReplaceChild(group, sameId[0]);
                            foreach (XmlElement other in sameId.Skip(1)) {
                                serverBox.RemoveChild(other);
                            }
                        } else {
                            serverBox.AppendChild(group);
                        }
                    }
                    added++;
                }
                if (added > 0) {
                    shortBoxes++;
                    groupsAdded += added;
                }
                continue;
            }

            var box = (XmlElement) server.ImportNode(clientBox, true);
            if (serverBox != null) {
                CopyMissing(serverBox, box);
                foreach (XmlElement group in Children(box, "group")) {
                    if (!available(group)) {
                        continue;
                    }
                    if (TakeServerValues(serverBox, group, available)) {
                        serverGroups++;
                    } else {
                        clientGroups++;
                    }
                }
                root.ReplaceChild(box, serverBox);
                replaced++;
            } else {
                clientGroups += Children(box, "group").Count(available);
                root.AppendChild(box);
                appended++;
            }
        }
        return new Counts(replaced, appended, serverGroups, clientGroups, shortBoxes, groupsAdded, groupsMatched, groupsOverlapping);
    }

    // The box's available groups that have an available row (0 when the box itself is not available).
    private static int GroupsWithRows(XmlElement box, Func<XmlElement, bool> available) {
        return available(box) ? Children(box, "group").Count(group => HasRows(group, available)) : 0;
    }

    // The same available rows: item ids, and counts where both rows give one (the client often leaves maxCount out).
    private static bool SameRows(XmlElement serverGroup, XmlElement clientGroup, Func<XmlElement, bool> available) {
        List<XmlElement> serverRows = Rows(serverGroup, available), clientRows = Rows(clientGroup, available);
        return serverRows.Count == clientRows.Count && serverRows.Zip(clientRows).All(pair =>
            pair.First.GetAttribute("itemID") == pair.Second.GetAttribute("itemID")
            && SameCount(pair.First, pair.Second, "minCount") && SameCount(pair.First, pair.Second, "maxCount"));
    }

    private static List<XmlElement> Rows(XmlElement group, Func<XmlElement, bool> available) {
        return Children(group, "v").Where(available).OrderBy(v => v.GetAttribute("itemID"), StringComparer.Ordinal)
            .ThenBy(v => v.GetAttribute("minCount"), StringComparer.Ordinal).ToList();
    }

    private static bool SameCount(XmlElement a, XmlElement b, string name) {
        return !a.HasAttribute(name) || !b.HasAttribute(name) || a.GetAttribute(name) == b.GetAttribute(name);
    }

    private static bool HasRows(XmlElement group, Func<XmlElement, bool> available) {
        return available(group) && Children(group, "v").Any(available);
    }

    // The server group of the same id, when each of the client group's available rows has a server row of the same
    // item id (the k-th row of an id with the k-th server row of that id): their attributes the client lacks.
    private static bool TakeServerValues(XmlElement serverBox, XmlElement group, Func<XmlElement, bool> available) {
        string groupId = group.GetAttribute("dropGroupID");
        List<XmlElement> serverGroups = Children(serverBox, "group").Where(g => g.GetAttribute("dropGroupID") == groupId).ToList();
        if (serverGroups.Count == 0) {
            return false;
        }
        var serverRows = serverGroups.SelectMany(g => Children(g, "v")).ToLookup(v => v.GetAttribute("itemID"));
        var seen = new Dictionary<string, int>();
        var pairs = new List<(XmlElement Client, XmlElement Server)>();
        foreach (XmlElement row in Children(group, "v").Where(available)) {
            string itemId = row.GetAttribute("itemID");
            int k = seen.GetValueOrDefault(itemId);
            seen[itemId] = k + 1;
            XmlElement? match = serverRows[itemId].ElementAtOrDefault(k);
            if (match == null) {
                return false;
            }
            pairs.Add((row, match));
        }
        CopyMissing(serverGroups[0], group);
        foreach ((XmlElement row, XmlElement match) in pairs) {
            CopyMissing(match, row);
        }
        return true;
    }

    private static void CopyMissing(XmlElement from, XmlElement to) {
        foreach (XmlAttribute attribute in from.Attributes) {
            if (!NotCopied.Contains(attribute.Name) && !to.HasAttribute(attribute.Name)) {
                to.SetAttribute(attribute.Name, attribute.Value);
            }
        }
    }

    private static IEnumerable<XmlElement> Children(XmlElement parent, string name) {
        return parent.ChildNodes.OfType<XmlElement>().Where(child => child.Name == name).ToList();
    }
}
