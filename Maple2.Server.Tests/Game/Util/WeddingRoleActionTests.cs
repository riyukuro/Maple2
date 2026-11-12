using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Common;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;

namespace Maple2.Server.Tests.Game.Util;

// The hall trigger actions for the groom and the bride. The groom is the one who reserved the hall, the bride the
// other of the couple; each action goes to the client of that one only (the client acts on its own player).
public class WeddingRoleActionTests {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private TestGameSession groom = null!;
    private TestGameSession bride = null!;
    private TestGameSession guest = null!;
    private TriggerContext context = null!;

    private static void Set<T>(object target, string field, object? value) {
        FieldInfo info = typeof(T).GetField(field, Flags) ?? throw new MissingFieldException(typeof(T).Name, field);
        info.SetValue(target, value);
    }

    private static T Bare<T>() {
        var bare = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(bare!);
        return bare;
    }

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        groom = new TestGameSession(options);
        bride = new TestGameSession(options);
        guest = new TestGameSession(options);
        foreach (TestGameSession test in new[] { groom, bride, guest }) {
            test.Start();
        }
        Assume.That(new[] { groom, bride, guest }.Select(test => test.CharacterId).Distinct().Count(), Is.EqualTo(3));

        FieldManager field = groom.Session.Field!;
        int objectId = 1;
        foreach (TestGameSession test in new[] { groom, bride, guest }) {
            field.Players[objectId++] = test.Session.Player;
        }
        groom.Session.Player.Position = new Vector3(100, 200, 300);
        bride.Session.Player.Position = new Vector3(400, 500, 600);
        // As storage loads it for the bride: the first partner is the other one.
        field.Wedding = new WeddingCeremony(7001, groom.CharacterId, bride.CharacterId, groom.CharacterId);

        var trigger = Bare<FieldTrigger>();
        Set<FieldEntity<TriggerModel>>(trigger, "<Field>k__BackingField", field);
        Set<FieldEntity<TriggerModel>>(trigger, "<Value>k__BackingField", new TriggerModel(1, "main", Vector3.Zero, Vector3.Zero));
        context = Bare<TriggerContext>();
        Set<TriggerContext>(context, "owner", trigger);
        Set<TriggerContext>(context, "logger", new LoggerConfiguration().CreateLogger());
        Set<TriggerContext>(context, "lastDebugKey", "");
        foreach (TestGameSession test in new[] { groom, bride, guest }) {
            test.TakeSent();
        }
    }

    private static string[] Sent(TestGameSession test, SendOp op) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == op).Select(Convert.ToHexString).ToArray();

    [Test]
    public void RoleId_GroomIsTheReserverAndBrideTheOther() {
        var ceremony = new WeddingCeremony(7001, 101, 102, 102);
        Assert.That(ceremony.RoleId("Groom"), Is.EqualTo(102));
        Assert.That(ceremony.RoleId("Bride"), Is.EqualTo(101));
        Assert.That(ceremony.RoleId("Guest"), Is.EqualTo(0));
        // A groom who is not one of the couple, or none, names nobody.
        Assert.That(new WeddingCeremony(7001, 101, 102, 103).RoleId("Groom"), Is.EqualTo(0));
        Assert.That(new WeddingCeremony(7001, 101, 102).RoleId("Bride"), Is.EqualTo(0));
        Assert.That(TriggerContext.WeddingRole(new long[] { 103, 101, 102 }, ceremony, "Bride", id => id), Is.EqualTo(new long[] { 101 }));
        Assert.That(TriggerContext.WeddingRole(new long[] { 103 }, ceremony, "Groom", id => id), Is.Empty);
    }

    // Wedding 0x27, result 2 (OnMode27Accept): the row id, keep-item false, loop false, and no time after it.
    [Test]
    public void Emotion_GoesToTheRoleOnly() {
        context.WeddingSetUserEmotion("Bride", 6);

        string expected = Convert.ToHexString(WeddingPacket.Emotion(6).ToArray());
        Assert.That(expected[4..], Is.EqualTo("27" + "02" + "06000000" + "00" + "00"));
        Assert.That(Sent(bride, SendOp.Wedding), Is.EqualTo(new[] { expected }));
        Assert.That(Sent(groom, SendOp.Wedding), Is.Empty);
        Assert.That(Sent(guest, SendOp.Wedding), Is.Empty);
    }

    // Trigger Ui 0xb: the point as three shorts and the immediate flag, to the one who looks.
    [Test]
    public void LookAt_SendsThePartnersPosition() {
        context.WeddingSetUserLookAt("Bride", "Groom", true);

        string expected = Convert.ToHexString(TriggerPacket.UiLookAt(new Vector3(100, 200, 300), true).ToArray());
        Assert.That(expected[^14..], Is.EqualTo("6400" + "C800" + "2C01" + "01"));
        Assert.That(Sent(bride, SendOp.Trigger), Is.EqualTo(new[] { expected }));
        Assert.That(Sent(groom, SendOp.Trigger), Is.Empty);
        Assert.That(Sent(guest, SendOp.Trigger), Is.Empty);
    }

    // Trigger Ui 10: the rotation and the immediate flag, to the one who turns.
    [Test]
    public void Rotation_GoesToTheRoleOnly() {
        context.WeddingSetUserRotation("Groom", new Vector3(0, 0, 90), true);

        string expected = Convert.ToHexString(TriggerPacket.UiPlayerRotation(new Vector3S(0, 0, 90), true).ToArray());
        Assert.That(expected[^14..], Is.EqualTo("0000" + "0000" + "5A00" + "01"));
        Assert.That(Sent(groom, SendOp.Trigger), Is.EqualTo(new[] { expected }));
        Assert.That(Sent(bride, SendOp.Trigger), Is.Empty);
        Assert.That(Sent(guest, SendOp.Trigger), Is.Empty);
    }

    // Without a groom (a ceremony made without the hall's reserver) the role actions do nothing.
    [Test]
    public void UnknownGroom_SendsNothing() {
        groom.Session.Field!.Wedding = new WeddingCeremony(7001, groom.CharacterId, bride.CharacterId);

        context.WeddingSetUserEmotion("Groom", 6);
        context.WeddingSetUserLookAt("Bride", "Groom", true);
        context.WeddingSetUserRotation("Bride", Vector3.Zero, true);

        foreach (TestGameSession test in new[] { groom, bride, guest }) {
            Assert.That(test.TakeSent(), Is.Empty);
        }
    }
}
