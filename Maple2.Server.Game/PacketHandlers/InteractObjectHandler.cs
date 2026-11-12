using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class InteractObjectHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.InteractObject;

    private enum Command : byte {
        DebugCreate = 9,
        Start = 11,
        End = 12,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Start:
                HandleStart(session, packet);
                return;
            case Command.End:
                HandleEnd(session, packet);
                return;
            case Command.DebugCreate:
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }

                // Not handled: the client's object create dialog (uiobjectcreatedialog.cpp:156), a debug window
                // that asks for an object of a kind with four strings, a count, a position and a rotation.
                // GM accounts only (above), then read and logged.
                int kind = packet.ReadInt();
                packet.ReadUnicodeString();
                packet.ReadUnicodeString();
                packet.ReadUnicodeString();
                packet.ReadUnicodeString();
                int count = packet.ReadInt();
                packet.Read<System.Numerics.Vector3>(); // position
                packet.Read<System.Numerics.Vector3>(); // rotation
                Logger.Debug("InteractObject {Command} from {CharacterId}: kind {Kind}, count {Count}", command, session.CharacterId, kind, count);
                return;
        }
    }

    // Start and End carry a count, then that many object names (CInteractObject and StateInteraction senders).
    private void HandleStart(GameSession session, IByteReader packet) {
        // The names are kept: the end of a gathered object is taken only if this start named it.
        // The client starts only on an object within reach (interactobject.cpp:1075, stateinteractdata.cpp:283, 382).
        InteractRules.TakeStarts(session.InteractStarted, InteractRules.ReadNames(packet), name => {
            if (session.Field?.TryGetInteract(name, out FieldInteract? interact) != true) {
                return null;
            }
            return InReach(session, interact);
        });
    }

    private static bool InReach(GameSession session, FieldInteract interact) {
        float reach = interact.Value.Type == InteractType.Gathering
            ? session.ServerTableMetadata.ConstantsTable.MasteryObjectInteractionDistance
            : InteractRules.OtherReach;
        return InteractRules.InReach(session.Player.Position, interact.Position, reach);
    }

    private void HandleEnd(GameSession session, IByteReader packet) {
        // The end of an object that is not gathered names the object the player chose, which the start
        // (the nearest one) need not have named: it is tested for reach instead.
        List<string> ends = InteractRules.TakeEnds(session.InteractStarted, InteractRules.ReadNames(packet), name => {
            if (session.Field?.TryGetInteract(name, out FieldInteract? interact) != true) {
                return null;
            }
            return (interact.Value.Type == InteractType.Gathering, InReach(session, interact));
        });
        foreach (string name in ends) {
            End(session, name);
        }
    }

    // The interaction (5) and the result (13) as KMS2 sends them for the object's kind.
    private static void SendAnswer(GameSession session, FieldInteract interact) {
        (bool sendInteract, bool sendResult) = InteractRules.Answer(interact.Value.Type);
        if (sendInteract) {
            session.Send(InteractObjectPacket.Interact(interact));
        }
        if (sendResult) {
            // Result 0 for every kind. For a telescope the client itself shows the "new telescope" notice when the
            // object's row is not yet in its own list (CSceneStateGame::OnInteractObject), so the named code and
            // none are the same byte.
            InteractResult result = interact.Value.Type == InteractType.Telescope ? InteractResult.s_interact_find_new_telescope : InteractResult.none;
            session.Send(InteractObjectPacket.Result(result, interact));
        }
    }

    private void End(GameSession session, string entityId) {
        if (session.Field?.TryGetInteract(entityId, out FieldInteract? interact) != true) {
            return;
        }

        if (interact.Value.Type == InteractType.Gathering) {
            // A gathered object answers every end. One that cannot give yet (this character's respawn
            // has not passed) answers as a failed try with no count: chosen here.
            if (!interact.Gather(session.CharacterId)) {
                session.Send(InteractObjectPacket.Interact(interact, GatherResult.Fail, decreaseAmount: 0));
                session.Send(InteractObjectPacket.Result(InteractResult.none, interact));
                return;
            }
        } else if (InteractRules.KeepsState(interact.Value.Type)) {
            // A telescope or a UI object stays as it is: KMS2 sends no state for its end.
            if (interact.State != InteractState.Reactable) {
                return;
            }
        } else {
            // A map treasure chest drops its items before its state and result, as KMS2 sends them.
            if (interact.Chest != MapChestRules.Kind.None) {
                if (interact.State != InteractState.Reactable) {
                    return;
                }
                DropItems(session, interact);
            }
            if (!interact.React()) {
                return;
            }
        }

        // A golden chest returns with every set under the same id: it counts (the map's golden chest trophy) only
        // the first time this character opens it.
        bool firstOpen = interact.Chest == MapChestRules.Kind.Gold
                         && session.Player.Value.Unlock.InteractedObjects.Add(interact.Object.Id);
        if (MapChestRules.Counts(interact.Chest, firstOpen)) {
            session.ConditionUpdate(ConditionType.interact_object, codeLong: interact.Object.Id);
        }
        session.ConditionUpdate(ConditionType.interact_object_rep, codeLong: interact.Object.Id, targetLong: (long) interact.Value.Type);
        session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnInvestigate);
        // Spring Farm: each use of a scored interact object adds its score (set_interact_score names the object id).
        session.Field.SpringFarmInteracted(interact.Value.Id);

        switch (interact.Value.Type) {
            case InteractType.Mesh:
                SendAnswer(session, interact);
                // A map treasure chest: its exp, more for a golden chest this character opens the first time.
                if (interact.Chest != MapChestRules.Kind.None) {
                    session.Exp.AddExp(MapChestRules.Exp(interact.Chest, firstOpen));
                }
                break;
            case InteractType.Ui:
                SendAnswer(session, interact);
                break;
            case InteractType.Telescope:
                SendAnswer(session, interact);

                if (session.Player.Value.Unlock.InteractedObjects.Add(interact.Object.Id)) {
                    session.Exp.AddExp(ExpType.telescope);
                }
                break;
            case InteractType.Web:
            case InteractType.DisplayImage:
            case InteractType.GuildPoster:
            case InteractType.WatchTower:
                break;
            case InteractType.BillBoard: // AdBalloon
                session.Send(PlayerHostPacket.AdBalloonWindow((interact.Object as InteractBillBoardObject)!));
                break;
            case InteractType.Gathering:
                // The mastery, the drops, the interaction and its result, then the experience.
                session.Mastery.Gather(interact);
                if (session.Field.MapId != Constant.DefaultHomeMapId) {
                    session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnLifeSkillGather);
                }
                break;
        }

        if (interact.Chest == MapChestRules.Kind.None) {
            DropItems(session, interact);
        }

        foreach (InteractObjectMetadataEffect.InvokeEffect invokeEffect in interact.Value.AdditionalEffect.Invoke) {
            if (invokeEffect.Probability < 10000 && invokeEffect.Probability / 100 < Random.Shared.Next(100)) {
                continue;
            }

            session.Player.Buffs.AddBuff(session.Player, session.Player, invokeEffect.Id, invokeEffect.Level, session.Field.FieldTick, notifyField: true);
        }

        if (interact.Value.AdditionalEffect.ModifyCode > 0) {
            session.Buffs.ModifyDuration(interact.Value.AdditionalEffect.ModifyCode, interact.Value.AdditionalEffect.ModifyTime);
        }
    }

    private static void DropItems(GameSession session, FieldInteract interact) {
        if (session.Field == null) {
            return;
        }

        // Each box is rolled for the player (groups, drop counts, weights, job and gender), as an opened item box.
        // Drop.Rarity is the object's objectDropRank, not an item grade.
        var items = new List<Item>();
        foreach (int individualDropBoxId in interact.Value.Drop.IndividualDropBoxIds) {
            items.AddRange(session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, individualDropBoxId));
        }

        var globalDropBoxItems = new List<Item>();
        foreach (int globalDropBoxId in interact.Value.Drop.GlobalDropBoxIds) {
            globalDropBoxItems.AddRange(session.Field.ItemDrop.GetGlobalDropItems(globalDropBoxId, session.Player.Value.Character.Level));
        }

        foreach (Item item in globalDropBoxItems) {
            session.Field.DropItem(interact.Position with {
                Z = interact.Position.Z + interact.Value.Drop.DropHeight,
            }, interact.Rotation, item, characterId: session.CharacterId, dropKind: ItemDropKind.InteractObject);
        }

        foreach (Item item in items) {
            session.Field.DropItem(interact.Position with {
                Z = interact.Position.Z + interact.Value.Drop.DropHeight,
            }, interact.Rotation, item, characterId: session.CharacterId, dropKind: ItemDropKind.InteractObject);
        }
    }
}
