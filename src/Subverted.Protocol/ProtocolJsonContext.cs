using System.Text.Json.Serialization;

namespace Subverted.Protocol;

/// <summary>
/// Ahead-of-time serialization metadata for every message on the wire. Reflection-free on purpose:
/// the CLI's whole budget is tens of milliseconds and reflective serializer warm-up is a
/// measurable slice of that.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(DaemonRequest))]
[JsonSerializable(typeof(DaemonResponse))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
