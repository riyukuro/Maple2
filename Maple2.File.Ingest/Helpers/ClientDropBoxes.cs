using System.Xml;

namespace Maple2.File.Ingest.Helpers;

// The server pack's drop table holds some KR boxes with NA or CN rows only, so under LOCALE=KR the filter leaves them
// empty: opening one gives nothing, and a select box lists nothing (the client builds its dialog from its own copy of
// the table, table/individualitemdrop_final.xml, ItemDropTable). The played client's copy has KR rows for many of
// them. A box with no available row in the server's table is taken from the client's copy when the client has
// available rows for it; a box the server table has rows for keeps the server's rows.
//
// The client's rows have the item ids, counts, rank, enchant level, socket data and smart drop settings, but no
// weight, properJobWeight, imProperJobWeight, dropCount, dropCountProbability, grade, gradeProbability, isAnnounce,
// isBindCharacter, disableBreak, constraintsQuest, mapDependency, dropGroupMinLevel or serverDrop. Where the server
// has the same group with the same items (each item id matched in order), those attributes come from the server's
// rows. Otherwise the client group is kept as it is: without weights a plain group gives one of its items at an
// equal chance and a smart drop group gives the player's job item; no rate is made up.
public static class ClientDropBoxes {
    public sealed record Counts(int Replaced, int Appended, int ServerGroups, int ClientGroups);

    private static readonly HashSet<string> NotCopied = ["locale", "feature"];

    public static Counts Add(XmlDocument server, XmlDocument client, Func<XmlElement, bool> available) {
        XmlElement root = server.DocumentElement!;
        var serverBoxes = new Dictionary<string, XmlElement>();
        foreach (XmlElement box in Children(root, "dropBox")) {
            serverBoxes.TryAdd(box.GetAttribute("dropBoxID"), box);
        }

        int replaced = 0, appended = 0, serverGroups = 0, clientGroups = 0;
        foreach (XmlElement clientBox in Children(client.DocumentElement!, "dropBox")) {
            if (!HasRows(clientBox, available)) {
                continue;
            }
            serverBoxes.TryGetValue(clientBox.GetAttribute("dropBoxID"), out XmlElement? serverBox);
            if (serverBox != null && HasRows(serverBox, available)) {
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
        return new Counts(replaced, appended, serverGroups, clientGroups);
    }

    private static bool HasRows(XmlElement box, Func<XmlElement, bool> available) {
        return available(box) && Children(box, "group").Any(group => available(group) && Children(group, "v").Any(available));
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
