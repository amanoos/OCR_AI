using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteelCertExtraction.Services;

/// <summary>
/// Reads the model's numeric fields without ever throwing: a real number, or a
/// string that is a plain number (optionally with a trailing '%'), becomes the
/// value; anything else — "&lt;0.001", "ND", "-", an object, true/false — becomes
/// null ("not found"), and the reviewer can type the value in.
///
/// Without this a single trace value such as "&lt;0.001" made the WHOLE extraction
/// fail to parse, which gets likelier the more elements are requested.
/// </summary>
public sealed class LenientDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetDecimal(out var number) ? number : null;

            case JsonTokenType.String:
            {
                var text = reader.GetString()?.Trim().TrimEnd('%').Trim();
                return decimal.TryParse(text,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            }

            case JsonTokenType.Null:
                return null;

            default:
                // Object, array, true/false: consume it so the reader stays in sync, then treat as not found.
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteNumberValue(value.Value);
    }
}
