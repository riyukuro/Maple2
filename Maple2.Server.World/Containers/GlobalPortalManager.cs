using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Channel.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

public class GlobalPortalManager : IDisposable {
    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public required GameStorage GameStorage { get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { get; init; }
    public required ChannelClientLookup ChannelClients { get; init; }
    // ReSharper restore All
    // ReSharper restore UnusedAutoPropertyAccessor.Global
    #endregion

    public readonly GlobalPortal Portal;
    public int Channel;

    public readonly int[] RoomIds;
    private readonly object mutex = new();


    public GlobalPortalManager(GlobalPortalMetadata metadata, int id, long endTick) {
        Portal = new GlobalPortal(metadata, id) {
            EndTick = endTick,
        };
        RoomIds = new int[metadata.Entries.Length];
    }

    public void CreateFields() {
        if (!ChannelClients.TryGetClient(ChannelClients.FirstChannel(), out ChannelClient? client)) {
            return;
        }

        Channel = ChannelClients.FirstChannel();

        for (int i = 0; i < RoomIds.Length; i++) {
            try {
                TimeEventResponse? response = client.TimeEvent(new TimeEventRequest {
                    GetField = new TimeEventRequest.Types.GetField {
                        MapId = Portal.Metadata.Entries[i].MapId,
                        RoomId = 0,
                    },
                });

                if (response.Field == null) {
                    continue;
                }
                RoomIds[i] = response.Field.RoomId;
            } catch (RpcException rpcException) {
                // If the channel is not available, we cannot create the field.
                if (rpcException.StatusCode == StatusCode.Unavailable) {
                    Log.Error("Channel {Channel} is unavailable for creating global portal fields: {Message}", Channel, rpcException.Message);
                    return;
                }
                Log.Error(rpcException, "Error creating global portal field for channel {Channel}: {Message}", Channel, rpcException.Message);
            } catch (Exception e) {
                Log.Error(e, "Unexpected error creating global portal field for channel {Channel}", Channel);
            }
        }

        // Each channel on its own: one that is down does not keep the announce from the channels after it.
        foreach ((int channelId, ChannelClient channelClient) in ChannelClients) {
            try {
                channelClient.TimeEvent(new TimeEventRequest {
                    AnnounceGlobalPortal = new TimeEventRequest.Types.AnnounceGlobalPortal {
                        MetadataId = Portal.MetadataId,
                        EventId = Portal.Id,
                    },
                });
            } catch (Exception e) {
                Log.Error(e, "Global portal {PortalId}: announce not sent to channel {Channel}", Portal.MetadataId, channelId);
            }
        }
    }

    // The room of entry index a player joins, 0 when there is none. The entry's room takes players up to its map's
    // instancefield maxCount; at the cap, or when the room is gone (never made, or the channel no longer has it), a
    // new room is made and kept for the next joins. Joins run on the World service's threads, one at a time here,
    // so two joins at the cap do not each make a room (the later one overwriting the first, which went on filling).
    // A map without an instancefield row has no cap: its room takes everyone.
    public int Join(int mapId, int index) {
        if (!ChannelClients.TryGetClient(Channel, out ChannelClient? client)) {
            return 0;
        }

        lock (mutex) {
            try {
                FieldInfo? room = RoomIds[index] == 0 ? null : client.TimeEvent(new TimeEventRequest {
                    GetField = new TimeEventRequest.Types.GetField {
                        MapId = mapId,
                        RoomId = RoomIds[index],
                    },
                }).Field;
                bool full = room != null
                            && ServerTableMetadata.InstanceFieldTable.Entries.TryGetValue(mapId, out InstanceFieldMetadata? instanceField)
                            && GlobalPortalRules.RoomFull(room.PlayerIds.Count, instanceField.MaxCount);
                if (room == null || full) {
                    FieldInfo? created = client.TimeEvent(new TimeEventRequest {
                        GetField = new TimeEventRequest.Types.GetField {
                            MapId = mapId,
                            RoomId = 0,
                        },
                    }).Field;
                    if (created == null) {
                        Log.Error("Global portal {PortalId}: no room of map {MapId} made on channel {Channel}", Portal.MetadataId, mapId, Channel);
                        return 0;
                    }
                    RoomIds[index] = created.RoomId;
                }
                return RoomIds[index];
            } catch (Exception e) {
                Log.Error(e, "Global portal {PortalId}: join of map {MapId} failed on channel {Channel}", Portal.MetadataId, mapId, Channel);
                return 0;
            }
        }
    }

    public void Dispose() {
        // Each channel on its own, as the announce.
        foreach ((int channelId, ChannelClient channelClient) in ChannelClients) {
            try {
                channelClient.TimeEvent(new TimeEventRequest {
                    CloseGlobalPortal = new TimeEventRequest.Types.CloseGlobalPortal {
                        EventId = Portal.MetadataId,
                    },
                });
            } catch (Exception e) {
                Log.Error(e, "Global portal {PortalId}: close not sent to channel {Channel}", Portal.MetadataId, channelId);
            }
        }
    }
}
