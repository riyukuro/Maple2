using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    public void AllocateBattlefieldPoints(int boxId, int points) {
        DebugLog("[AllocateBattlefieldPoints] boxId:{BoxId}, points:{Points}", boxId, points);
    }

    public void Announce(int type, string content, bool arg3) {
        DebugLog("[Announce] type:{Type}, content:{Content}", type, content);
    }

    public void ChangeBackground(string dds) {
        DebugLog("[ChangeBackground] dds:{Dds}", dds);
        Field.SetBackground(dds);
    }

    public void EnableLocalCamera(bool enabled) {
        DebugLog("[EnableLocalCamera] enabled:{Enabled}", enabled);
        Field.AddFieldProperty(new FieldPropertyLocalCamera { Enabled = enabled });
    }

    public void LockMyPc(bool isLock) {
        DebugLog("[LockMyPc] isLock:{IsLock}", isLock);
        if (isLock) {
            Field.AddFieldProperty(new FieldPropertyLockPlayer());
        } else {
            Field.RemoveFieldProperty(FieldProperty.LockPlayer);
        }
    }

    public void SetAmbientLight(Vector3 primary, Vector3 secondary, Vector3 tertiary) {
        DebugLog("[SetAmbientLight] color:{Color}", primary);
        Field.AddFieldProperty(new FieldPropertyAmbientLight { Color = primary });
    }

    public void SetDirectionalLight(Vector3 diffuseColor, Vector3 specularColor) {
        DebugLog("[SetDirectionalLight] diffuseColor:{DiffuseColor}, specularColor:{SpecularColor}", diffuseColor, specularColor);
        Field.AddFieldProperty(new FieldPropertyDirectionalLight {
            DiffuseColor = diffuseColor,
            SpecularColor = specularColor,
        });
    }

    public void SetGravity(float gravity) {
        DebugLog("[SetGravity] gravity:{Gravity}", gravity);
        Field.AddFieldProperty(new FieldPropertyGravity(gravity));
    }

    public void SetPhotoStudio(bool enabled) {
        DebugLog("[SetPhotoStudio] enabled:{Enabled}", enabled);
        Field.AddFieldProperty(new FieldPropertyPhotoStudio { Enabled = enabled });
    }

    public void UserTagSymbol(string symbol1, string symbol2) {
        DebugLog("[UserTagSymbol] symbol1:{Symbol1}, symbol2:{Symbol2}", symbol1, symbol2);
        Broadcast(FieldPropertyPacket.Add(new FieldPropertyUserTagSymbol { Symbol1 = symbol1, Symbol2 = symbol2 }));
    }

    public void VisibleMyPc(bool visible) {
        DebugLog("[VisibleMyPc] visible:{Visible}", visible);
        if (visible) {
            Field.RemoveFieldProperty(FieldProperty.HidePlayer);
        } else {
            Field.AddFieldProperty(new FieldPropertyHidePlayer());
        }
    }

    public void SetWeather(Weather weather) {
        DebugLog("[SetWeather] weatherType:{Type}", weather);
        Field.AddFieldProperty(new FieldPropertyWeather { WeatherType = (WeatherType) weather });
    }

    public void SightRange(bool enabled, int range, int rangeZ, int border) {
        DebugLog("[SightRange] enabled:{Enabled}, range:{Range}, rangeZ:{RangeZ}, border:{Border}", enabled, range, rangeZ, border);
        var sightRange = (Field.GetFieldProperty(FieldProperty.SightRange) as FieldPropertySightRange)!;
        for (int i = 0; i < range; i++) {
            sightRange.Fades[i] = rangeZ;
        }

        sightRange.Opacity = (byte) border;
        sightRange.Opaque = false;
        sightRange.Unknown = enabled;
        Broadcast(FieldPropertyPacket.Add(sightRange));
    }

    // The PvP zone of a map script. Its type is the client's PvP mode (CPvPInfo m_nMode): 1 the Red Arena's face-offs,
    // 2 the free-for-all of the Crimson Tear Mine, 3 Treasure Island's teams (RedArenaZoneField, PvpZoneField,
    // TeamZoneField), 4 the guild championship's rounds in the guild arena 64000001 (GuildPvpZoneField), 5 and 6
    // 61000035's one punch rounds (PvpZoneField in that mode).
    public void SetPvpZone(int boxId, int prepareTime, int matchTime, int additionalEffectId, int type, params int[] boxIds) {
        if (!Field.StartPvpZone(type, boxId, prepareTime, matchTime, additionalEffectId, boxIds, box => PlayersInBox(box))) {
            ErrorLog("[SetPvpZone] boxId:{BoxId}, prepareTime:{PrepareTime}, matchTime:{MatchTime}, additionalEffectId:{AdditionalEffectId}, type:{Type}, boxIds:{BoxIds}",
                boxId, prepareTime, matchTime, additionalEffectId, type, string.Join(", ", boxIds));
            return;
        }
        DebugLog("[SetPvpZone] boxId:{BoxId}, prepareTime:{PrepareTime}, matchTime:{MatchTime}, additionalEffectId:{AdditionalEffectId}, type:{Type}",
            boxId, prepareTime, matchTime, additionalEffectId, type);
    }

    public void SetTimeScale(bool enabled, float startScale, float endScale, float duration, int interpolator) {
        DebugLog("[SetTimeScale] enabled:{Enabled}, startScale:{StartScale}, endScale:{EndScale}, duration:{Duration}, interpolator:{Interpolator}",
            enabled, startScale, endScale, duration, interpolator);
        // TODO: Does this need to be persisted on the field?
        Broadcast(FieldPropertyPacket.TimeScale(enabled, startScale, endScale, duration, (byte) interpolator));
    }

    public void SetLocalCamera(int cameraId, bool enabled) {
        DebugLog("[SetLocalCamera] cameraId:{Id}, enabled:{Enabled}", cameraId, enabled);
        Broadcast(CameraPacket.Local(cameraId, enabled));
    }

    public void ResetCamera(float interpolationTime) {
        DebugLog("[CameraReset] interpolationTime:{Time}", interpolationTime);
        Broadcast(CameraPacket.Interpolate(interpolationTime));
    }

    public void SelectCamera(int triggerId, bool enabled) {
        DebugLog("[CameraSelect] triggerId:{Id}, enabled:{Enabled}", triggerId, enabled);
        if (!Objects.Cameras.TryGetValue(triggerId, out TriggerObjectCamera? camera)) {
            return;
        }

        camera.Visible = enabled;
        Broadcast(TriggerPacket.Update(camera));
    }

    public void SelectCameraPath(int[] pathIds, bool returnView) {
        DebugLog("[CameraSelectPath] pathIds:{Ids}, returnView:{ReturnView}", string.Join(", ", pathIds), returnView);
        Broadcast(TriggerPacket.CameraStart(pathIds, returnView));
    }

    #region Entities
    public void SetActor(int triggerId, bool visible, string initialSequence, bool arg4, bool arg5) {
        DebugLog("[SetActor] triggerId:{Id}, visible:{Visible}, initialSequence:{InitialSequence}, arg4:{Arg4}, arg5:{Arg5}",
            triggerId, visible, initialSequence, arg4, arg5);
        if (!Objects.Actors.TryGetValue(triggerId, out TriggerObjectActor? actor)) {
            return;
        }

        actor.Visible = visible;
        actor.SequenceName = initialSequence;
        Broadcast(TriggerPacket.Update(actor));
    }

    public void SetAgent(int[] triggerIds, bool visible) {
        WarnLog("[SetAgent] triggerIds:{Ids}, visible:{Visible}", string.Join(", ", triggerIds), visible);
        foreach (int triggerId in triggerIds) {
            if (!Objects.Agents.TryGetValue(triggerId, out TriggerObjectAgent? agent)) {
                continue;
            }

            agent.Visible = visible;
            Broadcast(TriggerPacket.Update(agent));
        }
    }

    public void SetBreakable(int[] triggerIds, bool enabled) {
        DebugLog("[SetBreakable] triggerIds:{Ids}, enabled:{Enabled}", string.Join(", ", triggerIds), enabled);
        // A changed state sends its own Update. The client replays a state's effect and animation on every packet
        // (CBreakableObject::SetState, breakableobject.cpp), so no second batch is sent, and nothing for a breakable
        // already in the state.
        foreach (int triggerId in triggerIds) {
            if (!Field.TryGetBreakable(triggerId, out FieldBreakable? breakable)) {
                continue;
            }

            breakable.UpdateState(enabled ? BreakableState.Show : BreakableState.Hide);
        }
    }

    public void SetCube(int[] triggerIds, bool visible, int randomCount) {
        DebugLog("[SetCube] triggerIds:{Ids}, visible:{Visible}, randomCount:{Count}", string.Join(", ", triggerIds), visible, randomCount);
        int count = triggerIds.Length;
        if (randomCount > 0 && randomCount < triggerIds.Length) {
            count = randomCount;
            Random.Shared.Shuffle(triggerIds);
        }

        for (int i = 0; i < count; i++) {
            if (!Objects.Cubes.TryGetValue(triggerIds[i], out TriggerObjectCube? cube)) {
                continue;
            }
            if (cube.Visible == visible) {
                continue;
            }

            cube.Visible = visible;
            Broadcast(TriggerPacket.Update(cube));
        }
    }

    public void SetEffect(int[] triggerIds, bool visible, int startDelay, int interval) {
        DebugLog("[SetEffect] triggerIds:{Ids}, visible:{Visible}, startDelay:{StartDelay}, interval:{Interval}", string.Join(", ", triggerIds), visible, startDelay, interval);
        for (int i = 0; i < triggerIds.Length; i++) {
            if (!Objects.Effects.TryGetValue(triggerIds[i], out TriggerObjectEffect? effect)) {
                continue;
            }

            int delay = TriggerStagger.Delay(i, startDelay, interval);
            if (delay > 0) {
                Events.Schedule(() => UpdateEffect(effect), TimeSpan.FromMilliseconds(delay));
            } else {
                UpdateEffect(effect);
            }
        }

        void UpdateEffect(TriggerObjectEffect effect) {
            if (effect.Visible == visible) {
                return;
            }

            effect.Visible = visible;
            Broadcast(TriggerPacket.Update(effect));
        }
    }

    public void SetInteractObject(int[] interactIds, int stateValue, bool arg3, bool arg4) {
        var state = (InteractState) stateValue;
        DebugLog("[SetInteractObject] interactIds:{Ids}, state:{State}, arg3:{Arg3}, arg4:{Arg4}", string.Join(", ", interactIds), state, arg3, arg4);
        foreach (FieldInteract interact in Field.EnumerateInteract()) {
            if (interactIds.Contains(interact.Value.Id)) {
                interact.SetState(state);
            }
        }
    }

    public void SetLadder(int[] triggerIds, bool visible, bool animationEffect, int animationDelay) {
        DebugLog("[SetLadder] triggerIds:[{Id}], visible:{Visible}, animationEffect:{Effect}, animationDelay:{Delay}",
            string.Join(",", triggerIds), visible, animationEffect, animationDelay);
        // A missing id skips that ladder only; the ones after it are still set.
        foreach (TriggerObjectLadder ladder in PresentObjects(Objects.Ladders, triggerIds)) {
            ladder.Visible = visible;
            ladder.Animate = animationEffect;
            ladder.Delay = animationDelay;
            Broadcast(TriggerPacket.Update(ladder));
        }
    }

    public static IEnumerable<T> PresentObjects<T>(IReadOnlyDictionary<int, T> objects, int[] triggerIds) {
        foreach (int triggerId in triggerIds) {
            if (objects.TryGetValue(triggerId, out T? value)) {
                yield return value;
            }
        }
    }

    public void SetMesh(int[] triggerIds, bool visible, int delay, int interval, float fade, string desc) {
        DebugLog("[SetMesh] triggerIds:{Ids}, visible:{Visible}, delay:{Delay}, interval:{Interval}, scale:{Scale}",
            string.Join(", ", triggerIds), visible, delay, interval, fade);
        UpdateMesh(triggerIds, visible, delay, interval, (int) fade);
    }

    // examples: arg3=200, arg4=3
    public void SetMeshAnimation(int[] triggerIds, bool visible, int startDelay, int interval) {
        DebugLog("[SetMeshAnimation] triggerIds:{Ids}, visible:{Visible}, startDelay:{StartDelay}, interval:{Interval}", string.Join(", ", triggerIds), visible, startDelay, interval);
        foreach (int triggerId in triggerIds) {
            if (!Objects.Meshes.TryGetValue(triggerId, out TriggerObjectMesh? mesh)) {
                continue;
            }
            if (mesh.Visible == visible) {
                continue;
            }

            mesh.Visible = visible;
            mesh.Fade = startDelay;
            Broadcast(TriggerPacket.Update(mesh));
        }
    }

    public void SetPortal(int portalId, bool visible, bool enabled, bool minimapVisible, bool arg5) {
        DebugLog("[SetPortal] portalId:{Id}, visible:{Visible}, enabled:{Enabled}, minimapVisible:{MinimapVisible}, arg5:{Arg5}",
            portalId, visible, enabled, minimapVisible, arg5);
        if (!Field.TryGetPortal(portalId, out FieldPortal? portal)) {
            return;
        }

        portal.Visible = visible;
        portal.Enabled = enabled;
        portal.MinimapVisible = minimapVisible;
        Broadcast(PortalPacket.Update(portal));
    }

    public void SetRandomMesh(int[] triggerIds, bool visible, int startDelay, int interval, int fade) {
        DebugLog("[SetRandomMesh] triggerIds:{Ids}, visible:{Visible}, startDelay:{StartDelay}, interval:{Interval}, fade:{Fade}",
            string.Join(", ", triggerIds), visible, startDelay, interval, fade);
        Random.Shared.Shuffle(triggerIds);
        UpdateMesh(triggerIds, visible, startDelay, interval, fade);
    }

    private void UpdateMesh(ArraySegment<int> triggerIds, bool visible, int delay, int interval, int fade = 0) {
        int intervalTotal = 0;
        // A trigger that names a mesh range the map lacks is one warning per call, not one per mesh id.
        List<int>? missing = null;
        foreach (int triggerId in triggerIds) {
            if (!Objects.Meshes.TryGetValue(triggerId, out TriggerObjectMesh? mesh)) {
                (missing ??= []).Add(triggerId);
                continue;
            }
            if (mesh.Visible == visible) {
                continue;
            }

            if (interval > 0) {
                intervalTotal += interval;
                Events.Schedule(() => UpdateSetMesh(mesh), TimeSpan.FromMilliseconds(intervalTotal + delay));
            } else {
                UpdateSetMesh(mesh);
            }
        }

        if (missing != null) {
            missing.Sort();
            logger.Warning("Invalid mesh: {Count} of {Total} ids not on the map: {Ids}", missing.Count, triggerIds.Count, MeshIdList(missing));
        }

        void UpdateSetMesh(TriggerObjectMesh mesh) {
            mesh.Visible = visible;
            mesh.Fade = fade;
            Broadcast(TriggerPacket.Update(mesh));
            // TODO: Should Fade be reset after sending packet?
        }
    }

    // Ids as ranges: "6009, 6018-6030, 6051-6200".
    public static string MeshIdList(IReadOnlyList<int> ids) {
        var parts = new List<string>();
        for (int i = 0; i < ids.Count;) {
            int j = i;
            while (j + 1 < ids.Count && ids[j + 1] == ids[j] + 1) {
                j++;
            }
            parts.Add(i == j ? $"{ids[i]}" : $"{ids[i]}-{ids[j]}");
            i = j + 1;
        }
        return string.Join(", ", parts);
    }

    public void SetRope(int triggerId, bool visible, bool animationEffect, int animationDelay) {
        DebugLog("[SetRope] triggerId:{Id}, visible:{Visible}, animationEffect:{Effect}, animationDelay:{Delay}",
            triggerId, visible, animationEffect, animationDelay);
        if (!Objects.Ropes.TryGetValue(triggerId, out TriggerObjectRope? rope)) {
            return;
        }

        rope.Visible = visible;
        rope.Animate = animationEffect;
        rope.Delay = animationDelay;
        Broadcast(TriggerPacket.Update(rope));
    }

    public void SetSkill(int[] triggerIds, bool enabled) {
        DebugLog("[SetSkill] triggerIds:{Ids}, enabled:{Enabled}", string.Join(", ", triggerIds), enabled);
        foreach (int triggerId in triggerIds) {
            if (enabled) {
                Ms2TriggerSkill? skill = Field.Entities.Trigger.Skills.FirstOrDefault(x => x.TriggerId == triggerId);
                if (skill == null) {
                    continue;
                }

                Field.AddSkill(skill, (int) TimeSpan.FromMilliseconds(150).TotalMilliseconds, skill.Position, skill.Rotation, triggerId);
            } else {
                Field.RemoveSkillByTriggerId(triggerId);
            }
        }
    }

    public void SetSound(int triggerId, bool enabled) {
        DebugLog("[SetSound] triggerId:{Id}, enabled:{Enabled}", triggerId, enabled);
        if (!Objects.Sounds.TryGetValue(triggerId, out TriggerObjectSound? sound)) {
            return;
        }

        sound.Visible = enabled;
        Broadcast(TriggerPacket.Update(sound));
    }

    public void SetVisibleBreakableObject(int[] triggerIds, bool visible) {
        DebugLog("[SetVisibleBreakableObject] triggerIds:{Ids}, visible:{Visible}", string.Join(", ", triggerIds), visible);
        var updated = new List<FieldBreakable>(triggerIds.Length);
        foreach (int triggerId in triggerIds) {
            if (!Field.TryGetBreakable(triggerId, out FieldBreakable? breakable)) {
                continue;
            }

            breakable.Visible = visible;
            updated.Add(breakable);
        }

        Field.Broadcast(BreakablePacket.Update(updated));
    }

    public void AddBuff(int[] boxIds, int buffId, int level, bool ignorePlayer, bool isSkillSet, string feature) {
        DebugLog("[AddBuff] boxIds:{Ids}, buffId:{BuffId}, level:{Level}, ignorePlayer:{ignorePlayer}, isSkillSet:{Arg5}, feature:{Feature}",
            string.Join(", ", boxIds), buffId, level, ignorePlayer, isSkillSet, feature);
        // The skill-set flag (arg5) is not applied. The KR client takes a skill set only from an additional effect's
        // own level data (its skill set id: AddSkillSet / ApplySkillSet, additionalcli_2.cpp), and the ids the scripts
        // pass with the flag are mostly no skill set of table/skillset.xml: 70000040 (12 uses, the rank duel and Red
        // Arena floors every second) would block the job skills (its skillUseDisable mask 62) of everyone fighting
        // there. Kept quiet (Debug) since the arenas run it once a second.
        if (isSkillSet) {
            logger.Debug("[AddBuff] skill set {BuffId} on boxes {Ids} not applied", buffId, string.Join(", ", boxIds));
            return;
        }

        long startTick = Field.FieldTick;
        if (!ignorePlayer) {
            foreach (IActor player in PlayersInBox(boxIds)) {
                player.Buffs.AddBuff(Field.FieldActor, player, buffId, (short) level, startTick);
            }
        } else {
            foreach (IActor monster in MonstersInBox(boxIds)) {
                monster.Buffs.AddBuff(Field.FieldActor, monster, buffId, (short) level, startTick);
            }
        }
    }

    // The third argument is is_player (arg3), not add_buff's ignore_player: the 17 KR removes that set it take off
    // effects the players hold. 10 undo an add_buff to players in the same map (70002151, 70002122 in 02020101 and
    // 02020111), deathflower's 62100021-62100023 come from random_additional_effect on pc targets, and 73000004 is
    // the players' mount interact of 02020301/52100301. The 96 removes without it that pair with an add_buff undo one
    // to players too, and none pairs with an add_buff to monsters, so either way the players in the box lose it.
    public void RemoveBuff(int boxId, int buffId, bool isPlayer) {
        DebugLog("[RemoveBuff] boxId:{Id}, buffId:{BuffId}, isPlayer:{IsPlayer}", boxId, buffId, isPlayer);
        foreach (IActor player in PlayersInBox(boxId)) {
            player.Buffs.Remove(buffId, Field.FieldActor.ObjectId);
        }
    }

    public void CreateItem(int[] spawnIds, int triggerId, int itemId, int arg5) {
        DebugLog("[CreateItem] spawnIds:{Ids}, triggerId:{TriggerId}, itemId:{ItemId}, arg5:{Arg5}", string.Join(", ", spawnIds), triggerId, itemId, arg5);

        foreach (int spawnId in spawnIds) {
            ICollection<Item> items = new List<Item>();
            if (itemId != 0) {
                Item? item = Field.ItemDrop.CreateItem(itemId);
                if (item != null) {
                    items.Add(item);
                }
            }

            if (!Field.Entities.EventItemSpawns.TryGetValue(spawnId, out EventSpawnPointItem? spawn)) {
                continue;
            }

            if (spawn.IndividualDropBoxId > 0) {
                items = items.Concat(Field.ItemDrop.GetIndividualDropItems(spawn.IndividualDropBoxId)).ToList();
            }

            if (spawn.GlobalDropBoxId > 0) {
                items = items.Concat(Field.ItemDrop.GetGlobalDropItems(spawn.GlobalDropBoxId, spawn.GlobalDropLevel)).ToList();
            }

            foreach (Item item in items) {
                Field.DropItem(spawn.Position, spawn.Rotation, item, characterId: 0, fixedPosition: true,
                    dropKind: ItemDropKind.Trigger, msDuration: FieldItem.SpawnPointDurationMs(spawn.Lifetime));
            }
        }
    }

    public void SpawnItemRange(int[] rangeIds, int randomPickCount) {
        DebugLog("[SpawnItemRange] rangeIds:{Ids}, randomPickCount:{Count}", string.Join(", ", rangeIds), randomPickCount);
        Random.Shared.Shuffle(rangeIds);
        int[] pickedIds = rangeIds.Take(randomPickCount).ToArray();
        int sent = 0;
        Vector3? firstPosition = null;

        foreach (int spawnIds in pickedIds) {
            if (!Field.Entities.EventItemSpawns.TryGetValue(spawnIds, out EventSpawnPointItem? spawn)) {
                continue;
            }

            ICollection<Item> items = new List<Item>();
            if (spawn.IndividualDropBoxId > 0) {
                items = items.Concat(Field.ItemDrop.GetIndividualDropItems(spawn.IndividualDropBoxId)).ToList();
            }

            if (spawn.GlobalDropBoxId > 0) {
                items = items.Concat(Field.ItemDrop.GetGlobalDropItems(spawn.GlobalDropBoxId, spawn.GlobalDropLevel)).ToList();
            }

            foreach (Item item in items) {
                Field.DropItem(spawn.Position, spawn.Rotation, item, characterId: 0, fixedPosition: true,
                    dropKind: ItemDropKind.Trigger, msDuration: FieldItem.SpawnPointDurationMs(spawn.Lifetime));
                sent++;
                firstPosition ??= spawn.Position;
            }
        }

        // Logged in every build (not only TRIGGER_DEBUG), so a Release run shows what the drops were.
        logger.Information("[SpawnItemRange] map {MapId}, trigger {Trigger}: picked {Picked}, sent {Sent} items, first at {Position}, players {Players}",
            Field.MapId, owner.Value.Name, string.Join(",", pickedIds), sent, firstPosition, Field.Players.Count);
    }

    public void StartCombineSpawn(int[] groupIds, bool isStart) {
        DebugLog("[StartCombineSpawn] groupIds:{Ids}, isStart:{IsStart}", string.Join(", ", groupIds), isStart);
        CombineSpawnTable table = Field.ServerTableMetadata.CombineSpawnTable;
        var npcSpawns = Field.Entities.NpcSpawns.Select(spawn => spawn.SpawnPointId).ToHashSet();
        var missing = new List<int>();
        foreach (int groupId in groupIds) {
            CombineSpawnRules.Find(table, Field.MapId, groupId, npcSpawns.Contains, Field.Entities.RegionSpawns.ContainsKey, out SpawnGroupMetadata? group);
            if (group == null) {
                missing.Add(groupId);
                continue;
            }

            Field.ToggleCombineSpawn(group, isStart);
        }
        if (missing.Count > 0) {
            WarnLog("[StartCombineSpawn] {Count} groups not in the table for this field: {Ids}", missing.Count, string.Join(", ", missing));
        }
    }

    public void SetTimer(string timerId, int seconds, bool autoRemove, bool display, int vOffset, string type, string desc) {
        DebugLog("[SetTimer] timerId:{Id}, seconds:{Seconds}", timerId, seconds);
        Field.Timers[timerId] = new TickTimer(seconds * 1000, autoRemove, vOffset, display, type);
        if (display) {
            Broadcast(TriggerPacket.TimerDialog(Field.Timers[timerId]));
        }
    }

    public void ResetTimer(string timerId) {
        DebugLog("[ResetTimer] timerId:{Id}", timerId);
        if (Field.Timers.TryGetValue(timerId, out TickTimer? timer)) {
            timer.Reset();
            if (timer.Display) {
                Broadcast(TriggerPacket.TimerDialog(timer));
            }
        }
    }

    public void RoomExpire() {
        int count = RoomExpiry.ReturnAll(Field.Players.Values, player => {
            RoomExpiry.DropOwnReturn(player.Value.Character.ReturnMaps, Field.MapId, Constant.DefaultReturnMapId);
            player.Session.ReturnField();
        });
        DebugLog("[RoomExpire] returned:{Count}", count);
    }

    public void FieldWarEnd(bool isClear) {
        // The war's rooms close by their own triggers (FieldGameStart 2 moves the players out); the end moves no one.
        int rewardId = Field.EndFieldWar(isClear);
        DebugLog("[FieldWarEnd] isClear:{IsClear}, reward:{RewardId}", isClear, rewardId);
    }
    #endregion

    #region Conditions
    public bool DetectLiftableObject(int[] boxIds, int itemId, bool negate) {
        DebugLog("[DetectLiftableObject] boxIds:{Ids}, itemId:{ItemId}", string.Join(", ", boxIds), itemId);

        if (itemId == 0) {
            return negate;
        }

        IEnumerable<TriggerBox> boxes = boxIds
            .Select(boxId => Objects.Boxes.GetValueOrDefault(boxId))
            .Where(box => box != null)!;

        // Gets the list of valid fieldliftables, only check ones that are placed
        var liftables = Field.EnumerateLiftables().Where(x => x.Value.ItemId == itemId && (x.State == LiftableState.Default || x.State == LiftableState.Disabled));
        foreach (FieldLiftable liftable in liftables) {
            if (boxes.Any(box => box.Contains(liftable.Position))) {
                return !negate;
            }
        }

        return negate;
    }

    public bool ObjectInteracted(int[] interactIds, int stateValue) {
        var state = (InteractState) stateValue;
        DebugLog("[ObjectInteracted] interactIds:{Ids}, state:{State}", string.Join(", ", interactIds), state);
        foreach (FieldInteract interact in Field.EnumerateInteract()) {
            if (interactIds.Contains(interact.Value.Id) && interact.State == state) {
                return true;
            }
        }

        return false;
    }

    public bool PvpZoneEnded(int boxId) {
        return Field.PvpZone is { } zone && zone.BoxId == boxId && zone.Ended;
    }

    public bool TimeExpired(string timerId) {
        DebugLog("[TimeExpired] timerId:{TimerId}", timerId);
        if (Field.Timers.TryGetValue(timerId, out TickTimer? timer)) {
            return timer.Expired();
        }

        return true;
    }
    #endregion
}
