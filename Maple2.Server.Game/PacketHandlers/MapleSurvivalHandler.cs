using Maple2.Model.Enum;
using Maple2.Model.Game.Pvp;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;

namespace Maple2.Server.Game.PacketHandlers;

// The restored client's Maple Survival requests (op 0xC2). The match goes through the World's arena
// queue as arena MapleSurvivalRules.Arena, MapleSurvivalRules.PlayerCount players a match.
public class MapleSurvivalHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.MapleSurvival;

    private enum Command : byte {
        Queue = 0,
        Accept = 1,
        Cancel = 2,
        Refuse = 3,
        RegisterSquad = 4,
        AcceptReinvite = 6,
        EquipSkin = 8,
        Refresh = 0x12,
        BuyPass = 0x22,
        ClaimPass = 0x23,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        switch ((Command) packet.ReadByte()) {
            case Command.Queue:
                Queue(session);
                return;
            case Command.RegisterSquad:
                QueueSquad(session);
                return;
            case Command.Accept:
            case Command.AcceptReinvite:
                Accept(session);
                return;
            case Command.Cancel:
                PvpHandler.Request(session, new PvpRequest { RequesterId = session.CharacterId, ArenaUnregister = new PvpRequest.Types.ArenaUnregister() });
                session.Send(MapleSurvivalPacket.Idle());
                return;
            case Command.Refuse:
                packet.ReadByte(); // reason
                PvpHandler.Request(session, new PvpRequest { RequesterId = session.CharacterId, ArenaDecline = new PvpRequest.Types.ArenaDecline() });
                session.Send(MapleSurvivalPacket.Idle());
                return;
            case Command.EquipSkin: {
                    byte slot = packet.ReadByte();
                    int skinId = packet.ReadInt();
                    if (slot <= (byte) MedalType.Gliding && session.Survival.Equip((MedalType) slot, skinId)) {
                        session.Send(MapleSurvivalPacket.SkinEquipped(slot, skinId));
                    }
                    return;
                }
            case Command.Refresh:
                MapleSurvivalProgress.SendLobby(session);
                return;
            case Command.BuyPass: {
                    bool paid = packet.ReadBool();
                    long price = packet.ReadLong();
                    MapleSurvivalProgress.BuyPass(session, paid, price);
                    return;
                }
            case Command.ClaimPass: {
                    int free = packet.ReadInt();
                    int paid = packet.ReadInt();
                    MapleSurvivalProgress.ClaimPass(session, free, paid);
                    return;
                }
            default:
                // The season record (5) is not served: the client's RequestSeasonRecord (maplesurvival.cpp:382) has no
                // caller, and the lobby sends the season (0x16) and the record (0x17) by itself. The others are not served.
                return;
        }
    }

    // State 1 first, then the World queues the character; a refusal takes it back to state 0.
    private static void Queue(GameSession session) {
        if (!PvpHandler.CanLeave(session) || session.Field?.Arena != null) {
            session.Send(MapleSurvivalPacket.Idle());
            return;
        }
        session.Send(MapleSurvivalPacket.Matching());
        int players = MapleSurvivalRules.PlayerCount;
        int? error = PvpHandler.Request(session, new PvpRequest {
            RequesterId = session.CharacterId,
            ArenaRegister = new PvpRequest.Types.ArenaRegister { Arena = MapleSurvivalRules.Arena, PlayerCount = players, MinPlayers = players },
        })?.Error;
        if (error is not ((int) RankDuelError.none or (int) RankDuelError.Queued)) {
            session.Send(MapleSurvivalPacket.Idle());
        }
    }

    // The party chief registers its party as a squad (the pass window's squad mode; the client sends it only from the
    // chief of a party of fewer than SquadMax). The members wait and play as one unit (World ArenaRegister with the
    // members); each member's party window gets Party 0x37 from the World's notice. Refused (nothing queued): no party,
    // not the chief, too many members, a member offline, or the chief cannot leave its field.
    public const int SquadMax = 4;

    private static void QueueSquad(GameSession session) {
        Maple2.Model.Game.Party.Party? party = session.Party.Party;
        if (party == null || party.LeaderCharacterId != session.CharacterId || party.Members.Count is < 2 or > SquadMax
            || party.Members.Values.Any(member => !member.Info.Online)
            || !PvpHandler.CanLeave(session) || session.Field?.Arena != null) {
            session.Send(PartyPacket.SurvivalPartySearch(false, false));
            return;
        }
        int players = MapleSurvivalRules.PlayerCount;
        var register = new PvpRequest.Types.ArenaRegister { Arena = MapleSurvivalRules.Arena, PlayerCount = players, MinPlayers = players };
        register.Members.Add(session.CharacterId);
        register.Members.AddRange(party.Members.Keys.Where(id => id != session.CharacterId));
        int? error = PvpHandler.Request(session, new PvpRequest { RequesterId = session.CharacterId, ArenaRegister = register })?.Error;
        if (error is not (int) RankDuelError.none) {
            session.Send(PartyPacket.SurvivalPartySearch(false, false));
        }
    }

    private static void Accept(GameSession session) {
        if (session.Field?.Arena != null) {
            return;
        }
        if (!PvpHandler.CanLeave(session)) {
            session.Send(MapleSurvivalPacket.Idle());
            return;
        }
        PvpResponse? response = PvpHandler.Request(session, new PvpRequest { RequesterId = session.CharacterId, ArenaJoin = new PvpRequest.Types.ArenaJoin() });
        if (response is not { Error: (int) RankDuelError.none }) {
            session.Send(MapleSurvivalPacket.Idle());
            return;
        }
        PvpHandler.MoveToMatch(session, response, MapleSurvivalPacket.Idle());
    }

    // After the game server entry: the client's survival state (Survival 0x00 queued, 0x02 in a match, else 0x01).
    public static void SendState(GameSession session) {
        PvpResponse? response = PvpHandler.Request(session, new PvpRequest { RequesterId = session.CharacterId, QueueState = new PvpRequest.Types.QueueState() });
        if (response is { Arena: MapleSurvivalRules.Arena, QueueState: 1 }) {
            session.Send(MapleSurvivalPacket.Matching());
        } else if (response is { Arena: MapleSurvivalRules.Arena, QueueState: 2 } && session.Field?.Arena == null) {
            session.Send(MapleSurvivalPacket.Matched(session.CharacterId, false));
        } else {
            session.Send(MapleSurvivalPacket.Idle());
        }
    }
}
