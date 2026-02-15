using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SqlServer.Dac.Model;
using SchemaTools.Models;
using SchemaTools.Utilities;
// DacFx type aliases (disambiguate from SchemaTools.Models types)
using DacCheckConstraint = Microsoft.SqlServer.Dac.Model.CheckConstraint;
using DacColumnStoreIndex = Microsoft.SqlServer.Dac.Model.ColumnStoreIndex;
using DacDefaultConstraint = Microsoft.SqlServer.Dac.Model.DefaultConstraint;
using DacFKConstraint = Microsoft.SqlServer.Dac.Model.ForeignKeyConstraint;
using DacIndex = Microsoft.SqlServer.Dac.Model.Index;
using DacPKConstraint = Microsoft.SqlServer.Dac.Model.PrimaryKeyConstraint;
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
  private string _sqlVersion = "Sql170";

  /// <summary>Override configuration for testing.</summary>
  internal SchemaToolsConfig? OverrideConfig { get; init; }

  /// <summary>Override model for testing (bypasses .dacpac file loading).</summary>
  internal TSqlModel? OverrideModel { get; init; }

  /// <summary>Override category map for testing (bypasses analysis file loading).</summary>
  internal Dictionary<string, string>? OverrideCategories { get; init; }

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

      TSqlModel model = LoadModel();
      _sqlVersion = GetSqlServerVersion(model);

      _info($"Extracting metadata from: {Path.GetFileName(_dacpacPath)}");

      var metadata = new SchemaMetadata
      {
        Version = GetAssemblyVersion(),
        GeneratedAt = DateTime.UtcNow,
        GeneratedBy = "SchemaMetadataExtractor (DacFx)",
        Database = _config.Database ?? _databaseName,
        DefaultSchema = _config.DefaultSchema,
        SqlServerVersion = _sqlVersion
      };

      IEnumerable<TSqlObject> tables = model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass);
      int tableCount = 0;
      int totalColumns = 0;
      int totalConstraints = 0;

      foreach (TSqlObject table in tables)
      {
        TableMetadata tableMeta = ExtractTableMetadata(table);
        metadata.Tables.Add(tableMeta);
        tableCount++;
        totalColumns += tableMeta.Columns.Count;
        totalConstraints += tableMeta.Constraints.PrimaryKey != null ? 1 : 0;
        totalConstraints += tableMeta.Constraints.ForeignKeys.Count;
        totalConstraints += tableMeta.Constraints.UniqueConstraints.Count;
        totalConstraints += tableMeta.Constraints.CheckConstraints.Count;
      }

      // Build FK dependency graph (sets column-level FK references and composite FK flags)
      ResolveForeignKeyGraph(metadata);

      // Mark history tables (must be done before pattern detection)
      MarkHistoryTables(metadata);

      // Bridge categories from pre-build analysis (dacpac has no comment annotations)
      BridgeCategoriesFromAnalysis(metadata);

      // Detect patterns using config (must run after categories are bridged
      // so that category-based config overrides resolve correctly)
      foreach (TableMetadata table in metadata.Tables)
      {
        DetectTablePatterns(table, _config, _sqlVersion);
      }

      metadata.Statistics = new SchemaStatistics
      {
        TotalTables = tableCount,
        TemporalTables = metadata.Tables.Count(t => t.HasTemporalVersioning),
        SoftDeleteTables = metadata.Tables.Count(t => t.HasSoftDelete),
        AppendOnlyTables = metadata.Tables.Count(t => t.IsAppendOnly),
        PolymorphicTables = metadata.Tables.Count(t => t.IsPolymorphic),
        TotalColumns = totalColumns,
        TotalConstraints = totalConstraints
      };

      metadata.Categories = _config.Categories;

      string? outputDir = Path.GetDirectoryName(_outputFile);
      if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
      {
        Directory.CreateDirectory(outputDir);
      }

      var options = new JsonSerializerOptions
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
      };

      string json = JsonSerializer.Serialize(metadata, options);
      File.WriteAllText(_outputFile, json, System.Text.Encoding.UTF8);

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
    if (OverrideConfig != null)
    {
      _config = OverrideConfig;
      return;
    }

    if (!string.IsNullOrEmpty(_configFile) && File.Exists(_configFile))
    {
      string json = File.ReadAllText(_configFile);
      _config = JsonSerializer.Deserialize<SchemaToolsConfig>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      }) ?? new SchemaToolsConfig();
    }
  }

  private TSqlModel LoadModel()
  {
    if (OverrideModel != null)
    {
      return OverrideModel;
    }

    if (!File.Exists(_dacpacPath))
    {
      throw new FileNotFoundException($"Dacpac not found: {_dacpacPath}");
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

    var metadata = new TableMetadata
    {
      Name = name,
      Schema = schema
    };

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

    IEnumerable<TSqlObject> columns = table.GetReferenced(Table.Columns);
    foreach (TSqlObject column in columns)
    {
      ColumnMetadata colMeta = ExtractColumnMetadata(column, computedColumns);
      metadata.Columns.Add(colMeta);

      if (string.Equals(colMeta.Name, _config.Columns.Active, StringComparison.OrdinalIgnoreCase))
      {
        metadata.HasActiveColumn = true;
      }
    }

    TSqlObject? historyTable = table.GetReferenced(Table.TemporalSystemVersioningHistoryTable).FirstOrDefault();
    metadata.HasTemporalVersioning = historyTable != null;

    if (historyTable != null)
    {
      ObjectIdentifier historyName = historyTable.Name;
      string historySchema = historyName.Parts.Count > 1 ? historyName.Parts[0] : _config.DefaultSchema;
      string historyTableName = historyName.Parts.Count > 1 ? historyName.Parts[1] : historyName.Parts[0];
      metadata.HistoryTable = $"[{historySchema}].[{historyTableName}]";
    }

    IEnumerable<TSqlObject> pkConstraints = table.GetReferencing(DacPKConstraint.Host);
    foreach (TSqlObject pk in pkConstraints)
    {
      IEnumerable<TSqlObject> pkColumns = pk.GetReferenced(DacPKConstraint.Columns);
      metadata.Constraints.PrimaryKey = new ModelPKConstraint
      {
        Name = pk.Name.Parts.LastOrDefault() ?? $"PK_{name}",
        Columns = pkColumns.Select(c => c.Name.Parts.LastOrDefault() ?? "").ToList(),
        IsClustered = SafeGetProperty<bool?>(pk, DacPKConstraint.Clustered) == true
      };
    }

    // Mark columns that are part of the primary key and set table-level PrimaryKey property
    if (metadata.Constraints.PrimaryKey != null)
    {
      foreach (string pkCol in metadata.Constraints.PrimaryKey.Columns)
      {
        foreach (ColumnMetadata col in metadata.Columns.Where(c =>
          string.Equals(c.Name, pkCol, StringComparison.OrdinalIgnoreCase)))
        {
          col.IsPrimaryKey = true;

          // Set table-level PrimaryKey (for single-column PKs)
          if (metadata.Constraints.PrimaryKey.Columns.Count == 1)
          {
            metadata.PrimaryKey = col.Name;
            metadata.PrimaryKeyType = col.Type;
          }
        }
      }
    }

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
        string onDelete = NormaliseFKAction(SafeGetProperty<ForeignKeyAction?>(fk, DacFKConstraint.DeleteAction)?.ToString());
        string onUpdate = NormaliseFKAction(SafeGetProperty<ForeignKeyAction?>(fk, DacFKConstraint.UpdateAction)?.ToString());

        // Script-based fallback for FK actions when DacFx property access returns the default
        if (onDelete == "NoAction" || onUpdate == "NoAction")
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

          if (onDelete == "NoAction" && actions.OnDelete != "NoAction")
          {
            onDelete = actions.OnDelete;
          }

          if (onUpdate == "NoAction" && actions.OnUpdate != "NoAction")
          {
            onUpdate = actions.OnUpdate;
          }
        }

        metadata.Constraints.ForeignKeys.Add(new ModelFKConstraint
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

    IEnumerable<TSqlObject> uniqueConstraints = table.GetReferencing(DacUniqueConstraint.Host);
    foreach (TSqlObject uc in uniqueConstraints)
    {
      IEnumerable<TSqlObject> ucColumns = uc.GetReferenced(DacUniqueConstraint.Columns);
      metadata.Constraints.UniqueConstraints.Add(new ModelUniqueConstraint
      {
        Name = uc.Name.Parts.LastOrDefault() ?? $"UQ_{name}",
        Columns = ucColumns.Select(c => c.Name.Parts.LastOrDefault() ?? "").ToList(),
        IsClustered = SafeGetProperty<bool?>(uc, DacUniqueConstraint.Clustered) == true
      });
    }

    IEnumerable<TSqlObject> checkConstraints = table.GetReferencing(DacCheckConstraint.Host);
    foreach (TSqlObject cc in checkConstraints)
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

      metadata.Constraints.CheckConstraints.Add(new ModelCheckConstraint
      {
        Name = cc.Name.Parts.LastOrDefault() ?? $"CK_{name}",
        Expression = expression
      });
    }

    ExtractDefaultConstraints(table, metadata);

    ExtractIndexes(table, metadata);

    foreach (ModelUniqueConstraint uc in metadata.Constraints.UniqueConstraints.Where(u => u.Columns.Count == 1))
    {
      foreach (ColumnMetadata col in metadata.Columns.Where(c =>
        string.Equals(c.Name, uc.Columns[0], StringComparison.OrdinalIgnoreCase)))
      {
        col.IsUnique = true;
      }
    }

    return metadata;
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
    string? generatedAlwaysType = null;

    try
    {
      object? genType = column.GetProperty(Column.GeneratedAlwaysType);
      if (genType != null)
      {
        string genStr = genType.ToString() ?? "";
        if (!string.IsNullOrEmpty(genStr) && !string.Equals(genStr, "None", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(genStr, "0", StringComparison.Ordinal))
        {
          isGeneratedAlways = true;
          generatedAlwaysType = genStr;
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
  /// Extracts default constraints for a table and populates column-level
  /// DefaultValue and DefaultConstraintName.
  /// </summary>
  /// <remarks>
  /// DefaultConstraint.Expression is a SqlScriptProperty in DacFx, which cannot
  /// be reliably accessed via GetProperty. We first attempt GetScript() which
  /// works for ALTER TABLE ADD CONSTRAINT statements but may return empty for
  /// inline defaults defined within CREATE TABLE. When GetScript() fails, we
  /// try to access the Expression property directly (may throw).
  /// </remarks>
  private void ExtractDefaultConstraints(TSqlObject table, TableMetadata metadata)
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
      ColumnMetadata? col = metadata.Columns.FirstOrDefault(c =>
        string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));

      if (col == null)
      {
        continue;
      }

      col.DefaultConstraintName = dc.Name.Parts.LastOrDefault();

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

      if (!string.IsNullOrEmpty(expression))
      {
        col.DefaultValue = expression;
      }
    }
  }

  /// <summary>
  /// Extracts indexes (rowstore and columnstore) for a table.
  /// PrimaryKeyConstraint and UniqueConstraint are handled separately via constraint extraction.
  /// </summary>
  private void ExtractIndexes(TSqlObject table, TableMetadata metadata)
  {
    IEnumerable<TSqlObject> indexes = table.GetReferencing(DacIndex.IndexedObject);
    foreach (TSqlObject index in indexes)
    {
      IEnumerable<TSqlObject> indexColumns = index.GetReferenced(DacIndex.Columns);
      IEnumerable<TSqlObject> includedColumns = index.GetReferenced(DacIndex.IncludedColumns);

      var indexMeta = new IndexMetadata
      {
        Name = index.Name.Parts.LastOrDefault() ?? "IX_Unknown",
        IsUnique = SafeGetProperty<bool>(index, DacIndex.Unique),
        IsClustered = SafeGetProperty<bool>(index, DacIndex.Clustered),
        Columns = indexColumns.Select(c => new IndexColumn
        {
          Name = c.Name.Parts.LastOrDefault() ?? "",
          IsDescending = false // Sort order requires relationship instance properties
        }).ToList()
      };

      List<string> included = includedColumns
        .Select(c => c.Name.Parts.LastOrDefault() ?? "")
        .ToList();
      if (included.Count > 0)
      {
        indexMeta.IncludedColumns = included;
      }

      // Filter predicate (may be SqlScriptProperty)
      try
      {
        string? filterPredicate = index.GetProperty<string>(DacIndex.FilterPredicate);
        if (!string.IsNullOrEmpty(filterPredicate))
        {
          indexMeta.FilterClause = $"WHERE {filterPredicate}";
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
              indexMeta.FilterClause = filter;
            }
          }
        }
        catch
        {
          // Script generation not supported for this index
        }
      }

      metadata.Indexes.Add(indexMeta);
    }

    IEnumerable<TSqlObject> csIndexes = table.GetReferencing(DacColumnStoreIndex.IndexedObject);
    foreach (TSqlObject csIndex in csIndexes)
    {
      IEnumerable<TSqlObject> csColumns = csIndex.GetReferenced(DacColumnStoreIndex.Columns);

      var indexMeta = new IndexMetadata
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
      };

      metadata.Indexes.Add(indexMeta);
    }
  }

  /// <summary>
  /// Bridges category annotations from the pre-build analysis into the post-build
  /// table metadata. The dacpac binary model does not contain SQL comment annotations,
  /// so categories parsed from <c>-- @category</c> during source analysis must be
  /// applied here to enable category-based config overrides and grouped documentation.
  /// </summary>
  private void BridgeCategoriesFromAnalysis(SchemaMetadata metadata)
  {
    Dictionary<string, string>? categoryMap = LoadCategoryMap();
    if (categoryMap == null || categoryMap.Count == 0)
    {
      return;
    }

    int matched = 0;
    foreach (TableMetadata table in metadata.Tables)
    {
      if (categoryMap.TryGetValue(table.Name, out string? category))
      {
        table.Category = category;
        matched++;
      }
    }

    _verbose($"Bridged categories for {matched}/{metadata.Tables.Count} tables from analysis");
  }

  /// <summary>
  /// Loads the table-name-to-category map from the pre-build analysis JSON.
  /// Returns null if there is no analysis file or it cannot be loaded.
  /// </summary>
  private Dictionary<string, string>? LoadCategoryMap()
  {
    if (OverrideCategories != null)
    {
      return OverrideCategories;
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

      var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (JsonElement tableElement in tablesElement.EnumerateArray())
      {
        if (tableElement.TryGetProperty("name", out JsonElement nameEl) &&
            tableElement.TryGetProperty("category", out JsonElement catEl) &&
            nameEl.ValueKind == JsonValueKind.String &&
            catEl.ValueKind == JsonValueKind.String)
        {
          string name = nameEl.GetString()!;
          string cat = catEl.GetString()!;
          if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(cat))
          {
            map[name] = cat;
          }
        }
      }

      return map;
    }
    catch (Exception ex)
    {
      _warning($"Could not load categories from analysis file: {ex.Message}");
      return null;
    }
  }

  /// <summary>
  /// Marks tables that are temporal history tables. These tables are referenced
  /// by another table's HistoryTable property and do not have primary keys by design.
  /// </summary>
  private static void MarkHistoryTables(SchemaMetadata metadata)
  {
    var historyTableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (TableMetadata table in metadata.Tables)
    {
      if (!string.IsNullOrEmpty(table.HistoryTable))
      {
        // HistoryTable format is "[schema].[name]" - extract just the name
        string historyName = table.HistoryTable!
          .Replace("[", "").Replace("]", "")
          .Split('.').LastOrDefault() ?? "";
        if (!string.IsNullOrEmpty(historyName))
        {
          historyTableNames.Add(historyName);
        }
      }
    }

    foreach (TableMetadata table in metadata.Tables)
    {
      if (historyTableNames.Contains(table.Name))
      {
        table.IsHistoryTable = true;
      }
    }
  }

  private static void ResolveForeignKeyGraph(SchemaMetadata metadata)
  {
    foreach (TableMetadata table in metadata.Tables)
    {
      foreach (ModelFKConstraint fk in table.Constraints.ForeignKeys)
      {
        string fkSchema = fk.ReferencedSchema ?? metadata.DefaultSchema;

        if (fk.Columns.Count == 1)
        {
          // Single-column FK
          ColumnMetadata? col = table.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, fk.Columns[0], StringComparison.OrdinalIgnoreCase));

          if (col != null && col.ForeignKey == null)
          {
            col.ForeignKey = new ForeignKeyReference
            {
              Table = fk.ReferencedTable,
              Column = fk.ReferencedColumns.FirstOrDefault() ?? "id",
              Schema = fkSchema
            };
          }
        }
        else
        {
          // Composite FK: set ForeignKey reference and mark IsCompositeFK on each column
          for (int i = 0; i < fk.Columns.Count; i++)
          {
            ColumnMetadata? col = table.Columns.FirstOrDefault(c =>
              string.Equals(c.Name, fk.Columns[i], StringComparison.OrdinalIgnoreCase));

            if (col == null)
            {
              continue;
            }

            col.IsCompositeFK = true;

            if (col.ForeignKey == null && i < fk.ReferencedColumns.Count)
            {
              col.ForeignKey = new ForeignKeyReference
              {
                Table = fk.ReferencedTable,
                Column = fk.ReferencedColumns[i],
                Schema = fkSchema
              };
            }
          }
        }
      }
    }
  }

  private static void DetectTablePatterns(TableMetadata table, SchemaToolsConfig config, string sqlVersion)
  {
    SchemaToolsConfig effective = config.ResolveForTable(table.Name, table.Category);

    if (table.HasActiveColumn)
    {
      table.ActiveColumnName = effective.Columns.Active;
    }

    if (effective.Features.EnableSoftDelete &&
        table.HasActiveColumn &&
        table.HasTemporalVersioning)
    {
      table.HasSoftDelete = true;
    }

    if (effective.Features.DetectAppendOnlyTables)
    {
      bool hasCreatedAt = table.Columns.Any(c =>
        string.Equals(c.Name, effective.Columns.CreatedAt, StringComparison.OrdinalIgnoreCase));
      bool hasUpdatedBy = table.Columns.Any(c =>
        string.Equals(c.Name, effective.Columns.UpdatedBy, StringComparison.OrdinalIgnoreCase));

      if (hasCreatedAt && !hasUpdatedBy && !table.HasTemporalVersioning)
      {
        table.IsAppendOnly = true;
      }
    }

    // Polymorphic detection (skip history tables -- they inherit columns but lack CHECK constraints)
    if (effective.Features.DetectPolymorphicPatterns && !table.IsHistoryTable)
    {
      foreach (PolymorphicPatternConfig pattern in effective.Columns.PolymorphicPatterns)
      {
        bool hasTypeCol = table.Columns.Any(c =>
          string.Equals(c.Name, pattern.TypeColumn, StringComparison.OrdinalIgnoreCase));
        bool hasIdCol = table.Columns.Any(c =>
          string.Equals(c.Name, pattern.IdColumn, StringComparison.OrdinalIgnoreCase));

        if (hasTypeCol && hasIdCol)
        {
          table.IsPolymorphic = true;
          table.PolymorphicOwner = new PolymorphicOwnerInfo
          {
            TypeColumn = pattern.TypeColumn,
            IdColumn = pattern.IdColumn,
            AllowedTypes = ExtractAllowedTypesForPolymorphic(table, pattern.TypeColumn, sqlVersion)
          };

          foreach (ColumnMetadata col in table.Columns)
          {
            if (string.Equals(col.Name, pattern.TypeColumn, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(col.Name, pattern.IdColumn, StringComparison.OrdinalIgnoreCase))
            {
              col.IsPolymorphicForeignKey = true;
            }
          }

          break;
        }
      }
    }
  }

  /// <summary>
  /// Extracts allowed polymorphic types from CHECK constraints that reference
  /// the given type column. Uses ScriptDom AST parsing to reliably extract
  /// string literals from IN-list expressions.
  /// </summary>
  private static List<string> ExtractAllowedTypesForPolymorphic(
    TableMetadata table, string typeColumn, string sqlVersion)
  {
    List<string> types = new List<string>();

    foreach (ModelCheckConstraint cc in table.Constraints.CheckConstraints)
    {
      if (string.IsNullOrEmpty(cc.Expression))
      {
        continue;
      }

      // Check if this constraint references the type column (with or without brackets)
      if (cc.Expression.IndexOf(typeColumn, StringComparison.OrdinalIgnoreCase) < 0
          && cc.Expression.IndexOf($"[{typeColumn}]", StringComparison.OrdinalIgnoreCase) < 0)
      {
        continue;
      }

      // Use ScriptDom to reliably extract string literals from the expression
      List<string> extracted = ScriptDomParser.ExtractAllowedTypesFromExpression(cc.Expression, sqlVersion);
      types.AddRange(extracted);
    }

    return types;
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

  private static string GetSqlServerVersion(TSqlModel model)
  {
    SqlServerVersion version = model.Version;
    return version.ToString();
  }

  /// <summary>
  /// Normalises a DacFx ForeignKeyAction string. Maps null and "NotSpecified"
  /// to "NoAction" since SQL Server defaults to NO ACTION when unspecified.
  /// </summary>
  private static string NormaliseFKAction(string? action)
  {
    if (string.IsNullOrEmpty(action) ||
        string.Equals(action, "NotSpecified", StringComparison.OrdinalIgnoreCase))
    {
      return "NoAction";
    }

    return action!;
  }

  private static string GetAssemblyVersion()
  {
    return typeof(DacpacMetadataEngine).Assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
      ?? typeof(DacpacMetadataEngine).Assembly.GetName().Version?.ToString()
      ?? "0.0.0";
  }
}
