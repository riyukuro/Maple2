using System;
using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The Revival packet (RecvOp.Revival): mode 0 is the safe revive, mode 2 the meso revive (or the free coupon), mode 1
// the meret revive, mode 3 a pet's revive hit. The client sends the byte after the mode as the coupon checkbox.
public class RevivalHandlerTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private RevivalHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        // The player was made before the field; a revive reads the field's properties through the player.
        typeof(Maple2.Server.Game.Model.Actor<Maple2.Model.Game.Player>).GetField("<Field>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Player, session.Field);
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
        }));
        typeof(ConfigManager).GetField(nameof(ConfigManager.Skill))!.SetValue(session.Config, new SkillManager(session, new SkillBook()));
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
        test.SetTable(new CharacterAbilityTable(new Dictionary<int, CharacterAbilityTable.Entry>()));
        test.SetTable(new PremiumClubTable(new Dictionary<int, PremiumClubTable.Buff>(), new Dictionary<int, PremiumClubTable.Item>(),
            new Dictionary<int, PremiumClubTable.Package>()));
        test.SetTable(new InsigniaTable(new Dictionary<int, InsigniaTable.Entry>()));
        typeof(GuildManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Guild, session);
        var spawns = new System.Collections.Concurrent.ConcurrentDictionary<int, FieldPlayerSpawnPoint>();
        spawns[1] = new FieldPlayerSpawnPoint(session.Field, 1, new SpawnPointPC(1, new System.Numerics.Vector3(100, 200, 0), System.Numerics.Vector3.Zero, true, true), 1);
        typeof(Maple2.Server.Game.Manager.Field.FieldManager).GetField("fieldPlayerSpawnPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Field, spawns);
        typeof(Maple2.Server.Game.Manager.Field.FieldManager).GetField("<Pets>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Field, new System.Collections.Concurrent.ConcurrentDictionary<int, FieldPet>());
        handler = new RevivalHandler { ServerTableMetadata = test.ServerTableMetadata };
        typeof(ConfigManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, session);
    }

    private void Die() {
        typeof(FieldPlayer).GetProperty("IsDead")!.GetSetMethod(true)!.Invoke(session.Player, [true]);
        session.Player.Tombstone = new Tombstone(session.Player, 1);
        test.TakeSent();
    }

    private void Send(byte mode, params byte[] rest) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            foreach (byte b in rest) {
                writer.WriteByte(b);
            }
        }));
    }

    [Test]
    public void SafeRevive_BringsTheDeadBack() {
        Die();
        Send(0);

        Assert.That(session.Player.IsDead, Is.False);
        Assert.That(session.Player.Tombstone, Is.Null);
    }

    [Test]
    public void SafeRevive_AliveIsIgnored() {
        test.TakeSent();
        Send(0);

        Assert.That(session.Player.IsDead, Is.False);
        Assert.That(test.TakeSent().Any(packet => TestGameSession.Op(packet) == SendOp.Revival), Is.False);
    }

    [Test]
    public void SafeRevive_MovesToTheSpawnPointAndTellsTheClient() {
        Die();
        Send(0);

        Assert.That(session.Player.Position, Is.EqualTo(new System.Numerics.Vector3(100, 200, 25)));
        var ops = test.TakeSent().Select(TestGameSession.Op).ToList();
        Assert.That(ops, Does.Contain(SendOp.Revival));
        Assert.That(ops, Does.Contain(SendOp.UserMoveByPortal));
    }

    [Test]
    public void VoucherRevive_WithoutAVoucherLeavesThePlayerDead() {
        Die();
        Send(2, 1);

        Assert.That(session.Player.IsDead, Is.True);
        Assert.That(session.Player.Tombstone, Is.Not.Null);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // A coupon is taken before the revive: one the bag shows but cannot spend (locked) revives no one. Before, the
    // revive came first and the take's result was not read, so such a coupon revived for nothing and stayed.
    [TestCase(2, false)]
    [TestCase(2, true)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    public void CouponRevive_TakesTheCouponFirst(byte mode, bool locked) {
        ItemMetadata metadata = TestGameSession.Metadata(20300100, type: 2);
        metadata = metadata with { Property = metadata.Property with { Tag = ItemTag.FreeReviveCoupon } };
        test.Register(metadata);
        var coupon = new Item(metadata, 1, 1);
        coupon.IsLocked = locked;
        Assert.That(session.Item.Inventory.Add(coupon), Is.True);
        Die();
        Send(mode, 1);

        Assert.Multiple(() => {
            Assert.That(session.Player.IsDead, Is.EqualTo(locked));
            Assert.That(session.Item.Inventory.Find(ItemTag.FreeReviveCoupon).Sum(item => item.Amount), Is.EqualTo(locked ? 1 : 0));
        });
    }

    [Test]
    public void MeretRevive_WithoutMeretsTellsTheClientAndStaysDead() {
        Die();
        Send(1, 0);

        Assert.That(session.Player.IsDead, Is.True);
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Contain(SendOp.Notice));
    }

    [Test]
    public void MeretRevive_AliveIsIgnored() {
        test.TakeSent();
        Send(1, 0);

        Assert.That(session.Player.IsDead, Is.False);
    }

    [Test]
    public void SummonRevive_UnknownPetIsIgnored() {
        Die();
        Send(3, 0, 0, 0, 0);

        Assert.That(session.Player.IsDead, Is.True);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    private int Level => session.Player.Value.Character.Level;
    private static int MesoPrice(int level, int usedToday) => Maple2.Server.Game.LuaFunctions.Lua.CalcRevivalMeso(level,
        Math.Max(Maple2.Server.Game.LuaFunctions.Lua.GetMesoRevivalDailyMaxCount() - usedToday, 0), 0);

    [Test]
    public void MesoRevive_ChargesThePriceOnceAndSendsTheBalance() {
        long price = MesoPrice(Level, 0);
        Assert.That(price, Is.GreaterThan(0));
        session.Currency.ChangeMeso(price + 123);
        Die();

        Send(2, 0);

        Assert.That(session.Player.IsDead, Is.False);
        Assert.That(session.Currency.Meso, Is.EqualTo(123));
        Assert.That(session.Config.InstantReviveCount, Is.EqualTo(1));
        var ops = test.TakeSent().Select(TestGameSession.Op).ToList();
        Assert.That(ops, Does.Contain(SendOp.Revival));
        Assert.That(ops.Count(op => op == SendOp.Meso), Is.EqualTo(1));
    }

    [Test]
    public void MesoRevive_ShortOfMesosChargesNothingAndStaysDead() {
        long price = MesoPrice(Level, 0);
        session.Currency.ChangeMeso(price - 1);
        Die();

        Send(2, 0);

        Assert.That(session.Player.IsDead, Is.True);
        Assert.That(session.Currency.Meso, Is.EqualTo(price - 1));
        Assert.That(session.Config.InstantReviveCount, Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void MesoRevive_TheDailyMaximumClosesIt() {
        session.Config.InstantReviveCount = Maple2.Server.Game.LuaFunctions.Lua.GetMesoRevivalDailyMaxCount();
        session.Currency.ChangeMeso(10_000_000);
        Die();

        Send(2, 0);

        Assert.That(session.Player.IsDead, Is.True);
        Assert.That(session.Currency.Meso, Is.EqualTo(10_000_000));
    }

    [Test]
    public void MesoRevive_ThePriceRisesWithEachRevive() {
        long first = MesoPrice(Level, 0);
        long second = MesoPrice(Level, 1);
        session.Currency.ChangeMeso(first + second + 5);
        Die();
        Send(2, 0);
        Die();

        Send(2, 0);

        Assert.That(session.Player.IsDead, Is.False);
        Assert.That(session.Currency.Meso, Is.EqualTo(5));
        Assert.That(session.Config.InstantReviveCount, Is.EqualTo(2));
    }

    [Test]
    public void MesoRevive_AliveChargesNothing() {
        session.Currency.ChangeMeso(10_000_000);
        test.TakeSent();

        Send(2, 0);

        Assert.That(session.Currency.Meso, Is.EqualTo(10_000_000));
        Assert.That(session.Config.InstantReviveCount, Is.EqualTo(0));
    }

    [Test]
    public void MeretRevive_ChargesThePriceOnceAndRevives() {
        long price = Maple2.Server.Game.LuaFunctions.Lua.CalcRevivalMerat(Level, 0);
        Assert.That(price, Is.GreaterThan(0));
        session.Currency.ChangeMeret(price + 7, reportSpent: false);
        Die();

        Send(1, 0);

        Assert.That(session.Player.IsDead, Is.False);
        Assert.That(session.Currency.Meret, Is.EqualTo(7));
        var ops = test.TakeSent().Select(TestGameSession.Op).ToList();
        Assert.That(ops, Does.Contain(SendOp.Revival));
        Assert.That(ops.Count(op => op == SendOp.Meret), Is.EqualTo(1));
    }

    [Test]
    public void MeretRevive_OneMeretShortChargesNothing() {
        long price = Maple2.Server.Game.LuaFunctions.Lua.CalcRevivalMerat(Level, 0);
        session.Currency.ChangeMeret(price - 1, reportSpent: false);
        Die();

        Send(1, 0);

        Assert.That(session.Player.IsDead, Is.True);
        Assert.That(session.Currency.Meret, Is.EqualTo(price - 1));
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Contain(SendOp.Notice));
    }
}
