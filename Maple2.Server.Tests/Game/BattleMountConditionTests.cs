using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// allowBattleRidingState / onlyBattleRidingState of a begin condition, as the client's skill test reads them
// (skillslot.cpp:3254-3256): on a battle mount only a condition that allows it holds, and an only-on-battle-mount
// condition holds nowhere else. A plain mount is not a battle mount. KR: 97 effect rows allow it; 99910241 needs it.
public class BattleMountConditionTests {
    private static BeginCondition Condition(bool allow, bool only) {
        return new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, allow, only, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);
    }

    private static FieldPlayer Player(RideOnType? ride) {
        var test = new TestGameSession();
        test.Start();
        test.Session.Ride = new RideManager(test.Session);
        if (ride is { } type) {
            var metadata = new RideMetadata(99910241, "", new RideMetadataBasic(type, 0, 0, 0, false, 0, 0),
                new RideMetadataSpeed(1, 1, 1, 1), new Dictionary<BasicAttribute, long>());
            RideOnAction action = type == RideOnType.Battle
                ? new RideOnActionBattle(metadata.Id, 1, 99910241, 1)
                : new RideOnAction(metadata.Id, 1);
            test.Session.Ride.Ride = new Ride(test.Session.Player.ObjectId, metadata, action);
        }
        return test.Session.Player;
    }

    private static bool Holds(BeginCondition condition, FieldPlayer player) => condition.Check(player, player, player);

    // Most rows (allowBattleRidingState unset): not on a battle mount, anywhere else.
    [Test]
    public void Default_FailsOnlyOnABattleMount() {
        BeginCondition condition = Condition(allow: false, only: false);
        Assert.Multiple(() => {
            Assert.That(Holds(condition, Player(null)), Is.True, "on foot");
            Assert.That(Holds(condition, Player(RideOnType.Default)), Is.True, "on a plain mount");
            Assert.That(Holds(condition, Player(RideOnType.Battle)), Is.False, "on a battle mount");
        });
    }

    // 62100148: allowBattleRidingState only, so it holds on a battle mount and off one.
    [Test]
    public void Allowed_HoldsOnAndOffABattleMount_62100148() {
        BeginCondition condition = Condition(allow: true, only: false);
        Assert.Multiple(() => {
            Assert.That(Holds(condition, Player(null)), Is.True, "on foot");
            Assert.That(Holds(condition, Player(RideOnType.Battle)), Is.True, "on a battle mount");
        });
    }

    // 99910241: both flags, so it holds only on a battle mount.
    [Test]
    public void Only_HoldsOnlyOnABattleMount_99910241() {
        BeginCondition condition = Condition(allow: true, only: true);
        Assert.Multiple(() => {
            Assert.That(Holds(condition, Player(null)), Is.False, "on foot");
            Assert.That(Holds(condition, Player(RideOnType.Default)), Is.False, "on a plain mount");
            Assert.That(Holds(condition, Player(RideOnType.Battle)), Is.True, "on a battle mount");
        });
    }

    // A held buff is suspended while its holder rides a battle mount unless its condition allows it
    // (AdditionalCli::Update): the holder decides, not the caster, and a plain mount suspends nothing.
    [Test]
    public void HeldBuff_SuspendedWhileTheHolderRidesABattleMount() {
        BeginCondition plain = Condition(allow: false, only: false);
        BeginCondition allowed = Condition(allow: true, only: false);
        Assert.Multiple(() => {
            Assert.That(Buff.SuspendedOnBattleMount(Player(RideOnType.Battle), plain), Is.True, "holder on a battle mount");
            Assert.That(Buff.SuspendedOnBattleMount(Player(RideOnType.Battle), allowed), Is.False, "allowBattleRidingState");
            Assert.That(Buff.SuspendedOnBattleMount(Player(RideOnType.Default), plain), Is.False, "holder on a plain mount");
            Assert.That(Buff.SuspendedOnBattleMount(Player(null), plain), Is.False, "holder on foot");
        });
    }
}
