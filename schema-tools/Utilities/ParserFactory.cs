using System.Collections.Concurrent;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Diagnostics;
using SchemaTools.Models;

namespace SchemaTools.Utilities;

/// <summary>
/// Creates and caches ScriptDom parsers matched to the configured SQL Server version.
/// Parsers are safe to share across calls -- <c>TSqlParser.Parse</c> creates fresh
/// internal state on each invocation.
/// </summary>
internal static class ParserFactory
{
  private static readonly ConcurrentDictionary<SqlServerVersion, TSqlParser> Cache = new();

  /// <summary>
  /// Returns a cached <see cref="TSqlParser"/> for the specified SQL Server version.
  /// </summary>
  internal static OperationResult<TSqlParser> CreateParser(SqlServerVersion version)
  {
    if (Cache.TryGetValue(version, out TSqlParser? cached))
    {
      return OperationResult<TSqlParser>.Success(cached);
    }

    TSqlParser? parser = version switch
    {
      SqlServerVersion.Sql100 => new TSql100Parser(initialQuotedIdentifiers: true),
      SqlServerVersion.Sql110 => new TSql110Parser(initialQuotedIdentifiers: true),
      SqlServerVersion.Sql120 => new TSql120Parser(initialQuotedIdentifiers: true),
      SqlServerVersion.Sql130 => new TSql130Parser(initialQuotedIdentifiers: true),
      SqlServerVersion.Sql140 => new TSql140Parser(initialQuotedIdentifiers: true),
      SqlServerVersion.Sql150 => new TSql150Parser(initialQuotedIdentifiers: true),
      SqlServerVersion.Sql160 => new TSql160Parser(initialQuotedIdentifiers: true),
      SqlServerVersion.Sql170 => new TSql170Parser(initialQuotedIdentifiers: true),
      _ => null
    };

    if (parser == null)
    {
      return OperationResult<TSqlParser>.Fail(new[]
      {
        new GenerationError { Code = "ST3003", Message = $"Unsupported SQL Server version: {version}" }
      });
    }

    return OperationResult<TSqlParser>.Success(Cache.GetOrAdd(version, parser));
  }
}
