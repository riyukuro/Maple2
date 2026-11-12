using System.Xml;
using Maple2.File.IO;
using M2dXmlGenerator;
using Maple2.File.Parser;
using Maple2.File.Parser.Enum;
using Maple2.File.Parser.Xml.Achieve;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using ConditionType = Maple2.Model.Enum.ConditionType;

namespace Maple2.File.Ingest.Mapper;

public class AchievementMapper : TypeMapper<AchievementMetadata> {
    private readonly AchieveParser parser;

    private readonly M2dReader? playedReader;

    public AchievementMapper(M2dReader xmlReader, M2dReader? playedReader = null) {
        parser = new AchieveParser(xmlReader);
        this.playedReader = playedReader;
    }

    private static bool RowAvailable(XmlElement node) =>
        FeatureLocaleFilter.FeatureEnabled(node.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(node.GetAttribute("locale"));

    protected override IEnumerable<AchievementMetadata> Map() {
        // The trophies the played client holds locked (PlayedClientAchievements); empty without a played client.
        HashSet<int> playedLocked = playedReader == null ? []
            : PlayedClientAchievements.LockedIds(
                playedReader.Files.Where(file => PlayedClientAchievements.IsAchieveFile(file.Name)).Select(file => playedReader.GetXmlDocument(file)),
                RowAvailable);
        if (playedLocked.Count > 0) {
            Console.WriteLine($"achievements: {playedLocked.Count} locked in the played client's achieve data");
        }
        foreach ((int id, string name, AchieveData data) in parser.Parse()) {
            var grades = new Dictionary<int, AchievementMetadataGrade>();
            foreach (Grade grade in data.grade) {
                grades.Add(grade.value, new AchievementMetadataGrade(
                    Grade: grade.value,
                    Condition: new ConditionMetadata(
                        Type: (ConditionType) grade.condition.type,
                        Value: grade.condition.value == 0 ? 1 : grade.condition.value,
                        Codes: grade.condition.code.ConvertCodes(),
                        Target: grade.condition.target.ConvertCodes()),
                    Reward: grade.reward is null || grade.reward.type == AchieveRewardType.unknown ? null : new AchievementMetadataReward(
                        Type: (AchievementRewardType) grade.reward.type,
                        Code: grade.reward.code,
                        Value: grade.reward.value,
                        Rank: grade.reward.rank)));
            }

            var category = AchievementCategory.Life;
            string[] tags = data.categoryTag;
            if (data.categoryTag.Length > 0) {
                // skip the first in the array and use it as the trophy category
                category = GetTrophyCategory(tags[0]);
                tags = tags.Skip(1).ToArray();
            }

            yield return new AchievementMetadata(
                Id: id,
                Name: name,
                AccountWide: data.account,
                Category: category,
                CategoryTags: tags,
                Grades: grades,
                Locked: data.locking || playedLocked.Contains(id));
        }
    }

    private static AchievementCategory GetTrophyCategory(string tag) {
        return tag switch {
            "combat" => AchievementCategory.Combat,
            "adventure" => AchievementCategory.Adventure,
            "living" => AchievementCategory.Life,
            _ => AchievementCategory.Life,
        };
    }
}
