namespace Maple2.Model.Metadata;

/// <summary>
/// The client features that are on for the ingested locale and server type (name -> feature.xml level), as the
/// client turns them on (FeatureLevels): the operator's feature.xml decides both the ingested rows and these. A rule
/// of the server that depends on a feature reads it here instead of fixing it in code.
/// </summary>
public record FeatureTable(IReadOnlyDictionary<string, int> Enabled) : ServerTable {
    /// <summary>The client's IsFeatureEnabledByName: a name the table lacks is off.</summary>
    public bool IsOn(string name) => Enabled.ContainsKey(name);
}
