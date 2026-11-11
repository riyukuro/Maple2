using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

public class BuddyAutoRejectTests {
    [Test]
    public void OptionValueIsZeroOrOne() {
        Assert.That(BuddyRules.TryAutoReject(0, out bool off), Is.True);
        Assert.That(off, Is.False);
        Assert.That(BuddyRules.TryAutoReject(1, out bool on), Is.True);
        Assert.That(on, Is.True);
        Assert.That(BuddyRules.TryAutoReject(2, out _), Is.False);
        Assert.That(BuddyRules.TryAutoReject(-1, out _), Is.False);
    }

    [Test]
    public void OnlyAnIncomingRequestIsDeclined() {
        Assert.That(BuddyRules.DeclinesOnReceipt(true, BuddyType.InRequest), Is.True);
        Assert.That(BuddyRules.DeclinesOnReceipt(false, BuddyType.InRequest), Is.False);
        Assert.That(BuddyRules.DeclinesOnReceipt(true, BuddyType.Default), Is.False);
        Assert.That(BuddyRules.DeclinesOnReceipt(true, BuddyType.OutRequest), Is.False);
    }

    [Test]
    public void AskBytes() {
        ByteWriter writer = BuddyPacket.AskAutoReject();
        // opcode 0x0061, sub-type 20, no body
        Assert.That(writer.Buffer.AsSpan(0, writer.Length).ToArray(), Is.EqualTo(new byte[] { 0x61, 0x00, 20 }));
    }

    [Test]
    public void DeclineBytes() {
        var info = new PlayerInfo(new CharacterInfo(1, 2, "a", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []);
        var buddy = new Buddy(new BuddyEntry { Id = 0x1122334455667788, OwnerId = 3, BuddyId = 2, LastModified = 0, Type = BuddyType.InRequest }, info);
        ByteWriter writer = BuddyPacket.Decline(buddy);
        byte[] body = writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();
        // sub-type 4, error 0, entry id
        Assert.That(body, Is.EqualTo(new byte[] { 4, 0, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11 }));
    }
}
