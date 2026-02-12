using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SchemaTools.Utilities;

/// <summary>
/// Generates SQL text from ScriptDom AST fragments.
/// TSqlFragment.ToString() returns the type name, not the SQL text —
/// a ScriptGenerator must be used instead.
/// </summary>
public static class ScriptFragmentFormatter
{
  private static readonly Sql160ScriptGenerator Generator = new(
      new SqlScriptGeneratorOptions
      {
        AlignClauseBodies = false,
        IncludeSemicolons = false
      });

  /// <summary>
  /// Converts a TSqlFragment AST node back to its SQL text representation.
  /// </summary>
  public static string ToSql(TSqlFragment? fragment)
  {
    if (fragment == null)
      return string.Empty;

    Generator.GenerateScript(fragment, out string? sql);
    return sql;
  }
}
