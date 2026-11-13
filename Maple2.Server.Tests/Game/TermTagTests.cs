using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game;

// Term tags (opcode 0x137, TermTagManagerCli::OnPacket): the client opens term-gated content (CGameRule::HasTermTag,
// e.g. a nurturing cube whose quest tag is the NurturingQuestTag constant) only while the term is registered. The
// names and ids here are made up.
public class TermTagTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly TermTagTable.Entry Old = new(7, "OldTerm", 1000, 2000);
    private static readonly TermTagTable.Entry Cats = new(153, "CatTerm", 1000, 2000);

    private static string Hex(ByteWriter writer) => Convert.ToHexString(writer.ToArray());

    private static string EntryHex(int id, string name, long start, long end) {
        var writer = new ByteWriter();
        writer.WriteInt(id);
        writer.WriteUnicodeString(name);
        writer.WriteLong(start);
        writer.WriteLong(end);
        writer.WriteBool(false);
        return Convert.ToHexString(writer.ToArray());
    }

    // Load: mode 0, the finished count, the running count, then the finished and the running entries (int id,
    // unicode name, long start, long end, bool).
    [Test]
    public void Load_Layout() {
        string hex = Hex(TermTagPacket.Load([Old], [Cats]));
        Assert.That(hex, Is.EqualTo("3701" + "00" + "01000000" + "01000000" + EntryHex(7, "OldTerm", 1000, 2000) + EntryHex(153, "CatTerm", 1000, 2000)));
        Assert.That(hex[..6], Is.EqualTo("370100"));
        Assert.That(EntryHex(7, "A", 1, 2), Is.EqualTo("07000000" + "0100" + "4100" + "0100000000000000" + "0200000000000000" + "00"));
    }

    // Start and End: the mode, a count and the entries.
    [Test]
    public void StartAndEnd_Layout() {
        Assert.That(Hex(TermTagPacket.Start([Cats])), Is.EqualTo("3701" + "01" + "01000000" + EntryHex(153, "CatTerm", 1000, 2000)));
        Assert.That(Hex(TermTagPacket.End([Cats, Old])), Is.EqualTo("3701" + "02" + "02000000" + EntryHex(153, "CatTerm", 1000, 2000) + EntryHex(7, "OldTerm", 1000, 2000)));
    }

    // Dates are KST (UTC+9), "yyyy-MM-dd-HH-mm" or with seconds; rows the filter drops and rows whose dates do not
    // read are left out.
    [Test]
    public void Ingest_KstDates() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <term id="200" name="CoinTerm" feature="Event" locale="KR"><v startDate="2022-03-17-08-00" endDate="2022-06-09-08-00"/></term>
              <term id="137" name="DayTerm" locale="KR"><v startDate="2019-04-12-00-00" endDate="2019-04-14-23-59-59"/></term>
              <term id="152" name="OtherTerm" locale="CN"><v startDate="2019-10-16-08-00" endDate="2019-11-06-08-00"/></term>
              <term id="5" name="BadTerm" locale="KR"><v startDate="2019-13-16-08-00" endDate="2019-11-06-08-00"/></term>
              <term id="6" name="NoPeriod" locale="KR"/>
            </ms2>
            """);
        TermTagTable table = ServerTableMapper.MapTermTag(document, row => row.GetAttribute("locale") == "KR");
        Assert.That(table.Entries.Keys, Is.EquivalentTo(new[] { 200, 137 }));
        Assert.Multiple(() => {
            // the KMS2 load's times of these two terms
            Assert.That(table.Entries[200], Is.EqualTo(new TermTagTable.Entry(200, "CoinTerm", 1647471600, 1654729200)));
            Assert.That(table.Entries[137], Is.EqualTo(new TermTagTable.Entry(137, "DayTerm", 1554994800, 1555253999)));
        });
    }

    // Running from the start until the end, finished from the end on, not sent before the start; a GM change
    // replaces the table's term.
    [Test]
    public void Split_ByTime() {
        var table = new TermTagTable(new Dictionary<int, TermTagTable.Entry> {
            [7] = Old,
            [153] = Cats,
            [9] = new(9, "Later", 5000, 6000),
        });
        var state = new TermTagState();
        (List<TermTagTable.Entry> finished, List<TermTagTable.Entry> running) = state.Split(table, 1500);
        Assert.That(finished, Is.Empty);
        Assert.That(running, Is.EqualTo(new[] { Old, Cats }));
        (finished, running) = state.Split(table, 2000);
        Assert.That(finished, Is.EqualTo(new[] { Old, Cats }));
        Assert.That(running, Is.Empty);

        state.Set(Cats with { Start = 2500, End = 3500 });
        (finished, running) = state.Split(table, 3000);
        Assert.That(finished, Is.EqualTo(new[] { Old }));
        Assert.That(running, Is.EqualTo(new[] { Cats with { Start = 2500, End = 3500 } }));
        Assert.That(state.Find(table, "catterm"), Is.EqualTo(Cats with { Start = 2500, End = 3500 }));
        Assert.That(state.Find(table, "None"), Is.Null);
    }

    // Two players on one channel; the GM is the first.
    private static (GameServer server, TestGameSession[] tests) Channel(TermTagTable table) {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        TestGameSession[] tests = [new(options), new(options)];
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        foreach (TestGameSession test in tests) {
            test.SetServerTable(table);
            GameSession session = test.Start();
            test.TakeSent();
            typeof(GameSession).GetField("server", Any)!.SetValue(session, server);
            sessions[session.CharacterId] = session;
        }
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        return (server, tests);
    }

    // The entry load lists the table's finished terms (the table's dates are past).
    [Test]
    public void Entry_LoadsTheTable() {
        (_, TestGameSession[] tests) = Channel(new TermTagTable(new Dictionary<int, TermTagTable.Entry> { [153] = Cats }));
        tests[0].Session.LoadTermTags();
        Assert.That(tests[0].TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(TermTagPacket.Load([Cats], [])) }));
    }

    // "termtag start" sends the started term to every player of the channel, and a later entry lists it running;
    // "termtag end" sends its end, and a later entry lists it finished.
    [Test]
    public void Command_StartAndEnd() {
        (_, TestGameSession[] tests) = Channel(new TermTagTable(new Dictionary<int, TermTagTable.Entry> { [153] = Cats, [7] = Old }));
        GameSession gm = tests[0].Session;

        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(new TermTagCommand(gm).Invoke(["start", "CatTerm", "30"], new TestConsole()), Is.EqualTo(0));
        TermTagTable.Entry started = gm.TermTags.Find(gm.ServerTableMetadata.TermTagTable, "CatTerm")!;
        Assert.That(started.Start, Is.InRange(before, before + 5));
        Assert.That(started.End, Is.EqualTo(started.Start + 1800));
        foreach (TestGameSession test in tests) {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(TermTagPacket.Start([started])) }));
        }
        tests[1].Session.LoadTermTags();
        Assert.That(tests[1].TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(TermTagPacket.Load([Old], [started])) }));

        Assert.That(new TermTagCommand(gm).Invoke(["end", "CatTerm"], new TestConsole()), Is.EqualTo(0));
        TermTagTable.Entry ended = gm.TermTags.Find(gm.ServerTableMetadata.TermTagTable, "CatTerm")!;
        Assert.That(ended.End, Is.InRange(started.Start, started.Start + 5));
        foreach (TestGameSession test in tests) {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(TermTagPacket.End([ended])) }));
        }
        tests[1].Session.LoadTermTags();
        Assert.That(tests[1].TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(TermTagPacket.Load([Old, ended], [])) }));
    }

    // An unknown term, or the end of a term that is not running, sends nothing.
    [Test]
    public void Command_UnknownOrNotRunning() {
        (_, TestGameSession[] tests) = Channel(new TermTagTable(new Dictionary<int, TermTagTable.Entry> { [153] = Cats }));
        GameSession gm = tests[0].Session;
        var console = new TestConsole();
        new TermTagCommand(gm).Invoke(["start", "None"], console);
        new TermTagCommand(gm).Invoke(["end", "CatTerm"], console);
        Assert.That(console.Out.ToString(), Does.Contain("No term None").And.Contain("No running term CatTerm"));
        Assert.That(tests[0].TakeSent(), Is.Empty);
        Assert.That(tests[1].TakeSent(), Is.Empty);
    }

    [Test]
    public void Command_NeedsEventManagement() {
        (_, TestGameSession[] tests) = Channel(new TermTagTable(new Dictionary<int, TermTagTable.Entry>()));
        var command = new TermTagCommand(tests[0].Session);
        Assert.Multiple(() => {
            Assert.That(CommandRouter.Allowed(AdminPermissions.GameMaster, command), Is.True);
            Assert.That(CommandRouter.Allowed(AdminPermissions.Debug, command), Is.False);
            Assert.That(CommandRouter.Allowed(AdminPermissions.None, command), Is.False);
        });
    }
}
