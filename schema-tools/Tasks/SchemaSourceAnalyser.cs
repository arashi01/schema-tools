using System.Reflection;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Models;
using SchemaTools.Utilities;
using SchemaTools.Visitors;
using MSTask = Microsoft.Build.Utilities.Task;

namespace SchemaTools.Tasks;

/// <summary>
/// Pre-build MSBuild task that analyses SQL source files to extract patterns 
/// needed for code generation (triggers, procedures). This runs BEFORE SqlBuild.
/// For authoritative metadata extraction, use SchemaMetadataExtractor post-build.
/// </summary>
public class SchemaSourceAnalyser : MSTask
{
  /// <summary>
  /// SQL files to analyse (typically @(Build) items from the SQL project)
  /// </summary>
  public ITaskItem[]? SqlFiles { get; set; }

  /// <summary>
  /// Fallback directory to scan if SqlFiles is not provided
  /// </summary>
  public string TablesDirectory { get; set; } = string.Empty;

  /// <summary>
  /// Output file for analysis results (intermediate, used by code generators)
  /// </summary>
  [Required]
  public string AnalysisOutput { get; set; } = string.Empty;

  /// <summary>
  /// Directory where generated triggers are placed. Files in this directory
  /// are considered "owned" by SchemaTools and can be regenerated.
  /// </summary>
  public string GeneratedTriggersDirectory { get; set; } = string.Empty;

  /// <summary>
  /// Directory where generated views are placed. Files in this directory
  /// are considered "owned" by SchemaTools and can be regenerated.
  /// </summary>
  public string GeneratedViewsDirectory { get; set; } = string.Empty;

  /// <summary>
  /// Configuration file path
  /// </summary>
  public string ConfigFile { get; set; } = string.Empty;

  // Fallback defaults (used when config file not provided)
  public string SqlServerVersion { get; set; } = SchemaToolsDefaults.SqlServerVersion;
  public string DefaultSchema { get; set; } = SchemaToolsDefaults.DefaultSchema;

  // Testing support
  internal SchemaToolsConfig? TestConfig { get; set; }

  private SchemaToolsConfig _config = new();

  public override bool Execute()
  {
    try
    {
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, "  Schema Source Analyser (Pre-Build)");
      Log.LogMessage(MessageImportance.High, "============================================================");
      Log.LogMessage(MessageImportance.High, string.Empty);

      LoadConfiguration();

      List<string> sqlFiles = ResolveSqlFiles();
      if (sqlFiles.Count == 0)
      {
        Log.LogWarning("No SQL files to analyse");
        return true;
      }

      Log.LogMessage(MessageImportance.High, $"Analysing {sqlFiles.Count} SQL file(s)...");

      TSqlParser parser = ParserFactory.CreateParser(_config.SqlServerVersion);
      var analysis = new SourceAnalysisResult
      {
        Version = GetAssemblyVersion(),
        AnalysedAt = DateTime.UtcNow,
        SqlServerVersion = _config.SqlServerVersion,
        DefaultSchema = _config.DefaultSchema,
        GeneratedTriggersDirectory = GeneratedTriggersDirectory,
        GeneratedViewsDirectory = GeneratedViewsDirectory,
        Columns = new ColumnConfig
        {
          Active = _config.Columns.Active,
          ActiveValue = _config.Columns.ActiveValue,
          InactiveValue = _config.Columns.InactiveValue,
          UpdatedBy = _config.Columns.UpdatedBy,
          UpdatedByType = _config.Columns.UpdatedByType,
          ValidFrom = _config.Columns.ValidFrom,
          ValidTo = _config.Columns.ValidTo
        },
        Features = new FeatureFlags
        {
          GenerateReactivationGuards = _config.Features.GenerateReactivationGuards
        }
      };

      int tablesParsed = 0;

      foreach (string sqlFile in sqlFiles)
      {
        try
        {
          // Analyse tables
          TableAnalysis? tableAnalysis = AnalyseTableFile(sqlFile, parser);
          if (tableAnalysis != null)
          {
            analysis.Tables.Add(tableAnalysis);
            tablesParsed++;
          }

          // Also discover existing triggers in this file
          DiscoverExistingTriggers(sqlFile, parser, analysis);

          // Also discover existing views in this file
          DiscoverExistingViews(sqlFile, parser, analysis);
        }
        catch (Exception ex)
        {
          Log.LogWarning($"Failed to analyse {Path.GetFileName(sqlFile)}: {ex.Message}");
        }
      }

      // Build FK dependency graph for cascade analysis
      BuildForeignKeyGraph(analysis);

      // Detect leaf tables (tables with no children referencing them)
      DetectLeafTables(analysis);

      // Ensure output directory exists
      string? outputDir = Path.GetDirectoryName(AnalysisOutput);
      if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
      {
        Directory.CreateDirectory(outputDir);
      }

      // Serialise analysis
      var options = new JsonSerializerOptions
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
      };

