using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using static Maple2.Model.Error.CharacterCreateError;

namespace Maple2.Server.Tests.Login;

// The create request of the character select scene (CharacterManagement 1, UICharacterCreateDialog::FUN_1418e3f50):
// the job the client's own create reader allows, and the answer when a name is taken between the check and the save.
public class CharacterCreateRequestTests {
    private TestLoginSession test = null!;
    private CharacterManagementHandler handler = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        words.BanWordMetadata.Add(new BanWordMetadata(0, "bad", false));
        words.BanWordMetadata.Add(new BanWordMetadata(0, "word", false));
        words.BanWordMetadata.Add(new BanWordMetadata(0, "badword", false));
        words.SaveChanges();
        var tutorial = new JobTable.Tutorial(2000023, 0, 0, [], [], [], []);
        var skills = new Dictionary<SkillRank, JobTable.Skill[]> { [SkillRank.Basic] = [], [SkillRank.Awakening] = [] };
        // job.xml holds the beginner row as well as the eleven jobs.
        test.Rows.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(tutorial, skills, []),
            [JobCode.Knight] = new(tutorial, skills, []),
            [JobCode.GameMaster] = new(tutorial, skills, []),
        }));
        handler = new CharacterManagementHandler {
            World = test.World, GameStorage = test.Storage, BanWordStorage = new BanWordStorage(words),
            ItemMetadata = null!, TableMetadata = test.Rows.TableMetadata, ServerTableMetadata = test.Rows.ServerTableMetadata,
        };
        session = test.Start();
        using (var context = new Ms2Context(test.Rows.Options)) {
            context.Account.Find(test.AccountId)!.MaxCharacters = 4;
            context.SaveChanges();
        }
        session.ListCharacters();
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    // The client's layout with no equips: u8 gender, i16 job, name, two colors, u16 equip bytes, u8 count, i32 time.
    private void Create(JobCode job, string name) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteByte(0);
            writer.WriteShort((short) job);
            writer.WriteUnicodeString(name);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.WriteInt(0);
        }));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private List<string> SentHex() => test.TakeSent().Select(Convert.ToHexString).ToList();

    private int Count(string name) {
        using var context = new Ms2Context(test.Rows.Options);
        return context.Character.Count(character => character.Name == name);
    }

    // FUN_140222590 refuses 1 (the beginner row of job.xml) and 999; a forged request with either made a character.
    [TestCase(JobCode.Newbie)]
    [TestCase(JobCode.GameMaster)]
    [TestCase(JobCode.None)]
    public void ForgedJob_IsRefused(JobCode job) {
        Create(job, "Fresh");

        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(CharacterListPacket.CreateError(s_char_err_job_forbidden)) }));
        Assert.That(Count("Fresh"), Is.Zero);
    }

    [Test]
    public void FirstJob_IsCreated() {
        Create(JobCode.Knight, "Fresh");

        Assert.That(Count("Fresh"), Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    // The client fills its banned word text ("Contains a forbidden word ({0}).", answer 9) with the answer's text;
    // it was empty. The word named is the first in the name, the longest of those starting there.
    [Test]
    public void BannedWord_IsNamedInTheAnswer() {
        Create(JobCode.Knight, "AWordBadword");

        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(CharacterListPacket.CreateError(s_char_err_ban_any, "word")) }));
        Assert.That(Count("AWordBadword"), Is.Zero);

        Create(JobCode.Knight, "Badwording");

        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(CharacterListPacket.CreateError(s_char_err_ban_any, "badword")) }));
    }

    // Two creates of one name: the second's save hits the unique name index after its check passed. The client
    // gets the taken-name error it shows for a name check (s_char_err_already_taken), not the system error.
    [Test]
    public void NameTakenDuringTheSave_SendsAlreadyTaken() {
        bool raced = false;
        test.Rows.FailSave = context => {
            if (raced || !context.ChangeTracker.Entries<Maple2.Database.Model.Character>().Any()) {
                return false;
            }
            raced = true;
            test.AddCharacter("Fresh", accountId: test.AccountId + 1);
            return true;
        };

        Create(JobCode.Knight, "Fresh");

        Assert.That(raced, Is.True);
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(CharacterListPacket.CreateError(s_char_err_already_taken)) }));
        Assert.That(Count("Fresh"), Is.EqualTo(1));
    }

    [TestCase(JobCode.Newbie, false)]
    [TestCase(JobCode.None, false)]
    [TestCase(JobCode.GameMaster, false)]
    [TestCase((JobCode) 15, false)]
    [TestCase(JobCode.Knight, true)]
    [TestCase(JobCode.Assassin, true)]
    [TestCase(JobCode.SoulBinder, true)]
    public void CreateJob(JobCode job, bool expected) {
        Assert.That(Maple2.Model.Validators.CharacterCreateRules.IsCreateJob(job), Is.EqualTo(expected));
    }

    // The create layout with the gender and the equips of the test's choice.
    private void CreateRaw(byte gender, string name, params (int Id, byte Slot)[] equips) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteByte(gender);
            writer.WriteShort((short) JobCode.Knight);
            writer.WriteUnicodeString(name);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteShort(1);
            writer.WriteByte((byte) equips.Length);
            foreach ((int id, byte slot) in equips) {
                writer.WriteInt(id);
                writer.WriteByte(slot);
            }
            writer.WriteInt(0);
        }));
    }

    private void ExpectError(CharacterCreateError error, string name) {
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(CharacterListPacket.CreateError(error)) }));
        Assert.That(Count(name), Is.Zero);
    }

    // The client sends gender 0 or 1 only.
    [Test]
    public void AForgedGender_IsRefused() {
        CreateRaw(2, "Fresh");
        ExpectError(s_char_err_system, "Fresh");
    }

    // A blank or too short name is the name error, an over long one the system error (the client's box limits it),
    // and a letter the region does not take is the ban-all error.
    [TestCase(" ", s_char_err_name)]
    [TestCase("a", s_char_err_name)]
    [TestCase("abcdefghijklm", s_char_err_system)]
    [TestCase("a!b", s_char_err_ban_all)]
    public void ANameTheClientWouldNotSend_IsRefused(string name, CharacterCreateError error) {
        CreateRaw(0, name);
        ExpectError(error, name);
    }

    [Test]
    public void ATakenName_IsRefused() {
        test.AddCharacter("Taken", accountId: test.AccountId + 1);
        CreateRaw(0, "Taken");
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(CharacterListPacket.CreateError(s_char_err_already_taken)) }));
        Assert.That(Count("Taken"), Is.EqualTo(1));
    }

    [Test]
    public void AFullAccount_IsRefused() {
        for (int i = 0; i < 4; i++) {
            test.AddCharacter($"Own{i}", accountId: test.AccountId);
        }
        CreateRaw(0, "Fresh");
        ExpectError(s_char_err_char_count, "Fresh");
    }

    // The SK and OH slots are never a default item of a new character.
    [TestCase((byte) EquipSlot.SK)]
    [TestCase((byte) EquipSlot.OH)]
    public void AnEquipOfAForbiddenSlot_IsRefused(byte slot) {
        CreateRaw(0, "Fresh", (11300001, slot));
        ExpectError(s_char_err_invalid_def_item, "Fresh");
    }
}
