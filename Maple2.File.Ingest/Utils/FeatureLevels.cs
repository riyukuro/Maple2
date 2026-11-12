using System.Xml;
using M2dXmlGenerator;
using Maple2.File.IO;

namespace Maple2.File.Ingest.Utils;

/// <summary>
/// The client's rule for which features are on (CFeatureManager, commonfiles/featuremanager.cpp): a feature is on
/// when its feature.xml level is not above the feature_setting.xml level of the locale, and a feature whose level
/// is 0 (no column for the locale) is skipped, so it is unknown and off. The parser's filter (Filter.Load) keeps
/// the features whose level is not above the setting, so it keeps a level-0 feature on (0 is not above 0).
/// </summary>
public static class FeatureLevels {
    /// <summary>The option naming the feature_setting.xml row (server type) the features are read at.</summary>
    public const string EnvOption = "MS2_FEATURE_ENV";
    /// <summary>The server types of feature_setting.xml; Live is the default, as for the client on the Nexon platform.</summary>
    public static readonly string[] Envs = { "Dev", "Qa", "DevStage", "Stage", "Live" };
    public const string DefaultEnv = "Live";

    public const string FeatureTable = "table/feature.xml";
    public const string FeatureSettingTable = "table/feature_setting.xml";

    /// <summary>The parser's enabled features without the ones the client skips.</summary>
    public static Dictionary<string, int> ClientEnabled(IReadOnlyDictionary<string, int> parserEnabled) =>
        parserEnabled.Where(entry => entry.Value > 0).ToDictionary(entry => entry.Key, entry => entry.Value);

    /// <summary>Applies the client's rule to the parser's filter; run after Filter.Load.</summary>
    public static void Apply() {
        FeatureLocaleFilter.Features = ClientEnabled(FeatureLocaleFilter.Features);
    }

    /// <summary>The server type of MS2_FEATURE_ENV (unset or empty = Live), in the case feature_setting.xml has.</summary>
    public static bool TryReadEnv(string? value, out string env) {
        if (string.IsNullOrWhiteSpace(value)) {
            env = DefaultEnv;
            return true;
        }
        env = Envs.FirstOrDefault(name => string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return env.Length > 0;
    }

    /// <summary>
    /// The features a client with this pack turns on, as CFeatureManager::Init reads them. LoadFeatureSetting
    /// (featuremanager.cpp:757) takes the level from the &lt;setting&gt; whose type is the server type (compared
    /// without case), at the attribute of the client's locale, and stays at 0 without one. LoadFeature (:787) skips a
    /// feature with no level for the locale and keeps the rest; IsFeatureEnabledByName turns on the ones not above
    /// the setting level and turns off a name the table lacks.
    /// </summary>
    /// <param name="level">The setting level; 0 when the table has no row for the server type or no column for the locale.</param>
    public static Dictionary<string, int> ClientEnabled(XmlDocument featureSetting, XmlDocument feature, string locale, string env,
            out int level) {
        level = 0;
        foreach (XmlElement setting in Children(featureSetting, "setting")) {
            if (string.Equals(setting.GetAttribute("type"), env, StringComparison.OrdinalIgnoreCase)) {
                level = Level(setting, locale);
                break;
            }
        }
        var enabled = new Dictionary<string, int>();
        foreach (XmlElement element in Children(feature, "feature")) {
            int featureLevel = Level(element, locale);
            if (featureLevel != 0 && featureLevel <= level) {
                enabled[element.GetAttribute("name")] = featureLevel;
            }
        }
        return enabled;
    }

    /// <summary>
    /// The played client's features (ClientEnabled on its feature tables), or null when its pack has no feature table.
    /// </summary>
    public static Dictionary<string, int>? PlayedEnabled(M2dReader playedReader, string locale, string env, out int level) {
        level = 0;
        XmlDocument? featureSetting = Table(playedReader, FeatureSettingTable);
        XmlDocument? feature = Table(playedReader, FeatureTable);
        if (featureSetting == null || feature == null) {
            return null;
        }
        return ClientEnabled(featureSetting, feature, locale, env, out level);
    }

    private static XmlDocument? Table(M2dReader reader, string name) {
        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = reader.Files.FirstOrDefault(file =>
            string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase));
        return entry == null ? null : reader.GetXmlDocument(entry);
    }

    private static IEnumerable<XmlElement> Children(XmlDocument document, string name) =>
        document.DocumentElement?.ChildNodes.OfType<XmlElement>().Where(element => element.Name == name) ?? [];

    // XmlGetAttr into an int: a missing or unreadable attribute leaves 0.
    private static int Level(XmlElement element, string locale) =>
        int.TryParse(element.GetAttribute(locale), out int value) ? value : 0;
}
