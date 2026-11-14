using System.Collections.Generic;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// A worn PC bang item's stats count only at a PC bang: SetGrade into or out of a PC bang rebuilds the stat totals
// (the client's grade setter re-evaluates the equipment, equipment.cpp:2958), as a whole rebuild would.
public class PcBangStatsTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Coat = 11400001;

    private TestGameSession test = null!;
    private FieldPlayer Player => test.Session.Player;
    private long Health => Player.Stats.Values[BasicAttribute.Health].Total;

    // A session with a PC bang coat of 500 health in the bag that runs whole stat rebuilds (as
    // AdditionalEffectLineTests with a coat): a 1000 health row for the character's level and the config parts the
    // stat allocation reads.
    private long Start() {
        test = new TestGameSession();
        test.SetTable(new SetItemTable(new Dictionary<int, SetItemTable.Entry>()));
        ItemMetadata coat = TestGameSession.Metadata(Coat, EquipSlot.CL, slotMax: 1);
        long coatUid = test.Store(coat with { Limit = coat.Limit with { RequirePcBang = true } }).Uid;
        test.SetServerTable(PcBangTests.Kr);
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        FieldManager field = session.Field!;
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options));
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(field, new FieldActor(field, 99, field.Metadata, null!));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(Player, field);

        session.Ride = new RideManager(session);
        Character character = Player.Value.Character;
        session.Stats = new StatsManager(Player, new UserStatTable(new Dictionary<JobCode, IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>>> {
            [character.Job.Code()] = new Dictionary<short, IReadOnlyDictionary<BasicAttribute, long>> {
                [character.Level] = new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 1000 },
            },
        }));
        typeof(ConfigManager).GetField("session", Any)!.SetValue(session.Config, session);
        typeof(ConfigManager).GetField("statAttributes", Any)!.SetValue(session.Config,
            new Maple2.Server.Game.Manager.Config.StatAttributes(new Dictionary<string, int>()));

        Item item = session.Item.Inventory.Get(coatUid)!;
        item.Stats = new ItemStats();
        item.Stats[ItemStats.Type.Constant].Basic[BasicAttribute.Health] = new BasicOption(500);
        return coatUid;
    }

    [Test]
    public void SetGradeRecountsTheWornPcBangItem() {
        long coatUid = Start();
        // Put on at a PC bang: the client puts a PC bang item on only there (invenslot.cpp:2090).
        test.Session.PcBang.SetGrade(1);
        Assert.That(test.Session.Item.Equips.Equip(coatUid, EquipSlot.CL, isSkin: false), Is.True);
        long atPcBang = Health;

        test.Session.PcBang.SetGrade(0);
        Assert.That(Health, Is.EqualTo(atPcBang - 500));

        // A grade change inside a PC bang counts it as before.
        test.Session.PcBang.SetGrade(1);
        Assert.That(Health, Is.EqualTo(atPcBang));
        test.Session.PcBang.SetGrade(2);
        Assert.That(Health, Is.EqualTo(atPcBang));
    }
}
