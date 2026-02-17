using System.Text.Json;

namespace SchemaTools.Configuration;

/// <summary>
/// Shared utility for loading <see cref="SchemaToolsConfig"/> from JSON.
/// Follows the same test-override pattern as <see cref="SchemaTools.Utilities.AnalysisLoader"/>.
/// </summary>
internal static class ConfigurationLoader
{
  private static readonly JsonSerializerOptions DeserialiseOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  /// <summary>
  /// Loads a <see cref="SchemaToolsConfig"/> from the given file path,
  /// or returns the test override if provided.
  /// Falls back to <paramref name="fallback"/> (or a new default instance)
  /// when no file is found.
  /// </summary>
  internal static SchemaToolsConfig Load(
    string? configFile,
    SchemaToolsConfig? testOverride = null,
    SchemaToolsConfig? fallback = null)
  {
    if (testOverride != null)
    {
      return testOverride;
    }

    if (!string.IsNullOrEmpty(configFile) && File.Exists(configFile))
    {
      string json = File.ReadAllText(configFile);
      return JsonSerializer.Deserialize<SchemaToolsConfig>(json, DeserialiseOptions)
        ?? new SchemaToolsConfig();
    }

    return fallback ?? new SchemaToolsConfig();
  }
}
