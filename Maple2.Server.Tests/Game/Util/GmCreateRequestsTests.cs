using System;
using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The bodies as the client's senders write them (after the opcode).
public class GmCreateRequestsTests {
    private static ByteReader Reader(string hex) {
        return new ByteReader(Convert.FromHexString(hex.Replace(" ", "")), 0);
    }

    [Test]
    public void ItemCreate_PlainWindow() {
        ByteReader reader = Reader(
            "00 2D 31 01 " + // item 20000000
            "05 00 00 00 " + // amount
            "04 " + // rank
            "00 00 00 00 " + // period
            "14 00 00 00 " + // level 20
            "05 00 00 00 " + // enchant = level - 15
            "00 00 " + // not set, 0
            "FF FF FF FF FF FF FF FF 00 00 00 00 " +
            "00 00 00 00 00 00 00 00 00 00"); // five empty option sets

        GmItemCreateRequest request = GmItemCreateRequest.Read(reader);

        Assert.That(request, Is.EqualTo(new GmItemCreateRequest(20000000, 5, 4, 0, 20, 5, 0)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void ItemCreate_OptionWindow() {
        ByteReader reader = Reader(
            "00 2D 31 01 01 00 00 00 01 00 00 00 00 01 00 00 00 00 00 00 00 " +
            "01 03 " + // set, 3
            "01 00 00 00 02 00 00 00 00 00 00 00 " +
            "02 01 00 00 00 0A 00 00 00 02 00 00 00 14 00 00 00 " + // set 1, map A: 2 pairs
            "01 03 00 00 00 1E 00 00 00 " + // set 1, map B: 1 pair
            "00 00 00 00 00 00 00 00"); // sets 2-5

        GmItemCreateRequest request = GmItemCreateRequest.Read(reader);

        Assert.That(request.ItemId, Is.EqualTo(20000000));
        Assert.That(request.OptionCount, Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void NpcCreate_Npc() {
        ByteReader reader = Reader(
            "71 35 41 01 " + // npc 21050737
            "00 00 80 3F 00 00 00 40 00 00 16 43 " + // position 1, 2, 150
            "00 00 00 00 00 00 00 00 00 00 B4 42 " + // rotation 0, 0, 90
            "03 00 00 00 " + // count
            "00"); // not a pet

        GmNpcCreateRequest request = GmNpcCreateRequest.Read(reader);

        Assert.That(request, Is.EqualTo(new GmNpcCreateRequest(21050737, new Vector3(1, 2, 150), new Vector3(0, 0, 90), 3, false, 0f)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void NpcCreate_PetHasOneMoreFloat() {
        ByteReader reader = Reader(
            "71 35 41 01 " +
            "00 00 80 3F 00 00 00 40 00 00 16 43 " +
            "00 00 00 00 00 00 00 00 00 00 B4 42 " +
            "01 00 00 00 " +
            "01 " + // pet
            "00 00 00 3F"); // 0.5

        GmNpcCreateRequest request = GmNpcCreateRequest.Read(reader);

        Assert.That(request.Pet, Is.True);
        Assert.That(request.PetValue, Is.EqualTo(0.5f));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void AdditionalEffect_Add() {
        ByteReader reader = Reader(
            "00 " + // add
            "65 66 A0 00 " + // effect 10511973
            "03 00 00 00 " + // level
            "00 00 20 41"); // 10

        GmEffectRequest request = GmEffectRequest.Read(reader);

        Assert.That(request, Is.EqualTo(new GmEffectRequest(GmEffectRequest.Add, 10511973, 3, 10f)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void AdditionalEffect_ResetHasNoBody() {
        ByteReader reader = Reader("01");

        GmEffectRequest request = GmEffectRequest.Read(reader);

        Assert.That(request.Mode, Is.EqualTo(GmEffectRequest.Reset));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
