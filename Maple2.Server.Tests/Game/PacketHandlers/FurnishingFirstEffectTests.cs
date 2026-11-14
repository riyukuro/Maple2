using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.Object;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A switch furnishing with a firstAdditionalEffect (client ObjectData m_cube.m_firstEffect,
// commonfiles/objectdata.cpp:286-295) gives its buff to the user when it goes to its first state; the server
// never read the node, so a souvenir switch (object 50710001: buff 90000138, target 0, range 150) gave nothing.
public class FurnishingFirstEffectTests {
    private const int CubeItemId = 50710001;
    private const int EffectId = 90000138;

    private TestGameSession test = null!;

    [Test]
    public void Ingest_ReadsTheBuffOnlyWithAnId() {
        FunctionCubeMetadata.FirstEffectData? effect = FunctionCubeMapper.ParseFirstEffect(new FirstAdditionalEffect {
            firstAdditionalEffectID = EffectId, firstAdditionalEffectLevel = 1, firstAdditionalTarget = 0, firstAdditionalRange = 150,
        });
        Assert.Multiple(() => {
            Assert.That(effect, Is.EqualTo(new FunctionCubeMetadata.FirstEffectData(EffectId, 1, 0, 150f)));
            Assert.That(FunctionCubeMapper.ParseFirstEffect(new FirstAdditionalEffect()), Is.Null);
            Assert.That(FunctionCubeMapper.ParseFirstEffect(null), Is.Null);
        });
    }

    private GameSession StartInHome() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.Register(TestGameSession.Metadata(CubeItemId));
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        test.EnterHome();
        session.Field!.Players.Clear();
        typeof(GameSession).GetField("<SeatRelease>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, new SeatReleaseEcho());
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(skills)!;
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(EffectId, (short) 1), BuffAttributeTests.Effect(EffectId, DisabledBuffTests.Property)]);
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(session.Field, new FieldActor(session.Field!, 99, session.Field!.Metadata, null!));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Player, session.Field);
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session, FieldFunctionInteract cube) {
        new FunctionCubeHandler {
            FunctionCubeMetadataStorage = session.FunctionCubeMetadata,
            ServerTableMetadataStorage = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(1);
            writer.WriteUnicodeString(cube.InteractCube.Id);
            writer.WriteByte(1);
        }));
    }

    private (InteractCube, FieldFunctionInteract) AddSwitch(GameSession session, float distance, FunctionCubeMetadata.FirstEffectData? effect) {
        var metadata = new FunctionCubeMetadata(CubeItemId, 0, ConfigurableCubeType.None, InteractCubeState.None,
            InteractCubeControlType.Switch, [1, 0], 100, null) { FirstEffect = effect };
        var interact = new InteractCube(new Vector3B(0, 0, 0), metadata);
        FieldFunctionInteract cube = test.AddFunctionCube(interact);
        cube.Position = session.Player.Position + new Vector3(distance, 0, 0);
        return (interact, cube);
    }

    // Within the range the switch turns on and gives the buff; beyond it (still in the action key's reach) it
    // turns on with no buff, and a switch without the node gives none.
    [TestCase(0f, true, true, TestName = "Switch_WithTheEffect_InRange_GivesTheBuff")]
    [TestCase(1000f, true, false, TestName = "Switch_WithTheEffect_BeyondItsRange_NoBuff")]
    [TestCase(0f, false, false, TestName = "Switch_WithoutTheEffect_NoBuff")]
    public void Use_GivesTheFirstEffect(float distance, bool withEffect, bool given) {
        GameSession session = StartInHome();
        (InteractCube interact, FieldFunctionInteract cube) = AddSwitch(session, distance,
            withEffect ? new FunctionCubeMetadata.FirstEffectData(EffectId, 1, 0, 150f) : null);

        Use(session, cube);

        Assert.Multiple(() => {
            Assert.That(interact.State, Is.EqualTo(InteractCubeState.InUse));
            Assert.That(session.Player.Buffs.HasBuff(EffectId), Is.EqualTo(given));
        });
    }
}
