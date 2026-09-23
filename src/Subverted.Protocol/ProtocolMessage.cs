using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Subverted.Protocol;

/// <summary>
/// Turns messages into frame payloads and back. UTF-8 JSON, because a wire format you can read
/// with <c>nc</c> is worth more during M1 than the bytes a binary one would save.
/// </summary>
public static class ProtocolMessage
{
    public static byte[] Encode(DaemonRequest request) =>
        JsonSerializer.SerializeToUtf8Bytes(request, ProtocolJsonContext.Default.DaemonRequest);

    public static byte[] Encode(DaemonResponse response) =>
        JsonSerializer.SerializeToUtf8Bytes(response, ProtocolJsonContext.Default.DaemonResponse);

    /// <exception cref="ProtocolException">The payload is not a request this build understands.</exception>
    public static DaemonRequest DecodeRequest(ReadOnlySpan<byte> payload) =>
        Decode(payload, ProtocolJsonContext.Default.DaemonRequest, "request");

    /// <exception cref="ProtocolException">The payload is not a response this build understands.</exception>
    public static DaemonResponse DecodeResponse(ReadOnlySpan<byte> payload) =>
        Decode(payload, ProtocolJsonContext.Default.DaemonResponse, "response");

    private static T Decode<T>(ReadOnlySpan<byte> payload, JsonTypeInfo<T> typeInfo, string what)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(payload, typeInfo)
                ?? throw new ProtocolException($"Peer sent a null {what}.");
        }
        // A missing type discriminator comes back as NotSupportedException rather than
        // JsonException, and it is just as much a malformed message as a stray brace is.
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new ProtocolException($"Peer sent a malformed {what}: {ex.Message}", ex);
        }
    }
}
