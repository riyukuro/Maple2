namespace Maple2.Model.Validators;

// The play time of a written score, computed the way the client's score writer computes the length it sends with a
// compose (UIWriteMusicDialog::OnClickMake): each tab is parsed (MMLParser::Parse) and timed
// (CInstrumentManager::ParseLine), and the score lasts as long as its longest tab.
public static class MmlLength {
    private enum Kind { Note, Rest, Control, PitchBend, Motion }

    private sealed class Token {
        public Kind Kind;
        public double Time;
        public int Pitch;
        public int Value;
    }

    // The score's length in ms. The mml is the tabs joined by ':'. With a percussion instrument only the first tab
    // plays (ParseLine returns at once for the others).
    public static long Of(string mml, int percussionId) {
        string[] tabs = mml.Split(':');
        long longest = 0;
        for (int i = 0; i < tabs.Length; i++) {
            if (percussionId != 0 && i > 0) {
                break;
            }
            longest = Math.Max(longest, TabMs(tabs[i], first: i == 0));
        }
        return longest;
    }

    // CInstrumentManager::ParseLine: a rest always takes its time, a note only when it lasts more than 1 ms; control
    // changes, pitch bends and (on the first tab) motion events take none. The client truncates the end time.
    private static long TabMs(string tab, bool first) {
        double cursor = 0;
        foreach (Token token in Parse(tab)) {
            if (first && token.Kind == Kind.Motion) {
                continue;
            }
            switch (token.Kind) {
                case Kind.Rest:
                    cursor += token.Time;
                    break;
                case Kind.Note when token.Time > 1.0:
                    cursor += token.Time;
                    break;
            }
        }
        return (long) cursor;
    }

    // MMLParser::Parse, from a reset parser: tempo 120, octave 4, length 4, volume 8, not dotted.
    private static List<Token> Parse(string text) {
        int tempo = 120, octave = 4, length = 4, volume = 8;
        bool dotted = false;
        var tokens = new List<Token>();
        bool tie = false;
        int pos = 0;
        while (pos < text.Length) {
            char c = text[pos];
            switch (Classify(c)) {
                case 1:
                case 4: {
                    bool isNote = Classify(c) == 1;
                    int note = 12;
                    bool noteDotted = dotted;
                    if (isNote) {
                        pos += ParseNote(text, pos, out note);
                    } else {
                        pos++;
                    }
                    int digits = ParseNumber(text, pos, out int noteLength);
                    if (digits == 0) {
                        noteLength = length;
                    } else {
                        noteDotted = false;
                    }
                    pos += digits;
                    if (pos < text.Length && text[pos] == '.') {
                        pos++;
                        noteDotted = true;
                    }
                    var token = new Token {
                        Kind = isNote ? Kind.Note : Kind.Rest,
                        Time = NoteLengthToMs(tempo, noteLength, noteDotted),
                        Pitch = isNote ? octave * 12 + note : 12,
                        Value = isNote ? volume << 3 : 0,
                    };
                    if (TieInto(tokens, tie, token)) {
                        tie = false;
                        continue;
                    }
                    tokens.Add(token);
                    break;
                }
                case 2: {
                    pos++;
                    int used = ParseNumber(text, pos, out int value);
                    if (used == 0) {
                        continue;
                    }
                    pos += used;
                    int pitch = value + 12;
                    if (pitch < 12 || pitch > 0x77) {
                        continue;
                    }
                    var token = new Token { Kind = Kind.Note, Time = NoteLengthToMs(tempo, length, dotted), Pitch = pitch, Value = volume << 3 };
                    if (TieInto(tokens, tie, token)) {
                        tie = false;
                        continue;
                    }
                    tokens.Add(token);
                    break;
                }
                case 5: {
                    pos++;
                    int used = ParseNumber(text, pos, out int value);
                    if (used == 0) {
                        continue;
                    }
                    pos += used;
                    if (value is >= 0 and <= 8) {
                        octave = value;
                    }
                    break;
                }
                case 6: {
                    pos++;
                    int used = ParseNumber(text, pos, out int value);
                    if (used == 0) {
                        continue;
                    }
                    pos += used;
                    if (pos < text.Length && text[pos] == '.') {
                        pos++;
                        dotted = true;
                    } else {
                        dotted = false;
                    }
                    if (value is >= 1 and <= 0x40) {
                        length = value;
                    }
                    break;
                }
                case 7: {
                    pos++;
                    int used = ParseNumber(text, pos, out int value);
                    if (used == 0) {
                        continue;
                    }
                    pos += used;
                    if (value is >= 0 and <= 0xf) {
                        volume = value;
                    }
                    break;
                }
                case 8: {
                    pos++;
                    int used = ParseNumber(text, pos, out int value);
                    if (used == 0) {
                        continue;
                    }
                    pos += used;
                    if (value is >= 0x20 and <= 0xff) {
                        tempo = value;
                    }
                    break;
                }
                case 9:
                    pos++;
                    octave = Math.Clamp(octave + 1, 0, 8);
                    break;
                case 10:
                    pos++;
                    octave = Math.Clamp(octave - 1, 0, 8);
                    break;
                case 0xb:
                    pos++;
                    tie = true;
                    continue;
                case 0xe: {
                    pos++;
                    int used = ParseNumber(text, pos, out int value);
                    if (used == 0) {
                        continue;
                    }
                    pos += used;
                    tokens.Add(new Token { Kind = Kind.Control, Pitch = 0x40, Value = value > 0 ? 0x7f : 0 });
                    break;
                }
                case 0x10: {
                    pos++;
                    int used = ParseNumber(text, pos, out int value);
                    if (used == 0) {
                        continue;
                    }
                    pos += used;
                    tokens.Add(new Token { Kind = Kind.Motion, Pitch = 1, Value = value });
                    break;
                }
                case 0x11: {
                    pos++;
                    int used = ParseNumber(text, pos, out int value);
                    if (used == 0) {
                        continue;
                    }
                    pos += used;
                    if (value is >= 0x20 and <= 0xff) {
                        tokens.Add(new Token { Kind = Kind.Motion, Pitch = 0, Value = value });
                    }
                    break;
                }
                case 0xd:
                case 0xf: {
                    bool control = Classify(c) == 0xd;
                    pos++;
                    int used = ParseNumber(text, pos, out int firstValue);
                    if (used == 0) {
                        continue;
                    }
                    pos += used;
                    if (pos >= text.Length || text[pos] != ',') {
                        continue;
                    }
                    pos++;
                    int usedSecond = ParseNumber(text, pos, out int secondValue);
                    if (usedSecond == 0) {
                        continue;
                    }
                    pos += usedSecond;
                    tokens.Add(new Token { Kind = control ? Kind.Control : Kind.PitchBend, Pitch = firstValue, Value = secondValue });
                    break;
                }
                default:
                    pos++;
                    break;
            }
            // Every branch that does not continue ends a tie (the client's loop tail); a continue keeps it.
            tie = false;
        }
        return tokens;
    }

