using Microsoft.Build.Framework;
using Microsoft.SqlServer.Dac.Model;
using SchemaTools.Extraction;
using SchemaTools.Models;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Post-build MSBuild task that extracts authoritative schema metadata from a compiled .dacpac.
/// This is a thin wrapper around <see cref="DacpacMetadataEngine"/>; the core extraction logic
/// is decoupled from MSBuild so it can also run as a standalone CLI process.
/// </summary>
/// <remarks>
/// Only used as an in-process task under .NET Core MSBuild (dotnet build).
/// Full Framework MSBuild (msbuild.exe / Visual Studio) invokes the CLI entry point
/// via dotnet exec to avoid DacFx assembly identity collisions with SSDT.
/// </remarks>
public class SchemaMetadataExtractor : MSTask
{
  /// <summary>
  /// Path to the compiled .dacpac file ($(SchemaToolsDacpacPath)).
  /// </summary>
  [Required]
  public string DacpacPath { get; set; } = string.Empty;

  /// <summary>
  /// Output file path for extracted metadata JSON.
  /// </summary>
  [Required]
  public string OutputFile { get; set; } = string.Empty;

  /// <summary>
  /// Configuration file path (schema-tools.json).
  /// </summary>
  public string ConfigFile { get; set; } = string.Empty;

  /// <summary>
  /// Database name for metadata.
  /// </summary>
  public string DatabaseName { get; set; } = "Database";

  /// <summary>Override configuration for testing.</summary>
  internal SchemaToolsConfig? TestConfig { get; set; }

  /// <summary>Override model for testing (bypasses .dacpac file loading).</summary>
  internal TSqlModel? TestModel { get; set; }

  public override bool Execute()
  {
    var engine = new DacpacMetadataEngine(
      DacpacPath,
      OutputFile,
      ConfigFile,
      DatabaseName,
      info: msg => Log.LogMessage(MessageImportance.High, msg),
      verbose: msg => Log.LogMessage(MessageImportance.Low, msg),
      warning: msg => Log.LogWarning(msg),
      error: msg => Log.LogError(msg))
    {
      OverrideConfig = TestConfig,
      OverrideModel = TestModel
    };

    return engine.Execute();
  }
}
