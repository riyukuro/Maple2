using M2dXmlGenerator;

namespace Maple2.File.Ingest.Utils;

/// <summary>
/// The client's rule for which features are on (CFeatureManager, commonfiles/featuremanager.cpp): a feature is on
/// when its feature.xml level is not above the feature_setting.xml level of the locale, and a feature whose level
/// is 0 (no column for the locale) is skipped, so it is unknown and off. The parser's filter (Filter.Load) keeps
/// the features whose level is not above the setting, so it keeps a level-0 feature on (0 is not above 0).
/// </summary>
public static class FeatureLevels {
    /// <summary>The parser's enabled features without the ones the client skips.</summary>
    public static Dictionary<string, int> ClientEnabled(IReadOnlyDictionary<string, int> parserEnabled) =>
        parserEnabled.Where(entry => entry.Value > 0).ToDictionary(entry => entry.Key, entry => entry.Value);

    /// <summary>Applies the client's rule to the parser's filter; run after Filter.Load.</summary>
    public static void Apply() {
        FeatureLocaleFilter.Features = ClientEnabled(FeatureLocaleFilter.Features);
    }
}
