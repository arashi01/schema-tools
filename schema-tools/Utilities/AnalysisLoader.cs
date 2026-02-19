using System.Text.Json;
using SchemaTools.Diagnostics;
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
  /// Returns a failed <see cref="OperationResult{T}"/> when the file is
  /// missing or deserialisation fails.
  /// </summary>
  internal static OperationResult<SourceAnalysisResult> Load(string filePath, SourceAnalysisResult? testOverride = null)
  {
    if (testOverride != null)
    {
      return OperationResult<SourceAnalysisResult>.Success(testOverride);
    }

    if (!File.Exists(filePath))
    {
      return OperationResult<SourceAnalysisResult>.Fail(new[]
      {
        new GenerationError { Code = "ST3001", Message = $"Analysis file not found: {filePath}" }
      });
    }

    try
    {
      string json = File.ReadAllText(filePath);
      SourceAnalysisResult? analysis = JsonSerializer.Deserialize<SourceAnalysisResult>(json, DeserialiseOptions);

      if (analysis == null || analysis.Tables == null)
      {
        return OperationResult<SourceAnalysisResult>.Fail(new[]
        {
          new GenerationError { Code = "ST3002", Message = $"Failed to deserialise analysis file: {filePath}" }
        });
      }

      return OperationResult<SourceAnalysisResult>.Success(analysis);
    }
    catch (JsonException ex)
    {
      return OperationResult<SourceAnalysisResult>.Fail(new[]
      {
        new GenerationError { Code = "ST3002", Message = $"Failed to deserialise analysis file: {filePath} - {ex.Message}" }
      });
    }
  }
}
