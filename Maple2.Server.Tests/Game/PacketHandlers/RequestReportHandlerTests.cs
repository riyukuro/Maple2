using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestReport through the handler (UIReportDialog::SendReport, uireportdialog.cpp:348-399): target name, text of 1 to
// 4000 letters, kind byte, flags int, then the kind's attachment. A report is stored only for a known character and
// starts a one minute cooldown; an unknown kind stores nothing and starts none.
public class RequestReportHandlerTests {
    private TestGameSession reporter = null!;
    private TestGameSession target = null!;

    [SetUp]
    public void SetUp() {
        reporter = new TestGameSession();
        reporter.Start();
        target = new TestGameSession(reporter.Options);
        target.Start();
    }

    private string TargetName => target.Session.PlayerName;

    private void Send(string name, string reason, ReportCategory kind, int flags, Action<ByteWriter>? attach = null) {
        new RequestReportHandler().Handle(reporter.Session, TestGameSession.Packet(w => {
            w.WriteUnicodeString(name);
            w.WriteUnicodeString(reason);
            w.WriteByte((byte) kind);
            w.WriteInt(flags);
            attach?.Invoke(w);
        }));
    }

    private IList<PlayerReportEntry> Reports() {
        using GameStorage.Request db = reporter.Storage.Context();
        return db.GetPlayerReports(true, 50);
    }

    [Test]
    public void PlayerReportStoresBothSidesAndFlag() {
        Send(TargetName, "bad", ReportCategory.Player, (int) PlayerReportFlag.Hacking);
        PlayerReportEntry report = Reports().Single();
        Assert.That(report.Category, Is.EqualTo(ReportCategory.Player));
        Assert.That(report.CharacterId, Is.EqualTo(target.Session.CharacterId));
        Assert.That(report.PlayerName, Is.EqualTo(TargetName));
        Assert.That(report.ReporterCharacterId, Is.EqualTo(reporter.Session.CharacterId));
        Assert.That(report.ReporterName, Is.EqualTo(reporter.Session.PlayerName));
        Assert.That(report.Reason, Is.EqualTo("bad"));
        Assert.That(report.Detail, Is.EqualTo(PlayerReportFlag.Hacking.ToString()));
        Assert.That(report.CloseTime, Is.Null);
        Assert.That(reporter.Session.ReportCooldown, Is.GreaterThan(0));
    }

    [Test]
    public void TargetNameMatchesAnyCase() {
        Send(TargetName.ToUpperInvariant(), "x", ReportCategory.Player, 8);
        Assert.That(Reports().Single().CharacterId, Is.EqualTo(target.Session.CharacterId));
    }

    [Test]
    public void UnknownNameStoresNothingAndNoCooldown() {
        Send("NoSuchPlayerName", "x", ReportCategory.Player, 8);
        Assert.That(Reports(), Is.Empty);
        Assert.That(reporter.Session.ReportCooldown, Is.EqualTo(0));
    }

    [Test]
    public void EmptyReasonIgnored() {
        Send(TargetName, "", ReportCategory.Player, 8);
        Assert.That(Reports(), Is.Empty);
        Assert.That(reporter.Session.ReportCooldown, Is.EqualTo(0));
    }

    [Test]
    public void ReasonLengthEdges() {
        Send(TargetName, new string('a', ReportRules.MaxTextLength + 1), ReportCategory.Player, 8);
        Assert.That(Reports(), Is.Empty);
        Send(TargetName, new string('a', ReportRules.MaxTextLength), ReportCategory.Player, 8);
        Assert.That(Reports().Single().Reason, Has.Length.EqualTo(ReportRules.MaxTextLength));
    }

    [Test]
    public void SecondReportWithinMinuteIgnored() {
        Send(TargetName, "one", ReportCategory.Player, 8);
        Send(TargetName, "two", ReportCategory.Player, 8);
        Assert.That(Reports().Single().Reason, Is.EqualTo("one"));
    }

    [Test]
    public void ReportAfterCooldownStored() {
        Send(TargetName, "one", ReportCategory.Player, 8);
        reporter.Session.ReportCooldown = DateTimeOffset.UtcNow.AddSeconds(-61).ToUnixTimeSeconds();
        Send(TargetName, "two", ReportCategory.Player, 8);
        Assert.That(Reports().Select(r => r.Reason), Is.EquivalentTo(new[] { "one", "two" }));
    }

