using System.Threading.Tasks;
using Grpc.Core;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Login.Service;

public partial class LoginService {
    // The account's login session is entered (LoginSession.Init, after its key was checked) and still on the character
    // list: it has not gone on to a game server or back to the server list (LeaveCharacterList). The character list
    // uploads a new character's profile picture from there (UICharacterListDialog: a character list of mode 1 calls
    // CUgcManager::FUN_141880600), so the web server takes the upload for a character of this account.
    public override Task<CharacterListResponse> CharacterList(CharacterListRequest request, ServerCallContext context) {
        return Task.FromResult(new CharacterListResponse {
            OnCharacterList = OnCharacterList(loginServer, request.AccountId),
        });
    }

    public static bool OnCharacterList(LoginServer server, long accountId) {
        return accountId > 0 && server.GetSession(accountId, out LoginSession? session) && !session.LeftCharacterList;
    }
}