      string json = JsonSerializer.Serialize(analysis, options);
      File.WriteAllText(AnalysisOutput, json, System.Text.Encoding.UTF8);

      Log.LogMessage(MessageImportance.High, string.Empty);
      Log.LogMessage(MessageImportance.High, $"+ Analysed {tablesParsed} tables");
      Log.LogMessage(MessageImportance.High, $"  Soft-delete: {analysis.Tables.Count(t => t.HasSoftDelete)}");
      Log.LogMessage(MessageImportance.High, $"  Leaf tables: {analysis.Tables.Count(t => t.IsLeafTable)}");
      int explicitTriggers = analysis.ExistingTriggers.Count(t => !t.IsGenerated);
      if (explicitTriggers > 0)
      {
        Log.LogMessage(MessageImportance.High, $"  Explicit triggers: {explicitTriggers}");
      }
      int explicitViews = analysis.ExistingViews.Count(v => !v.IsGenerated);
      if (explicitViews > 0)
      {
        Log.LogMessage(MessageImportance.High, $"  Explicit views: {explicitViews}");
      }
      Log.LogMessage(MessageImportance.High, $"  Output: {AnalysisOutput}");

      return true;
    }
    catch (Exception ex)
    {
      Log.LogError($"Source analysis failed: {ex.Message}");
      Log.LogErrorFromException(ex, showStackTrace: true);
      return false;
    }
  }

  private void LoadConfiguration()
  {
    if (TestConfig != null)
    {
      _config = TestConfig;
      return;
    }

    if (!string.IsNullOrEmpty(ConfigFile) && File.Exists(ConfigFile))
    {
      string json = File.ReadAllText(ConfigFile);
      _config = JsonSerializer.Deserialize<SchemaToolsConfig>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      }) ?? new SchemaToolsConfig();
    }
    else
    {
      _config = new SchemaToolsConfig
      {
        SqlServerVersion = SqlServerVersion,
        DefaultSchema = DefaultSchema
      };
    }
  }

  private List<string> ResolveSqlFiles()
  {
    // Explicit file list takes precedence (from @(Build) items)
    if (SqlFiles != null && SqlFiles.Length > 0)
    {
      return SqlFiles
        .Select(item => item.GetMetadata("FullPath"))
        .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
        .OrderBy(f => f)
        .ToList();
    }

    // Fallback: scan directory
    if (!string.IsNullOrEmpty(TablesDirectory) && Directory.Exists(TablesDirectory))
    {
      return Directory.GetFiles(TablesDirectory, "*.sql", SearchOption.TopDirectoryOnly)
        .OrderBy(f => f)
        .ToList();
    }

    return new List<string>();
  }

  private TableAnalysis? AnalyseTableFile(string filePath, TSqlParser parser)
  {
    string sqlText = File.ReadAllText(filePath);

    using var reader = new StringReader(sqlText);
    TSqlFragment fragment = parser.Parse(reader, out IList<ParseError>? errors);

    if (errors != null && errors.Count > 0)
    {
      Log.LogMessage(MessageImportance.Low, $"Parse warnings in {Path.GetFileName(filePath)}");
    }

    var visitor = new TableMetadataVisitor();
    fragment.Accept(visitor);

    if (string.IsNullOrEmpty(visitor.TableName))
    {
      return null;
    }

    string? category = SqlCommentParser.ExtractCategory(sqlText);
    SchemaToolsConfig effective = _config.ResolveForTable(visitor.TableName!, category);

    var analysis = new TableAnalysis
    {
      Name = visitor.TableName!,
      Schema = visitor.SchemaName ?? effective.DefaultSchema,
      Category = category,
      SourceFile = filePath
    };

    // Detect active column (soft-delete indicator)
    string activeColumnName = effective.Columns.Active;
    bool hasActiveColumn = visitor.ColumnDefinitions.Any(c =>
      string.Equals(c.ColumnIdentifier.Value, activeColumnName, StringComparison.OrdinalIgnoreCase));

    analysis.HasActiveColumn = hasActiveColumn;
    analysis.HasTemporalVersioning = visitor.HasTemporalVersioning;

    // Populate temporal table details if detected
    if (visitor.HasTemporalVersioning)
    {
      string historySchema = visitor.HistorySchemaName ?? effective.DefaultSchema;
      if (!string.IsNullOrEmpty(visitor.HistoryTableName))
      {
        analysis.HistoryTable = $"[{historySchema}].[{visitor.HistoryTableName}]";
      }
      analysis.ValidFromColumn = effective.Columns.ValidFrom;
      analysis.ValidToColumn = effective.Columns.ValidTo;
    }

    // Soft-delete = has active column + temporal versioning
    if (effective.Features.EnableSoftDelete && hasActiveColumn && visitor.HasTemporalVersioning)
    {
      analysis.HasSoftDelete = true;
      analysis.ActiveColumnName = activeColumnName;
      analysis.SoftDeleteMode = effective.Features.SoftDeleteMode;
      analysis.ReactivationCascade = effective.Features.ReactivationCascade;
      analysis.ReactivationCascadeToleranceMs = effective.Features.ReactivationCascadeToleranceMs;
    }

    // Extract primary key
    foreach (ConstraintDefinition constraint in visitor.Constraints)
    {
      if (constraint is UniqueConstraintDefinition unique && unique.IsPrimaryKey)
      {
        analysis.PrimaryKeyColumns = unique.Columns
          .Select(c => c.Column.MultiPartIdentifier.Identifiers.Last().Value)
          .ToList();
        break;
      }
    }

    // Also check inline PK constraints
    if (analysis.PrimaryKeyColumns.Count == 0)
    {
      foreach (ColumnDefinition colDef in visitor.ColumnDefinitions)
      {
        foreach (ConstraintDefinition constraint in colDef.Constraints)
        {
          if (constraint is UniqueConstraintDefinition { IsPrimaryKey: true })
          {
            analysis.PrimaryKeyColumns.Add(colDef.ColumnIdentifier.Value);
            break;
          }
        }
      }
    }

    // Extract FK references (for dependency graph)
    foreach (ConstraintDefinition constraint in visitor.Constraints)
    {
      if (constraint is ForeignKeyConstraintDefinition fk)
      {
        analysis.ForeignKeyReferences.Add(new ForeignKeyRef
        {
          ReferencedTable = fk.ReferenceTableName.BaseIdentifier.Value,
          ReferencedSchema = fk.ReferenceTableName.SchemaIdentifier?.Value ?? _config.DefaultSchema,
          Columns = fk.Columns.Select(c => c.Value).ToList(),
          ReferencedColumns = fk.ReferencedTableColumns.Select(c => c.Value).ToList(),
          OnDelete = fk.DeleteAction.ToString()
        });
      }
    }

    return analysis;
  }

  private void DiscoverExistingTriggers(string filePath, TSqlParser parser, SourceAnalysisResult analysis)
  {
    string sqlText = File.ReadAllText(filePath);
    using var reader = new StringReader(sqlText);
    TSqlFragment fragment = parser.Parse(reader, out _);

    var visitor = new TriggerDiscoveryVisitor();
    fragment.Accept(visitor);

    if (visitor.Triggers.Count == 0)
    {
      return;
    }

    // Determine if this file is in the generated directory
    string normalisedPath = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
    string normalisedGenDir = string.IsNullOrEmpty(GeneratedTriggersDirectory)
      ? string.Empty
      : Path.GetFullPath(GeneratedTriggersDirectory).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    bool isInGeneratedDir = !string.IsNullOrEmpty(normalisedGenDir) &&
                            normalisedPath.StartsWith(normalisedGenDir, StringComparison.OrdinalIgnoreCase);

    foreach (DiscoveredTrigger trigger in visitor.Triggers)
    {
      analysis.ExistingTriggers.Add(new ExistingTrigger
      {
        Name = trigger.Name,
        Schema = trigger.Schema ?? _config.DefaultSchema,
        TargetTable = trigger.TargetTable ?? string.Empty,
        SourceFile = filePath,
        IsGenerated = isInGeneratedDir
      });

      if (!isInGeneratedDir)
      {
        Log.LogMessage(MessageImportance.Low,
          $"  Discovered explicit trigger: [{trigger.Schema ?? _config.DefaultSchema}].[{trigger.Name}] in {Path.GetFileName(filePath)}");
      }
    }
  }

  private void DiscoverExistingViews(string filePath, TSqlParser parser, SourceAnalysisResult analysis)
  {
    string sqlText = File.ReadAllText(filePath);
    using var reader = new StringReader(sqlText);
    TSqlFragment fragment = parser.Parse(reader, out _);

    var visitor = new ViewDiscoveryVisitor();
    fragment.Accept(visitor);

    if (visitor.Views.Count == 0)
    {
      return;
    }

    // Determine if this file is in the generated directory
    string normalisedPath = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
    string normalisedGenDir = string.IsNullOrEmpty(GeneratedViewsDirectory)
      ? string.Empty
      : Path.GetFullPath(GeneratedViewsDirectory).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    bool isInGeneratedDir = !string.IsNullOrEmpty(normalisedGenDir) &&
                            normalisedPath.StartsWith(normalisedGenDir, StringComparison.OrdinalIgnoreCase);

    foreach (DiscoveredView view in visitor.Views)
    {
      analysis.ExistingViews.Add(new ExistingView
      {
        Name = view.Name,
        Schema = view.Schema ?? _config.DefaultSchema,
        SourceFile = filePath,
        IsGenerated = isInGeneratedDir
      });

      if (!isInGeneratedDir)
      {
        Log.LogMessage(MessageImportance.Low,
          $"  Discovered explicit view: [{view.Schema ?? _config.DefaultSchema}].[{view.Name}] in {Path.GetFileName(filePath)}");
      }
    }
  }

  private static void BuildForeignKeyGraph(SourceAnalysisResult analysis)
  {
    // Build lookup for quick access
    var tablesByName = analysis.Tables.ToDictionary(
      t => $"[{t.Schema}].[{t.Name}]",
      t => t,
      StringComparer.OrdinalIgnoreCase);

    // For each FK, record the parent table's children
    foreach (TableAnalysis table in analysis.Tables)
    {
      foreach (ForeignKeyRef fkRef in table.ForeignKeyReferences)
      {
        string parentKey = $"[{fkRef.ReferencedSchema}].[{fkRef.ReferencedTable}]";
        if (tablesByName.TryGetValue(parentKey, out TableAnalysis? parentTable))
        {
          parentTable.ChildTables.Add(table.Name);
        }
      }
    }
  }

  private static void DetectLeafTables(SourceAnalysisResult analysis)
  {
    // Leaf tables are tables that have no children referencing them
    foreach (TableAnalysis table in analysis.Tables)
    {
      table.IsLeafTable = table.ChildTables.Count == 0;
    }
  }

  private static string GetAssemblyVersion()
  {
    return typeof(SchemaSourceAnalyser).Assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
      ?? typeof(SchemaSourceAnalyser).Assembly.GetName().Version?.ToString()
      ?? "0.0.0";
  }
}
