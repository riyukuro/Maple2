namespace Maple2.Model.Enum;

public enum SkillPointSource {
    Trophy = 1,
    Chapter = 2,
    Unknown = 3,
    // Exploration rounds (fieldMission.xml skillPoint); the client sums every source into the points it shows.
    Exploration = 4,
}
