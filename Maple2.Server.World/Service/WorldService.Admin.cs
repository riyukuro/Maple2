using Grpc.Core;
using Maple2.Model.Validators;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<AdminResponse> Admin(AdminRequest request, ServerCallContext context) {
        switch (request.RequestCase) {
            case AdminRequest.RequestOneofCase.Alert:
            case AdminRequest.RequestOneofCase.AccountBlock:
            case AdminRequest.RequestOneofCase.ShutdownNotice:
                return Task.FromResult(Alert(request));
            case AdminRequest.RequestOneofCase.AddStringBoard:
                return Task.FromResult(AddStringBoard(request.AddStringBoard));
            case AdminRequest.RequestOneofCase.RemoveStringBoard:
                return Task.FromResult(RemoveStringBoard(request.RemoveStringBoard));
            case AdminRequest.RequestOneofCase.ListStringBoard:
                return Task.FromResult(ListStringBoards());
            case AdminRequest.RequestOneofCase.Notice:
                return Task.FromResult(Notice(request));
            default:
                return Task.FromResult(new AdminResponse());
        }
    }

    // Sent to every channel: an alert or a shutdown notice to all players, an account restriction to whichever channel
    // has the account.
    private AdminResponse Alert(AdminRequest request) {
        string responseMessage = string.Empty;
        foreach (int channel in channelClients.Keys) {
            if (!channelClients.TryGetClient(channel, out Channel.Service.Channel.ChannelClient? channelClient)) {
                responseMessage += $"Channel {channel} could not be found. \n";
                continue;
            }

            // A channel that is down or stopping fails its own call; the other channels still get the request.
            AdminResponse? channelResponse;
            try {
                channelResponse = channelClient.Admin(request);
            } catch (RpcException ex) {
                responseMessage += $"Channel {channel} failed: {ex.Status.StatusCode}\n";
                continue;
            }
            if (channelResponse == null) {
                responseMessage += $"Channel {channel} failed: null response\n";
                continue;
            }
            if (channelResponse.Error > 0 || !string.IsNullOrEmpty(channelResponse.Message)) {
                responseMessage += $"Channel {channel} failed: {channelResponse.Error} - {channelResponse.Message}\n";
            }
        }

        return new AdminResponse {
            Message = responseMessage,
        };
    }

    // The notice config (NoticeRules). A banner change or show goes to every channel; the login notice is read by the
    // login server at each login (a status request).
    private AdminResponse Notice(AdminRequest request) {
        AdminRequest.Types.Notice notice = request.Notice;
        if (!NoticeRules.ValidRequest(notice.Kind, notice.Action)) {
            (bool bannerNow, bool loginNow) = worldServer.NoticeConfig();
            return new AdminResponse {
                Error = 1,
                Message = $"Unknown notice request: kind {notice.Kind}, action {notice.Action}",
                NoticeBanner = bannerNow,
                NoticeLogin = loginNow,
            };
        }

        (bool banner, bool login) = worldServer.ApplyNotice(notice.Kind, notice.Action);
        AdminResponse response = notice.Kind == NoticeRules.Banner && notice.Action != NoticeRules.Status
            ? Alert(request)
            : new AdminResponse();
        response.NoticeBanner = banner;
        response.NoticeLogin = login;
        return response;
    }

    private AdminResponse AddStringBoard(AdminRequest.Types.AddStringBoard add) {
        int id = worldServer.AddCustomStringBoard(add.Message);
        if (id < 0) {
            return new AdminResponse {
                Message = "Failed to add string board",
            };
        }

        string responseMessage = string.Empty;
        foreach (int channel in channelClients.Keys) {
            if (!channelClients.TryGetClient(channel, out Channel.Service.Channel.ChannelClient? channelClient)) {
                responseMessage += $"Channel {channel} could not be found. \n";
                continue;
            }

            AdminResponse? channelResponse = channelClient.Admin(new AdminRequest {
                AddStringBoard = new AdminRequest.Types.AddStringBoard {
                    Id = id,
                    Message = add.Message,
                },
            });
            if (channelResponse == null) {
                responseMessage += $"Channel {channel} failed: null response\n";
                continue;
            }
            if (channelResponse.Error > 0 || !string.IsNullOrEmpty(channelResponse.Message)) {
                responseMessage += $"Channel {channel} failed: {channelResponse.Error} - {channelResponse.Message}\n";
            }
        }

        return new AdminResponse {
            Message = responseMessage,
        };
    }

    private AdminResponse RemoveStringBoard(AdminRequest.Types.RemoveStringBoard remove) {
        if (!worldServer.RemoveCustomStringBoard(remove.Id)) {
            return new AdminResponse {
                Message = "Failed to remove string board",
            };
        }

        string responseMessage = string.Empty;
        foreach (int channel in channelClients.Keys) {
            if (!channelClients.TryGetClient(channel, out Channel.Service.Channel.ChannelClient? channelClient)) {
                responseMessage += $"Channel {channel} could not be found. \n";
                continue;
            }

            AdminResponse? channelResponse = channelClient.Admin(new AdminRequest {
                RemoveStringBoard = new AdminRequest.Types.RemoveStringBoard {
                    Id = remove.Id,
                },
            });
            if (channelResponse == null) {
                responseMessage += $"Channel {channel} failed: null response\n";
                continue;
            }
            if (channelResponse.Error > 0 || !string.IsNullOrEmpty(channelResponse.Message)) {
                responseMessage += $"Channel {channel} failed: {channelResponse.Error} - {channelResponse.Message}\n";
            }
        }

        return new AdminResponse {
            Message = responseMessage,
        };
    }

    private AdminResponse ListStringBoards() {
        string responseMessage = string.Empty;

        IReadOnlyDictionary<int, string> stringBoards = worldServer.GetCustomStringBoards();
        foreach (KeyValuePair<int, string> stringBoard in stringBoards) {
            responseMessage += $"ID: {stringBoard.Key}, Message: {stringBoard.Value}\n";
        }

        return new AdminResponse {
            Message = responseMessage,
        };
    }
}
