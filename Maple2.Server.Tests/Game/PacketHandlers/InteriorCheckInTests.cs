using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The daily check-in (RequestCube 0x28) takes the reward row the client shows (CMasteryInteriorTable::GetDropBox,
// masteryinteriortable.cpp:359): a home whose decoration score is below every row of a point above 0 has no reward,
// so the client offers no check-in; one sent anyway is refused with 0x28 85 and the day's check-in stays open.
// The box ids are made up.
public class InteriorCheckInTests {
    private TestGameSession test = null!;

    private (GameSession Session, Plot Plot) StartInHome() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry> {
            [0] = new(0, 900001),
            [100] = new(100, 900001),
            [300] = new(300, 900002),
        }));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        Plot plot = test.EnterHome();
        test.TakeSent();
        return (session, plot);
    }

    [Test]
    public void AnEmptyHome_HasNoCheckIn() {
        (GameSession session, Plot plot) = StartInHome();
        Assert.That(plot.Cubes, Is.Empty);

        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0x28)));

        byte[] refusal = CubePacket.InteriorCheckInError(UgcMapError.s_ugcmap_take_interior_gift_fail_from_db).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(refusal) }));
            Assert.That(session.Player.Value.Home.DecorationRewardTimestamp, Is.EqualTo(0));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    private const int BoxItem = 20000001, GradeItem = 20000002;

    // A home at level 1 whose plot holds a bed (a score the reward row 1 reaches) and whose box drops one item; the
    // grade 1 reward is another item.
    private (GameSession Session, Plot Plot) StartFurnished() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(BoxItem), TestGameSession.Metadata(GradeItem));
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry> {
            [0] = new(0, 900001),
            [1] = new(1, 900001),
        }));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> {
            [1] = new(1, 0, GradeItem),
            [2] = new(2, 1000, 0),
        }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [900001] = TestGameSession.DropBox([(BoxItem, 1)]),
        }));
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        Plot plot = test.EnterHome();
        ItemMetadata bed = TestGameSession.Metadata(50100001) with { Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.Bed, false) };
        plot.Cubes[new Maple2.Model.Common.Vector3B(1, 1, 1)] = new PlotCube(bed, 1) { Type = PlotCube.CubeType.Default };
        test.TakeSent();
        return (session, plot);
    }

    private void Send(GameSession session, params byte[] bytes) =>
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            foreach (byte b in bytes) writer.WriteByte(b);
        }));

    private Maple2.Database.Model.Home StoredHome() {
        using var context = new Ms2Context(test.Options);
        return context.Home.AsNoTracking().Single(home => home.AccountId == test.AccountId);
    }

    private int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    // The check-in is stored before the box is given: a mailed box item's row is written at once, so a crash before
    // the next session save left the home not checked in and the box could be taken again after a relog.
    [Test]
    public void CheckIn_IsStoredWithTheBox() {
        (GameSession session, _) = StartFurnished();

        Send(session, 0x28);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.DecorationRewardTimestamp, Is.Not.Zero);
            Assert.That(StoredHome().DecorationRewardTimestamp, Is.EqualTo(session.Player.Value.Home.DecorationRewardTimestamp));
            Assert.That(Held(session, BoxItem), Is.EqualTo(1));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A check-in whose store fails gives nothing, puts the exp back and leaves the day's check-in open (0x28 85).
    [Test]
    public void CheckIn_NotStored_GivesNothing() {
        (GameSession session, _) = StartFurnished();
        Home home = session.Player.Value.Home;
        (long level, long exp) = (home.DecorationLevel, home.DecorationExp);
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Home>().Any(entry => entry.State == EntityState.Modified);

        Send(session, 0x28);
        test.FailSave = null;

        byte[] refusal = CubePacket.InteriorCheckInError(UgcMapError.s_ugcmap_take_interior_gift_fail_from_db).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ResponseCube).Select(Convert.ToHexString),
                Is.EqualTo(new[] { Convert.ToHexString(refusal) }));
            Assert.That(home.DecorationRewardTimestamp, Is.Zero);
            Assert.That((home.DecorationLevel, home.DecorationExp), Is.EqualTo((level, exp)));
            Assert.That(Held(session, BoxItem), Is.Zero);
        });

        Send(session, 0x28);
        Assert.That(Held(session, BoxItem), Is.EqualTo(1));
    }

    // A grade reward: the claimed grade is stored before the item is given; a failed store gives nothing (0x29 85)
    // and leaves the grade to claim.
    [Test]
    public void GradeReward_IsStoredBeforeTheItem() {
        (GameSession session, _) = StartFurnished();
        Home home = session.Player.Value.Home;
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Home>().Any(entry => entry.State == EntityState.Modified);

        Send(session, 0x29, 1);
        test.FailSave = null;

        byte[] refusal = CubePacket.InteriorGradeRewardError(UgcMapError.s_ugcmap_take_interior_gift_fail_from_db).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ResponseCube).Select(Convert.ToHexString),
                Is.EqualTo(new[] { Convert.ToHexString(refusal) }));
            Assert.That(home.InteriorRewardsClaimed, Is.Empty);
            Assert.That(Held(session, GradeItem), Is.Zero);
        });

        Send(session, 0x29, 1);
        Assert.Multiple(() => {
            Assert.That(Held(session, GradeItem), Is.EqualTo(1));
            Assert.That(StoredHome().InteriorRewardsClaimed, Is.EqualTo(new[] { 1 }));
        });
    }
}
