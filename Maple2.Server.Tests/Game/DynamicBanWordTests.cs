using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game;

// The live ban-word list (DynamicBanword 0x124, CDynamicBanWordManager::Read): the client sends its list's version
// at server entry (RecvOp 0x0E) and the reply brings it to the GM-kept list; the banword command changes the list and
// every channel sends it to its players.
public class DynamicBanWordTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static DynamicBanWord Word(int id, string word, int type = DynamicBanWordRules.Mask, string replacement = "") =>
        new() { Id = id, Type = type, Word = word, Replacement = replacement };

    [Test]
    public void TheVersionIsZeroForNoWordsAndFollowsTheList() {
        DynamicBanWord[] list = [Word(1, "foo"), Word(2, "bar", DynamicBanWordRules.Block)];
        long version = DynamicBanWordRules.Version(list);
        Assert.Multiple(() => {
            Assert.That(DynamicBanWordRules.Version([]), Is.EqualTo(0));
            Assert.That(version, Is.Not.EqualTo(0));
            Assert.That(DynamicBanWordRules.Version(list.Reverse()), Is.EqualTo(version)); // by id
            Assert.That(DynamicBanWordRules.Version([Word(1, "foo"), Word(2, "bar")]), Is.Not.EqualTo(version));
            Assert.That(DynamicBanWordRules.Version([Word(1, "foo"), Word(2, "bar", DynamicBanWordRules.Block, "x")]), Is.Not.EqualTo(version));
            Assert.That(DynamicBanWordRules.Version([Word(1, "foo"), Word(3, "bar", DynamicBanWordRules.Block)]), Is.Not.EqualTo(version));
            Assert.That(DynamicBanWordRules.Version([Word(1, "fo"), Word(2, "obar", DynamicBanWordRules.Block)]), Is.Not.EqualTo(version));
        });
    }

    [Test]
    public void TheModeFollowsTheClientsVersion() {
        DynamicBanWord[] list = [Word(1, "foo")];
        long version = DynamicBanWordRules.Version(list);
        Assert.Multiple(() => {
            Assert.That(DynamicBanWordRules.ModeFor(0, []), Is.Null);
            Assert.That(DynamicBanWordRules.ModeFor(5, []), Is.EqualTo(DynamicBanWordRules.ModeClear));
            Assert.That(DynamicBanWordRules.ModeFor(0, list), Is.EqualTo(DynamicBanWordRules.ModeLoad));
            Assert.That(DynamicBanWordRules.ModeFor(version + 1, list), Is.EqualTo(DynamicBanWordRules.ModeLoad));
            Assert.That(DynamicBanWordRules.ModeFor(version, list), Is.Null);
        });
    }

    [Test]
    public void RefusesEmptyLongAndDuplicateWords() {
        DynamicBanWord[] list = [Word(4, "Foo")];
        Assert.Multiple(() => {
            Assert.That(DynamicBanWordRules.Refusal("bar", "", list), Is.Null);
            Assert.That(DynamicBanWordRules.Refusal(" ", "", list), Is.EqualTo("The word is empty."));
            Assert.That(DynamicBanWordRules.Refusal(new string('a', 51), "", list), Is.Not.Null);
            Assert.That(DynamicBanWordRules.Refusal(new string('a', 50), "", list), Is.Null);
            Assert.That(DynamicBanWordRules.Refusal("bar", new string('a', 51), list), Is.Not.Null);
            Assert.That(DynamicBanWordRules.Refusal("fOO", "", list), Is.EqualTo("'Foo' is on the list already (#4)."));
            Assert.That(DynamicBanWordRules.Refusal("new", "",
                Enumerable.Range(1, DynamicBanWordRules.MaxWords).Select(id => Word(id, $"w{id}")).ToList()), Is.Not.Null);
        });
    }

    // A 1-letter word would mask or drop nearly every line; the client puts a replacement into the line as it is and
    // never matches a word with a tag, so < and > are refused in both.
    [Test]
    public void RefusesOneLetterWordsAndTags() {
        DynamicBanWord[] list = [];
        Assert.Multiple(() => {
            Assert.That(DynamicBanWordRules.Refusal("a", "", list), Is.EqualTo("The word is at least 2 letters."));
            Assert.That(DynamicBanWordRules.Refusal(" a ", "", list), Is.EqualTo("The word is at least 2 letters."));
            Assert.That(DynamicBanWordRules.Refusal("ab", "", list), Is.Null);
            Assert.That(DynamicBanWordRules.Refusal("ab", "<FONT color='#ff0000'>", list),
                Is.EqualTo("The word and its replacement cannot hold < or >."));
            Assert.That(DynamicBanWordRules.Refusal("<b>", "", list), Is.Not.Null);
            Assert.That(DynamicBanWordRules.Refusal("a>b", "", list), Is.Not.Null);
            Assert.That(DynamicBanWordRules.Refusal("ab", "x<y", list), Is.Not.Null);
        });
    }

    // The entry reply waits while a broadcast holds the gate, so it never sends an older list after a newer one.
    [Test]
    public void ServerEntry_WaitsForTheBroadcastGate() {
        var test = new TestGameSession();
        test.Start();
        test.TakeSent();
        System.Threading.Tasks.Task entry;
        lock (DynamicBanwordPacket.Gate) {
            entry = System.Threading.Tasks.Task.Run(() => Enter(test, 12345));
            Assert.That(entry.Wait(300), Is.False);
        }
        Assert.That(entry.Wait(5000), Is.True);
        Assert.That(Sent(test), Has.Length.EqualTo(1));
    }

    // BannedWordData::Read: int id, int type, unicode word, unicode replacement.
    [Test]
    public void ThePacketsAreTheClientsLayout() {
        DynamicBanWord[] list = [Word(7, "ab", DynamicBanWordRules.Mask, "c")];
        long version = DynamicBanWordRules.Version(list);
        string load = "2401" + "01" + Long(version) + "0100"
            + "07000000" + "01000000" + "0200" + "61006200" + "0100" + "6300";
        Assert.Multiple(() => {
            Assert.That(Convert.ToHexString(DynamicBanwordPacket.Clear().ToArray()), Is.EqualTo("2401" + "00" + Long(0)));
            Assert.That(Convert.ToHexString(DynamicBanwordPacket.Load(version, list).ToArray()), Is.EqualTo(load));
            Assert.That(Convert.ToHexString(DynamicBanwordPacket.For(0, list)!.ToArray()), Is.EqualTo(load));
            Assert.That(DynamicBanwordPacket.For(version, list), Is.Null);
        });
    }

    private static string Long(long value) => Convert.ToHexString(BitConverter.GetBytes(value));

    private static void Add(TestGameSession test, string word, int type = DynamicBanWordRules.Mask, string replacement = "") {
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.AddDynamicBanWord(type, word, replacement, test.AccountId), Is.Not.Null);
    }

    private static string[] Sent(TestGameSession test) => test.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == SendOp.DynamicBanword).Select(Convert.ToHexString).ToArray();

    private static void Enter(TestGameSession test, long version) {
        new DynamicBanwordHandler().Handle(test.Session, TestGameSession.Packet(writer => writer.WriteLong(version)));
    }

    [Test]
    public void ServerEntry_NoWords_SendsNothing() {
        var test = new TestGameSession();
        test.Start();
        test.TakeSent();
        Enter(test, 0);
        Assert.That(Sent(test), Is.Empty);
    }

    [Test]
    public void ServerEntry_SendsTheListUnlessTheClientHoldsIt() {
        var test = new TestGameSession();
        test.Start();
        test.TakeSent();
        Add(test, "foo");
        Add(test, "bar", DynamicBanWordRules.Block);
        IList<DynamicBanWord> words;
        using (GameStorage.Request db = test.Storage.Context()) {
            words = db.GetDynamicBanWords();
        }
        long version = DynamicBanWordRules.Version(words);

        Enter(test, 0);
        Assert.That(Sent(test), Is.EqualTo(new[] { Convert.ToHexString(DynamicBanwordPacket.Load(version, words).ToArray()) }));
        Enter(test, version);
        Assert.That(Sent(test), Is.Empty);
    }

    [Test]
    public void ServerEntry_StaleVersionAndNoWords_Clears() {
        var test = new TestGameSession();
        test.Start();
        test.TakeSent();
        Enter(test, 12345);
        Assert.That(Sent(test), Is.EqualTo(new[] { "2401" + "00" + Long(0) }));
    }

    private static (int Code, string[] Lines) Run(TestGameSession test, params string[] args) {
        var console = new TestConsole();
        int code = new BanWordCommand(test.Session).Invoke(args, console);
        return (code, console.Out.ToString()!.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Test]
    public void Command_AddsListsAndRemoves() {
        var test = new TestGameSession();
        test.Start();

        Assert.That(Run(test, "list").Lines, Is.EqualTo(new[] { "No ban words." }));
        Assert.That(Run(test, "add", "foo").Lines, Is.EqualTo(new[] { "Added #1 'foo': masked with *" }));
        Assert.That(Run(test, "add", "bar", "--block").Lines, Is.EqualTo(new[] { "Added #2 'bar': hides the line" }));
        Assert.That(Run(test, "add", "baz", "--replace", "qux").Lines, Is.EqualTo(new[] { "Added #3 'baz': replaced by 'qux'" }));
        Assert.That(test.World.AdminRequests.Select(request => request.RequestCase),
            Is.EqualTo(Enumerable.Repeat(AdminRequest.RequestOneofCase.BanWord, 3)));

        (int dup, string[] dupLines) = Run(test, "add", "FOO");
        Assert.Multiple(() => {
            Assert.That(dup, Is.EqualTo(1));
            Assert.That(dupLines, Is.EqualTo(new[] { "'foo' is on the list already (#1)." }));
            Assert.That(Run(test, "add", "x", "--block", "--replace", "y").Code, Is.EqualTo(1));
        });

        Assert.That(Run(test, "list").Lines, Is.EqualTo(new[] {
            "#1 'foo': masked with *", "#2 'bar': hides the line", "#3 'baz': replaced by 'qux'",
        }));
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetDynamicBanWords().Select(word => (word.Type, word.Replacement)), Is.EqualTo(new[] {
                (DynamicBanWordRules.Mask, ""), (DynamicBanWordRules.Block, ""), (DynamicBanWordRules.Mask, "qux"),
            }));
        }

        Assert.That(Run(test, "remove", "2").Lines, Is.EqualTo(new[] { "Removed ban word 2." }));
        (int missing, string[] missingLines) = Run(test, "remove", "2");
        Assert.Multiple(() => {
            Assert.That(missing, Is.EqualTo(1));
            Assert.That(missingLines, Is.EqualTo(new[] { "Ban word 2 not found." }));
            Assert.That(Run(test, "list").Lines.Select(line => line.Split(' ')[0]), Is.EqualTo(new[] { "#1", "#3" }));
            Assert.That(test.World.AdminRequests, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public void Command_NeedsTheBanPermission() {
        var test = new TestGameSession();
        test.Start();
        var command = new BanWordCommand(test.Session);
        Assert.Multiple(() => {
            Assert.That(CommandRouter.Allowed(AdminPermissions.Ban, command), Is.True);
            Assert.That(CommandRouter.Allowed(AdminPermissions.GameMaster, command), Is.True);
            Assert.That(CommandRouter.Allowed(AdminPermissions.Debug, command), Is.False);
            Assert.That(CommandRouter.Allowed(AdminPermissions.None, command), Is.False);
        });
    }

    [Test]
    public void Channel_SendsTheListToEveryPlayer() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        TestGameSession[] tests = [new(options), new(options)];
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        foreach (TestGameSession test in tests) {
            GameSession session = test.Start();
            test.TakeSent();
            sessions[session.CharacterId] = session;
        }
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        var channel = new ChannelService(server, null!, tests[0].Storage, null!, null!, null!);

        AdminResponse Push() => channel.Admin(new AdminRequest { BanWord = new AdminRequest.Types.BanWord() }, null!)
            .GetAwaiter().GetResult();

        Add(tests[0], "foo");
        Assert.That(Push().Message, Is.Empty);
        IList<DynamicBanWord> words;
        using (GameStorage.Request db = tests[0].Storage.Context()) {
            words = db.GetDynamicBanWords();
        }
        string load = Convert.ToHexString(DynamicBanwordPacket.Load(DynamicBanWordRules.Version(words), words).ToArray());
        foreach (TestGameSession test in tests) {
            Assert.That(Sent(test), Is.EqualTo(new[] { load }));
        }

        using (GameStorage.Request db = tests[0].Storage.Context()) {
            Assert.That(db.RemoveDynamicBanWord(words[0].Id), Is.True);
        }
        Push();
        foreach (TestGameSession test in tests) {
            Assert.That(Sent(test), Is.EqualTo(new[] { "2401" + "00" + Long(0) }));
        }
    }
}
