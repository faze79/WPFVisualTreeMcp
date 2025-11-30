using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfVisualTreeMcp.Shared.Ipc;

/// <summary>
/// Serializes and deserializes IPC messages.
/// </summary>
public static class IpcSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Serialize<T>(T message) where T : class
    {
        return JsonSerializer.Serialize(message, Options);
    }

    public static T? Deserialize<T>(string json) where T : class
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    public static string SerializeRequest(IpcRequest request)
    {
        // Wrap with type info for deserialization
        var wrapper = new
        {
            type = request.RequestType,
            data = request
        };
        return JsonSerializer.Serialize(wrapper, Options);
    }

    public static (string type, JsonElement data)? DeserializeRequest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeElement) &&
                root.TryGetProperty("data", out var dataElement))
            {
                return (typeElement.GetString() ?? "", dataElement.Clone());
            }
        }
        catch
        {
            // Invalid JSON
        }

        return null;
    }

    public static T? DeserializeRequestData<T>(JsonElement data) where T : IpcRequest
    {
        return data.Deserialize<T>(Options);
    }

    public static string SerializeResponse(IpcResponse response)
    {
        return JsonSerializer.Serialize(response, response.GetType(), Options);
    }

    public static T? DeserializeResponse<T>(string json) where T : IpcResponse
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
