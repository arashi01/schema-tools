using System.Text.Json;
using Microsoft.SqlServer.Dac.Model;
using SchemaTools.Configuration;
using SchemaTools.Models;
using SchemaTools.Utilities;

// DacFx type aliases (disambiguate from SchemaTools.Models types)
using DacCheckConstraint = Microsoft.SqlServer.Dac.Model.CheckConstraint;
using DacColumnStoreIndex = Microsoft.SqlServer.Dac.Model.ColumnStoreIndex;
using DacDefaultConstraint = Microsoft.SqlServer.Dac.Model.DefaultConstraint;
using DacFKConstraint = Microsoft.SqlServer.Dac.Model.ForeignKeyConstraint;
using DacForeignKeyAction = Microsoft.SqlServer.Dac.Model.ForeignKeyAction;
using DacIndex = Microsoft.SqlServer.Dac.Model.Index;
using DacPKConstraint = Microsoft.SqlServer.Dac.Model.PrimaryKeyConstraint;
using DacSqlServerVersion = Microsoft.SqlServer.Dac.Model.SqlServerVersion;
using DacUniqueConstraint = Microsoft.SqlServer.Dac.Model.UniqueConstraint;
using ModelCheckConstraint = SchemaTools.Models.CheckConstraint;
using ModelFKConstraint = SchemaTools.Models.ForeignKeyConstraint;
using ModelPKConstraint = SchemaTools.Models.PrimaryKeyConstraint;
using ModelUniqueConstraint = SchemaTools.Models.UniqueConstraint;

namespace SchemaTools.Extraction;

/// <summary>
/// Core engine that extracts schema metadata from a compiled .dacpac using DacFx.
/// Decoupled from MSBuild so it can be invoked both as an in-process task (net10.0
/// MSBuild) and as a standalone CLI process (Full Framework MSBuild via dotnet exec).
/// </summary>
internal sealed class DacpacMetadataEngine
{
  private readonly string _dacpacPath;
  private readonly string _outputFile;
  private readonly string _configFile;
  private readonly string _analysisFile;
  private readonly string _databaseName;
  private readonly Action<string> _info;
  private readonly Action<string> _verbose;
  private readonly Action<string> _warning;
  private readonly Action<string> _error;

  private SchemaToolsConfig _config = new();
  private Models.SqlServerVersion _sqlVersion = Models.SqlServerVersion.Sql170;

  /// <summary>Override configuration for testing.</summary>
  internal SchemaToolsConfig? OverrideConfig { get; init; }

  /// <summary>Override model for testing (bypasses .dacpac file loading).</summary>
  internal TSqlModel? OverrideModel { get; init; }

  /// <summary>Override category map for testing (bypasses analysis file loading).</summary>
  internal Dictionary<string, string>? OverrideCategories { get; init; }

  /// <summary>Override description map for testing (bypasses analysis file loading).</summary>
  internal Dictionary<string, string>? OverrideDescriptions { get; init; }

  /// <summary>Override column description map for testing (bypasses analysis file loading).</summary>
  internal Dictionary<string, Dictionary<string, string>>? OverrideColumnDescriptions { get; init; }

  internal DacpacMetadataEngine(
    string dacpacPath,
    string outputFile,
    string configFile,
    string analysisFile,
    string databaseName,
    Action<string> info,
    Action<string> verbose,
    Action<string> warning,
    Action<string> error)
  {
    _dacpacPath = dacpacPath;
    _outputFile = outputFile;
    _configFile = configFile;
    _analysisFile = analysisFile;
    _databaseName = databaseName;
    _info = info;
    _verbose = verbose;
    _warning = warning;
    _error = error;
  }

