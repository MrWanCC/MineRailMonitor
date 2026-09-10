using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineRailMonitor.Infrastructure.Configuration;

/// <summary>
/// Reads both the numeric byte arrays emitted by the legacy Newtonsoft writer
/// and the Base64 representation used by System.Text.Json's default converter.
/// </summary>
internal sealed class ByteArrayJsonConverter : JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Convert.FromBase64String(value);
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var values = new System.Collections.Generic.List<byte>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.Number)
                {
                    throw new JsonException("Byte array values must be numbers.");
                }

                values.Add(reader.GetByte());
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("Byte array is missing its closing bracket.");
            }

            return values.ToArray();
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return Array.Empty<byte>();
        }

        throw new JsonException($"Expected a Base64 string or numeric byte array, got {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) =>
        writer.WriteBase64StringValue(value ?? Array.Empty<byte>());
}
