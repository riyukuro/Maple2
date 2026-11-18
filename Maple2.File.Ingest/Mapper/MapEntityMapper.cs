using Maple2.Database.Context;
using Maple2.File.Flat;
using Maple2.File.Flat.maplestory2library;
using Maple2.File.Flat.standardmodellibrary;
using Maple2.File.Ingest.Helpers;
using Maple2.File.IO;
using Maple2.File.Parser.MapXBlock;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Tools.Extensions;
using System.Numerics;
using static M2dXmlGenerator.FeatureLocaleFilter;

namespace Maple2.File.Ingest.Mapper;

public class MapEntityMapper : TypeMapper<MapEntity> {
    private readonly HashSet<string> xBlocks;
    private readonly XBlockParser parser;
    private readonly Dictionary<int, List<ServerGroupSpawn.Npc>> groupSpawns;

    public MapEntityMapper(MetadataContext db, XBlockParser parser, M2dReader? serverReader = null) {
        xBlocks = db.MapMetadata.Select(metadata => metadata.XBlock).ToHashSet();
        groupSpawns = ServerGroupSpawn.Read(serverReader);

        this.parser = parser;
    }

    // An xblock index=value property as int pairs; entries that are not numbers are skipped.
    private static IEnumerable<(int Key, int Value)> IntPairs<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>>? entries) {
        foreach ((TKey key, TValue value) in entries ?? []) {
            if (int.TryParse(Convert.ToString(key), out int k) && int.TryParse(Convert.ToString(value), out int v)) {
                yield return (k, v);
            }
        }
    }

    private IEnumerable<MapEntity> ParseMap(string xblock, IEnumerable<IMapEntity> entities) {
        IMS2Bounding? firstBounding = null;
        IMS2Bounding? secondBounding = null;

        Dictionary<string, IMS2WayPoint> ms2WayPoints = new();
        IMapEntity[] mapEntities = entities as IMapEntity[] ?? entities.ToArray();
        // An npc is placed on the ground below its spawn point, 1 unit down, as the official server sends it (SpawnGround);
        // the colliders are read only for a map that has npc spawns.
        SpawnGround? spawnGround = null;
        Vector3 Grounded(Vector3 position) => (spawnGround ??= new SpawnGround(mapEntities)).Place(position);
        foreach (IMapEntity wayPoint in mapEntities) {
            switch (wayPoint) {
                case IMS2WayPoint ms2WayPoint:
                    ms2WayPoints.Add(ms2WayPoint.EntityId, ms2WayPoint);
                    break;
                default:
                    break;
            }
        }

        foreach (IMapEntity entity in mapEntities) {
            switch (entity) {
                case IMS2InteractObject interactObject:

                    switch (interactObject) {
                        case IMS2InteractActor interactActor:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new Ms2InteractActor(interactActor.interactID, interactActor.Position, interactActor.Rotation),
                            };
                            continue;
                        case IMS2InteractDisplay interactDisplay:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new Ms2InteractDisplay(interactDisplay.interactID, interactDisplay.Position, interactDisplay.Rotation),
                            };
                            continue;
                        case IMS2InteractMesh interactMesh:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new Ms2InteractMesh(interactMesh.interactID, interactMesh.Position, interactMesh.Rotation),
                            };
                            continue;
                        case IMS2SimpleUiObject simpleUiObject:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new Ms2SimpleUiObject(simpleUiObject.interactID, simpleUiObject.Position, simpleUiObject.Rotation),
                            };
                            continue;
                        case IMS2Telescope telescope:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new Ms2Telescope(telescope.interactID, telescope.Position, telescope.Rotation),
                            };
                            continue;
                    }
                    continue;
                case IPortal portal:
                    if (!FeatureEnabled(portal.feature) || !HasLocale(portal.locale)) continue;
                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new Portal(portal.PortalID, portal.TargetFieldSN, portal.TargetPortalID, (PortalType) portal.PortalType, (PortalActionType) portal.ActionType, portal.Position, portal.Rotation, portal.PortalDimension, portal.frontOffset, portal.RandomDestRadius, portal.IsVisible, portal.MinimapIconVisible, portal.PortalEnable, portal.PortalOffset),
                    };
                    continue;
                case ISpawnPoint spawn:
                    switch (spawn) {
                        case ISpawnPointPC pcSpawn:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new SpawnPointPC(pcSpawn.SpawnPointID, pcSpawn.Position, pcSpawn.Rotation, pcSpawn.IsVisible, pcSpawn.Enable),
                            };
                            continue;
                        case IEventSpawnGroupNPC groupSpawn:
                            // Npc groups instead of an npc list (ServerGroupSpawn); a point none of whose groups has an
                            // npc of its tag (02000328's empty one) spawns nothing and is left out.
                            List<SpawnGroupEntry> groups = ServerGroupSpawn.Groups(groupSpawns, groupSpawn.NpcGroupTag ?? string.Empty,
                                IntPairs(groupSpawn.NpcGroupCount), IntPairs(groupSpawn.NpcGroupProbability).ToDictionary(),
                                NpcMapper.NpcMetadataById.ContainsKey);
                            if (groups.Count == 0) {
                                continue;
                            }
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new EventSpawnGroupNPC(groupSpawn.EntityId, groupSpawn.SpawnPointID, Grounded(groupSpawn.Position), groupSpawn.Rotation, groupSpawn.IsVisible, groupSpawn.IsSpawnOnFieldCreate, groupSpawn.SpawnRadius, (int) groupSpawn.NpcCount, (int) groupSpawn.RegenCheckTime, (int) groupSpawn.LifeTime, groupSpawn.SpawnAnimation, groupSpawn.NpcGroupTag ?? string.Empty, groups),
                            };
                            continue;
                        case ISpawnPointNPC npcSpawn:
                            IList<SpawnPointNPCListEntry> npcList = npcSpawn.NpcList.Select(entry => {
                                if (!int.TryParse(entry.Key, out int npcId)) {
                                    return null;
                                }
                                if (!int.TryParse(entry.Value, out int npcCount)) {
                                    npcCount = 1;
                                }

                                NpcMapper.NpcMetadataById.TryGetValue(npcId, out NpcMetadata? npcMetadata);
                                if (npcMetadata == null) {
                                    return null;
                                }

                                // Some NPC spawns have these be equal, so default to 1
                                if (npcId == npcCount) {
                                    npcCount = 1;
                                }

                                return new SpawnPointNPCListEntry(npcId, npcCount);
                            }).WhereNotNull().ToList();
                            if (!KeepsNpcSpawn(npcSpawn is IEventSpawnPointNPC, npcSpawn.NpcCount, npcList.Count)) {
                                //Console.WriteLine($"No NPCs for {xblock}:{entity.EntityId}");
                                continue;
                            }

                            string? patrolData = SpawnPatrolData(npcSpawn.PatrolData);
                            switch (npcSpawn) {
                                case IEventSpawnPointNPC eventNpcSpawn:
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new EventSpawnPointNPC(npcSpawn.EntityId, npcSpawn.SpawnPointID, Grounded(npcSpawn.Position), npcSpawn.Rotation, npcSpawn.IsVisible, npcSpawn.IsSpawnOnFieldCreate, npcSpawn.SpawnRadius, (int) npcSpawn.NpcCount, npcList, (int) npcSpawn.RegenCheckTime, (int) eventNpcSpawn.LifeTime, eventNpcSpawn.SpawnAnimation, patrolData),
                                    };
                                    continue;
                                default:
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new SpawnPointNPC(npcSpawn.EntityId, npcSpawn.SpawnPointID, Grounded(npcSpawn.Position), npcSpawn.Rotation, npcSpawn.IsVisible, npcSpawn.IsSpawnOnFieldCreate, npcSpawn.SpawnRadius, npcList, (int) npcSpawn.RegenCheckTime, patrolData, (int) npcSpawn.NpcCount),
                                    };
                                    continue;
                            }
                        case IEventSpawnPointItem itemSpawn:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new EventSpawnPointItem(itemSpawn.SpawnPointID, itemSpawn.Position, itemSpawn.Rotation, itemSpawn.LifeTime, int.TryParse(itemSpawn.individualDropBoxId, out int individualDropBoxId) ? individualDropBoxId : 0, int.TryParse(itemSpawn.globalDropBoxId, out int globalDropBoxId) ? globalDropBoxId : 0, (int) itemSpawn.globalDropLevel, itemSpawn.IsVisible),
                            };
                            continue;
                    }
                    continue;
                case IMS2RegionSpawnBase spawn:
                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new Ms2RegionSpawn(spawn.SpawnPointID, spawn.UseRotAsSpawnDir, spawn.Position, spawn.Rotation),
                    };
                    continue;
                case IMS2TriggerObject triggerObject:
                    MapEntity? trigger = ParseTrigger(xblock, triggerObject);
                    if (trigger != null) {
                        yield return trigger;
                    }
                    continue;
                case IMS2RegionSkill skill:
                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new Ms2RegionSkill(skill.skillID, (short) skill.skillLevel, skill.Interval, skill.Position, skill.Rotation),
                    };
                    continue;
                // The client makes a breakable object of every MS2BreakableNIF (the moving platforms and boxes of the
                // maps, presets such as el_move_woodbox_B04_ derive it) and changes its state only from the server's
                // Breakable packet, so set_breakable / set_visible_breakable_object and a hit reach it only from here.
                case IMS2BreakableNIF nif: {
                        int.TryParse(nif.additionGlobalDropBoxId, out int globalDropBoxId);
                        yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                            Block = new Breakable(nif.IsVisible, (int) nif.TriggerBreakableID, nif.hideTimer, nif.resetTimer, globalDropBoxId, nif.moveTimeOffset, nif.Position, nif.Rotation,
                                nif.moveDistance, nif.ShapeDimensions),
                        };
                        continue;
                    }
                case IMS2TriggerModel triggerModel:
                    string name = Path.GetFileNameWithoutExtension(triggerModel.XmlFilePath);
                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new TriggerModel(triggerModel.TriggerModelID, name, triggerModel.Position, triggerModel.Rotation),
                    };
                    continue;
                case IMS2Bounding bounding:
                    if (firstBounding == null) {
                        firstBounding = bounding;
                        continue;
                    }
                    // Map 020000118 has 3 bounding boxes. Quick fix to ignore the 3rd.
                    if (secondBounding == null) {
                        secondBounding = bounding;
                        yield return new MapEntity(xblock, new Guid(entity.EntityId), $"{firstBounding.EntityName},{bounding.EntityName}") {
                            Block = new Ms2Bounding(firstBounding.Position, bounding.Position),
                        };
                    }
                    continue;
                case IMS2LiftableTargetBox liftableTargetBox:
                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new LiftableTargetBox(liftableTargetBox.Position, liftableTargetBox.Rotation, liftableTargetBox.isForceFinish, liftableTargetBox.liftableTarget),
                    };
                    continue;
                case IMS2MapProperties mapProperties:
                    switch (mapProperties) {
                        case IMS2PhysXProp physXProp:
                            if (mapProperties.IsObjectWeapon) {
                                int[] itemIds = physXProp.ObjectWeaponItemCode.Split(',').Select(int.Parse).ToArray();
                                if (physXProp.ObjectWeaponSpawnNpcCode == 0) {
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new ObjectWeapon(itemIds, (int) physXProp.ObjectWeaponRespawnTick, physXProp.ObjectWeaponActiveDistance, physXProp.Position, physXProp.Rotation),
                                    };
                                } else {
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new ObjectWeapon(itemIds, (int) physXProp.ObjectWeaponRespawnTick, physXProp.ObjectWeaponActiveDistance, physXProp.Position, physXProp.Rotation, (int) physXProp.ObjectWeaponSpawnNpcCode, (int) physXProp.ObjectWeaponSpawnNpcCount, physXProp.ObjectWeaponSpawnNpcRate, (int) physXProp.ObjectWeaponSpawnNpcLifeTick),
                                    };
                                }
                                continue;
                            }

                            switch (physXProp) {
                                case IMS2CubeProp { skillID: > 0, skillLevel: > 0 } cubeProp:
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new Ms2CubeSkill(cubeProp.skillID, (short) cubeProp.skillLevel, cubeProp.Position, cubeProp.Rotation),
                                    };
                                    continue;
                                case IMS2Liftable liftable:
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new Liftable((int) liftable.ItemID, liftable.ItemStackCount, liftable.ItemLifeTime, liftable.LiftableRegenCheckTime, liftable.LiftableFinishTime, liftable.MaskQuestID, liftable.MaskQuestState, liftable.EffectQuestID, liftable.EffectQuestState, liftable.IsReactEffect, liftable.Position, liftable.Rotation),
                                    };
                                    continue;
                                case IMS2TaxiStation taxiStation:
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new TaxiStation(taxiStation.Position, taxiStation.Rotation),
                                    };
                                    continue;
                                    // Intentionally do not parse IMS2Vibrate, there are 4M entries.
                                    // case IMS2Vibrate vibrate:
                            }
                            continue;
                    }
                    continue;
                case IActor actor: {
                        switch (actor) {
                            case IMS2BreakableActor breakable:
                                int.TryParse(breakable.additionGlobalDropBoxId, out int globalDropBoxId);
                                yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                    Block = new BreakableActor(actor.IsVisible, (int) breakable.TriggerBreakableID, breakable.hideTimer, breakable.resetTimer, globalDropBoxId, breakable.Position, breakable.Rotation, breakable.moveTimeOffset,
                                        breakable.moveDistance, breakable.ShapeDimensions),
                                };
                                continue;
                        }
                        continue;
                    }
                case IMS2PatrolData patrolData:
                    List<MS2WayPoint> wayPoints = [];
                    foreach (KeyValuePair<string, string> wayPointDict in patrolData.WayPoints) {
                        IMS2WayPoint? wayPoint = ms2WayPoints.GetValueOrDefault(wayPointDict.Value.Replace("-", string.Empty));
                        if (wayPoint is null) {
                            continue;
                        }

                        patrolData.ApproachAnims.TryGetValue(wayPointDict.Key, out string? approachAnimation);
                        patrolData.ArriveAnims.TryGetValue(wayPointDict.Key, out string? arriveAnimation);
                        patrolData.ArriveAnimsTime.TryGetValue(wayPointDict.Key, out uint arriveAnimationTime);
                        wayPoints.Add(new MS2WayPoint(wayPoint.EntityId, wayPoint.IsVisible, wayPoint.Position, wayPoint.Rotation, approachAnimation ?? "", arriveAnimation ?? "", (int) arriveAnimationTime, patrolData.IsAirWayPoint));
                    }


                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new MS2PatrolData(patrolData.EntityId, patrolData.EntityName, patrolData.IsAirWayPoint, (int) patrolData.PatrolSpeed, patrolData.IsLoop, wayPoints),
                    };
                    continue;
            }
        }

    }

    // Whether the ingest keeps an npc spawn point. NpcCount defaults to 0 in both flats (spawnpointnpc and
    // eventspawnpointnpc), and each NpcList value is that npc's count. An event point is spawned only by a trigger,
    // which names it to spawn its npcs: one left at 0 spawns its list (12 in the KR map files, among them 02000384_bf 402, the
    // barricade fight the script waits on, and 02000294_bf 3002/3009/3013). A plain point spawns at field create,
    // and the plain points at 0 are switched-off copies (02000487_bf lists 11003988 on a second point that is on), so
    // they stay out.
    public static bool KeepsNpcSpawn(bool eventPoint, uint npcCount, int listCount) {
        return listCount > 0 && (npcCount > 0 || eventPoint);
    }

    // A spawn point's PatrolData: the MS2PatrolData entity's uuid without dashes (MapEntity.Guid's form), or null for
    // the empty guid the points without a patrol carry.
    public static string? SpawnPatrolData(string? patrolData) {
        if (string.IsNullOrEmpty(patrolData) || patrolData == "00000000-0000-0000-0000-000000000000") {
            return null;
        }
        return patrolData.Replace("-", string.Empty);
    }

    private MapEntity? ParseTrigger(string xblock, IMS2TriggerObject trigger) {
        switch (trigger) {
            case IMS2TriggerActor actor:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerActor(actor.InitialSequence, actor.TriggerObjectID, actor.IsVisible),
                };
            case IMS2TriggerAgent agent:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerAgent(agent.TriggerObjectID, agent.IsVisible),
                };
            case IMS2TriggerBlock block:
                return null;
            case IMS2TriggerBox box:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerBox(box.Position, box.ShapeDimensions, box.TriggerObjectID, box.IsVisible, box.Rotation),
                };
            case IMS2TriggerCamera camera:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerCamera(camera.TriggerObjectID, camera.IsVisible),
                };
            case IMS2TriggerCube cube:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerCube(cube.TriggerObjectID, cube.IsVisible),
                };
            case IMS2TriggerEffect effect:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerEffect(effect.TriggerObjectID, effect.IsVisible),
                };
            case IMS2TriggerLadder ladder:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerLadder(ladder.TriggerObjectID, ladder.IsVisible),
                };
            case IMS2TriggerMesh mesh:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerMesh(mesh.Scale, mesh.TriggerObjectID, mesh.IsVisible, mesh.MinimapInVisible),
                };
            case IMS2TriggerPortal _:
                throw new InvalidOperationException("IMS2TriggerPortal should be parsed as IPortal.");
            case IMS2TriggerRope rope:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerRope(rope.TriggerObjectID, rope.IsVisible),
                };
            case IMS2TriggerSkill skill:
                if (skill.skillID <= 0 || skill.skillLevel <= 0) {
                    return null;
                }
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerSkill(skill.skillID, (short) skill.skillLevel, skill.Position, skill.Rotation, skill.TriggerObjectID, skill.IsVisible, skill.count),
                };
            case IMS2TriggerSound sound:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerSound(sound.TriggerObjectID, sound.IsVisible),
                };
        }

        // Generic MS2TriggerObject
        return null;
    }

    protected override IEnumerable<MapEntity> Map() {
        return parser.Parallel().SelectMany(map => {
            string xblock = map.xblock.ToLower();
            if (!xBlocks.Contains(xblock)) {
                return [];
            }

            return ParseMap(xblock, map.entities);
        }) // Ordering to ensure deterministic checksums.
        .OrderBy(entity => entity.XBlock)
        .ThenBy(entity => entity.Guid);
    }
}
