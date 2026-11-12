using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using M2dXmlGenerator;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Enum;
using Maple2.File.Parser.Xml.Table;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// LOCALE=KR over the NA pack: the chapter books the played KR client lists take its rewards, whose skillPoint value
// is "amount,rank" or "amount" alone (the client's form). Synthetic rows of the client's shapes.
[NonParallelizable]
public class ChapterBookSkillPointTests {
    private Dictionary<string, int> oldFeatures = null!;
    private string oldLocale = null!;
    private static readonly XmlSerializer BookSerializer = new(typeof(ChapterBookRoot));

    [SetUp]
    public void SetFilter() {
        oldFeatures = FeatureLocaleFilter.Features;
        oldLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = "KR";
        FeatureLocaleFilter.Features = new Dictionary<string, int> { ["Chapter0"] = 1 };
    }

    [TearDown]
    public void RestoreFilter() {
        FeatureLocaleFilter.Features = oldFeatures;
        FeatureLocaleFilter.Locale = oldLocale;
    }

    private const string Pack = """
        <ms2>
          <chapterbook id="3" prologue="10" epilogue="11" rewardType1="item" rewardValue1="20301978,1,1" feature="Chapter0" locale="NA" />
          <chapterbook id="4" prologue="12" epilogue="13" rewardType1="item" rewardValue1="20301103,1,5" feature="Chapter0" locale="NA" />
          <chapterbook id="5" prologue="14" epilogue="15" rewardType1="skillPoint" rewardValue1="2,1" feature="Chapter0" locale="NA" />
        </ms2>
        """;

    // 3: a capital "SkillPoint" with one value; 4: "skillPoint" with one value; 5: "amount,rank".
    private const string Client = """
        <ms2>
          <chapterbook id="3" prologue="10" epilogue="11" rewardType1="statPoint" rewardValue1="2" rewardType2="SkillPoint" rewardValue2="1" feature="Chapter0" locale="KR" />
          <chapterbook id="4" prologue="12" epilogue="13" rewardType1="statPoint" rewardValue1="1" rewardType2="skillPoint" rewardValue2="1" feature="Chapter0" locale="KR" />
          <chapterbook id="5" prologue="14" epilogue="15" rewardType1="skillPoint" rewardValue1="2,1" feature="Chapter0" locale="KR" />
        </ms2>
        """;

    private static XmlDocument Doc(string xml) {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    private static (ClientListedRows.Counts Counts, Dictionary<int, ChapterBook> Books) Run() {
        XmlDocument server = Doc(Pack);
        ClientListedRows.Counts counts = ClientListedRows.Add(server, Doc(Client), "chapterbook", "id", "KR", "NA",
            ClientListedRows.Available, ClientListedRows.RowReads(BookSerializer));
        using var reader = new XmlNodeReader(server);
        var root = (ChapterBookRoot) BookSerializer.Deserialize(reader)!;
        return (counts, root.chapterbook.ToDictionary(book => book.id));
    }

    [Test]
    public void Add_TakesTheClientRewards() {
        (ClientListedRows.Counts counts, Dictionary<int, ChapterBook> books) = Run();
        Assert.Multiple(() => {
            Assert.That(counts.AddedIds, Is.EqualTo(new[] { "3", "4", "5" }));
            Assert.That(counts.PackValues, Is.Empty);
            Assert.That(books[3].rewardType1, Is.EqualTo(QuestRewardType.statPoint));
            Assert.That(books[3].rewardType2, Is.EqualTo(QuestRewardType.skillPoint));
            Assert.That(books[3].rewardValue2, Is.EqualTo(new[] { "1" }));
            Assert.That(books[4].rewardType2, Is.EqualTo(QuestRewardType.skillPoint));
        });
    }

    // One value: that amount at rank 0 (the client's first rank). Two: amount and rank.
    [Test]
    public void SkillPoint_ReadsBothForms() {
        Dictionary<int, ChapterBook> books = Run().Books;
        Assert.Multiple(() => {
            Assert.That(TableMapper.ChapterBookSkillPoint(books[3].rewardValue2), Is.EqualTo(new ChapterBookTable.Entry.SkillPoint(1, 0)));
            Assert.That(TableMapper.ChapterBookSkillPoint(books[4].rewardValue2), Is.EqualTo(new ChapterBookTable.Entry.SkillPoint(1, 0)));
            Assert.That(TableMapper.ChapterBookSkillPoint(books[5].rewardValue1), Is.EqualTo(new ChapterBookTable.Entry.SkillPoint(2, 1)));
        });
    }
}
