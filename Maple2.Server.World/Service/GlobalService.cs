using Grpc.Core;
using Maple2.Database.Model;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Serilog;
using ILogger = Serilog.ILogger;

// TODO: Move this to a Global server
// ReSharper disable once CheckNamespace
namespace Maple2.Server.Global.Service;

public partial class GlobalService : Global.GlobalBase {
    private readonly GameStorage gameStorage;

    private readonly ILogger logger = Log.Logger.ForContext<GlobalService>();
    private static readonly object createLock = new();

    public GlobalService(GameStorage gameStorage) {
        this.gameStorage = gameStorage;
    }

    // A ban's times are stored in UTC (CreateBan); the database gives them back with no kind.
    private static long UtcEpoch(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    public override Task<LoginResponse> Login(LoginRequest request, ServerCallContext context) {
#if !DEBUG // Allow empty username for testing
        if (string.IsNullOrWhiteSpace(request.Username)) {
            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.ErrorId,
                Message = "Invalid Id",
            });
        }

        if (string.IsNullOrWhiteSpace(request.Password)) {
            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.ErrorPassword,
                Message = "Invalid Password.",
            });
        }
#endif

        // Normalize username
        string username = request.Username.Trim().ToLower();
        string password = request.Password.Trim();
        var machineId = new Guid(request.MachineId);
        string clientIp = request.ClientIp;

        using GameStorage.Request db = gameStorage.Context();

        // Hardware and ip ban pre-check (no account context yet, prevents auto-registration for banned hardware)
        (bool IsBanned, Ban? Ban) hwStatus = db.GetBanStatus(null, clientIp, machineId);
        if (hwStatus is { IsBanned: true, Ban: not null }) {
            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.Restricted,
                Message = hwStatus.Ban.Reason,
                AccountId = 0,
                BanStart = UtcEpoch(hwStatus.Ban.CreatedAt),
                BanExpiry = UtcEpoch(hwStatus.Ban.ExpiresAt),
            });
        }

        Account? account = db.GetAccount(username);
        if (account is null) {
            if (!Constant.AutoRegister) {
                return Task.FromResult(new LoginResponse {
                    Code = LoginResponse.Types.Code.ErrorId,
                    Message = "Account not found",
                });
            }

            Account? created = CreateAccount(username, password, machineId, out account);
            if (created != null) {
                return Task.FromResult(new LoginResponse { AccountId = created.Id });
            }
            // Another login made the account first: it is checked below as an existing one.
        }

        bool isPasswordValid = db.VerifyPassword(account.Id, password);
        if (!isPasswordValid) {
            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.ErrorPassword,
                Message = "Incorrect Password.",
            });
        }

        if (account.MachineId != machineId) {
            logger.Warning("MachineId mismatch for account {AccountId}", account.Id);
            if (Constant.BlockLoginWithMismatchedMachineId) {
                return Task.FromResult(new LoginResponse {
                    Code = LoginResponse.Types.Code.BlockNexonSn,
                    Message = "MachineId mismatch",
                });
            }
        }

        if (account.MachineId == default) {
            db.UpdateMachineId(account.Id, machineId);
        }

        (bool IsBanned, Ban? Ban) status = db.GetBanStatus(account.Id, clientIp, machineId);
        if (status is { IsBanned: true, Ban: not null }) {
            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.Restricted,
                Message = status.Ban.Reason,
                AccountId = account.Id,
                BanStart = UtcEpoch(status.Ban.CreatedAt),
                BanExpiry = UtcEpoch(status.Ban.ExpiresAt),
            });
        }

        return Task.FromResult(new LoginResponse { AccountId = account.Id });
    }

    // The client may open two login connections at once, and both find no account for a new name. The creates are
    // serialized and each looks again first; a create that still fails on the unique username (another writer) reads
    // the account back. Returns the new account, or null with the one another login made in existing.
    private Account? CreateAccount(string username, string password, Guid machineId, out Account existing) {
        lock (createLock) {
            using (GameStorage.Request db = gameStorage.Context()) {
                Account? found = db.GetAccount(username);
                if (found != null) {
                    existing = found;
                    return null;
                }

                try {
                    db.BeginTransaction();
                    Account account = db.CreateAccount(new Account {
                        Username = username,
                        MachineId = machineId,
                    }, password);
                    db.Commit();
                    existing = account;
                    return account;
                } catch (DbUpdateException ex) {
                    logger.Warning(ex, "Account {Username} was not created, reading it back", username);
                }
            }

            using GameStorage.Request reread = gameStorage.Context();
            existing = reread.GetAccount(username)
                       ?? throw new RpcException(new Status(StatusCode.Internal, "Failed to create account."));
            return null;
        }
    }
}
