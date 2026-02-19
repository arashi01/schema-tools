using System.Text;
using Microsoft.Build.Framework;
using SchemaTools.Diagnostics;
using SchemaTools.Models;
using SchemaTools.Utilities;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Generates filtered views for soft-delete tables to simplify application queries.
///
/// Generated views:
/// - Active view (vw_{table}): SELECT * WHERE active = 1
/// - Deleted view (vw_{table}_deleted): SELECT * WHERE active = 0 (optional)
///
/// Benefits:
/// - Application code queries vw_users instead of users WHERE active = 1
/// - Centralised soft-delete filter logic
/// - Query optimiser inlines views (no performance penalty)
/// - Explicit-wins policy: user-defined views take precedence
/// </summary>
public class SqlViewGenerator : MSTask
{
  /// <summary>
  /// Path to source analysis JSON from SchemaSourceAnalyser.
  /// </summary>
  [Required]
  public string AnalysisFile { get; set; } = string.Empty;

  /// <summary>
  /// Output directory for generated view files.
  /// </summary>
  [Required]
  public string OutputDirectory { get; set; } = string.Empty;

  /// <summary>
  /// Naming pattern for active-record views. {table} is replaced with table name.
  /// </summary>
  public string NamingPattern { get; set; } = SchemaToolsDefaults.ViewNamingPattern;

  /// <summary>
  /// Generate companion views for soft-deleted records.
  /// </summary>
  public bool IncludeDeletedViews { get; set; } = false;

  /// <summary>
  /// Naming pattern for deleted-record views. {table} is replaced with table name.
  /// </summary>
  public string DeletedViewNamingPattern { get; set; } = SchemaToolsDefaults.DeletedViewNamingPattern;

  /// <summary>
  /// Force regeneration even if files exist.
  /// </summary>
  public bool Force { get; set; }

