using System.Text;
using System.Text.Json;

namespace Philips.IBE.IBEAgent.Abstractions;

public sealed record TransportMessageEnvelope(
    byte[] Payload,
    string? CorrelationId,
    IDictionary<string, string>? Headers)
{
    private const string PayloadProperty = "payload";
    private const string PayloadBase64Property = "payloadBase64";
    private const string CorrelationIdProperty = "correlationId";
    private const string RequestIdProperty = "requestId";
    private const string MessageIdProperty = "messageId";
    private const string LogicalEndpointIdProperty = "logicalEndpointId";
    private const string HeadersProperty = "headers";

    public static TransportMessageEnvelope Raw(byte[] payload) => new(payload, null, null);

    public static TransportMessageEnvelope ParseJson(byte[] payload, bool requireJsonObjectPrefix = true)
    {
        if (requireJsonObjectPrefix && !LooksLikeJsonObject(payload))
            return Raw(payload);

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Raw(payload);

            byte[]? body = null;
            if (root.TryGetProperty(PayloadBase64Property, out var base64) && base64.ValueKind == JsonValueKind.String)
            {
                var value = base64.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    body = Convert.FromBase64String(value);
            }
            else if (root.TryGetProperty(PayloadProperty, out var textPayload) && textPayload.ValueKind == JsonValueKind.String)
            {
                body = Encoding.UTF8.GetBytes(textPayload.GetString() ?? string.Empty);
            }

            if (body is null)
                return Raw(payload);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddHeader(root, RequestIdProperty, TransportCorrelationHeaders.RequestId, headers);
            AddHeader(root, MessageIdProperty, TransportCorrelationHeaders.MessageId, headers);
            AddHeader(root, LogicalEndpointIdProperty, TransportCorrelationHeaders.LogicalEndpointId, headers);

            if (root.TryGetProperty(HeadersProperty, out var headerElement) && headerElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in headerElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            headers[property.Name] = value;
                    }
                }
            }

            var correlationId = GetString(root, CorrelationIdProperty) ?? GetString(root, RequestIdProperty);
            return new TransportMessageEnvelope(body, correlationId, headers.Count == 0 ? null : headers);
        }
        catch (JsonException) { return Raw(payload); }
        catch (FormatException) { return Raw(payload); }
    }

    private static bool LooksLikeJsonObject(byte[] payload)
    {
        foreach (var b in payload)
        {
            if (char.IsWhiteSpace((char)b))
                continue;
            return b == (byte)'{';
        }

        return false;
    }

    private static void AddHeader(JsonElement root, string propertyName, string headerName, Dictionary<string, string> headers)
    {
        var value = GetString(root, propertyName);
        if (!string.IsNullOrWhiteSpace(value))
            headers[headerName] = value;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
