using System.Text.Json.Serialization;

namespace SchemaTools.Configuration;

/// <summary>
/// Controls the inclusion and rendering of temporal history tables in generated documentation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HistoryTableMode
{
  /// <summary>
  /// History tables are excluded from documentation entirely.
  /// Source tables still reference their history table by name.
  /// </summary>
  None,

  /// <summary>
  /// A single summary table lists all history tables with their source table and column count.
  /// No per-table detail sections are generated.
  /// </summary>
  Compact,

  /// <summary>
  /// Each history table receives full documentation identical to authored tables.
  /// </summary>
  Full
}
