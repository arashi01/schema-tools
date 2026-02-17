using SchemaTools.Diagnostics;

namespace SchemaTools.Annotations;

/// <summary>
/// Indicates the style of a SQL comment.
/// </summary>
internal enum CommentStyle
{
  /// <summary>Single-line comment (<c>-- ...</c>).</summary>
  SingleLine,

  /// <summary>Block comment (<c>/* ... */</c>).</summary>
  Block
}

/// <summary>
/// A normalised comment line extracted from SQL source, with position tracking.
/// </summary>
/// <param name="Location">Source location where the comment body begins.</param>
/// <param name="Body">Comment body text with prefix characters stripped.</param>
/// <param name="Style">Whether this originated from a single-line or block comment.</param>
/// <param name="AssociatedColumnName">
/// For trailing comments on column definitions, the name of the associated column.
/// Null for leading (header) comments.
/// </param>
internal sealed record NormalisedCommentLine(
  SourceLocation Location,
  string Body,
  CommentStyle Style,
  string? AssociatedColumnName = null);

/// <summary>
/// Extracts and normalises SQL comments from source text, handling both
/// single-line (<c>--</c>) and block (<c>/* */</c>) comment styles.
/// Produces a unified sequence of comment lines with position tracking.
/// </summary>
internal static class CommentNormaliser
{
  /// <summary>
  /// Extracts the leading comment block from SQL source text. Reads all
  /// contiguous comment and blank lines before the first SQL statement,
  /// normalising both <c>--</c> and <c>/* */</c> styles.
  /// </summary>
  /// <param name="sqlText">Raw SQL source text.</param>
  /// <param name="sourceFile">File path for diagnostic source locations.</param>
  /// <returns>
  /// A tuple containing the normalised comment lines and a flag indicating
  /// whether an unterminated block comment was detected.
  /// </returns>
  public static (IReadOnlyList<NormalisedCommentLine> Lines, bool HasUnterminatedBlockComment) ExtractLeadingComments(
    string sqlText, string sourceFile)
  {
    List<NormalisedCommentLine> results = new();
    string[] lines = SplitLines(sqlText);
    bool inBlockComment = false;

    for (int i = 0; i < lines.Length; i++)
    {
      string line = lines[i];
      string trimmed = line.TrimStart();
      int lineNumber = i + 1;

      if (inBlockComment)
      {
        int closeIndex = line.IndexOf("*/", StringComparison.Ordinal);
        if (closeIndex >= 0)
        {
          // Content before */ on the closing line
          string bodyText = line[..closeIndex].Trim();
          bodyText = StripBlockCommentLinePrefix(bodyText);

          if (!string.IsNullOrWhiteSpace(bodyText))
          {
            int column = ComputeBodyColumn(line, bodyText);
            results.Add(new NormalisedCommentLine(
              new SourceLocation(sourceFile, lineNumber, column),
              bodyText,
              CommentStyle.Block));
          }

          inBlockComment = false;
          continue;
        }

        // Middle of block comment
        string middleBody = StripBlockCommentLinePrefix(trimmed);

        if (!string.IsNullOrWhiteSpace(middleBody))
        {
          int column = ComputeBodyColumn(line, middleBody);
          results.Add(new NormalisedCommentLine(
            new SourceLocation(sourceFile, lineNumber, column),
            middleBody,
            CommentStyle.Block));
        }

        continue;
      }

      // Single-line comment
      if (trimmed.StartsWith("--", StringComparison.Ordinal))
      {
        string body = trimmed.Length > 2 ? trimmed[2..].TrimStart() : string.Empty;
        if (!string.IsNullOrEmpty(body))
        {
          int dashIndex = line.IndexOf("--", StringComparison.Ordinal);
          int bodyColumn = line.IndexOf(body, dashIndex + 2, StringComparison.Ordinal) + 1;
          if (bodyColumn < 1)
          {
            bodyColumn = dashIndex + 3;
          }

          results.Add(new NormalisedCommentLine(
            new SourceLocation(sourceFile, lineNumber, bodyColumn),
            body,
            CommentStyle.SingleLine));
        }

        continue;
      }

      // Block comment opening
      if (trimmed.StartsWith("/*", StringComparison.Ordinal))
      {
        int openIndex = line.IndexOf("/*", StringComparison.Ordinal);

        // Check for same-line close
        int closeIndex = trimmed.IndexOf("*/", 2, StringComparison.Ordinal);
        if (closeIndex >= 0)
        {
          string body = trimmed[2..closeIndex].Trim();
          if (!string.IsNullOrWhiteSpace(body))
          {
            results.Add(new NormalisedCommentLine(
              new SourceLocation(sourceFile, lineNumber, openIndex + 3),
              body,
              CommentStyle.Block));
          }

          continue;
        }

        // Multi-line block comment starts
        inBlockComment = true;
        string afterOpen = trimmed[2..].TrimStart();
        if (!string.IsNullOrWhiteSpace(afterOpen))
        {
          results.Add(new NormalisedCommentLine(
            new SourceLocation(sourceFile, lineNumber, openIndex + 3),
            afterOpen,
            CommentStyle.Block));
        }

        continue;
      }

      // Blank line - continue looking for more comments
      if (string.IsNullOrWhiteSpace(trimmed))
      {
        continue;
      }

      // Non-comment, non-blank line - leading comment block ends
      break;
    }

    return (results, inBlockComment);
  }

