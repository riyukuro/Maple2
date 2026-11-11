using System.Text.Json;
using System.Text.Json.Serialization;
using Maple2.Model.Game;

namespace Maple2.Database.Extensions;

// A special option is stored as {"Value": int}. Rows written before the option became an int hold
// {"Rate": float} instead, where the float was either a rate (below 1 in size) or a flat value (1 or more).
// Those are converted here to the int the client reads: a rate as rate * 1000, a value as it is.
public class SpecialOptionConverter : JsonConverter<SpecialOption> {
    public override SpecialOption Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) {
            return default;
        }
        if (root.TryGetProperty("Value", out JsonElement value)) {
            // A hand-edited or older row may hold a fraction here: it is rounded, not refused.
            return value.TryGetInt32(out int number) ? new SpecialOption(number) : new SpecialOption((int) Math.Round(value.GetDouble()));
        }
        if (root.TryGetProperty("Rate", out JsonElement rate)) {
            return FromLegacy(rate.GetSingle());
        }
        return default;
    }

    public override void Write(Utf8JsonWriter writer, SpecialOption value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        if (value.Value != 0) {
            writer.WriteNumber("Value", value.Value);
        }
        writer.WriteEndObject();
    }

    public static SpecialOption FromLegacy(float legacy) {
        // The old rate was built as thousandths / 1000f, so rounding gives the thousandths back.
        return new SpecialOption((int) MathF.Round(MathF.Abs(legacy) < 1f ? legacy * 1000f : legacy));
    }
}
