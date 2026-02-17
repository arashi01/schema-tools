using System.Text.Json.Serialization;

namespace SchemaTools.Models;

/// <summary>
/// SQL Server language version for ScriptDom parsing.
/// Values match the DacFx DSP naming convention.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SqlServerVersion
{
  /// <summary>SQL Server 2008.</summary>
  Sql100,

  /// <summary>SQL Server 2012.</summary>
  Sql110,

  /// <summary>SQL Server 2014.</summary>
  Sql120,

  /// <summary>SQL Server 2016.</summary>
  Sql130,

  /// <summary>SQL Server 2017.</summary>
  Sql140,

  /// <summary>SQL Server 2019.</summary>
  Sql150,

  /// <summary>SQL Server 2022.</summary>
  Sql160,

  /// <summary>SQL Server 2025.</summary>
  Sql170
}
