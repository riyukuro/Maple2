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

                // Not handled: the client's object create dialog. Read what it writes.
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
        // The names are kept: an end is taken only for an object this start named.
        session.InteractStarted.Clear();
        foreach (string name in InteractRules.ReadNames(packet)) {
            if (session.InteractStarted.Count >= InteractRules.MaxStarted) {
                break;
            }
            session.InteractStarted.Add(name);
        }
    }

    private void HandleEnd(GameSession session, IByteReader packet) {
        foreach (string name in InteractRules.ReadNames(packet)) {
            // One end per started object.
            if (InteractRules.CanEnd(session.InteractStarted, name) && session.InteractStarted.Remove(name)) {
                End(session, name);
            }
        }
    }

    private void End(GameSession session, string entityId) {
        if (session.Field?.TryGetInteract(entityId, out FieldInteract? interact) == true && interact.React()) {
            session.ConditionUpdate(ConditionType.interact_object, codeLong: interact.Object.Id);
            session.ConditionUpdate(ConditionType.interact_object_rep, codeLong: interact.Object.Id);
            session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnInvestigate);

            switch (interact.Value.Type) {
                case InteractType.Mesh:
                    session.Send(InteractObjectPacket.Interact(interact));
                    break;
                case InteractType.Telescope:
                    session.Send(InteractObjectPacket.Interact(interact));
                    session.Send(InteractObjectPacket.Result(InteractResult.s_interact_find_new_telescope, interact));

                    if (session.Player.Value.Unlock.InteractedObjects.Add(interact.Object.Id)) {
                        session.Exp.AddExp(ExpType.telescope);
                    }
                    break;
                case InteractType.Ui:
                    session.Send(InteractObjectPacket.Interact(interact));
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
                    session.Mastery.Gather(interact);
                    session.Send(InteractObjectPacket.Result(InteractResult.none, interact));
                    if (session.Field.MapId != Constant.DefaultHomeMapId) {
                        session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnLifeSkillGather);
                    }
                    break;
            }

            ICollection<Item> items = new List<Item>();
            if (interact.Value.Drop.IndividualDropBoxIds.Length > 0) {
                foreach (int individualDropBoxId in interact.Value.Drop.IndividualDropBoxIds) {
                    ICollection<Item> individualDropBoxItems = session.Field.ItemDrop.GetIndividualDropItems(individualDropBoxId, interact.Value.Drop.Rarity);
                    if (individualDropBoxItems.Count <= 0) {
                        continue;
                    }
                    items.Add(individualDropBoxItems.ElementAt(Random.Shared.Next(individualDropBoxItems.Count)));
                }
            }

            ICollection<Item> globalDropBoxItems = new List<Item>();
            foreach (int globalDropBoxId in interact.Value.Drop.GlobalDropBoxIds) {
                globalDropBoxItems = session.Field.ItemDrop.GetGlobalDropItems(globalDropBoxId, session.Player.Value.Character.Level);
            }

            foreach (Item item in globalDropBoxItems) {
                session.Field.DropItem(interact.Position with {
                    Z = interact.Position.Z + interact.Value.Drop.DropHeight,
                }, interact.Rotation, item, owner: interact, characterId: session.CharacterId);
            }

            foreach (Item item in items) {
                session.Field.DropItem(interact.Position with {
                    Z = interact.Position.Z + interact.Value.Drop.DropHeight,
                }, interact.Rotation, item, owner: interact, characterId: session.CharacterId);
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
    }
}