    [Test]
    public void ReportInsideCooldownEdge() {
        Send(TargetName, "one", ReportCategory.Player, 8);
        reporter.Session.ReportCooldown = DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeSeconds();
        Send(TargetName, "two", ReportCategory.Player, 8);
        Assert.That(Reports(), Has.Count.EqualTo(1));
    }

    [Test]
    public void ChatReportStoresMessage() {
        Send(TargetName, "r", ReportCategory.Chat, (int) ChatReportFlag.Spam, w => w.WriteUnicodeString("buy gold"));
        PlayerReportEntry report = Reports().Single();
        Assert.That(report.Category, Is.EqualTo(ReportCategory.Chat));
        Assert.That(report.Detail, Is.EqualTo("Spam: buy gold"));
    }

    [Test]
    public void ChatReportEmptyOrLongMessage() {
        Send(TargetName, "r", ReportCategory.Chat, 1, w => w.WriteUnicodeString(new string('a', ReportRules.MaxTextLength + 1)));
        Assert.That(Reports(), Is.Empty);
        Assert.That(reporter.Session.ReportCooldown, Is.EqualTo(0));
    }

    [Test]
    public void PosterReportStoresIds() {
        Send(TargetName, "r", ReportCategory.Poster, (int) PosterReportFlag.Swearing, w => {
            w.WriteLong(5_000_000_000L);
            w.WriteUnicodeString("tpl");
        });
        PlayerReportEntry report = Reports().Single();
        Assert.That(report.Category, Is.EqualTo(ReportCategory.Poster));
        Assert.That(report.Detail, Is.EqualTo("Swearing, poster 5000000000 template tpl"));
    }

    [Test]
    public void PosterReportLongTemplateIgnored() {
        Send(TargetName, "r", ReportCategory.Poster, 1, w => {
            w.WriteLong(1);
            w.WriteUnicodeString(new string('t', ReportRules.MaxTextLength + 1));
        });
        Assert.That(Reports(), Is.Empty);
    }

    [Test]
    public void ItemDesignReportStoresListing() {
        Send(TargetName, "r", ReportCategory.ItemDesign, (int) DesignItemReportFlag.Copying, w => w.WriteLong(77));
        PlayerReportEntry report = Reports().Single();
        Assert.That(report.Category, Is.EqualTo(ReportCategory.ItemDesign));
        Assert.That(report.Detail, Is.EqualTo("Copying, listing 77"));
    }

    [Test]
    public void HomeReportStoresHomeMapPlot() {
        Send(TargetName, "r", ReportCategory.Home, (int) HomeReportFlag.HomeName, w => {
            w.WriteLong(9);
            w.WriteInt(62000000);
            w.WriteInt(3);
        });
        PlayerReportEntry report = Reports().Single();
        Assert.That(report.Category, Is.EqualTo(ReportCategory.Home));
        Assert.That(report.Detail, Is.EqualTo("HomeName, home 9 map 62000000 plot 3"));
    }

    [Test]
    public void PetReportStoresName() {
        Send(TargetName, "r", ReportCategory.Pet, (int) PetReportFlag.Name, w => w.WriteUnicodeString("Rex"));
        PlayerReportEntry report = Reports().Single();
        Assert.That(report.Category, Is.EqualTo(ReportCategory.Pet));
        Assert.That(report.Detail, Is.EqualTo("Name, pet Rex"));
    }

    [Test]
    public void PetReportLongNameIgnored() {
        Send(TargetName, "r", ReportCategory.Pet, 1, w => w.WriteUnicodeString(new string('p', ReportRules.MaxTextLength + 1)));
        Assert.That(Reports(), Is.Empty);
        Assert.That(reporter.Session.ReportCooldown, Is.EqualTo(0));
    }

    [Test]
    public void UnknownKindStoresNothingAndNoCooldown() {
        Send(TargetName, "r", (ReportCategory) 99, 1);
        Assert.That(Reports(), Is.Empty);
        Assert.That(reporter.Session.ReportCooldown, Is.EqualTo(0));
        Send(TargetName, "ok", ReportCategory.Player, 8);
        Assert.That(Reports(), Has.Count.EqualTo(1));
    }

    [Test]
    public void RejectedReportKeepsCooldownOpen() {
        Send(TargetName, "", ReportCategory.Player, 8);
        Send(TargetName, "real", ReportCategory.Player, 8);
        Assert.That(Reports().Single().Reason, Is.EqualTo("real"));
    }
}
