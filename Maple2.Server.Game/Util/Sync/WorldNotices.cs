using Grpc.Core;
using Maple2.Server.World.Service;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.Util.Sync;

// World calls whose answer nobody reads: they are sent without waiting for it. Asked on a field's thread, a waited
// notice held every player of the field until the World answered, once per recipient (a wedding's guests, the
// OX quiz payments); a notice that fails only costs the badge, the mail is there at the next mailbox load.
public static class WorldNotices {
    internal static TimeSpan NoticeWait = TimeSpan.FromSeconds(10);

    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(WorldNotices));

    // Tells the recipient's game server about a new mail (the mailbox badge), if they are online.
    public static void NotifyMail(this WorldClient world, long characterId, long mailId) {
        try {
            AsyncUnaryCall<MailNotificationResponse> call = world.MailNotificationAsync(new MailNotificationRequest {
                CharacterId = characterId,
                MailId = mailId,
            }, deadline: DateTime.UtcNow + NoticeWait);
            call.ResponseAsync.ContinueWith(task => {
                if (task.Exception?.InnerException is { } ex and not RpcException { StatusCode: StatusCode.NotFound }) {
                    Logger.Debug(ex, "Mail notice not delivered CharacterId={CharacterId} MailId={MailId}", characterId, mailId);
                }
                call.Dispose();
            }, TaskScheduler.Default);
        } catch (Exception ex) {
            Logger.Debug(ex, "Mail notice not sent CharacterId={CharacterId} MailId={MailId}", characterId, mailId);
        }
    }
}
