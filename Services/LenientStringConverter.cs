using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteelCertExtraction.Services;

/// <summary>
/// Reads a measurement's text parts leniently: the model may return a value as a
/// string ("54") or a bare number (54); both become text. true/false become
/// "true"/"false"; null stays null; objects and arrays are skipped (null) so one
/// odd entry can't break the response.
/// </summary>
public sealed class LenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.HasValueSequence
                    ? Encoding.UTF8.GetString(reader.ValueSequence)
                    : Encoding.UTF8.GetString(reader.ValueSpan);
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
