using SchemaTools.Configuration;
using SchemaTools.Diagnostics;
using SchemaTools.Models;
using SchemaTools.Utilities;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// MSBuild task that validates schema metadata for correctness and convention
/// compliance. This is the impure shell; all validation logic is in
/// <see cref="SchemaValidation"/>.
/// </summary>
public class SchemaValidator : MSTask
{
  [Microsoft.Build.Framework.Required]
  public string MetadataFile { get; set; } = string.Empty;

  public string ConfigFile { get; set; } = string.Empty;

  // Override validation settings
  public bool? ValidateForeignKeys { get; set; }
  public bool? ValidatePolymorphic { get; set; }
  public bool? ValidateTemporal { get; set; }
  public bool? ValidateAuditColumns { get; set; }
  public bool? EnforceNamingConventions { get; set; }
  public bool? TreatWarningsAsErrors { get; set; }

  internal SchemaToolsConfig? TestConfig { get; set; }
  internal SchemaMetadata? TestMetadata { get; set; }

  private SchemaToolsConfig _config = new();
  private readonly List<string> _errors = new();
  private readonly List<string> _warnings = new();

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "  Schema Validator");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);

      LoadConfiguration();

      SchemaMetadata? metadata = LoadMetadata();
      if (metadata == null)
      {
        return false;
      }

      Log.LogMessage($"Validating {metadata.Tables.Count} tables...");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);

      // Pure core: all validation logic with no I/O or logging
      SchemaValidation.ValidationResult result = SchemaValidation.Validate(
        metadata,
        _config,
        validateForeignKeys: ValidateForeignKeys,
        validatePolymorphic: ValidatePolymorphic,
        validateTemporal: ValidateTemporal,
        validateAuditColumns: ValidateAuditColumns,
        enforceNamingConventions: EnforceNamingConventions);

      _errors.AddRange(result.Errors);
      _warnings.AddRange(result.Warnings);

      // Impure shell: report results via MSBuild logging
      bool treatAsErrors = TreatWarningsAsErrors ?? _config.Validation.TreatWarningsAsErrors;

      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "============================================================");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "  Validation Results");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "============================================================");

      if (_warnings.Count > 0)
      {
        Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"! {_warnings.Count} warning(s):");
        foreach (string warning in _warnings)
        {
          if (treatAsErrors)
            Log.LogError($"  {warning}");
          else
            Log.LogWarning($"  {warning}");
        }
        Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);
      }

      if (_errors.Count > 0)
      {
        Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"X {_errors.Count} error(s):");
        foreach (string error in _errors)
        {
          Log.LogError($"  {error}");
        }
        Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, string.Empty);
        return false;
      }

      if (treatAsErrors && _warnings.Count > 0)
      {
        Log.LogError("Build failed due to warnings (TreatWarningsAsErrors=true)");
        return false;
      }

      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
          $"+ Schema validation passed: {metadata.Tables.Count} tables validated successfully");
      Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "============================================================");

      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Validation failed: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }

  private void LoadConfiguration()
  {
    _config = ConfigurationLoader.Load(ConfigFile, TestConfig);
  }

  private SchemaMetadata? LoadMetadata()
  {
    OperationResult<SchemaMetadata> metadataResult = MetadataLoader.Load(MetadataFile, TestMetadata);

    if (!metadataResult.IsSuccess)
    {
      DiagnosticReporter.Report(Log, metadataResult.Diagnostics);
      return null;
    }

    return metadataResult.Value;
  }

  internal IReadOnlyList<string> ValidationErrors => _errors;
  internal IReadOnlyList<string> ValidationWarnings => _warnings;
}
