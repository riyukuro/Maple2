using System.Linq;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// The played client's recipe rows win over the data folder's pack; ids only the pack lists stay. Synthetic rows.
public class MasteryRecipeIngestTests {
    [Test]
    public void PlayedRecipesFirst_ClientRowWinsAndAddsItsIds() {
        (int, string)[] played = [(50000001, "client limits 10/13"), (70000050, "client-only SkinSocket")];
        (int, string)[] pack = [(50000001, "pack limits 8/10"), (70000012, "pack-only")];

        (int Id, string Recipe)[] result = TableMapper.PlayedRecipesFirst(played, pack).ToArray();

        Assert.That(result, Is.EqualTo(new (int, string)[] {
            (50000001, "client limits 10/13"),
            (70000050, "client-only SkinSocket"),
            (70000012, "pack-only"),
        }));
    }

    [Test]
    public void PlayedRecipesFirst_WithoutAPlayedTableIsThePack() {
        (int, string)[] pack = [(30000001, "a"), (30000002, "b")];
        Assert.That(TableMapper.PlayedRecipesFirst([], pack).ToArray(), Is.EqualTo(pack));
    }

    [Test]
    public void PlayedRecipesFirst_FirstRowOfAnIdWins() {
        // The parser yields one row per id after its own filter; a repeat keeps the first, as the played file wins.
        (int, string)[] played = [(80000042, "first"), (80000042, "second")];
        Assert.That(TableMapper.PlayedRecipesFirst(played, []).ToArray(), Is.EqualTo(new (int, string)[] { (80000042, "first") }));
    }
}