  /// <summary>
  /// Extracts trailing single-line comments from column definition lines.
  /// A trailing comment is a <c>--</c> comment appearing after SQL content
  /// on the same line, associated with the column defined on that line.
  /// </summary>
  /// <param name="sqlText">Raw SQL source text.</param>
  /// <param name="sourceFile">File path for diagnostic source locations.</param>
  /// <returns>Comment lines with <see cref="NormalisedCommentLine.AssociatedColumnName"/> populated.</returns>
  public static IReadOnlyList<NormalisedCommentLine> ExtractTrailingComments(
    string sqlText, string sourceFile)
  {
    List<NormalisedCommentLine> results = new();
    string[] lines = SplitLines(sqlText);

    for (int i = 0; i < lines.Length; i++)
    {
      string line = lines[i];
      int lineNumber = i + 1;

      int dashIndex = FindTrailingCommentStart(line);
      if (dashIndex < 0)
      {
        continue;
      }

      string sqlPart = line[..dashIndex].Trim();
      string commentBody = line[(dashIndex + 2)..].TrimStart();

      if (string.IsNullOrWhiteSpace(sqlPart) || string.IsNullOrWhiteSpace(commentBody))
      {
        continue;
      }

      string? columnName = ExtractColumnName(sqlPart);
      if (columnName == null)
      {
        continue;
      }

      int bodyColumn = line.IndexOf(commentBody, dashIndex + 2, StringComparison.Ordinal) + 1;
      if (bodyColumn < 1)
      {
        bodyColumn = dashIndex + 3;
      }

      results.Add(new NormalisedCommentLine(
        new SourceLocation(sourceFile, lineNumber, bodyColumn),
        commentBody,
        CommentStyle.SingleLine,
        columnName));
    }

    return results;
  }

  /// <summary>
  /// Finds the position of <c>--</c> that constitutes a trailing comment
  /// (i.e. appears after SQL content, not inside a string literal).
  /// Returns -1 if no trailing comment is found.
  /// </summary>
  private static int FindTrailingCommentStart(string line)
  {
    bool inString = false;

    for (int i = 0; i < line.Length - 1; i++)
    {
      char c = line[i];

      if (c == '\'')
      {
        inString = !inString;
        continue;
      }

      if (!inString && c == '-' && line[i + 1] == '-')
      {
        // Ensure there is non-whitespace content before the --
        string before = line[..i].Trim();
        if (!string.IsNullOrWhiteSpace(before))
        {
          return i;
        }
      }
    }

    return -1;
  }

  /// <summary>
  /// Extracts the column name from the SQL portion of a column definition line.
  /// Handles both bracketed (<c>[column_name]</c>) and bare identifiers.
  /// </summary>
  private static string? ExtractColumnName(string sqlPart)
  {
    string trimmed = sqlPart.TrimStart();

    if (trimmed.StartsWith("[", StringComparison.Ordinal))
    {
      int closeBracket = trimmed.IndexOf(']');
      if (closeBracket > 1)
      {
        return trimmed[1..closeBracket];
      }
    }
    else
    {
      // Bare identifier: characters until whitespace or special character
      int end = 0;
      while (end < trimmed.Length &&
             !char.IsWhiteSpace(trimmed[end]) &&
             trimmed[end] != ',' &&
             trimmed[end] != '(')
      {
        end++;
      }

      if (end > 0)
      {
        return trimmed[..end];
      }
    }

    return null;
  }

  /// <summary>
  /// Strips the leading <c>*</c> character commonly used in block comment
  /// formatting (e.g. <c> * @description ...</c>).
  /// </summary>
  private static string StripBlockCommentLinePrefix(string text)
  {
    if (text.StartsWith("*", StringComparison.Ordinal) && !text.StartsWith("*/", StringComparison.Ordinal))
    {
      return text[1..].TrimStart();
    }

    return text;
  }

  /// <summary>
  /// Computes the one-based column position of <paramref name="bodyText"/>
  /// within <paramref name="line"/>.
  /// </summary>
  private static int ComputeBodyColumn(string line, string bodyText)
  {
    int index = line.IndexOf(bodyText, StringComparison.Ordinal);
    return index >= 0 ? index + 1 : 1;
  }

  /// <summary>
  /// Splits text into lines, handling both <c>\r\n</c> and <c>\n</c> line endings.
  /// </summary>
  private static string[] SplitLines(string text)
  {
    return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
  }
}
