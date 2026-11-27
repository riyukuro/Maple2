using System.Text.RegularExpressions;
using System.Xml;
using Maple2.Tools;

namespace Maple2.Server.Game.Util;

// GM test triggers: a trigger script from the TestTriggers folder run on the current field by "trigger load <name>", for
// trigger actions the story reaches rarely or late (set_gravity first). The script runs on the field's own assets (its
// boxes, meshes and spawns), as a trigger named gmtest_<name> that no shipped map uses.
public static partial class TestTriggers {
    public static readonly string Dir = Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", "TestTriggers");
    public const string Prefix = "gmtest_";

    // Lower-case letters, digits and '_' only, so the name cannot leave the folder.
    [GeneratedRegex("^[a-z0-9_]{1,40}$")]
    private static partial Regex NameRegex();

    public static bool IsValidName(string? name) => name != null && NameRegex().IsMatch(name);

    // The script <dir>/<name>.xml parsed for the field xBlock, or null with the reason.
    public static Trigger.Helpers.Trigger? Load(string dir, string name, string xBlock, out string error) {
        if (!IsValidName(name)) {
            error = $"Invalid test trigger name '{name}': use a-z, 0-9 and '_'.";
            return null;
        }

        string path = Path.Combine(dir, name + ".xml");
        if (!File.Exists(path)) {
            error = $"No test trigger '{name}'.";
            return null;
        }

        try {
            var document = new XmlDocument();
            document.LoadXml(File.ReadAllText(path));
            Trigger.Helpers.Trigger trigger = TriggerCache.ParseTrigger(xBlock, Prefix + name, document);
            if (trigger.States.Count == 0) {
                error = $"Test trigger '{name}' has no states.";
                return null;
            }

            error = "";
            return trigger;
        } catch (Exception ex) when (ex is XmlException or ArgumentException) {
            error = $"Test trigger '{name}' does not parse: {ex.Message}";
            return null;
        }
    }
}
