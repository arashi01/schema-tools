using System.Text.Json;
using SchemaTools.Diagnostics;
using SchemaTools.Models;

namespace SchemaTools.Utilities;

/// <summary>
/// Shared utility for loading <see cref="SchemaMetadata"/> from JSON.
/// Follows the same test-override pattern as <see cref="AnalysisLoader"/>.
/// </summary>
internal static class MetadataLoader
{
  private static readonly JsonSerializerOptions DeserialiseOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  /// <summary>
  /// Loads a <see cref="SchemaMetadata"/> from the given file path,
  /// or returns the test override if provided.
  /// Returns a failed <see cref="OperationResult{T}"/> when the file is
  /// missing or deserialisation fails.
  /// </summary>
  internal static OperationResult<SchemaMetadata> Load(
    string metadataFile,
    SchemaMetadata? testOverride = null)
  {
    if (testOverride != null)
    {
      return OperationResult<SchemaMetadata>.Success(testOverride);
    }

    if (!File.Exists(metadataFile))
    {
      return OperationResult<SchemaMetadata>.Fail(new[]
      {
        new ExtractionError { Code = "ST4001", Message = $"Metadata file not found: {metadataFile}" }
      });
    }

    try
    {
      string json = File.ReadAllText(metadataFile);
      SchemaMetadata? metadata = JsonSerializer.Deserialize<SchemaMetadata>(json, DeserialiseOptions);

      if (metadata == null || metadata.Tables == null)
      {
        return OperationResult<SchemaMetadata>.Fail(new[]
        {
          new ExtractionError { Code = "ST4002", Message = $"Failed to deserialise metadata file: {metadataFile}" }
        });
      }

      return OperationResult<SchemaMetadata>.Success(metadata);
    }
    catch (JsonException ex)
    {
      return OperationResult<SchemaMetadata>.Fail(new[]
      {
        new ExtractionError { Code = "ST4002", Message = $"Failed to deserialise metadata file: {metadataFile} — {ex.Message}" }
      });
    }
  }
}
