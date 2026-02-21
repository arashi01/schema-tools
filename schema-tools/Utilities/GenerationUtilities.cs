using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchemaTools.Utilities;

/// <summary>
/// Shared utilities for JSON serialisation output and file writing
/// used by generators and extractors.
/// </summary>
internal static class GenerationUtilities
{
  /// <summary>
  /// Standard JSON serialisation options for all SchemaTools output files
  /// (schema.json, analysis.json). Produces indented, camelCase JSON with
  /// null properties omitted and no unnecessary Unicode escaping.
  /// </summary>
  internal static readonly JsonSerializerOptions SerialiseOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  /// <summary>
  /// Serialises <paramref name="value"/> to JSON and writes the result
  /// to <paramref name="filePath"/>, creating the parent directory if needed.
  /// </summary>
  internal static void WriteJson<T>(string filePath, T value)
  {
    EnsureDirectoryExists(filePath);
    string json = JsonSerializer.Serialize(value, SerialiseOptions);
    File.WriteAllText(filePath, json, Encoding.UTF8);
  }

  /// <summary>
  /// Returns the SchemaTools package version (major.minor.patch[-prerelease])
  /// stripped of any semver build metadata (<c>+commitsha</c>) suffix.
  /// Falls back to the assembly file version, then <c>"0.0.0"</c>.
  /// </summary>
  internal static string GetToolVersion()
  {
    string version = typeof(GenerationUtilities).Assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
      ?? typeof(GenerationUtilities).Assembly.GetName().Version?.ToString()
      ?? "0.0.0";

    // Strip semver build metadata (everything after '+')
    int plusIndex = version.IndexOf('+');
    return plusIndex >= 0 ? version[..plusIndex] : version;
  }

  /// <summary>
  /// Ensures the parent directory of <paramref name="filePath"/> exists,
  /// creating it if necessary.
  /// </summary>
  internal static void EnsureDirectoryExists(string filePath)
  {
    string? directory = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }
  }
}