    // A tied note or rest of the same pitch and value lengthens the token before it instead of adding one.
    private static bool TieInto(List<Token> tokens, bool tie, Token token) {
        if (!tie || tokens.Count == 0) {
            return false;
        }
        Token previous = tokens[^1];
        if (previous.Pitch != token.Pitch || previous.Value != token.Value) {
            return false;
        }
        previous.Time += token.Time;
        return true;
    }

    private static int Classify(char c) {
        return char.ToUpperInvariant(c) switch {
            >= 'A' and <= 'G' => 1,
            'N' => 2,
            'R' => 4,
            'O' => 5,
            'L' => 6,
            'V' => 7,
            'T' => 8,
            '>' => 9,
            '<' => 10,
            '&' => 0xb,
            'Y' => 0xd,
            'I' => 0xf,
            'S' => 0xe,
            'M' => 0x10,
            'P' => 0x11,
            _ => 0,
        };
    }

    // The digits at pos and their value (atoi, which stops at int.MaxValue).
    private static int ParseNumber(string text, int pos, out int value) {
        int count = 0;
        long result = 0;
        while (pos + count < text.Length && text[pos + count] is >= '0' and <= '9') {
            result = Math.Min(result * 10 + (text[pos + count] - '0'), int.MaxValue);
            count++;
        }
        value = (int) result;
        return count;
    }

    // A note letter, with a sharp (+) or flat (-) after it.
    private static int ParseNote(string text, int pos, out int note) {
        note = char.ToUpperInvariant(text[pos]) switch {
            'C' => 0xc,
            'D' => 0xe,
            'E' => 0x10,
            'F' => 0x11,
            'G' => 0x13,
            'A' => 0x15,
            _ => 0x17,
        };
        if (pos + 1 < text.Length) {
            if (text[pos + 1] == '+') {
                note++;
                return 2;
            }
            if (text[pos + 1] == '-') {
                note--;
                return 2;
            }
        }
        return 1;
    }

    // MMLParser::NoteLengthToMs: a whole note is four beats.
    private static double NoteLengthToMs(int tempo, int length, bool dotted) {
        if (length == 0) {
            return 0.0;
        }
        double time = 60000.0;
        time /= tempo;
        time /= length;
        time *= 4.0;
        if (dotted) {
            time *= 1.5;
        }
        return time;
    }
}