  internal SourceAnalysisResult? TestAnalysis { get; set; }

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, "  Active-Record View Generator");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, string.Empty);

      OperationResult<SourceAnalysisResult> analysisResult = AnalysisLoader.Load(AnalysisFile, TestAnalysis);
      if (!analysisResult.IsSuccess)
      {
        DiagnosticReporter.Report(Log, analysisResult.Diagnostics);
        return false;
      }
      SourceAnalysisResult analysis = analysisResult.Value;

      var softDeleteTables = analysis.Tables
        .Where(t => t.HasSoftDelete && t.SoftDeleteMode != SoftDeleteMode.Ignore)
        .ToList();

      if (softDeleteTables.Count == 0)
      {
        Log.LogMessage("No soft-delete tables found - no views needed");
        return true;
      }

      Log.LogMessage(MessageImportance.High, $"Found {softDeleteTables.Count} soft-delete table(s)");

      // Build lookup for explicit views (not in _generated directory)
      var explicitViews = analysis.ExistingViews
        .Where(v => !v.IsGenerated)
        .ToDictionary(v => v.Name, v => v, StringComparer.OrdinalIgnoreCase);

      Directory.CreateDirectory(OutputDirectory);

      int activeGenerated = 0;
      int deletedGenerated = 0;
      int skippedExplicit = 0;
      int skippedExists = 0;

      Log.LogMessage(MessageImportance.High, string.Empty);
      Log.LogMessage(MessageImportance.High, "Generating active-record views:");

      foreach (TableAnalysis table in softDeleteTables)
      {
        string viewName = NamingPattern.Replace("{table}", table.Name);
        string fileName = $"{viewName}.sql";
        string filePath = Path.Combine(OutputDirectory, fileName);

        // EXPLICIT WINS: Check if user has defined this view outside _generated
        if (explicitViews.TryGetValue(viewName, out ExistingView? explicitView))
        {
          Log.LogMessage(MessageImportance.High,
            $"  - Skipped [{viewName}]: Explicit definition at {explicitView.SourceFile}");
          skippedExplicit++;
          continue;
        }

        if (File.Exists(filePath) && !Force)
        {
          Log.LogMessage(MessageImportance.Low, $"  Skipped {viewName}: Already exists");
          skippedExists++;
          continue;
        }

        string viewSql = GenerateActiveView(table, viewName, analysis.Columns);
        File.WriteAllText(filePath, viewSql, Encoding.UTF8);
        Log.LogMessage($"  + {fileName}");
        activeGenerated++;
      }

      if (IncludeDeletedViews)
      {
        Log.LogMessage(MessageImportance.High, string.Empty);
        Log.LogMessage(MessageImportance.High, "Generating deleted-record views:");

        foreach (TableAnalysis table in softDeleteTables)
        {
          string viewName = DeletedViewNamingPattern.Replace("{table}", table.Name);
          string fileName = $"{viewName}.sql";
          string filePath = Path.Combine(OutputDirectory, fileName);

          // EXPLICIT WINS
          if (explicitViews.TryGetValue(viewName, out ExistingView? explicitView))
          {
            Log.LogMessage(MessageImportance.High,
              $"  - Skipped [{viewName}]: Explicit definition at {explicitView.SourceFile}");
            skippedExplicit++;
            continue;
          }

          if (File.Exists(filePath) && !Force)
          {
            Log.LogMessage(MessageImportance.Low, $"  Skipped {viewName}: Already exists");
            skippedExists++;
            continue;
          }

          string viewSql = GenerateDeletedView(table, viewName, analysis.Columns);
          File.WriteAllText(filePath, viewSql, Encoding.UTF8);
          Log.LogMessage($"  + {fileName}");
          deletedGenerated++;
        }
      }

      Log.LogMessage(MessageImportance.High, string.Empty);
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, "  Summary");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, $"Active views:      {activeGenerated}");
      if (IncludeDeletedViews)
        Log.LogMessage(MessageImportance.High, $"Deleted views:     {deletedGenerated}");
      if (skippedExplicit > 0)
        Log.LogMessage(MessageImportance.High, $"Explicit override: {skippedExplicit}");
      if (skippedExists > 0)
        Log.LogMessage(MessageImportance.High, $"Unchanged:         {skippedExists}");
      Log.LogMessage(MessageImportance.High, $"Output dir:        {OutputDirectory}");
      Log.LogMessage(MessageImportance.High, "============================================================");

      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"View generation failed: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }



  private static string GenerateActiveView(TableAnalysis table, string viewName, ColumnConfig columns)
  {
    var sb = new StringBuilder();
    sb.AppendLine("-- =============================================================================");
    sb.AppendLine("-- AUTO-GENERATED BY SCHEMATOOLS - DO NOT EDIT MANUALLY");
    sb.AppendLine("-- =============================================================================");
    sb.AppendLine($"-- Active-record view for [{table.Schema}].[{table.Name}]");
    sb.AppendLine($"-- Filters to records where [{columns.Active}] = {columns.ActiveValue}");
    sb.AppendLine("-- =============================================================================");
    sb.AppendLine();
    sb.AppendLine($"CREATE VIEW [{table.Schema}].[{viewName}]");
    sb.AppendLine("AS");
    sb.AppendLine($"    SELECT *");
    sb.AppendLine($"    FROM [{table.Schema}].[{table.Name}]");
    sb.AppendLine($"    WHERE [{columns.Active}] = {columns.ActiveValue};");
    sb.AppendLine("GO");
    return sb.ToString();
  }

  private static string GenerateDeletedView(TableAnalysis table, string viewName, ColumnConfig columns)
  {
    var sb = new StringBuilder();
    sb.AppendLine("-- =============================================================================");
    sb.AppendLine("-- AUTO-GENERATED BY SCHEMATOOLS - DO NOT EDIT MANUALLY");
    sb.AppendLine("-- =============================================================================");
    sb.AppendLine($"-- Deleted-record view for [{table.Schema}].[{table.Name}]");
    sb.AppendLine($"-- Filters to records where [{columns.Active}] = {columns.InactiveValue}");
    sb.AppendLine("-- =============================================================================");
    sb.AppendLine();
    sb.AppendLine($"CREATE VIEW [{table.Schema}].[{viewName}]");
    sb.AppendLine("AS");
    sb.AppendLine($"    SELECT *");
    sb.AppendLine($"    FROM [{table.Schema}].[{table.Name}]");
    sb.AppendLine($"    WHERE [{columns.Active}] = {columns.InactiveValue};");
    sb.AppendLine("GO");
    return sb.ToString();
  }
}
