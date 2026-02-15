using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SchemaTools.Utilities;

/// <summary>
/// Creates ScriptDom parsers matched to the configured SQL Server version.
/// </summary>
public static class ParserFactory
{
  /// <summary>
  /// Creates a TSqlParser for the specified SQL Server version string.
  /// Version strings match the DacFx DSP naming convention (e.g. "Sql170" for SQL Server 2025).
  /// </summary>
  /// <exception cref="ArgumentException">Thrown when the version string is not recognised.</exception>
  public static TSqlParser CreateParser(string version)
  {
    return version switch
    {
      "Sql100" => new TSql100Parser(initialQuotedIdentifiers: true),
      "Sql110" => new TSql110Parser(initialQuotedIdentifiers: true),
      "Sql120" => new TSql120Parser(initialQuotedIdentifiers: true),
      "Sql130" => new TSql130Parser(initialQuotedIdentifiers: true),
      "Sql140" => new TSql140Parser(initialQuotedIdentifiers: true),
      "Sql150" => new TSql150Parser(initialQuotedIdentifiers: true),
      "Sql160" => new TSql160Parser(initialQuotedIdentifiers: true),
      "Sql170" => new TSql170Parser(initialQuotedIdentifiers: true),
      _ => throw new ArgumentException($"Unsupported SQL Server version: {version}")
    };
  }
}
