using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MigrationTool.Service.Logging.Configuration;

/// <summary>
/// Custom JsonConverter for handling polymorphic deserialization of ProviderConfiguration.
/// </summary>
public class ProviderConfigurationConverter : JsonConverter<ProviderConfiguration>
{
    public override ProviderConfiguration? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Clone the element so we can read it multiple times
        var json = root.GetRawText();

        // Check if there are provider-specific properties to determine the type
        if (root.TryGetProperty("logDirectory", out _) ||
            root.TryGetProperty("LogDirectory", out _) ||
            root.TryGetProperty("filePrefix", out _) ||
            root.TryGetProperty("FilePrefix", out _))
        {
            return JsonSerializer.Deserialize<FileProviderConfiguration>(json, options);
        }

        if (root.TryGetProperty("source", out _) ||
            root.TryGetProperty("Source", out _) ||
            root.TryGetProperty("logName", out _) ||
            root.TryGetProperty("LogName", out _))
        {
            return JsonSerializer.Deserialize<EventLogProviderConfiguration>(json, options);
        }

        // Default to file provider if we can't determine the type
        return JsonSerializer.Deserialize<FileProviderConfiguration>(json, options);
    }

    public override void Write(Utf8JsonWriter writer, ProviderConfiguration value, JsonSerializerOptions options)
    {
        // Let the default serialization handle writing
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}