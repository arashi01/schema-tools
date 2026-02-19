using SchemaTools.Configuration;
using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

/// <summary>
/// Direct tests for the pure <see cref="SchemaValidation"/> static class.
/// These tests exercise validation logic without MSBuild infrastructure,
/// validating the pure core extraction from <see cref="SchemaValidator"/>.
/// </summary>
public class SchemaValidationTests
{
  // SchemaToolsConfig is mutable (not converted to record) -- Action<T> is fine.
  private static SchemaToolsConfig CreateConfig(Action<SchemaToolsConfig>? configure = null)
  {
    var config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Validation = new ValidationConfig
      {
        ValidateForeignKeys = true,
        ValidatePolymorphic = true,
        ValidateTemporal = true,
        ValidateAuditColumns = true,
        EnforceNamingConventions = true,
        TreatWarningsAsErrors = false
      }
    };
    configure?.Invoke(config);
    return config;
  }

  /// <summary>
  /// Creates a default table with id + audit columns and a PK constraint.
  /// Use <paramref name="configure"/> to create a modified copy via <c>with</c>.
  /// </summary>
  private static TableMetadata CreateTable(string name, Func<TableMetadata, TableMetadata>? configure = null)
  {
    var table = new TableMetadata
    {
      Name = name,
      Schema = "test",
      PrimaryKey = "id",
      Columns =
      [
        new ColumnMetadata { Name = "id", Type = "UNIQUEIDENTIFIER", IsPrimaryKey = true },
        new ColumnMetadata { Name = "record_created_by", Type = "UNIQUEIDENTIFIER" },
        new ColumnMetadata { Name = "record_updated_by", Type = "UNIQUEIDENTIFIER" }
      ],
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = new PrimaryKeyConstraint { Name = $"pk_{name}", Columns = ["id"] }
      }
    };
    return configure != null ? configure(table) : table;
  }

  private static SchemaMetadata CreateMetadata(params TableMetadata[] tables) => new()
  {
    Database = "TestDB",
    DefaultSchema = "test",
    Tables = [.. tables]
  };

  /// <summary>Appends a foreign key constraint + FK column to a table.</summary>
  private static TableMetadata WithFK(TableMetadata table, string constraintName,
    string column, string referencedTable, params string[] referencedColumns)
  {
    return table with
    {
      Columns = [.. table.Columns, new ColumnMetadata { Name = column, Type = "UNIQUEIDENTIFIER" }],
      Constraints = table.Constraints with
      {
        ForeignKeys =
        [
          .. table.Constraints.ForeignKeys,
          new ForeignKeyConstraint
          {
            Name = constraintName,
            Columns = [column],
            ReferencedTable = referencedTable,
            ReferencedColumns = [.. referencedColumns.Length > 0 ? referencedColumns : ["id"]]
          }
        ]
      }
    };
  }

  /// <summary>Appends a FK constraint without adding a column (for column-mismatch tests).</summary>
  private static TableMetadata WithFKConstraintOnly(TableMetadata table, string constraintName,
    string[] columns, string referencedTable, string[] referencedColumns)
  {
    return table with
    {
      Constraints = table.Constraints with
      {
        ForeignKeys =
        [
          .. table.Constraints.ForeignKeys,
          new ForeignKeyConstraint
          {
            Name = constraintName,
            Columns = [.. columns],
            ReferencedTable = referencedTable,
            ReferencedColumns = [.. referencedColumns]
          }
        ]
      }
    };
  }

  // --- Validate orchestrator -----------------------------------------------

  public class ValidateOrchestratorTests : SchemaValidationTests
  {
    [Fact]
    public void EmptyMetadata_ReturnsEmptyResult()
    {
      SchemaValidation.ValidationResult result =
        SchemaValidation.Validate(CreateMetadata(), CreateConfig());

      result.Errors.Should().BeEmpty();
      result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void ValidTable_ReturnsNoErrorsOrWarnings()
    {
      TableMetadata table = CreateTable("users");

      SchemaValidation.ValidationResult result =
        SchemaValidation.Validate(CreateMetadata(table), CreateConfig());

      result.Errors.Should().BeEmpty();
      result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void TaskOverrideTrue_TakesPrecedenceOverConfigFalse()
    {
      SchemaToolsConfig config = CreateConfig(c => c.Validation.ValidateForeignKeys = false);
      TableMetadata child = WithFKConstraintOnly(
        CreateTable("child"), "fk_child_missing", ["ref_id"], "nonexistent", ["id"]);

      SchemaValidation.ValidationResult result =
        SchemaValidation.Validate(CreateMetadata(child), config, validateForeignKeys: true);

      result.Errors.Should().ContainMatch("*references non-existent table*");
    }

    [Fact]
    public void TaskOverrideFalse_TakesPrecedenceOverConfigTrue()
    {
      SchemaToolsConfig config = CreateConfig(c => c.Validation.ValidateForeignKeys = true);
      TableMetadata child = WithFKConstraintOnly(
        CreateTable("child"), "fk_child_missing", ["ref_id"], "nonexistent", ["id"]);

      SchemaValidation.ValidationResult result =
        SchemaValidation.Validate(CreateMetadata(child), config, validateForeignKeys: false);

      result.Errors.Should().NotContainMatch("*references non-existent table*");
    }

    [Fact]
    public void NullTaskOverride_FallsBackToConfig()
    {
      SchemaToolsConfig config = CreateConfig(c => c.Validation.ValidateForeignKeys = true);
      TableMetadata child = WithFKConstraintOnly(
        CreateTable("child"), "fk_child_missing", ["ref_id"], "nonexistent", ["id"]);

      SchemaValidation.ValidationResult result =
        SchemaValidation.Validate(CreateMetadata(child), config, validateForeignKeys: null);

      result.Errors.Should().ContainMatch("*references non-existent table*");
    }

    [Fact]
    public void AllValidationsDisabled_OnlyAlwaysOnValidationsRun()
    {
      SchemaToolsConfig config = CreateConfig(c =>
      {
        c.Validation.ValidateForeignKeys = false;
        c.Validation.ValidatePolymorphic = false;
        c.Validation.ValidateTemporal = false;
        c.Validation.ValidateAuditColumns = false;
        c.Validation.EnforceNamingConventions = false;
      });

      var table = new TableMetadata
      {
        Name = "no_pk",
        Schema = "test",
        Columns = [new ColumnMetadata { Name = "data", Type = "VARCHAR(100)" }],
        Constraints = new ConstraintsCollection()
      };

      SchemaValidation.ValidationResult result =
        SchemaValidation.Validate(CreateMetadata(table), config);

      result.Errors.Should().ContainMatch("*no primary key*");
    }
  }

  // --- FK validation -------------------------------------------------------

  public class ForeignKeyValidationTests : SchemaValidationTests
  {
    [Fact]
    public void ValidFK_NoErrors()
    {
      var errors = new List<string>();
      TableMetadata parent = CreateTable("parent");
      TableMetadata child = WithFK(CreateTable("child"), "fk_child_parent", "parent_id", "parent");

      SchemaValidation.ValidateForeignKeyReferences(CreateMetadata(parent, child), errors);
      errors.Should().BeEmpty();
    }

    [Fact]
    public void MissingTable_ReportsError()
    {
      var errors = new List<string>();
      TableMetadata table = WithFKConstraintOnly(
        CreateTable("orphan"), "fk_orphan_missing", ["ref_id"], "nonexistent", ["id"]);

      SchemaValidation.ValidateForeignKeyReferences(CreateMetadata(table), errors);
      errors.Should().ContainSingle().Which.Should().Contain("non-existent table");
    }

    [Fact]
    public void MissingColumn_ReportsError()
    {
      var errors = new List<string>();
      TableMetadata parent = CreateTable("parent");
      TableMetadata child = WithFKConstraintOnly(
        CreateTable("child"), "fk_child_parent", ["parent_id"], "parent", ["nonexistent_col"]);

      SchemaValidation.ValidateForeignKeyReferences(CreateMetadata(parent, child), errors);
      errors.Should().ContainSingle().Which.Should().Contain("non-existent column");
    }

    [Fact]
    public void MismatchedColumnCounts_ReportsError()
    {
      var errors = new List<string>();
      TableMetadata parent = CreateTable("parent");
      TableMetadata child = WithFKConstraintOnly(
        CreateTable("child"), "fk_child_parent", ["a", "b"], "parent", ["id"]);

      SchemaValidation.ValidateForeignKeyReferences(CreateMetadata(parent, child), errors);
      errors.Should().ContainSingle().Which.Should().Contain("mismatched column counts");
    }

    [Fact]
    public void MultipleIssues_ReportsAllErrors()
    {
      var errors = new List<string>();
      TableMetadata table = CreateTable("multi_error");
      table = WithFKConstraintOnly(table, "fk_multi_error_a", ["a_id"], "missing_a", ["id"]);
      table = WithFKConstraintOnly(table, "fk_multi_error_b", ["b_id"], "missing_b", ["id"]);

      SchemaValidation.ValidateForeignKeyReferences(CreateMetadata(table), errors);
      errors.Should().HaveCount(2);
    }
  }

  // --- Polymorphic validation ----------------------------------------------

  public class PolymorphicValidationTests : SchemaValidationTests
  {
    private static TableMetadata CreatePolymorphicTable(
      string name,
      string typeColumn = "owner_type",
      string idColumn = "owner_id",
      string[]? allowedTypes = null,
      bool includeTypeColumn = true,
      bool includeIdColumn = true,
      bool includeCheckConstraint = true,
      bool isHistoryTable = false)
    {
      var columns = new List<ColumnMetadata>
      {
        new() { Name = "id", Type = "UNIQUEIDENTIFIER", IsPrimaryKey = true },
        new() { Name = "record_created_by", Type = "UNIQUEIDENTIFIER" },
        new() { Name = "record_updated_by", Type = "UNIQUEIDENTIFIER" }
      };

      if (includeTypeColumn)
        columns.Add(new ColumnMetadata { Name = typeColumn, Type = "VARCHAR(20)" });
      if (includeIdColumn)
        columns.Add(new ColumnMetadata { Name = idColumn, Type = "UNIQUEIDENTIFIER" });

      var checkConstraints = new List<CheckConstraint>();
      if (includeCheckConstraint)
      {
        string types = string.Join(", ", (allowedTypes ?? ["user"]).Select(t => $"'{t}'"));
        checkConstraints.Add(new CheckConstraint
        {
          Name = $"ck_{name}_{typeColumn}",
          Expression = $"[{typeColumn}] IN ({types})"
        });
      }

      return new TableMetadata
      {
        Name = name,
        Schema = "test",
        PrimaryKey = "id",
        IsPolymorphic = true,
        IsHistoryTable = isHistoryTable,
        PolymorphicOwner = new PolymorphicOwnerInfo
        {
          TypeColumn = typeColumn,
          IdColumn = idColumn,
          AllowedTypes = [.. allowedTypes ?? ["user"]]
        },
        Columns = columns,
        Constraints = new ConstraintsCollection
        {
          PrimaryKey = new PrimaryKeyConstraint { Name = $"pk_{name}", Columns = ["id"] },
          CheckConstraints = checkConstraints
        }
      };
    }

    [Fact]
    public void ValidSetup_NoErrors()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      TableMetadata table = CreatePolymorphicTable("notes", allowedTypes: ["user", "company"]);

      SchemaValidation.ValidatePolymorphicTables(CreateMetadata(table), errors, warnings);
      errors.Should().BeEmpty();
      warnings.Should().BeEmpty();
    }

    [Fact]
    public void MissingOwner_ReportsError()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      TableMetadata table = CreateTable("notes", t => t with
      {
        IsPolymorphic = true,
        PolymorphicOwner = null
      });

      SchemaValidation.ValidatePolymorphicTables(CreateMetadata(table), errors, warnings);
      errors.Should().ContainSingle().Which.Should().Contain("missing PolymorphicOwner");
    }

    [Fact]
    public void MissingTypeColumn_ReportsError()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      TableMetadata table = CreatePolymorphicTable("notes", includeTypeColumn: false);

      SchemaValidation.ValidatePolymorphicTables(CreateMetadata(table), errors, warnings);
      errors.Should().Contain(e => e.Contains("owner_type") && e.Contains("not found"));
    }

    [Fact]
    public void MissingIdColumn_ReportsError()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      TableMetadata table = CreatePolymorphicTable("notes", includeIdColumn: false);

      SchemaValidation.ValidatePolymorphicTables(CreateMetadata(table), errors, warnings);
      errors.Should().Contain(e => e.Contains("owner_id") && e.Contains("not found"));
    }

    [Fact]
    public void NoAllowedTypes_Warns()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      TableMetadata table = CreatePolymorphicTable("notes");
      table = table with
      {
        PolymorphicOwner = table.PolymorphicOwner! with { AllowedTypes = [] }
      };

      SchemaValidation.ValidatePolymorphicTables(CreateMetadata(table), errors, warnings);
      errors.Should().BeEmpty();
      warnings.Should().ContainSingle().Which.Should().Contain("no allowed types");
    }

    [Fact]
    public void MissingCheckConstraint_ReportsError()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      TableMetadata table = CreatePolymorphicTable("notes", includeCheckConstraint: false);

      SchemaValidation.ValidatePolymorphicTables(CreateMetadata(table), errors, warnings);
      errors.Should().Contain(e => e.Contains("missing CHECK constraint"));
    }

    [Fact]
    public void HistoryTable_SkippedFromPolymorphicValidation()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      TableMetadata table = CreateTable("notes", t => t with
      {
        IsPolymorphic = true,
        IsHistoryTable = true,
        PolymorphicOwner = null
      });

      SchemaValidation.ValidatePolymorphicTables(CreateMetadata(table), errors, warnings);
      errors.Should().BeEmpty("history tables should be skipped");
    }
  }

  // --- Temporal validation -------------------------------------------------

  public class TemporalValidationTests : SchemaValidationTests
  {
    private static TableMetadata CreateTemporalTable(
      string name,
      string? historyTable = null,
      bool includeValidFrom = true,
      bool includeValidTo = true,
      bool validFromIsGeneratedAlways = true,
      bool validToIsGeneratedAlways = true)
    {
      var columns = new List<ColumnMetadata>
      {
        new() { Name = "id", Type = "UNIQUEIDENTIFIER", IsPrimaryKey = true },
        new() { Name = "record_created_by", Type = "UNIQUEIDENTIFIER" },
        new() { Name = "record_updated_by", Type = "UNIQUEIDENTIFIER" }
      };

      if (includeValidFrom)
      {
        columns.Add(new ColumnMetadata
        {
          Name = "record_valid_from",
          Type = "DATETIME2(7)",
          IsGeneratedAlways = validFromIsGeneratedAlways,
          GeneratedAlwaysType = validFromIsGeneratedAlways ? GeneratedAlwaysType.RowStart : null
        });
      }
      if (includeValidTo)
      {
        columns.Add(new ColumnMetadata
        {
          Name = "record_valid_until",
          Type = "DATETIME2(7)",
          IsGeneratedAlways = validToIsGeneratedAlways,
          GeneratedAlwaysType = validToIsGeneratedAlways ? GeneratedAlwaysType.RowEnd : null
        });
      }

      return new TableMetadata
      {
        Name = name,
        Schema = "test",
        PrimaryKey = "id",
        HasTemporalVersioning = true,
        HistoryTable = historyTable ?? $"[test].[{name}_history]",
        Columns = columns,
        Constraints = new ConstraintsCollection
        {
          PrimaryKey = new PrimaryKeyConstraint { Name = $"pk_{name}", Columns = ["id"] }
        }
      };
    }

    [Fact]
    public void ValidSetup_NoErrors()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTemporalTable("events");

      SchemaValidation.ValidateTemporalTables(
        CreateMetadata(table), config, null, errors, warnings);

      errors.Should().BeEmpty();
      warnings.Should().BeEmpty();
    }

    [Fact]
    public void MissingValidFrom_ReportsError()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTemporalTable("events", includeValidFrom: false);

      SchemaValidation.ValidateTemporalTables(
        CreateMetadata(table), config, null, errors, warnings);

      errors.Should().Contain(e => e.Contains("missing 'record_valid_from'"));
    }

    [Fact]
    public void NotGeneratedAlways_ReportsError()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTemporalTable("events", validFromIsGeneratedAlways: false);

      SchemaValidation.ValidateTemporalTables(
        CreateMetadata(table), config, null, errors, warnings);

      errors.Should().Contain(e => e.Contains("GENERATED ALWAYS AS ROW START"));
    }

    [Fact]
    public void MissingHistoryTable_Warns()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTemporalTable("events");
      table = table with { HistoryTable = null };

      SchemaValidation.ValidateTemporalTables(
        CreateMetadata(table), config, null, errors, warnings);

      errors.Should().BeEmpty();
      warnings.Should().ContainSingle().Which.Should().Contain("missing history table");
    }

    [Fact]
    public void PerTableOverrideDisabled_SkipsValidation()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig(c =>
      {
        c.Overrides = new Dictionary<string, TableOverrideConfig>
        {
          ["special_table"] = new TableOverrideConfig
          {
            Validation = new ValidationOverrideConfig { ValidateTemporal = false }
          }
        };
      });

      TableMetadata table = CreateTable("special_table", t => t with
      {
        HasTemporalVersioning = true
      });

      SchemaValidation.ValidateTemporalTables(
        CreateMetadata(table), config, null, errors, warnings);

      errors.Should().BeEmpty("per-table override disabled temporal validation");
    }

    [Fact]
    public void TaskOverrideTrue_OverridesPerTableDisabled()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig(c =>
      {
        c.Overrides = new Dictionary<string, TableOverrideConfig>
        {
          ["forced_table"] = new TableOverrideConfig
          {
            Validation = new ValidationOverrideConfig { ValidateTemporal = false }
          }
        };
      });

      TableMetadata table = CreateTable("forced_table", t => t with
      {
        HasTemporalVersioning = true
      });

      SchemaValidation.ValidateTemporalTables(
        CreateMetadata(table), config, true, errors, warnings);

      errors.Should().NotBeEmpty("task override true should force validation");
    }
  }

  // --- Audit column validation ---------------------------------------------

  public class AuditColumnValidationTests : SchemaValidationTests
  {
    [Fact]
    public void MissingAuditColumns_ReportsErrors()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();

      var table = new TableMetadata
      {
        Name = "bare_table",
        Schema = "test",
        PrimaryKey = "id",
        Columns =
        [
          new ColumnMetadata { Name = "id", Type = "INT", IsPrimaryKey = true },
          new ColumnMetadata { Name = "name", Type = "VARCHAR(100)" }
        ],
        Constraints = new ConstraintsCollection()
      };

      SchemaValidation.ValidateAuditColumnConsistency(
        CreateMetadata(table), config, null, errors, warnings);

      errors.Should().Contain(e => e.Contains("record_created_by"));
      errors.Should().Contain(e => e.Contains("record_updated_by"));
    }

    [Fact]
    public void AppendOnly_MissingCreatedAt_Warns()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();

      TableMetadata table = CreateTable("logs", t => t with
      {
        IsAppendOnly = true,
        Columns =
        [
          new ColumnMetadata { Name = "id", Type = "UNIQUEIDENTIFIER", IsPrimaryKey = true },
          new ColumnMetadata { Name = "record_created_by", Type = "UNIQUEIDENTIFIER" }
        ]
      });

      SchemaValidation.ValidateAuditColumnConsistency(
        CreateMetadata(table), config, null, errors, warnings);

      errors.Should().BeEmpty("append-only tables are validated with warnings, not errors");
      warnings.Should().Contain(w => w.Contains("record_created_at"));
    }

    [Fact]
    public void AppendOnly_HasUpdatedBy_Warns()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();

      TableMetadata table = CreateTable("logs", t => t with
      {
        IsAppendOnly = true,
        Columns =
        [
          .. t.Columns,
          new ColumnMetadata { Name = "record_created_at", Type = "DATETIMEOFFSET" }
        ]
      });

      SchemaValidation.ValidateAuditColumnConsistency(
        CreateMetadata(table), config, null, errors, warnings);

      warnings.Should().Contain(w => w.Contains("should not have 'record_updated_by'"));
    }

    [Fact]
    public void PerTableOverrideDisabled_SkipsValidation()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig(c =>
      {
        c.Overrides = new Dictionary<string, TableOverrideConfig>
        {
          ["category:system"] = new TableOverrideConfig
          {
            Validation = new ValidationOverrideConfig { ValidateAuditColumns = false }
          }
        };
      });

      var table = new TableMetadata
      {
        Name = "system_config",
        Schema = "test",
        Category = "system",
        PrimaryKey = "id",
        Columns = [new ColumnMetadata { Name = "id", Type = "INT", IsPrimaryKey = true }],
        Constraints = new ConstraintsCollection()
      };

      SchemaValidation.ValidateAuditColumnConsistency(
        CreateMetadata(table), config, null, errors, warnings);

      errors.Should().BeEmpty("category override disabled audit validation");
    }
  }

  // --- Naming convention validation ----------------------------------------

  public class NamingConventionTests : SchemaValidationTests
  {
    [Fact]
    public void SnakeCaseNames_NoWarnings()
    {
      var warnings = new List<string>();
      TableMetadata table = CreateTable("user_accounts");

      SchemaValidation.ValidateNamingConventions(CreateMetadata(table), warnings);
      warnings.Should().BeEmpty();
    }

    [Fact]
    public void PascalCaseTableName_Warns()
    {
      var warnings = new List<string>();
      TableMetadata table = CreateTable("UserAccounts");

      SchemaValidation.ValidateNamingConventions(CreateMetadata(table), warnings);
      warnings.Should().Contain(w => w.Contains("snake_case"));
    }

    [Fact]
    public void PascalCaseColumnName_Warns()
    {
      var warnings = new List<string>();
      TableMetadata table = CreateTable("users", t => t with
      {
        Columns = [.. t.Columns, new ColumnMetadata { Name = "FirstName", Type = "VARCHAR(50)" }]
      });

      SchemaValidation.ValidateNamingConventions(CreateMetadata(table), warnings);
      warnings.Should().Contain(w => w.Contains("FirstName") && w.Contains("snake_case"));
    }

    [Fact]
    public void WrongPkName_Warns()
    {
      var warnings = new List<string>();
      TableMetadata table = CreateTable("users", t => t with
      {
        Constraints = t.Constraints with
        {
          PrimaryKey = new PrimaryKeyConstraint { Name = "PK_Users", Columns = ["id"] }
        }
      });

      SchemaValidation.ValidateNamingConventions(CreateMetadata(table), warnings);
      warnings.Should().Contain(w => w.Contains("pk_users") && w.Contains("PK_Users"));
    }

    [Fact]
    public void WrongFkPrefix_Warns()
    {
      var warnings = new List<string>();
      TableMetadata parent = CreateTable("parent");
      TableMetadata child = CreateTable("child", t => t with
      {
        Constraints = t.Constraints with
        {
          ForeignKeys =
          [
            new ForeignKeyConstraint
            {
              Name = "FK_Child_Parent",
              Columns = ["parent_id"],
              ReferencedTable = "parent",
              ReferencedColumns = ["id"]
            }
          ]
        }
      });

      SchemaValidation.ValidateNamingConventions(
        CreateMetadata(parent, child), warnings);

      warnings.Should().Contain(w => w.Contains("fk_child_"));
    }
  }

  // --- Primary key validation ----------------------------------------------

  public class PrimaryKeyValidationTests : SchemaValidationTests
  {
    [Fact]
    public void MissingPK_ReportsError()
    {
      var errors = new List<string>();
      var table = new TableMetadata
      {
        Name = "no_pk",
        Schema = "test",
        Columns = [new ColumnMetadata { Name = "data", Type = "VARCHAR(100)" }],
        Constraints = new ConstraintsCollection()
      };

      SchemaValidation.ValidatePrimaryKeys(CreateMetadata(table), errors);
      errors.Should().ContainSingle().Which.Should().Contain("no primary key");
    }

    [Fact]
    public void HistoryTable_SkippedFromPKValidation()
    {
      var errors = new List<string>();
      var table = new TableMetadata
      {
        Name = "events_history",
        Schema = "test",
        IsHistoryTable = true,
        Columns = [new ColumnMetadata { Name = "data", Type = "VARCHAR(100)" }],
        Constraints = new ConstraintsCollection()
      };

      SchemaValidation.ValidatePrimaryKeys(CreateMetadata(table), errors);
      errors.Should().BeEmpty("history tables have no PK by design");
    }

    [Fact]
    public void PKColumnNotFound_ReportsError()
    {
      var errors = new List<string>();
      var table = new TableMetadata
      {
        Name = "bad_pk",
        Schema = "test",
        PrimaryKey = "phantom_id",
        Columns = [new ColumnMetadata { Name = "data", Type = "VARCHAR(100)" }],
        Constraints = new ConstraintsCollection()
      };

      SchemaValidation.ValidatePrimaryKeys(CreateMetadata(table), errors);
      errors.Should().ContainSingle().Which.Should().Contain("phantom_id");
    }

    [Fact]
    public void CompositePK_ValidWithoutPrimaryKeyProperty()
    {
      var errors = new List<string>();
      var table = new TableMetadata
      {
        Name = "composite",
        Schema = "test",
        Columns =
        [
          new ColumnMetadata { Name = "a", Type = "INT" },
          new ColumnMetadata { Name = "b", Type = "INT" }
        ],
        Constraints = new ConstraintsCollection
        {
          PrimaryKey = new PrimaryKeyConstraint { Name = "pk_composite", Columns = ["a", "b"] }
        }
      };

      SchemaValidation.ValidatePrimaryKeys(CreateMetadata(table), errors);
      errors.Should().BeEmpty("composite PK via Constraints.PrimaryKey is valid");
    }
  }

  // --- Circular FK detection -----------------------------------------------

  public class CircularForeignKeyTests : SchemaValidationTests
  {
    [Fact]
    public void CycleDetected_ReportsError()
    {
      var errors = new List<string>();
      TableMetadata a = WithFKConstraintOnly(
        CreateTable("table_a"), "fk_table_a_b", ["b_id"], "table_b", ["id"]);
      TableMetadata b = WithFKConstraintOnly(
        CreateTable("table_b"), "fk_table_b_a", ["a_id"], "table_a", ["id"]);

      SchemaValidation.ValidateCircularForeignKeys(CreateMetadata(a, b), errors);
      errors.Should().ContainMatch("*Circular foreign key*");
    }

    [Fact]
    public void SelfReference_NotReportedAsCycle()
    {
      var errors = new List<string>();
      TableMetadata table = WithFKConstraintOnly(
        CreateTable("categories"), "fk_categories_parent", ["parent_id"], "categories", ["id"]);

      SchemaValidation.ValidateCircularForeignKeys(CreateMetadata(table), errors);
      errors.Should().BeEmpty("self-references are allowed for hierarchies");
    }

    [Fact]
    public void LinearChain_NoCycle()
    {
      var errors = new List<string>();
      TableMetadata a = CreateTable("table_a");
      TableMetadata b = WithFKConstraintOnly(
        CreateTable("table_b"), "fk_table_b_a", ["a_id"], "table_a", ["id"]);
      TableMetadata c = WithFKConstraintOnly(
        CreateTable("table_c"), "fk_table_c_b", ["b_id"], "table_b", ["id"]);

      SchemaValidation.ValidateCircularForeignKeys(CreateMetadata(a, b, c), errors);
      errors.Should().BeEmpty("A -> B -> C is not circular");
    }

    [Fact]
    public void ThreeTableCycle_ReportsError()
    {
      var errors = new List<string>();
      TableMetadata a = WithFKConstraintOnly(
        CreateTable("table_a"), "fk_table_a_c", ["c_id"], "table_c", ["id"]);
      TableMetadata b = WithFKConstraintOnly(
        CreateTable("table_b"), "fk_table_b_a", ["a_id"], "table_a", ["id"]);
      TableMetadata c = WithFKConstraintOnly(
        CreateTable("table_c"), "fk_table_c_b", ["b_id"], "table_b", ["id"]);

      SchemaValidation.ValidateCircularForeignKeys(CreateMetadata(a, b, c), errors);
      errors.Should().ContainMatch("*Circular foreign key*");
    }
  }

  // --- Soft-delete consistency ---------------------------------------------

  public class SoftDeleteConsistencyTests : SchemaValidationTests
  {
    [Theory]
    [InlineData(false, true, "HasActiveColumn is false")]
    [InlineData(true, false, "HasTemporalVersioning is false")]
    public void InvalidSoftDeleteConfiguration_ReportsError(
      bool hasActiveColumn, bool hasTemporal, string expectedMessage)
    {
      var errors = new List<string>();
      TableMetadata table = CreateTable("bad_soft", t => t with
      {
        HasSoftDelete = true,
        HasActiveColumn = hasActiveColumn,
        HasTemporalVersioning = hasTemporal
      });

      SchemaValidation.ValidateSoftDeleteConsistency(CreateMetadata(table), errors);
      errors.Should().Contain(e => e.Contains(expectedMessage));
    }

    [Fact]
    public void ValidSoftDelete_NoErrors()
    {
      var errors = new List<string>();
      TableMetadata table = CreateTable("good_soft", t => t with
      {
        HasSoftDelete = true,
        HasActiveColumn = true,
        HasTemporalVersioning = true
      });

      SchemaValidation.ValidateSoftDeleteConsistency(CreateMetadata(table), errors);
      errors.Should().BeEmpty();
    }

    [Fact]
    public void NonSoftDeleteTable_Skipped()
    {
      var errors = new List<string>();
      TableMetadata table = CreateTable("normal");

      SchemaValidation.ValidateSoftDeleteConsistency(CreateMetadata(table), errors);
      errors.Should().BeEmpty();
    }
  }

  // --- Unique constraint validation ----------------------------------------

  public class UniqueConstraintTests : SchemaValidationTests
  {
    [Fact]
    public void InvalidColumnReference_ReportsError()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTable("users", t => t with
      {
        Constraints = t.Constraints with
        {
          UniqueConstraints =
          [
            new UniqueConstraint { Name = "uq_users_email", Columns = ["email"] }
          ]
        }
      });

      SchemaValidation.ValidateUniqueConstraints(
        CreateMetadata(table), config, errors, warnings);

      errors.Should().ContainSingle().Which.Should().Contain("non-existent column 'email'");
    }

    [Fact]
    public void FilteredWithoutActiveColumn_WarnsForSoftDeleteTable()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTable("users", t => t with
      {
        HasSoftDelete = true,
        Columns = [.. t.Columns, new ColumnMetadata { Name = "email", Type = "VARCHAR(255)" }],
        Constraints = t.Constraints with
        {
          UniqueConstraints =
          [
            new UniqueConstraint
            {
              Name = "uq_users_email",
              Columns = ["email"],
              FilterClause = "[status] = 1"
            }
          ]
        }
      });

      SchemaValidation.ValidateUniqueConstraints(
        CreateMetadata(table), config, errors, warnings);

      warnings.Should().Contain(w => w.Contains("record_active"));
    }

    [Fact]
    public void FilteredWithActiveColumn_NoWarningForSoftDeleteTable()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTable("users", t => t with
      {
        HasSoftDelete = true,
        Columns = [.. t.Columns, new ColumnMetadata { Name = "email", Type = "VARCHAR(255)" }],
        Constraints = t.Constraints with
        {
          UniqueConstraints =
          [
            new UniqueConstraint
            {
              Name = "uq_users_email",
              Columns = ["email"],
              FilterClause = "[record_active] = 1"
            }
          ]
        }
      });

      SchemaValidation.ValidateUniqueConstraints(
        CreateMetadata(table), config, errors, warnings);

      errors.Should().BeEmpty();
      warnings.Should().BeEmpty();
    }

    [Fact]
    public void ValidColumnReference_NoErrors()
    {
      var errors = new List<string>();
      var warnings = new List<string>();
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTable("users", t => t with
      {
        Columns = [.. t.Columns, new ColumnMetadata { Name = "email", Type = "VARCHAR(255)" }],
        Constraints = t.Constraints with
        {
          UniqueConstraints =
          [
            new UniqueConstraint { Name = "uq_users_email", Columns = ["email"] }
          ]
        }
      });

      SchemaValidation.ValidateUniqueConstraints(
        CreateMetadata(table), config, errors, warnings);

      errors.Should().BeEmpty();
      warnings.Should().BeEmpty();
    }
  }
}
