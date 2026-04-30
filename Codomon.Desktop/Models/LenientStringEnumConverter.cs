using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codomon.Desktop.Models;

/// <summary>
/// A JSON converter for enum values that falls back to <c>default(T)</c> when the serialized
/// string is not a recognised member of the enum, instead of throwing.  This prevents LLM
/// responses that contain novel or mis-cased enum names from crashing the parse pipeline.
/// </summary>
public sealed class LenientStringEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (str is not null && Enum.TryParse<T>(str, ignoreCase: true, out var result))
            return result;
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
