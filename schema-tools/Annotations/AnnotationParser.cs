using SchemaTools.Diagnostics;

namespace SchemaTools.Annotations;

/// <summary>
/// Parses SQL annotation comments (<c>@category</c>, <c>@description</c>) from
/// source files, accumulating diagnostics for unknown annotations, duplicates,
/// and malformed comments.
/// </summary>
/// <remarks>
/// Supports a closed vocabulary of annotations; unknown <c>@name</c> tokens
/// produce <see cref="AnnotationWarning"/> diagnostics rather than errors,
/// allowing forward-compatible annotation additions.
/// </remarks>
internal static class AnnotationParser
{
  private static readonly HashSet<string> KnownAnnotations = new(StringComparer.OrdinalIgnoreCase)
  {
    "description",
    "category"
  };

  /// <summary>
  /// Parses all annotations from a SQL source file, extracting table-level
  /// and column-level metadata with full diagnostic accumulation.
  /// </summary>
  /// <param name="sqlText">Raw SQL source text.</param>
  /// <param name="sourceFile">File path for diagnostic source locations.</param>
  /// <returns>
  /// An <see cref="OperationResult{T}"/> containing the parsed annotations
  /// and any diagnostics (warnings for unknown/duplicate annotations,
  /// errors for malformed comments).
  /// </returns>
  public static OperationResult<ParsedAnnotations> Parse(string sqlText, string sourceFile)
  {
    List<SchemaToolsError> diagnostics = new();

    // Extract and validate leading comments
    (IReadOnlyList<NormalisedCommentLine> leadingComments, bool hasUnterminatedBlock) =
      CommentNormaliser.ExtractLeadingComments(sqlText, sourceFile);

    if (hasUnterminatedBlock)
    {
      diagnostics.Add(new AnnotationError
      {
        Code = "ST1003",
        Message = "Unterminated block comment (missing closing */)",
        Location = new SourceLocation(sourceFile, 1, 1)
      });

      return OperationResult<ParsedAnnotations>.Fail(diagnostics);
    }

    // Parse table-level annotations from leading comments
    string? description = null;
    string? category = null;
    string? lastAnnotationKey = null;

    foreach (NormalisedCommentLine comment in leadingComments)
    {
      if (TryExtractAnnotation(comment.Body, out string? key, out string? value))
      {
        if (!KnownAnnotations.Contains(key!))
        {
          diagnostics.Add(new AnnotationWarning
          {
            Code = "ST1001",
            Message = $"Unknown annotation @{key} will be ignored",
            Location = comment.Location
          });

          lastAnnotationKey = null;
          continue;
        }

        if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
        {
          if (description != null)
          {
            diagnostics.Add(new AnnotationWarning
            {
              Code = "ST1004",
              Message = "Duplicate annotation @description; using last value",
              Location = comment.Location
            });
          }

          description = value;
          lastAnnotationKey = "description";
        }
        else if (string.Equals(key, "category", StringComparison.OrdinalIgnoreCase))
        {
          if (category != null)
          {
            diagnostics.Add(new AnnotationWarning
            {
              Code = "ST1004",
              Message = "Duplicate annotation @category; using last value",
              Location = comment.Location
            });
          }

          category = value;
          lastAnnotationKey = "category";
        }
      }
      else if (lastAnnotationKey != null && !string.IsNullOrWhiteSpace(comment.Body))
      {
        // Continuation line: append to the previous annotation value.
        // Only @description supports multi-line continuation; @category is a single token.
        if (string.Equals(lastAnnotationKey, "description", StringComparison.OrdinalIgnoreCase))
        {
          description = description + " " + comment.Body.Trim();
        }
      }
      else if (!string.IsNullOrWhiteSpace(comment.Body))
      {
        // Non-annotation, non-continuation comment line; reset continuation tracking
        lastAnnotationKey = null;
      }
    }

    // Parse column-level annotations from trailing comments
    IReadOnlyList<NormalisedCommentLine> trailingComments =
      CommentNormaliser.ExtractTrailingComments(sqlText, sourceFile);

    List<ColumnAnnotation> columnAnnotations = new();

    foreach (NormalisedCommentLine trailing in trailingComments)
    {
      if (!TryExtractAnnotation(trailing.Body, out string? key, out string? value))
      {
        continue;
      }

      if (!KnownAnnotations.Contains(key!))
      {
        diagnostics.Add(new AnnotationWarning
        {
          Code = "ST1001",
          Message = $"Unknown annotation @{key} will be ignored",
          Location = trailing.Location
        });

        continue;
      }

      if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase) &&
          trailing.AssociatedColumnName != null)
      {
        columnAnnotations.Add(new ColumnAnnotation(trailing.AssociatedColumnName, value));
      }
    }

    ParsedAnnotations result = new(description, category, columnAnnotations);

    if (diagnostics.Count > 0)
    {
      return OperationResult<ParsedAnnotations>.WithWarnings(result, diagnostics);
    }

    return OperationResult<ParsedAnnotations>.Success(result);
  }

  /// <summary>
  /// Attempts to extract an annotation key-value pair from a comment body.
  /// An annotation starts with <c>@</c> followed by a key name.
  /// </summary>
  /// <returns><see langword="true"/> if an annotation was found.</returns>
  private static bool TryExtractAnnotation(string body, out string? key, out string? value)
  {
    key = null;
    value = null;

    if (!body.StartsWith("@", StringComparison.Ordinal))
    {
      return false;
    }

    int spaceIndex = body.IndexOf(' ', 1);
    if (spaceIndex < 0)
    {
      // Bare annotation with no value (e.g. "@description")
      key = body[1..];
      value = string.Empty;
      return true;
    }

    key = body[1..spaceIndex];
    value = body[(spaceIndex + 1)..].Trim();
    return true;
  }
}
