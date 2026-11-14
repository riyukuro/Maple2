using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The home OX quiz event: HomeAction 10 read the way the client writes it.
public class UgcOxRulesTests {
    [Test]
    public void CreateSettingsAreFeeMaxShare() {
        // UIQuizOXCreateDialog::OnCreate: the fee combo's data (an __int64), then the most entrants and the winners'
        // share, after the command byte 10 and the sub-type 0 the handler has read.
        var writer = new ByteWriter();
        writer.WriteLong(5000);
        writer.WriteInt(20);
        writer.WriteInt(90);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);

        UgcOxRules.Settings settings = UgcOxRules.ReadSettings(reader);

        Assert.That(settings, Is.EqualTo(new UgcOxRules.Settings(5000, 20, 90)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Only the create dialog's own choices pass; anything else is a forged packet.
    [Test]
    public void SettingsMustBeDialogChoices() {
        Assert.Multiple(() => {
            Assert.That(UgcOxRules.Valid(new UgcOxRules.Settings(1000, 10, 10)), Is.True);
            Assert.That(UgcOxRules.Valid(new UgcOxRules.Settings(9000, 40, 100)), Is.True);
            Assert.That(UgcOxRules.Valid(new UgcOxRules.Settings(1500, 10, 10)), Is.False);
            Assert.That(UgcOxRules.Valid(new UgcOxRules.Settings(0, 10, 10)), Is.False);
            Assert.That(UgcOxRules.Valid(new UgcOxRules.Settings(-1000, 10, 10)), Is.False);
            Assert.That(UgcOxRules.Valid(new UgcOxRules.Settings(1000L + (1L << 32), 10, 10)), Is.False);
            Assert.That(UgcOxRules.Valid(new UgcOxRules.Settings(1000, 50, 10)), Is.False);
            Assert.That(UgcOxRules.Valid(new UgcOxRules.Settings(1000, 10, 0)), Is.False);
            Assert.That(UgcOxRules.Valid(new UgcOxRules.Settings(1000, 10, 110)), Is.False);
        });
    }

    // The stadium script's move_user maps are the stadium itself from the stadium, and nowhere else.
    [Test]
    public void MoveUserStaysInTheStadium() {
        Assert.Multiple(() => {
            Assert.That(UgcOxRules.MoveUserMap(UgcOxRules.StadiumMapId, 61000021), Is.EqualTo(UgcOxRules.StadiumMapId));
            Assert.That(UgcOxRules.MoveUserMap(UgcOxRules.StadiumMapId, 61000003), Is.EqualTo(UgcOxRules.StadiumMapId));
            Assert.That(UgcOxRules.MoveUserMap(UgcOxRules.StadiumMapId, 2000062), Is.EqualTo(2000062));
            Assert.That(UgcOxRules.MoveUserMap(61000003, 61000021), Is.EqualTo(61000021));
        });
    }

    [Test]
    public void CreateRefusalOrder() {
        Assert.Multiple(() => {
            Assert.That(UgcOxRules.CreateRefusal(true, false, true, false), Is.Null);
            Assert.That(UgcOxRules.CreateRefusal(false, true, false, true), Is.EqualTo("s_ugcox_create_fail_other_home"));
            Assert.That(UgcOxRules.CreateRefusal(true, true, false, true), Is.EqualTo("s_ugcox_create_fail_design_home"));
            Assert.That(UgcOxRules.CreateRefusal(true, false, false, true), Is.EqualTo("s_ugcox_create_fail_already"));
            Assert.That(UgcOxRules.CreateRefusal(true, false, false, false), Is.EqualTo("s_ugcox_create_fail_item_lack"));
        });
    }

    [Test]
    public void QuestionIsTrimmedAndCapped() {
        Assert.Multiple(() => {
            Assert.That(UgcOxRules.Question("  Is the sky blue?  "), Is.EqualTo("Is the sky blue?"));
            Assert.That(UgcOxRules.Question("   "), Is.Null);
            Assert.That(UgcOxRules.Question(new string('a', 300))!.Length, Is.EqualTo(UgcOxRules.MaxQuestionLength));
        });
    }
}
