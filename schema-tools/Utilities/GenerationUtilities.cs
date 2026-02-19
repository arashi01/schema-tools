using System.Text;
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
  /// null properties omitted.
  /// </summary>
  internal static readonly JsonSerializerOptions SerialiseOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
