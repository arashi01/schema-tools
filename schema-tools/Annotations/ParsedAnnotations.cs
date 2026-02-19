namespace SchemaTools.Annotations;

/// <summary>
/// Result of parsing all annotations from a SQL source file.
/// Contains table-level annotations (description, category) and
/// column-level annotations extracted from trailing comments.
/// </summary>
/// <param name="Description">Table description from <c>@description</c> annotation, or null if absent.</param>
/// <param name="Category">Table category from <c>@category</c> annotation, or null if absent.</param>
/// <param name="ColumnAnnotations">Column-level annotations extracted from trailing comments.</param>
internal sealed record ParsedAnnotations(
  string? Description,
  string? Category,
  IReadOnlyList<ColumnAnnotation> ColumnAnnotations);

/// <summary>
/// A column-level annotation extracted from a trailing comment on a column definition line.
/// </summary>
/// <param name="ColumnName">The name of the column this annotation is associated with.</param>
/// <param name="Description">The description text from the trailing <c>@description</c> annotation.</param>
internal sealed record ColumnAnnotation(
  string ColumnName,
  string? Description);
