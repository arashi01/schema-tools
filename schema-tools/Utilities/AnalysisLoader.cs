using System.Text.Json;
using SchemaTools.Models;

namespace SchemaTools.Utilities;

/// <summary>
/// Shared utility for loading pre-build analysis results from JSON.
/// Used by SqlTriggerGenerator, SqlProcedureGenerator, and SqlViewGenerator.
/// </summary>
internal static class AnalysisLoader
{
  private static readonly JsonSerializerOptions DeserialiseOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  /// <summary>
  /// Loads a <see cref="SourceAnalysisResult"/> from the given file path,
  /// or returns the test override if provided.
  /// </summary>
  /// <exception cref="FileNotFoundException">Thrown when the analysis file does not exist.</exception>
  /// <exception cref="InvalidOperationException">Thrown when deserialisation fails.</exception>
  internal static SourceAnalysisResult Load(string filePath, SourceAnalysisResult? testOverride = null)
  {
    if (testOverride != null)
    {
      return testOverride;
    }

    if (!File.Exists(filePath))
    {
      throw new FileNotFoundException($"Analysis file not found: {filePath}");
    }

    string json = File.ReadAllText(filePath);
    SourceAnalysisResult? analysis = JsonSerializer.Deserialize<SourceAnalysisResult>(json, DeserialiseOptions);

    if (analysis == null || analysis.Tables == null)
    {
      throw new InvalidOperationException("Failed to deserialise analysis file");
    }

    return analysis;
  }
}
