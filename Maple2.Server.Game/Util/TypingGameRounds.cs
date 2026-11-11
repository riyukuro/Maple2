namespace Maple2.Server.Game.Util;

/// <summary>
/// The widget_value condition text: a bare number ("1") or an operator and a number ("= 2", ">= 3").
/// An empty condition asks whether the value is set and not 0 (Round GameClear and GameFail give none).
/// A value that was never set matches nothing, so a typing game with no reply yet is neither "1" nor "0".
/// </summary>
public static class WidgetCondition {
    public static bool Matches(int? value, string? condition) {
        if (value == null) {
            return false;
        }
        string text = condition?.Trim() ?? string.Empty;
        if (text.Length == 0) {
            return value != 0;
        }

        string op = "=";
        foreach (string candidate in new[] { ">=", "<=", "!=", "==", ">", "<", "=" }) {
            if (text.StartsWith(candidate, StringComparison.Ordinal)) {
                op = candidate;
                text = text[candidate.Length..].Trim();
                break;
            }
        }
        if (!int.TryParse(text, out int expected)) {
            return false;
        }

        return op switch {
            ">=" => value >= expected,
            "<=" => value <= expected,
            "!=" => value != expected,
            ">" => value > expected,
            "<" => value < expected,
            _ => value == expected,
        };
    }
}

/// <summary>
/// One typing game at a time. The client's UITypingGameDialog answers a TypingGame Start with Trigger Ui 0xc and
/// int 1 (SuccessGameProgress) or 0 (FailGameProgress); only the first answer to a started game counts, and a reply
/// with no game running is dropped.
/// </summary>
public sealed class TypingGameState {
    private readonly object mutex = new();
    private bool running;
    private int? result;

    public int? Result {
        get {
            lock (mutex) {
                return result;
            }
        }
    }

    public void Start() {
        lock (mutex) {
            running = true;
            result = null;
        }
    }

    public bool Reply(int value) {
        lock (mutex) {
            if (!running) {
                return false;
            }
            running = false;
            result = value == 1 ? 1 : 0;
            return true;
        }
    }
}

/// <summary>
/// The Round widget. The client has no round widget, so the rules come from the only placed scripts that use it
/// (02000533_bf, 02000535_bf, 02000555_bf): SettingFinalRound 1, SettingAllowedFailCount 3, SettingRoundInitIfFail 0,
/// then up to three rounds ("CurrentRound = 1/2/3", each with its own digits, time and texts) that each end in
/// RoundResult 1 or 0. Every result moves to the next round; a win count reaching the final round sets GameClear,
/// a fail count reaching the allowed fail count sets GameFail. So one cleared round of three tries clears the game,
/// and the third fail ends it at round 3, the last round the scripts have. With RoundInitIfFail a fail goes back to
/// round 1 and drops the wins. InitWidgetRound starts over and keeps the settings.
/// </summary>
public sealed class RoundCounter {
    private readonly object mutex = new();

    public int FinalRound { get; private set; } = 1;
    public int AllowedFailCount { get; private set; } = 1;
    public bool InitIfFail { get; private set; }
    public int CurrentRound { get; private set; } = 1;
    public int Wins { get; private set; }
    public int Fails { get; private set; }
    public bool GameClear { get; private set; }
    public bool GameFail { get; private set; }

    public void Action(string function, int value) {
        lock (mutex) {
            switch (function) {
                case "SettingFinalRound":
                    FinalRound = Math.Max(1, value);
                    break;
                case "SettingAllowedFailCount":
                    AllowedFailCount = Math.Max(1, value);
                    break;
                case "SettingRoundInitIfFail":
                    InitIfFail = value != 0;
                    break;
                case "RoundResult":
                    Result(value == 1);
                    break;
                case "InitWidgetRound":
                    CurrentRound = 1;
                    Wins = 0;
                    Fails = 0;
                    GameClear = false;
                    GameFail = false;
                    break;
            }
        }
    }

    public int? Value(string name) {
        lock (mutex) {
            return name switch {
                "CurrentRound" => CurrentRound,
                "GameClear" => GameClear ? 1 : 0,
                "GameFail" => GameFail ? 1 : 0,
                _ => null,
            };
        }
    }

    private void Result(bool success) {
        if (GameClear || GameFail) {
            return;
        }
        if (success) {
            Wins++;
            if (Wins >= FinalRound) {
                GameClear = true;
                return;
            }
            CurrentRound++;
            return;
        }

        Fails++;
        if (Fails >= AllowedFailCount) {
            GameFail = true;
            return;
        }
        if (InitIfFail) {
            CurrentRound = 1;
            Wins = 0;
        } else {
            CurrentRound++;
        }
    }
}
