using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SchemaTools.Utilities;

/// <summary>
/// Generates SQL text from ScriptDom AST fragments.
/// TSqlFragment.ToString() returns the type name, not the SQL text --
/// a ScriptGenerator must be used instead.
/// </summary>
/// <remarks>
/// A new <see cref="Sql170ScriptGenerator"/> is created per call to ensure
/// thread safety. Construction cost is negligible compared to the SQL
/// generation work itself.
/// </remarks>
public static class ScriptFragmentFormatter
{
  private static readonly SqlScriptGeneratorOptions GeneratorOptions = new()
  {
    AlignClauseBodies = false,
    IncludeSemicolons = false
  };

  /// <summary>
  /// Converts a TSqlFragment AST node back to its SQL text representation.
  /// </summary>
  public static string ToSql(TSqlFragment? fragment)
  {
    if (fragment == null)
      return string.Empty;

    var generator = new Sql170ScriptGenerator(GeneratorOptions);
    generator.GenerateScript(fragment, out string? sql);
    return sql;
  }
}
