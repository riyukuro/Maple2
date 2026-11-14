using System;
using System.Collections.Generic;
using Maple2.Server.Game.Session;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

[assembly: Maple2.Server.Tests.NestedStoreCheck]

namespace Maple2.Server.Tests;

// Fails every test during which a session started a store inside another store of its own (GameSession.NestedStore):
// the World's account lock is not re-entrant, so on a live server the inner store waits on its own thread's lock and
// its release frees the outer store's lock. A test that nests one on purpose sets GameSession.OnNestedStore itself.
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class NestedStoreCheck : Attribute, ITestAction {
    private static readonly List<string> Found = [];

    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test) {
        lock (Found) {
            Found.Clear();
        }
        GameSession.OnNestedStore = (where, stack) => {
            lock (Found) {
                Found.Add($"{where}{Environment.NewLine}{stack}");
            }
        };
    }

    public void AfterTest(ITest test) {
        GameSession.OnNestedStore = null;
        string[] nested;
        lock (Found) {
            nested = Found.ToArray();
            Found.Clear();
        }
        if (nested.Length > 0) {
            Assert.Fail($"A store ran inside another store of the same session ({nested.Length}):{Environment.NewLine}{string.Join(Environment.NewLine, nested)}");
        }
    }
}
