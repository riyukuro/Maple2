using System;
using System.IO;

namespace Maple2.Server.Game.Session;

// LOCAL TEST HOOK (tst1): if TST_CMD_FILE is set, once per second run each line of that file as a chat command
// (as the logged-in player, e.g. "warp 2000001") and delete the file. File = TST_CMD_FILE + "." + character id;
// ack lines go to <that file>.ack.
public sealed partial class GameSession {
    private string tstState = "";
    private void TstPollCommands() {
        string? prefix = Environment.GetEnvironmentVariable("TST_CMD_FILE");
        if (string.IsNullOrEmpty(prefix)) return;
        string path = $"{prefix}.{CharacterId}";
        // <path>.state = id of the map the player is in right now (0 while between maps), rewritten on change
        string state = (Field?.MapId ?? 0).ToString();
        if (state != tstState) { tstState = state; TstAppend(CharacterId, "#MAP\t" + state); try { File.WriteAllText(path + ".state", state); } catch (IOException) { } }
        if (Field == null) return;
        if (!File.Exists(path)) return;
        string[] lines;
        try { lines = File.ReadAllLines(path); File.Delete(path); } catch (IOException) { return; }
        foreach (string line in lines) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            int rc = CommandHandler.Invoke(line);
            File.AppendAllText(path + ".ack", $"{DateTime.UtcNow:HH:mm:ss.fff}\t{line}\trc={rc}\n");
        }
    }
}
