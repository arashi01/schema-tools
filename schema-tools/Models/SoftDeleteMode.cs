using System.Text.Json.Serialization;

namespace SchemaTools.Models;

/// <summary>
/// Soft-delete trigger mode for parent tables.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SoftDeleteMode
{
  /// <summary>
  /// Cascade soft-delete to children automatically (default).
  /// When parent.active = 0, all children.active = 0.
  /// </summary>
  Cascade,

  /// <summary>
  /// Restrict soft-delete if active children exist.
  /// Must soft-delete children first before parent.
  /// </summary>
  Restrict,

  /// <summary>
  /// Ignore - no trigger generated for this table.
  /// Table excluded from soft-delete handling entirely.
  /// </summary>
  Ignore
}