  /// <summary>
  /// Extracts metadata from the .dacpac and writes the result as JSON.
  /// Returns true on success, false on failure.
  /// </summary>
  internal bool Execute()
  {
    try
    {
      _info("============================================================");
      _info("  Schema Metadata Extractor (Post-Build)");
      _info("============================================================");
      _info(string.Empty);

      LoadConfiguration();

      TSqlModel? model = LoadModel();
      if (model == null)
      {
        return false;
      }
      _sqlVersion = GetSqlServerVersion(model);

      _info($"Extracting metadata from: {Path.GetFileName(_dacpacPath)}");

      IEnumerable<TSqlObject> tables = model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass);
      var tableList = new List<TableMetadata>();
      int totalColumns = 0;
      int totalConstraints = 0;

      foreach (TSqlObject table in tables)
      {
        TableMetadata tableMeta = ExtractTableMetadata(table);
        tableList.Add(tableMeta);
        totalColumns += tableMeta.Columns.Count;
        totalConstraints += tableMeta.Constraints.PrimaryKey != null ? 1 : 0;
        totalConstraints += tableMeta.Constraints.ForeignKeys.Count;
        totalConstraints += tableMeta.Constraints.UniqueConstraints.Count;
        totalConstraints += tableMeta.Constraints.CheckConstraints.Count;
      }

      // Build FK dependency graph (sets column-level FK references and composite FK flags)
      tableList = ResolveForeignKeyGraph(tableList, _config.DefaultSchema);

      // Mark history tables (must be done before pattern detection)
      IReadOnlyList<TableMetadata> enrichedTables = PatternDetector.MarkHistoryTables(tableList);

      // Bridge annotations from pre-build analysis (dacpac has no comment annotations)
      enrichedTables = BridgeAnnotationsFromAnalysis(enrichedTables);

      // Detect patterns using config (must run after categories are bridged
      // so that category-based config overrides resolve correctly)
      enrichedTables = enrichedTables
        .Select(t => PatternDetector.DetectTablePatterns(t, _config, _sqlVersion))
        .ToList();

      SchemaMetadata metadata = new SchemaMetadata
      {
        ToolVersion = GenerationUtilities.GetToolVersion(),
        GeneratedAt = DateTime.UtcNow,
        GeneratedBy = "SchemaTools",
        Database = _config.Database ?? _databaseName,
        DefaultSchema = _config.DefaultSchema,
        SqlServerVersion = _sqlVersion,
        Tables = enrichedTables,
        Statistics = new SchemaStatistics
        {
          TotalTables = enrichedTables.Count,
          TemporalTables = enrichedTables.Count(t => t.HasTemporalVersioning),
          SoftDeleteTables = enrichedTables.Count(t => t.HasSoftDelete),
          AppendOnlyTables = enrichedTables.Count(t => t.IsAppendOnly),
          PolymorphicTables = enrichedTables.Count(t => t.IsPolymorphic),
          TotalColumns = totalColumns,
          TotalConstraints = totalConstraints
        },
        Categories = _config.Categories
      };

      GenerationUtilities.WriteJson(_outputFile, metadata);

      _info(string.Empty);
      _info("============================================================");
      _info("  Extraction Summary");
      _info("============================================================");
      _info($"Tables:            {metadata.Statistics.TotalTables}");
      _info($"Temporal:          {metadata.Statistics.TemporalTables}");
      _info($"Soft-delete:       {metadata.Statistics.SoftDeleteTables}");
      _info($"Total columns:     {metadata.Statistics.TotalColumns}");
      _info($"Total constraints: {metadata.Statistics.TotalConstraints}");
      _info($"Total indexes:     {metadata.Tables.Sum(t => t.Indexes.Count)}");
      _info("============================================================");
      _info($"+ Metadata written to: {_outputFile}");

      return true;
    }
    catch (Exception ex)
    {
      _error($"Metadata extraction failed: {ex.Message}");
      _error(ex.ToString());
      return false;
    }
  }

  private void LoadConfiguration()
  {
    _config = ConfigurationLoader.Load(_configFile, OverrideConfig);
  }

  private TSqlModel? LoadModel()
  {
    if (OverrideModel != null)
    {
      return OverrideModel;
    }

    if (!File.Exists(_dacpacPath))
    {
      _error($"Dacpac not found: {_dacpacPath}");
      return null;
    }

    return TSqlModel.LoadFromDacpac(_dacpacPath, new ModelLoadOptions
    {
      LoadAsScriptBackedModel = true,
      ThrowOnModelErrors = false
    });
  }

  private TableMetadata ExtractTableMetadata(TSqlObject table)
  {
    ObjectIdentifier tableName = table.Name;
    string schema = tableName.Parts.Count > 1 ? tableName.Parts[0] : _config.DefaultSchema;
    string name = tableName.Parts.Count > 1 ? tableName.Parts[1] : tableName.Parts[0];

    // Cache table script once for script-based fallbacks
    // (computed column expressions, FK actions, check constraints)
    string? tableScript = null;
    try
    {
      tableScript = table.GetScript();
    }
    catch
    {
      // Script not available
    }

    // Pre-parse computed columns from table script via ScriptDom
    // (DacFx Column.Expression is a SqlScriptProperty that throws InvalidCastException)
    Dictionary<string, ScriptDomParser.ComputedColumnInfo> computedColumns = !string.IsNullOrWhiteSpace(tableScript)
      ? ScriptDomParser.ExtractAllComputedColumns(tableScript, _sqlVersion)
      : new Dictionary<string, ScriptDomParser.ComputedColumnInfo>(StringComparer.OrdinalIgnoreCase);

    // Phase 1: Extract columns into a mutable local list
    var columns = new List<ColumnMetadata>();
    bool hasActiveColumn = false;

    IEnumerable<TSqlObject> columnObjects = table.GetReferenced(Table.Columns);
    foreach (TSqlObject column in columnObjects)
    {
      ColumnMetadata colMeta = ExtractColumnMetadata(column, computedColumns);
      columns.Add(colMeta);

      if (string.Equals(colMeta.Name, _config.Columns.Active, StringComparison.OrdinalIgnoreCase))
      {
        hasActiveColumn = true;
      }
    }

    // Phase 2: Temporal versioning
    TSqlObject? historyTableObj = table.GetReferenced(Table.TemporalSystemVersioningHistoryTable).FirstOrDefault();
    bool hasTemporalVersioning = historyTableObj != null;
    string? historyTableRef = null;

    if (historyTableObj != null)
    {
      ObjectIdentifier historyName = historyTableObj.Name;
      string historySchema = historyName.Parts.Count > 1 ? historyName.Parts[0] : _config.DefaultSchema;
      string historyTableName = historyName.Parts.Count > 1 ? historyName.Parts[1] : historyName.Parts[0];
      historyTableRef = $"[{historySchema}].[{historyTableName}]";
    }

    // Phase 3: Primary key constraint
    ModelPKConstraint? primaryKey = null;
    string? tablePrimaryKey = null;
    string? tablePrimaryKeyType = null;

    IEnumerable<TSqlObject> pkConstraints = table.GetReferencing(DacPKConstraint.Host);
    foreach (TSqlObject pk in pkConstraints)
    {
      IEnumerable<TSqlObject> pkColumns = pk.GetReferenced(DacPKConstraint.Columns);
      primaryKey = new ModelPKConstraint
      {
        Name = pk.Name.Parts.LastOrDefault() ?? $"PK_{name}",
        Columns = pkColumns.Select(c => c.Name.Parts.LastOrDefault() ?? "").ToList(),
        IsClustered = SafeGetProperty<bool?>(pk, DacPKConstraint.Clustered) == true
      };
    }

    // Enrich columns with IsPrimaryKey flag and set table-level PrimaryKey property
    if (primaryKey != null)
    {
      for (int i = 0; i < columns.Count; i++)
      {
        if (primaryKey.Columns.Any(pkCol =>
          string.Equals(columns[i].Name, pkCol, StringComparison.OrdinalIgnoreCase)))
        {
          columns[i] = columns[i] with { IsPrimaryKey = true };

          if (primaryKey.Columns.Count == 1)
          {
            tablePrimaryKey = columns[i].Name;
            tablePrimaryKeyType = columns[i].Type;
          }
        }
      }
    }

    // Phase 4: Foreign key constraints
    var foreignKeys = new List<ModelFKConstraint>();

    IEnumerable<TSqlObject> fkConstraints = table.GetReferencing(DacFKConstraint.Host);
    foreach (TSqlObject fk in fkConstraints)
    {
      IEnumerable<TSqlObject> fkColumns = fk.GetReferenced(DacFKConstraint.Columns);
      TSqlObject? refTable = fk.GetReferenced(DacFKConstraint.ForeignTable).FirstOrDefault();
      IEnumerable<TSqlObject> refColumns = fk.GetReferenced(DacFKConstraint.ForeignColumns);

      if (refTable != null)
      {
        ObjectIdentifier refTableName = refTable.Name;
        string refSchema = refTableName.Parts.Count > 1 ? refTableName.Parts[0] : _config.DefaultSchema;
        string refName = refTableName.Parts.Count > 1 ? refTableName.Parts[1] : refTableName.Parts[0];

        string fkName = fk.Name.Parts.LastOrDefault() ?? $"FK_{name}";
        Models.ForeignKeyAction onDelete = NormaliseFKAction(SafeGetProperty<DacForeignKeyAction?>(fk, DacFKConstraint.DeleteAction));
        Models.ForeignKeyAction onUpdate = NormaliseFKAction(SafeGetProperty<DacForeignKeyAction?>(fk, DacFKConstraint.UpdateAction));

        // Script-based fallback for FK actions when DacFx property access returns the default
        if (onDelete == Models.ForeignKeyAction.NoAction || onUpdate == Models.ForeignKeyAction.NoAction)
        {
          // Try FK constraint's own script first, then the table script
          string? fkScript = null;
          try
          {
            fkScript = fk.GetScript();
          }
          catch
          {
            // Script not available
          }

          ScriptDomParser.ForeignKeyActionInfo actions = !string.IsNullOrWhiteSpace(fkScript)
            ? ScriptDomParser.ExtractFKActions(fkScript, _sqlVersion)
            : !string.IsNullOrWhiteSpace(tableScript)
              ? ScriptDomParser.ExtractFKActionsByName(tableScript, fkName, _sqlVersion)
              : new ScriptDomParser.ForeignKeyActionInfo();

          if (onDelete == Models.ForeignKeyAction.NoAction && actions.OnDelete != Models.ForeignKeyAction.NoAction)
          {
            onDelete = actions.OnDelete;
          }

          if (onUpdate == Models.ForeignKeyAction.NoAction && actions.OnUpdate != Models.ForeignKeyAction.NoAction)
          {
            onUpdate = actions.OnUpdate;
          }
        }

        foreignKeys.Add(new ModelFKConstraint
        {
          Name = fkName,
          Columns = fkColumns.Select(c => c.Name.Parts.LastOrDefault() ?? "").ToList(),
          ReferencedTable = refName,
          ReferencedSchema = refSchema,
          ReferencedColumns = refColumns.Select(c => c.Name.Parts.LastOrDefault() ?? "").ToList(),
          OnDelete = onDelete,
          OnUpdate = onUpdate
        });
      }
    }

    // Phase 5: Unique constraints
    var uniqueConstraints = new List<ModelUniqueConstraint>();

    IEnumerable<TSqlObject> ucObjects = table.GetReferencing(DacUniqueConstraint.Host);
    foreach (TSqlObject uc in ucObjects)
    {
      IEnumerable<TSqlObject> ucColumns = uc.GetReferenced(DacUniqueConstraint.Columns);
      uniqueConstraints.Add(new ModelUniqueConstraint
      {
        Name = uc.Name.Parts.LastOrDefault() ?? $"UQ_{name}",
        Columns = ucColumns.Select(c => c.Name.Parts.LastOrDefault() ?? "").ToList(),
        IsClustered = SafeGetProperty<bool?>(uc, DacUniqueConstraint.Clustered) == true
      });
    }

    // Phase 6: Check constraints
    var checkConstraints = new List<ModelCheckConstraint>();

    IEnumerable<TSqlObject> ccObjects = table.GetReferencing(DacCheckConstraint.Host);
    foreach (TSqlObject cc in ccObjects)
    {
      // CheckConstraint.Expression is a SqlScriptProperty in DacFx
      // which throws InvalidCastException via GetProperty. Use GetScript() + ScriptDom parsing.
      string expression = "";
      try
      {
        string? ccScript = cc.GetScript();
        if (!string.IsNullOrWhiteSpace(ccScript))
        {
          expression = ScriptDomParser.ExtractCheckExpression(ccScript, _sqlVersion) ?? "";
        }
      }
      catch
      {
        // GetScript() may fail for certain constraint configurations
      }

      if (string.IsNullOrEmpty(expression))
      {
        // Fallback: extract from the cached table script by constraint name
        string constraintName = cc.Name.Parts.LastOrDefault() ?? "";
        if (!string.IsNullOrWhiteSpace(tableScript) && !string.IsNullOrEmpty(constraintName))
        {
          expression = ScriptDomParser.ExtractCheckExpressionByName(tableScript, constraintName, _sqlVersion) ?? "";
        }
      }

      checkConstraints.Add(new ModelCheckConstraint
      {
        Name = cc.Name.Parts.LastOrDefault() ?? $"CK_{name}",
        Expression = expression
      });
    }

    // Phase 7: Default constraints -- enrich columns
    columns = ExtractDefaultConstraints(table, columns);

    // Phase 8: Indexes
    IReadOnlyList<IndexMetadata> indexes = ExtractIndexes(table);

    // Phase 9: Mark single-column unique constraint columns
    foreach (ModelUniqueConstraint uc in uniqueConstraints.Where(u => u.Columns.Count == 1))
    {
      for (int i = 0; i < columns.Count; i++)
      {
        if (string.Equals(columns[i].Name, uc.Columns[0], StringComparison.OrdinalIgnoreCase))
        {
          columns[i] = columns[i] with { IsUnique = true };
        }
      }
    }

    // Construct the final immutable TableMetadata
    return new TableMetadata
    {
      Name = name,
      Schema = schema,
      HasActiveColumn = hasActiveColumn,
      HasTemporalVersioning = hasTemporalVersioning,
      HistoryTable = historyTableRef,
      PrimaryKey = tablePrimaryKey,
      PrimaryKeyType = tablePrimaryKeyType,
      Columns = columns,
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = primaryKey,
        ForeignKeys = foreignKeys,
        UniqueConstraints = uniqueConstraints,
        CheckConstraints = checkConstraints
      },
      Indexes = indexes
    };
  }

  private ColumnMetadata ExtractColumnMetadata(
    TSqlObject column, Dictionary<string, ScriptDomParser.ComputedColumnInfo> computedColumns)
  {
    string name = column.Name.Parts.LastOrDefault() ?? "";

    TSqlObject? dataType = column.GetReferenced(Column.DataType).FirstOrDefault();
    string typeName = dataType?.Name.Parts.LastOrDefault() ?? "unknown";

    // SafeGetProperty handles DacFx model storage engine exceptions with Sql170+ models
    int? length = SafeGetProperty<int?>(column, Column.Length, isStructural: true);
    int? precision = SafeGetProperty<int?>(column, Column.Precision, isStructural: true);
    int? scale = SafeGetProperty<int?>(column, Column.Scale, isStructural: true);
    bool isMax = SafeGetProperty<bool?>(column, Column.IsMax, isStructural: true) == true;

    string typeStr = typeName;
    if (isMax)
    {
      typeStr = $"{typeName}(max)";
    }
    else if (length.HasValue && length > 0)
    {
      typeStr = $"{typeName}({length})";
    }
    else if (precision.HasValue && precision > 0)
    {
      typeStr = scale.HasValue && scale > 0 ? $"{typeName}({precision},{scale})" : $"{typeName}({precision})";
    }

    // Computed column detection -- use pre-parsed ScriptDom data, with DacFx property as primary attempt
    bool isComputed = false;
    string? computedExpression = null;
    bool isPersisted = false;

    try
    {
      // Column.Expression is a SqlScriptProperty in DacFx - may throw InvalidCastException.
      string? expression = column.GetProperty<string>(Column.Expression);
      if (!string.IsNullOrEmpty(expression))
      {
        isComputed = true;
        computedExpression = expression;
      }
    }
    catch
    {
      // SqlScriptProperty -- fall back to ScriptDom-parsed data
      if (computedColumns.TryGetValue(name, out ScriptDomParser.ComputedColumnInfo? info))
      {
        isComputed = true;
        computedExpression = info.Expression;
        isPersisted = info.IsPersisted;
      }
    }

    if (isComputed && !isPersisted)
    {
      try
      {
        isPersisted = column.GetProperty<bool>(Column.Persisted);
      }
      catch
      {
        // Persisted property not accessible -- ScriptDom value already set above
      }
    }

    bool isGeneratedAlways = false;
    Models.GeneratedAlwaysType? generatedAlwaysType = null;

    try
    {
      object? genType = column.GetProperty(Column.GeneratedAlwaysType);
      if (genType != null)
      {
        string genStr = genType.ToString() ?? "";
        // DacFx uses "AsRowStart"/"AsRowEnd" names; strip the "As" prefix
        // to match our RowStart/RowEnd enum values.
        if (genStr.StartsWith("As", StringComparison.Ordinal) && genStr.Length > 2)
          genStr = genStr[2..];

        if (!string.IsNullOrEmpty(genStr) && !string.Equals(genStr, "None", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(genStr, "0", StringComparison.Ordinal)
            && Enum.TryParse<Models.GeneratedAlwaysType>(genStr, out Models.GeneratedAlwaysType parsedGenType)
            && Enum.IsDefined(typeof(Models.GeneratedAlwaysType), parsedGenType))
        {
          isGeneratedAlways = true;
          generatedAlwaysType = parsedGenType;
        }
      }
    }
    catch
    {
      // GeneratedAlwaysType not available on this DacFx version
    }

    return new ColumnMetadata
    {
      Name = name,
      Type = typeStr,
      MaxLength = isMax ? null : (length.HasValue && length > 0 ? length : null),
      Precision = precision.HasValue && precision > 0 ? precision : null,
      Scale = scale.HasValue && scale > 0 ? scale : null,
      IsMaxLength = isMax,
      Nullable = SafeGetProperty<bool?>(column, Column.Nullable, isStructural: true) ?? true,
      IsPrimaryKey = false, // Set after PK constraint extraction
      IsIdentity = SafeGetProperty<bool?>(column, Column.IsIdentity, isStructural: true) == true,
      IsComputed = isComputed,
      ComputedExpression = computedExpression,
      IsPersisted = isPersisted,
      IsGeneratedAlways = isGeneratedAlways,
      GeneratedAlwaysType = generatedAlwaysType
    };
  }

  /// <summary>
  /// Extracts default constraints for a table and returns updated columns with
  /// DefaultValue and DefaultConstraintName populated.
  /// </summary>
  /// <remarks>
  /// DefaultConstraint.Expression is a SqlScriptProperty in DacFx, which cannot
  /// be reliably accessed via GetProperty. We first attempt GetScript() which
  /// works for ALTER TABLE ADD CONSTRAINT statements but may return empty for
  /// inline defaults defined within CREATE TABLE. When GetScript() fails, we
  /// try to access the Expression property directly (may throw).
  /// </remarks>
  private List<ColumnMetadata> ExtractDefaultConstraints(TSqlObject table, List<ColumnMetadata> columns)
  {
    IEnumerable<TSqlObject> defaults = table.GetReferencing(DacDefaultConstraint.Host);
    foreach (TSqlObject dc in defaults)
    {
      TSqlObject? targetCol = dc.GetReferenced(DacDefaultConstraint.TargetColumn).FirstOrDefault();
      if (targetCol == null)
      {
        continue;
      }

      string columnName = targetCol.Name.Parts.LastOrDefault() ?? "";
      int colIndex = -1;
      for (int i = 0; i < columns.Count; i++)
      {
        if (string.Equals(columns[i].Name, columnName, StringComparison.OrdinalIgnoreCase))
        {
          colIndex = i;
          break;
        }
      }

      if (colIndex < 0)
      {
        continue;
      }

      string? constraintName = dc.Name.Parts.LastOrDefault();

      // DefaultConstraint.Expression is a SqlScriptProperty -- use GetScript() + ScriptDom parsing
      string expression = "";
      try
      {
        string? dcScript = dc.GetScript();
        if (!string.IsNullOrWhiteSpace(dcScript))
        {
          expression = ScriptDomParser.ExtractDefaultExpression(dcScript, _sqlVersion) ?? "";
        }
      }
      catch
      {
        // GetScript() may fail
      }

      // Direct property access fallback (may throw for SqlScriptProperty)
      if (string.IsNullOrEmpty(expression))
      {
        try
        {
          string? propValue = dc.GetProperty<string>(DacDefaultConstraint.Expression);
          if (!string.IsNullOrEmpty(propValue))
          {
            expression = propValue!;
          }
        }
        catch
        {
          // SqlScriptProperty - property access not supported
          // DefaultValue will remain unset; pre-build analysis should have the value
        }
      }

      columns[colIndex] = columns[colIndex] with
      {
        DefaultConstraintName = constraintName,
        DefaultValue = !string.IsNullOrEmpty(expression) ? expression : columns[colIndex].DefaultValue
      };
    }

    return columns;
  }

  /// <summary>
  /// Extracts indexes (rowstore and columnstore) for a table.
  /// PrimaryKeyConstraint and UniqueConstraint are handled separately via constraint extraction.
  /// </summary>
  private IReadOnlyList<IndexMetadata> ExtractIndexes(TSqlObject table)
  {
    var result = new List<IndexMetadata>();

    IEnumerable<TSqlObject> indexes = table.GetReferencing(DacIndex.IndexedObject);
    foreach (TSqlObject index in indexes)
    {
      IEnumerable<TSqlObject> indexColumns = index.GetReferenced(DacIndex.Columns);
      IEnumerable<TSqlObject> includedColumns = index.GetReferenced(DacIndex.IncludedColumns);

      List<string> included = includedColumns
        .Select(c => c.Name.Parts.LastOrDefault() ?? "")
        .ToList();

      // Filter predicate (may be SqlScriptProperty)
      string? filterClause = null;
      try
      {
        string? filterPredicate = index.GetProperty<string>(DacIndex.FilterPredicate);
        if (!string.IsNullOrEmpty(filterPredicate))
        {
          filterClause = $"WHERE {filterPredicate}";
        }
      }
      catch
      {
        // FilterPredicate is a SqlScriptProperty -- use GetScript() + ScriptDom parsing
        try
        {
          string? script = index.GetScript();
          if (!string.IsNullOrWhiteSpace(script))
          {
            string? filter = ScriptDomParser.ExtractFilterClause(script, _sqlVersion);
            if (!string.IsNullOrEmpty(filter))
            {
              filterClause = filter;
            }
          }
        }
        catch
        {
          // Script generation not supported for this index
        }
      }

      result.Add(new IndexMetadata
      {
        Name = index.Name.Parts.LastOrDefault() ?? "IX_Unknown",
        IsUnique = SafeGetProperty<bool>(index, DacIndex.Unique),
        IsClustered = SafeGetProperty<bool>(index, DacIndex.Clustered),
        Columns = indexColumns.Select(c => new IndexColumn
        {
          Name = c.Name.Parts.LastOrDefault() ?? "",
          IsDescending = false // Sort order requires relationship instance properties
        }).ToList(),
        IncludedColumns = included.Count > 0 ? included : null,
        FilterClause = filterClause
      });
    }

    IEnumerable<TSqlObject> csIndexes = table.GetReferencing(DacColumnStoreIndex.IndexedObject);
    foreach (TSqlObject csIndex in csIndexes)
    {
      IEnumerable<TSqlObject> csColumns = csIndex.GetReferenced(DacColumnStoreIndex.Columns);

      result.Add(new IndexMetadata
      {
        Name = csIndex.Name.Parts.LastOrDefault() ?? "CCI_Unknown",
        IsUnique = false,
        IsClustered = SafeGetProperty<bool>(csIndex, DacColumnStoreIndex.Clustered),
        IsColumnStore = true,
        Columns = csColumns.Select(c => new IndexColumn
        {
          Name = c.Name.Parts.LastOrDefault() ?? "",
          IsDescending = false
        }).ToList()
      });
    }

    return result;
  }

  /// <summary>
  /// Bridges all annotation data (categories, descriptions, column descriptions)
  /// from the pre-build analysis into the post-build table metadata. The dacpac
  /// binary model does not contain SQL comment annotations, so data parsed from
  /// <c>@category</c> and <c>@description</c> during source analysis must be
  /// applied here.
  /// </summary>
  private IReadOnlyList<TableMetadata> BridgeAnnotationsFromAnalysis(IReadOnlyList<TableMetadata> tables)
  {
    AnnotationMaps? maps = LoadAnnotationMaps();
    if (maps == null)
    {
      return tables;
    }

    int categoriesMatched = 0;
    int descriptionsMatched = 0;
    int columnDescriptionsMatched = 0;

    var result = new List<TableMetadata>(tables.Count);

    foreach (TableMetadata table in tables)
    {
      string? category = null;
      string? description = null;

      if (maps.Categories.TryGetValue(table.Name, out string? cat))
      {
        category = cat;
        categoriesMatched++;
      }

      if (maps.Descriptions.TryGetValue(table.Name, out string? desc))
      {
        description = desc;
        descriptionsMatched++;
      }

      IReadOnlyList<ColumnMetadata> columns = table.Columns;
      if (maps.ColumnDescriptions.TryGetValue(table.Name, out Dictionary<string, string>? colDescs))
      {
        var enrichedColumns = new List<ColumnMetadata>(columns);
        for (int i = 0; i < enrichedColumns.Count; i++)
        {
          if (colDescs.TryGetValue(enrichedColumns[i].Name, out string? colDesc))
          {
            enrichedColumns[i] = enrichedColumns[i] with { Description = colDesc };
            columnDescriptionsMatched++;
          }
        }
        columns = enrichedColumns;
      }

      if (category != null || description != null || columns != table.Columns)
      {
        result.Add(table with
        {
          Category = category ?? table.Category,
          Description = description ?? table.Description,
          Columns = columns
        });
      }
      else
      {
        result.Add(table);
      }
    }

    _verbose($"Bridged annotations: {categoriesMatched} categories, {descriptionsMatched} descriptions, {columnDescriptionsMatched} column descriptions");
    return result;
  }

  /// <summary>
  /// Internal record holding annotation data loaded from the analysis JSON.
  /// </summary>
  private sealed record AnnotationMaps(
    Dictionary<string, string> Categories,
    Dictionary<string, string> Descriptions,
    Dictionary<string, Dictionary<string, string>> ColumnDescriptions);

  /// <summary>
  /// Loads annotation maps (categories, descriptions, column descriptions)
  /// from the pre-build analysis JSON. Returns null if the analysis file
  /// is unavailable or cannot be parsed.
  /// </summary>
  private AnnotationMaps? LoadAnnotationMaps()
  {
    // Test override path: construct maps from override properties
    if (OverrideCategories != null || OverrideDescriptions != null || OverrideColumnDescriptions != null)
    {
      return new AnnotationMaps(
        OverrideCategories ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        OverrideDescriptions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        OverrideColumnDescriptions ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase));
    }

    if (string.IsNullOrEmpty(_analysisFile) || !File.Exists(_analysisFile))
    {
      return null;
    }

    try
    {
      string json = File.ReadAllText(_analysisFile);
      using JsonDocument doc = JsonDocument.Parse(json);
      JsonElement root = doc.RootElement;

      if (!root.TryGetProperty("tables", out JsonElement tablesElement) ||
          tablesElement.ValueKind != JsonValueKind.Array)
      {
        return null;
      }

      var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      var columnDescriptions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

      foreach (JsonElement tableElement in tablesElement.EnumerateArray())
      {
        if (!tableElement.TryGetProperty("name", out JsonElement nameEl) ||
            nameEl.ValueKind != JsonValueKind.String)
        {
          continue;
        }

        string name = nameEl.GetString()!;
        if (string.IsNullOrEmpty(name))
        {
          continue;
        }

        if (tableElement.TryGetProperty("category", out JsonElement catEl) &&
            catEl.ValueKind == JsonValueKind.String)
        {
          string cat = catEl.GetString()!;
          if (!string.IsNullOrEmpty(cat))
          {
            categories[name] = cat;
          }
        }

        if (tableElement.TryGetProperty("description", out JsonElement descEl) &&
            descEl.ValueKind == JsonValueKind.String)
        {
          string desc = descEl.GetString()!;
          if (!string.IsNullOrEmpty(desc))
          {
            descriptions[name] = desc;
          }
        }

        if (tableElement.TryGetProperty("columnDescriptions", out JsonElement colDescsEl) &&
            colDescsEl.ValueKind == JsonValueKind.Object)
        {
          var colMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
          foreach (JsonProperty prop in colDescsEl.EnumerateObject())
          {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
              string colDesc = prop.Value.GetString()!;
              if (!string.IsNullOrEmpty(colDesc))
              {
                colMap[prop.Name] = colDesc;
              }
            }
          }

          if (colMap.Count > 0)
          {
            columnDescriptions[name] = colMap;
          }
        }
      }

      return new AnnotationMaps(categories, descriptions, columnDescriptions);
    }
    catch (Exception ex)
    {
      _warning($"Could not load annotations from analysis file: {ex.Message}");
      return null;
    }
  }



  private static List<TableMetadata> ResolveForeignKeyGraph(List<TableMetadata> tables, string defaultSchema)
  {
    var result = new List<TableMetadata>(tables.Count);

    foreach (TableMetadata table in tables)
    {
      var columns = new List<ColumnMetadata>(table.Columns);
      bool changed = false;

      foreach (ModelFKConstraint fk in table.Constraints.ForeignKeys)
      {
        string fkSchema = fk.ReferencedSchema ?? defaultSchema;

        if (fk.Columns.Count == 1)
        {
          // Single-column FK
          int colIndex = columns.FindIndex(c =>
            string.Equals(c.Name, fk.Columns[0], StringComparison.OrdinalIgnoreCase));

          if (colIndex >= 0 && columns[colIndex].ForeignKey == null)
          {
            columns[colIndex] = columns[colIndex] with
            {
              ForeignKey = new ForeignKeyReference
              {
                Table = fk.ReferencedTable,
                Column = fk.ReferencedColumns.FirstOrDefault() ?? "id",
                Schema = fkSchema
              }
            };
            changed = true;
          }
        }
        else
        {
          // Composite FK: set ForeignKey reference and mark IsCompositeFK on each column
          for (int i = 0; i < fk.Columns.Count; i++)
          {
            int colIndex = columns.FindIndex(c =>
              string.Equals(c.Name, fk.Columns[i], StringComparison.OrdinalIgnoreCase));

            if (colIndex < 0)
            {
              continue;
            }

            ColumnMetadata col = columns[colIndex];
            ForeignKeyReference? fkRef = col.ForeignKey;
            if (fkRef == null && i < fk.ReferencedColumns.Count)
            {
              fkRef = new ForeignKeyReference
              {
                Table = fk.ReferencedTable,
                Column = fk.ReferencedColumns[i],
                Schema = fkSchema
              };
            }

            columns[colIndex] = col with
            {
              IsCompositeFK = true,
              ForeignKey = fkRef ?? col.ForeignKey
            };
            changed = true;
          }
        }
      }

      result.Add(changed ? table with { Columns = columns } : table);
    }

    return result;
  }



  /// <summary>
  /// Safely gets a property value from a TSqlObject, returning the default value
  /// if the property cannot be accessed (e.g., SqlScriptProperty types throw from
  /// the DacFx model storage engine).
  /// </summary>
  /// <param name="obj">The DacFx object to query.</param>
  /// <param name="property">The property class to retrieve.</param>
  /// <param name="defaultValue">Fallback value when extraction fails.</param>
  /// <param name="isStructural">
  /// If true, failures are logged as warnings (affects schema correctness).
  /// If false, failures are logged at verbose level (purely informational metadata).
  /// </param>
  private T SafeGetProperty<T>(TSqlObject obj, ModelPropertyClass property, T defaultValue = default!, bool isStructural = false)
  {
    try
    {
      return obj.GetProperty<T>(property);
    }
    catch (Exception ex)
    {
      if (isStructural)
      {
        _warning($"DacFx property access failed for {obj.Name}.{property.Name}: {ex.Message}");
      }
      else
      {
        _verbose($"DacFx property access failed for {obj.Name}.{property.Name} (using default '{defaultValue}'): {ex.Message}");
      }
      return defaultValue;
    }
  }

  private static Models.SqlServerVersion GetSqlServerVersion(TSqlModel model)
  {
    DacSqlServerVersion version = model.Version;
    return Enum.TryParse<Models.SqlServerVersion>(version.ToString(), out Models.SqlServerVersion parsed)
      ? parsed
      : Models.SqlServerVersion.Sql170;
  }

  /// <summary>
  /// Normalises a DacFx <see cref="DacForeignKeyAction"/> value. Maps null and
  /// <c>NotSpecified</c> to <see cref="Models.ForeignKeyAction.NoAction"/> since
  /// SQL Server defaults to NO ACTION when unspecified.
  /// </summary>
  private static Models.ForeignKeyAction NormaliseFKAction(DacForeignKeyAction? action)
  {
    return action switch
    {
      DacForeignKeyAction.Cascade => Models.ForeignKeyAction.Cascade,
      DacForeignKeyAction.SetNull => Models.ForeignKeyAction.SetNull,
      DacForeignKeyAction.SetDefault => Models.ForeignKeyAction.SetDefault,
      _ => Models.ForeignKeyAction.NoAction
    };
  }


}
