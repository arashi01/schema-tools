using Microsoft.Build.Framework;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Annotations;
using SchemaTools.Configuration;
using SchemaTools.Diagnostics;
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
  public string SqlServerVersion { get; set; } = nameof(Models.SqlServerVersion.Sql170);
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

      OperationResult<TSqlParser> parserResult = ParserFactory.CreateParser(_config.SqlServerVersion);
      if (!parserResult.IsSuccess)
      {
        DiagnosticReporter.Report(Log, parserResult.Diagnostics);
        return false;
      }
      TSqlParser parser = parserResult.Value;

      var tableList = new List<TableAnalysis>();
      var triggerList = new List<ExistingTrigger>();
      var viewList = new List<ExistingView>();
      int tablesParsed = 0;

      foreach (string sqlFile in sqlFiles)
      {
        try
        {
          TableAnalysis? tableAnalysis = AnalyseTableFile(sqlFile, parser);
          if (tableAnalysis != null)
          {
            tableList.Add(tableAnalysis);
            tablesParsed++;
          }

          DiscoverExistingTriggers(sqlFile, parser, triggerList);
          DiscoverExistingViews(sqlFile, parser, viewList);
        }
        catch (Exception ex)
        {
          Log.LogWarning($"Failed to analyse {Path.GetFileName(sqlFile)}: {ex.Message}");
        }
      }

      tableList = BuildForeignKeyGraph(tableList);
      tableList = DetectLeafTables(tableList);

      SourceAnalysisResult analysis = new SourceAnalysisResult
      {
        ToolVersion = GenerationUtilities.GetToolVersion(),
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
        },
        Tables = tableList,
        ExistingTriggers = triggerList,
        ExistingViews = viewList
      };

      GenerationUtilities.WriteJson(AnalysisOutput, analysis);

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
    SchemaToolsConfig fallback = new()
    {
      SqlServerVersion = Enum.TryParse<Models.SqlServerVersion>(SqlServerVersion, out Models.SqlServerVersion parsed)
        ? parsed
        : Models.SqlServerVersion.Sql170,
      DefaultSchema = DefaultSchema
    };

    _config = ConfigurationLoader.Load(ConfigFile, TestConfig, fallback);
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

    // Skip temporary tables (# and ## prefixed names found in stored procedure scripts)
    if (visitor.TableName!.StartsWith("#"))
    {
      return null;
    }

    OperationResult<ParsedAnnotations> annotationResult = AnnotationParser.Parse(sqlText, filePath);
    ParsedAnnotations annotations = annotationResult.IsSuccess || !annotationResult.HasErrors
      ? annotationResult.Value
      : new ParsedAnnotations(null, null, []);

    // Report annotation diagnostics (warnings/errors) via MSBuild
    if (annotationResult.Diagnostics.Count > 0)
    {
      DiagnosticReporter.Report(Log, annotationResult.Diagnostics);
    }

    string? category = annotations.Category;
    SchemaToolsConfig effective = _config.ResolveForTable(visitor.TableName!, category);

    // Wire column-level descriptions
    Dictionary<string, string>? columnDescriptions = null;
    if (annotations.ColumnAnnotations.Count > 0)
    {
      var colDescs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (ColumnAnnotation colAnnotation in annotations.ColumnAnnotations)
      {
        if (!string.IsNullOrEmpty(colAnnotation.Description))
        {
          colDescs[colAnnotation.ColumnName] = colAnnotation.Description!;
        }
      }

      if (colDescs.Count > 0)
      {
        columnDescriptions = colDescs;
      }
    }

    string activeColumnName = effective.Columns.Active;
    bool hasActiveColumn = visitor.ColumnDefinitions.Any(c =>
      string.Equals(c.ColumnIdentifier.Value, activeColumnName, StringComparison.OrdinalIgnoreCase));

    // Temporal versioning
    string? historyTable = null;
    string? validFromColumn = null;
    string? validToColumn = null;

    if (visitor.HasTemporalVersioning)
    {
      string historySchema = visitor.HistorySchemaName ?? effective.DefaultSchema;
      if (!string.IsNullOrEmpty(visitor.HistoryTableName))
      {
        historyTable = $"[{historySchema}].[{visitor.HistoryTableName}]";
      }
      validFromColumn = effective.Columns.ValidFrom;
      validToColumn = effective.Columns.ValidTo;
    }

    // Soft-delete detection
    bool hasSoftDelete = false;
    string? softDeleteActiveColumn = null;
    SoftDeleteMode softDeleteMode = SoftDeleteMode.Cascade;
    bool reactivationCascade = false;
    int reactivationCascadeToleranceMs = SchemaToolsDefaults.ReactivationCascadeToleranceMs;

    if (effective.Features.EnableSoftDelete && hasActiveColumn && visitor.HasTemporalVersioning)
    {
      hasSoftDelete = true;
      softDeleteActiveColumn = activeColumnName;
      softDeleteMode = effective.Features.SoftDeleteMode;
      reactivationCascade = effective.Features.ReactivationCascade;
      reactivationCascadeToleranceMs = effective.Features.ReactivationCascadeToleranceMs;
    }

    // Primary key columns
    var primaryKeyColumns = new List<string>();
    foreach (ConstraintDefinition constraint in visitor.Constraints)
    {
      if (constraint is UniqueConstraintDefinition unique && unique.IsPrimaryKey)
      {
        primaryKeyColumns = unique.Columns
          .Select(c => c.Column.MultiPartIdentifier.Identifiers.Last().Value)
          .ToList();
        break;
      }
    }

    // Inline PK constraints (fallback for single-column syntax)
    if (primaryKeyColumns.Count == 0)
    {
      foreach (ColumnDefinition colDef in visitor.ColumnDefinitions)
      {
        foreach (ConstraintDefinition constraint in colDef.Constraints)
        {
          if (constraint is UniqueConstraintDefinition { IsPrimaryKey: true })
          {
            primaryKeyColumns.Add(colDef.ColumnIdentifier.Value);
            break;
          }
        }
      }
    }

    // Foreign key references
    var foreignKeyReferences = new List<ForeignKeyRef>();
    foreach (ConstraintDefinition constraint in visitor.Constraints)
    {
      if (constraint is ForeignKeyConstraintDefinition fk)
      {
        foreignKeyReferences.Add(new ForeignKeyRef
        {
          ReferencedTable = fk.ReferenceTableName.BaseIdentifier.Value,
          ReferencedSchema = fk.ReferenceTableName.SchemaIdentifier?.Value ?? _config.DefaultSchema,
          Columns = fk.Columns.Select(c => c.Value).ToList(),
          ReferencedColumns = fk.ReferencedTableColumns.Select(c => c.Value).ToList(),
          OnDelete = ScriptDomParser.ConvertDeleteUpdateAction(fk.DeleteAction)
        });
      }
    }

    return new TableAnalysis
    {
      Name = visitor.TableName!,
      Schema = visitor.SchemaName ?? effective.DefaultSchema,
      Category = category,
      Description = annotations.Description,
      SourceFile = filePath,
      ColumnDescriptions = columnDescriptions,
      HasActiveColumn = hasActiveColumn,
      HasTemporalVersioning = visitor.HasTemporalVersioning,
      HistoryTable = historyTable,
      ValidFromColumn = validFromColumn,
      ValidToColumn = validToColumn,
      HasSoftDelete = hasSoftDelete,
      ActiveColumnName = softDeleteActiveColumn,
      SoftDeleteMode = softDeleteMode,
      ReactivationCascade = reactivationCascade,
      ReactivationCascadeToleranceMs = reactivationCascadeToleranceMs,
      PrimaryKeyColumns = primaryKeyColumns,
      ForeignKeyReferences = foreignKeyReferences
    };
  }

  private void DiscoverExistingTriggers(string filePath, TSqlParser parser, List<ExistingTrigger> triggers)
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

    string normalisedPath = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
    string normalisedGenDir = string.IsNullOrEmpty(GeneratedTriggersDirectory)
      ? string.Empty
      : Path.GetFullPath(GeneratedTriggersDirectory).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    bool isInGeneratedDir = !string.IsNullOrEmpty(normalisedGenDir) &&
                            normalisedPath.StartsWith(normalisedGenDir, StringComparison.OrdinalIgnoreCase);

    foreach (DiscoveredTrigger trigger in visitor.Triggers)
    {
      triggers.Add(new ExistingTrigger
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

  private void DiscoverExistingViews(string filePath, TSqlParser parser, List<ExistingView> views)
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

    string normalisedPath = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
    string normalisedGenDir = string.IsNullOrEmpty(GeneratedViewsDirectory)
      ? string.Empty
      : Path.GetFullPath(GeneratedViewsDirectory).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    bool isInGeneratedDir = !string.IsNullOrEmpty(normalisedGenDir) &&
                            normalisedPath.StartsWith(normalisedGenDir, StringComparison.OrdinalIgnoreCase);

    foreach (DiscoveredView view in visitor.Views)
    {
      views.Add(new ExistingView
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

  private static List<TableAnalysis> BuildForeignKeyGraph(List<TableAnalysis> tables)
  {
    // Build a map from qualified name to index for parent lookup
    var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < tables.Count; i++)
    {
      indexByName[$"[{tables[i].Schema}].[{tables[i].Name}]"] = i;
    }

    // Accumulate child table names per parent index
    var childrenByIndex = new Dictionary<int, List<string>>();

    // For each FK, record the parent table's children.
    // A child with multiple FKs to the same parent (e.g. created_by and updated_by
    // both referencing users.id) must only appear once in the parent's ChildTables.
    foreach (TableAnalysis table in tables)
    {
      foreach (ForeignKeyRef fkRef in table.ForeignKeyReferences)
      {
        string parentKey = $"[{fkRef.ReferencedSchema}].[{fkRef.ReferencedTable}]";
        if (indexByName.TryGetValue(parentKey, out int parentIndex))
        {
          if (!childrenByIndex.TryGetValue(parentIndex, out List<string>? children))
          {
            children = new List<string>();
            childrenByIndex[parentIndex] = children;
          }

          if (!children.Contains(table.Name))
          {
            children.Add(table.Name);
          }
        }
      }
    }

    // Produce new list with ChildTables populated
    var result = new List<TableAnalysis>(tables.Count);
    for (int i = 0; i < tables.Count; i++)
    {
      if (childrenByIndex.TryGetValue(i, out List<string>? children))
      {
        result.Add(tables[i] with { ChildTables = children });
      }
      else
      {
        result.Add(tables[i]);
      }
    }

    return result;
  }

  private static List<TableAnalysis> DetectLeafTables(List<TableAnalysis> tables)
  {
    var result = new List<TableAnalysis>(tables.Count);
    foreach (TableAnalysis table in tables)
    {
      result.Add(table with { IsLeafTable = table.ChildTables.Count == 0 });
    }
    return result;
  }
}
