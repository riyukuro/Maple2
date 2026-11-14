namespace Maple2.Server.World;

// What the login check (GlobalService.Login, LoginGateRules) reads from the world server.
public interface ILoginGate {
    // The maintenance text while the server is in maintenance, else null.
    string? Maintenance { get; }

    // The online characters on the listed channels and the number of listed channels (the server list's counts).
    (int Online, int Channels) Load();

    bool AccountOnline(long accountId);
}
