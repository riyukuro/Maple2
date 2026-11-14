using System.Xml;
using System.Xml.Serialization;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.File.Parser.Tools;
using Maple2.File.Parser.Xml.String;
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
        // The played client's trophies and whether it holds them locked (PlayedClientAchievements); empty without a
        // played client.
        Dictionary<int, bool> playedStates = playedReader == null ? []
            : PlayedClientAchievements.LockStates(
                playedReader.Files.Where(file => PlayedClientAchievements.IsAchieveFile(file.Name)).Select(file => playedReader.GetXmlDocument(file)),
                RowAvailable);
        M2dReader? played = playedReader;
        int playedOnly = 0;
        var unread = new List<int>();
        IEnumerable<(int Id, string Name, AchieveData Data)> trophies = PlayedOnlyAdded(parser.Parse(),
            played == null ? null : () => PlayedTrophies(played, unread), trophy => trophy.Id, playedStates, () => playedOnly++);
        foreach ((int id, string name, AchieveData data) in trophies) {
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
                        Rank: grade.reward.rank,
                        SubJobLevel: grade.reward.subJobLevel)));
            }

            string[] tags = data.categoryTag;
            if (data.categoryTag.Length > 0) {
                // the first tag is the category the trophy window files it under; the tally goes by the id's digit
                tags = tags.Skip(1).ToArray();
            }
            // The client takes the type and category from the id (CAchieveData load, achievedata.cpp:910-911).
            int type = id / 10000000;
            var category = ((id / 1000000) % 10) switch {
                1 => AchievementCategory.Combat,
                2 => AchievementCategory.Adventure,
                3 => AchievementCategory.Life,
                _ => AchievementCategory.None,
            };

            yield return new AchievementMetadata(
                Id: id,
                Name: name,
                AccountWide: data.account,
                Category: category,
                CategoryTags: tags,
                Grades: grades,
                Locked: PlayedClientAchievements.Locked(id, data.locking, playedStates),
                Type: type);
        }
        if (playedStates.Count > 0) {
            Console.WriteLine($"achievements: {playedStates.Count(state => state.Value)} locked of {playedStates.Count} in the played client's achieve data, {playedOnly} of them only in that client's pack"
                              + (unread.Count == 0 ? "" : $"; played files the parser cannot read: {string.Join(",", unread)}"));
        }
    }

    // The data pack's trophies, then the played client's trophies that the pack yields no row for, from the played
    // client's own achieve files: the client lists every trophy of its achieve data in the trophy window. A played
    // trophy is taken when its id is in playedStates (LockStates: the client has an available row for it); the pack's
    // trophy wins where the pack yields one. The pack yields none for an id it lacks and for one whose rows the
    // feature/locale filter drops all of (KR Live 92000139: the pack's only row is locale NA, the client's is KR). KR
    // Live: 21 trophies added (21300103-112 among them), 26 with DungeonOS04 on (its 21300113-117). No played client
    // (empty playedStates or no reader): the pack's trophies alone.
    public static IEnumerable<T> PlayedOnlyAdded<T>(IEnumerable<T> pack, Func<IEnumerable<T>>? played, Func<T, int> id,
            IReadOnlyDictionary<int, bool> playedStates, Action? playedOnly) =>
        QuestMapper.PackThenPlayed(pack, playedStates.Count == 0 ? null : played, id, playedStates.Keys.ToHashSet(), playedOnly);

    // The played pack's trophies, read the way AchieveParser.Parse reads a pack (parser 2.3.7), one file at a time: a
    // file the parser cannot read (KR Live: achieve/22200019.xml, condition item_collect_script_function) is skipped and
    // its id put in unread, where the parser's own Parse would stop at it.
    private static IEnumerable<(int Id, string Name, AchieveData Data)> PlayedTrophies(M2dReader played, List<int> unread) {
        var achieveSerializer = new XmlSerializer(typeof(AchievesData));
        Dictionary<int, string> names = [];
        // A client pack keeps its names at string/en/ (the data pack at en/). KR Live's file has one of these trophies
        // (92000139), in a locale="NA" row the KR filter drops, so the added trophies come without a name. The server
        // reads a trophy's name only for map chest trophies (MapChestRules, interact_object), which none of these is.
        PackFileEntry? nameEntry = played.Files.FirstOrDefault(file => file.Name == "string/en/achievename.xml")
                                   ?? played.Files.FirstOrDefault(file => file.Name == "en/achievename.xml");
        if (nameEntry != null && new XmlSerializer(typeof(StringMapping)).Deserialize(played.GetXmlReader(nameEntry)) is StringMapping mapping) {
            names = mapping.key.ToDictionary(key => int.Parse(key.id), key => key.name);
        }
        foreach (PackFileEntry entry in played.Files.Where(file => file.Name.StartsWith("achieve/"))) {
            int id = int.Parse(Path.GetFileNameWithoutExtension(entry.Name));
            AchieveData? data;
            try {
                using var reader = XmlReader.Create(new StringReader(Sanitizer.RemoveEmpty(played.GetString(entry))));
                data = (achieveSerializer.Deserialize(reader) as AchievesData)?.achieves;
            } catch (InvalidOperationException) {
                unread.Add(id);
                continue;
            }
            if (data != null) {
                yield return (id, names.GetValueOrDefault(id, ""), data);
            }
        }
    }
}
