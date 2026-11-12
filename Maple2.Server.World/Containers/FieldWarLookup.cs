using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Channel.Service;
using FieldWarInfo = Maple2.Server.World.Service.FieldWarInfo;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

// The open field war (one at a time; the two KR events run an hour apart for 5 minutes). Its rooms are on the
// first channel; a room takes players for the constant FieldWarInstanceEnterableDurationSeconds from its first
// join, and up to the map's instancefield maxCount (FieldWarRules.NeedsNewRoom).
public class FieldWarLookup {
    private readonly ChannelClientLookup channelClients;
    private readonly ServerTableMetadataStorage serverTableMetadata;
    private readonly ILogger logger = Log.ForContext<FieldWarLookup>();
    private readonly object mutex = new();

    private OpenWar? open;
    // The uid the client gets in the announce (and shows nowhere): unique for the World server's run.
    private long nextUid = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private sealed class OpenWar(long uid, FieldWarMetadata metadata, int channel) {
        public readonly long Uid = uid;
        public readonly FieldWarMetadata Metadata = metadata;
        public readonly int Channel = channel;
        public int RoomId;
        public long OpenedTick;
        public int Joined;
    }

    public FieldWarLookup(ChannelClientLookup channelClients, ServerTableMetadataStorage serverTableMetadata) {
        this.channelClients = channelClients;
        this.serverTableMetadata = serverTableMetadata;
    }

    public FieldWarInfo? Get() {
        lock (mutex) {
            return open == null ? null : new FieldWarInfo {
                EventId = open.Uid,
                FieldWarId = open.Metadata.Id,
                Channel = open.Channel,
                MapId = open.Metadata.MapId,
            };
        }
    }

    // Opens the war and announces it on every channel. Returns its uid.
    public long Open(FieldWarMetadata war) {
        long uid;
        lock (mutex) {
            uid = ++nextUid;
            open = new OpenWar(uid, war, channelClients.FirstChannel());
        }
        foreach ((int channelId, ChannelClient client) in channelClients) {
            try {
                client.TimeEvent(new TimeEventRequest {
                    AnnounceFieldWar = new TimeEventRequest.Types.AnnounceFieldWar {
                        FieldWarId = war.Id,
                        EventId = uid,
                    },
                });
            } catch (RpcException ex) {
                logger.Error("Channel {Channel} did not take the field war announce: {Message}", channelId, ex.Message);
            }
        }
        return uid;
    }

    public void Close(long uid) {
        lock (mutex) {
            if (open == null || open.Uid != uid) {
                return;
            }
            open = null;
        }
        foreach ((int channelId, ChannelClient client) in channelClients) {
            try {
                client.TimeEvent(new TimeEventRequest {
                    CloseFieldWar = new TimeEventRequest.Types.CloseFieldWar {
                        EventId = uid,
                    },
                });
            } catch (RpcException ex) {
                logger.Error("Channel {Channel} did not take the field war close: {Message}", channelId, ex.Message);
            }
        }
    }

    // The room a SidePopup enter goes to; null when the war is not open (or not this one) or no room was made.
    public FieldWarInfo? Join(long uid) {
        lock (mutex) {
            if (open == null || open.Uid != uid || !channelClients.TryGetClient(open.Channel, out ChannelClient? client)) {
                return null;
            }

            int mapId = open.Metadata.MapId;
            int maxCount = serverTableMetadata.InstanceFieldTable.Entries.TryGetValue(mapId, out InstanceFieldMetadata? instance) ? instance.MaxCount : 0;
            long now = Environment.TickCount64;
            if (FieldWarRules.NeedsNewRoom(open.RoomId, open.OpenedTick, now, serverTableMetadata.ConstantsTable.FieldWarInstanceEnterableDurationSeconds, open.Joined, maxCount)) {
                try {
                    // Room 0 of an instance map is a new room (FieldEntryRules.NewFieldPerEntry).
                    TimeEventResponse response = client.TimeEvent(new TimeEventRequest {
                        GetField = new TimeEventRequest.Types.GetField {
                            MapId = mapId,
                            RoomId = 0,
                        },
                    });
                    if (response.Field == null) {
                        return null;
                    }
                    open.RoomId = response.Field.RoomId;
                } catch (RpcException ex) {
                    logger.Error("Channel {Channel} did not make the field war room of map {MapId}: {Message}", open.Channel, mapId, ex.Message);
                    return null;
                }
                open.OpenedTick = now;
                open.Joined = 0;
            }

            open.Joined++;
            return new FieldWarInfo {
                EventId = open.Uid,
                FieldWarId = open.Metadata.Id,
                Channel = open.Channel,
                RoomId = open.RoomId,
                MapId = mapId,
            };
        }
    }
}
