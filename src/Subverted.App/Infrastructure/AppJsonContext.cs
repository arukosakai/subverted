using System.Text.Json.Serialization;

namespace Subverted.App.Infrastructure;

/// <summary>Source-generated, like the protocol's, so nothing here needs reflection to start.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(string[]))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
