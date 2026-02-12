using System.Text.RegularExpressions;

namespace SchemaTools.Utilities;

/// <summary>
/// Extracts descriptions from SQL comments.
/// </summary>
public static class SqlCommentParser
{
  private static readonly Regex DescriptionPattern =
      new(@"--\s*@description\s+(.+)$", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

  private static readonly Regex CategoryPattern =
      new(@"--\s*@category\s+(.+)$", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

  public static string? ExtractDescription(string sqlText)
  {
    Match match = DescriptionPattern.Match(sqlText);
    return match.Success ? match.Groups[1].Value.Trim() : null;
  }

  public static string? ExtractCategory(string sqlText)
  {
    Match match = CategoryPattern.Match(sqlText);
    return match.Success ? match.Groups[1].Value.Trim() : null;
  }
}
