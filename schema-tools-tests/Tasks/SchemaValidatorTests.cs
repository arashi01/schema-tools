using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

public class SchemaValidatorTests
{
  private static SchemaToolsConfig CreateConfig(Action<ValidationConfig>? configure = null)
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
    configure?.Invoke(config.Validation);
    return config;
  }

  private static TableMetadata CreateTable(string name, Action<TableMetadata>? configure = null)
  {
    var table = new TableMetadata
    {
      Name = name,
      Schema = "test",
      PrimaryKey = "id",
      Columns =
        [
            new ColumnMetadata { Name = "id", Type = "UNIQUEIDENTIFIER", IsPrimaryKey = true },
                new ColumnMetadata { Name = "created_by", Type = "UNIQUEIDENTIFIER" },
                new ColumnMetadata { Name = "updated_by", Type = "UNIQUEIDENTIFIER" }
        ],
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = new PrimaryKeyConstraint { Name = $"pk_{name}", Columns = ["id"] }
      }
    };
    configure?.Invoke(table);
    return table;
  }

  private static SchemaMetadata CreateMetadata(params TableMetadata[] tables) => new()
  {
    Database = "TestDB",
    DefaultSchema = "test",
    Tables = [.. tables]
  };

  private (bool success, SchemaValidator task) RunValidator(
      SchemaMetadata metadata, SchemaToolsConfig? config = null)
  {
    var task = new SchemaValidator
    {
      MetadataFile = "unused",
      TestConfig = config ?? CreateConfig(),
      TestMetadata = metadata,
      BuildEngine = new MockBuildEngine()
    };
    bool result = task.Execute();
    return (result, task);
  }

  // ─── FK validation ──────────────────────────────────────────────

  [Fact]
  public void Validate_ForeignKeyToExistingTable_Passes()
  {
    TableMetadata parent = CreateTable("parent");
    TableMetadata child = CreateTable("child", t =>
    {
      t.Constraints.ForeignKeys.Add(new ForeignKeyConstraint
      {
        Name = "fk_child_parent",
        Columns = ["parent_id"],
        ReferencedTable = "parent",
        ReferencedColumns = ["id"]
      });
      t.Columns.Add(new ColumnMetadata { Name = "parent_id", Type = "UNIQUEIDENTIFIER" });
    });

    (bool success, SchemaValidator _) = RunValidator(CreateMetadata(parent, child));
    success.Should().BeTrue();
  }

  [Fact]
  public void Validate_ForeignKeyToMissingTable_ReportsError()
  {
    TableMetadata table = CreateTable("orphan", t =>
    {
      t.Constraints.ForeignKeys.Add(new ForeignKeyConstraint
      {
        Name = "fk_orphan_missing",
        Columns = ["ref_id"],
        ReferencedTable = "nonexistent",
        ReferencedColumns = ["id"]
      });
      t.Columns.Add(new ColumnMetadata { Name = "ref_id", Type = "UNIQUEIDENTIFIER" });
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*references non-existent table*");
  }

  [Fact]
  public void Validate_ForeignKeyToMissingColumn_ReportsError()
  {
    TableMetadata parent = CreateTable("parent");
    TableMetadata child = CreateTable("child", t =>
    {
      t.Constraints.ForeignKeys.Add(new ForeignKeyConstraint
      {
        Name = "fk_child_parent",
        Columns = ["parent_id"],
        ReferencedTable = "parent",
        ReferencedColumns = ["nonexistent_col"]
      });
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(parent, child));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*references non-existent column*");
  }

  [Fact]
  public void Validate_ForeignKeyColumnCountMismatch_ReportsError()
  {
    TableMetadata parent = CreateTable("parent");
    TableMetadata child = CreateTable("child", t =>
    {
      t.Constraints.ForeignKeys.Add(new ForeignKeyConstraint
      {
        Name = "fk_child_parent",
        Columns = ["a", "b"],
        ReferencedTable = "parent",
        ReferencedColumns = ["id"]
      });
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(parent, child));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*mismatched column counts*");
  }

  // ─── Circular FK detection ──────────────────────────────────────

  [Fact]
  public void Validate_CircularForeignKeys_ReportsError()
  {
    TableMetadata a = CreateTable("table_a", t =>
    {
      t.Constraints.ForeignKeys.Add(new ForeignKeyConstraint
      {
        Name = "fk_table_a_b",
        Columns = ["b_id"],
        ReferencedTable = "table_b",
        ReferencedColumns = ["id"]
      });
      t.Columns.Add(new ColumnMetadata { Name = "b_id", Type = "UNIQUEIDENTIFIER" });
    });

    TableMetadata b = CreateTable("table_b", t =>
    {
      t.Constraints.ForeignKeys.Add(new ForeignKeyConstraint
      {
        Name = "fk_table_b_a",
        Columns = ["a_id"],
        ReferencedTable = "table_a",
        ReferencedColumns = ["id"]
      });
      t.Columns.Add(new ColumnMetadata { Name = "a_id", Type = "UNIQUEIDENTIFIER" });
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(a, b));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*Circular foreign key*");
  }

  // ─── Polymorphic validation ─────────────────────────────────────

  [Fact]
  public void Validate_PolymorphicWithCheckConstraint_Passes()
  {
    TableMetadata table = CreateTable("notes", t =>
    {
      t.IsPolymorphic = true;
      t.PolymorphicOwner = new PolymorphicOwnerInfo
      {
        TypeColumn = "owner_type",
        IdColumn = "owner_id",
        AllowedTypes = ["user", "company"]
      };
      t.Columns.Add(new ColumnMetadata { Name = "owner_type", Type = "VARCHAR(20)" });
      t.Columns.Add(new ColumnMetadata { Name = "owner_id", Type = "UNIQUEIDENTIFIER" });
      t.Constraints.CheckConstraints.Add(new CheckConstraint
      {
        Name = "ck_notes_owner_type",
        Expression = "[owner_type] IN ('user', 'company')"
      });
    });

    (bool success, SchemaValidator _) = RunValidator(CreateMetadata(table));
    success.Should().BeTrue();
  }

  [Fact]
  public void Validate_PolymorphicWithoutCheckConstraint_ReportsError()
  {
    TableMetadata table = CreateTable("notes", t =>
    {
      t.IsPolymorphic = true;
      t.PolymorphicOwner = new PolymorphicOwnerInfo
      {
        TypeColumn = "owner_type",
        IdColumn = "owner_id",
        AllowedTypes = ["user"]
      };
      t.Columns.Add(new ColumnMetadata { Name = "owner_type", Type = "VARCHAR(20)" });
      t.Columns.Add(new ColumnMetadata { Name = "owner_id", Type = "UNIQUEIDENTIFIER" });
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*missing CHECK constraint*");
  }

  [Fact]
  public void Validate_PolymorphicWithNoAllowedTypes_WarnsButPasses()
  {
    TableMetadata table = CreateTable("notes", t =>
    {
      t.IsPolymorphic = true;
      t.PolymorphicOwner = new PolymorphicOwnerInfo
      {
        TypeColumn = "owner_type",
        IdColumn = "owner_id",
        AllowedTypes = [] // empty
      };
      t.Columns.Add(new ColumnMetadata { Name = "owner_type", Type = "VARCHAR(20)" });
      t.Columns.Add(new ColumnMetadata { Name = "owner_id", Type = "UNIQUEIDENTIFIER" });
      t.Constraints.CheckConstraints.Add(new CheckConstraint
      {
        Name = "ck_notes_owner_type",
        Expression = "[owner_type] IN ('user')"
      });
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeTrue("empty allowed types is a warning, not an error");
    task.ValidationWarnings.Should().ContainMatch("*no allowed types*");
  }

  // ─── Temporal validation ────────────────────────────────────────

  [Fact]
  public void Validate_TemporalTableWithCorrectStructure_Passes()
  {
    TableMetadata table = CreateTable("events", t =>
    {
      t.HasTemporalVersioning = true;
      t.HistoryTable = "[test].[events_history]";
      t.Columns.Add(new ColumnMetadata
      {
        Name = "valid_from",
        Type = "DATETIME2(7)",
        IsGeneratedAlways = true,
        GeneratedAlwaysType = "RowStart"
      });
      t.Columns.Add(new ColumnMetadata
      {
        Name = "valid_to",
        Type = "DATETIME2(7)",
        IsGeneratedAlways = true,
        GeneratedAlwaysType = "RowEnd"
      });
    });

    (bool success, SchemaValidator _) = RunValidator(CreateMetadata(table));
    success.Should().BeTrue();
  }

  [Fact]
  public void Validate_TemporalMissingValidFrom_ReportsError()
  {
    TableMetadata table = CreateTable("events", t =>
    {
      t.HasTemporalVersioning = true;
      t.HistoryTable = "[test].[events_history]";
      t.Columns.Add(new ColumnMetadata
      {
        Name = "valid_to",
        Type = "DATETIME2(7)",
        IsGeneratedAlways = true
      });
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*missing 'valid_from'*");
  }

  [Fact]
  public void Validate_TemporalWithoutGeneratedAlways_ReportsError()
  {
    TableMetadata table = CreateTable("events", t =>
    {
      t.HasTemporalVersioning = true;
      t.HistoryTable = "[test].[events_history]";
      t.Columns.Add(new ColumnMetadata
      {
        Name = "valid_from",
        Type = "DATETIME2(7)",
        IsGeneratedAlways = false
      });
      t.Columns.Add(new ColumnMetadata
      {
        Name = "valid_to",
        Type = "DATETIME2(7)",
        IsGeneratedAlways = true
      });
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*GENERATED ALWAYS*");
  }

  [Fact]
  public void Validate_TemporalMissingHistoryTable_WarnsButPasses()
  {
    TableMetadata table = CreateTable("events", t =>
    {
      t.HasTemporalVersioning = true;
      t.HistoryTable = null;
      t.Columns.Add(new ColumnMetadata
      {
        Name = "valid_from",
        Type = "DATETIME2(7)",
        IsGeneratedAlways = true,
        GeneratedAlwaysType = "RowStart"
      });
      t.Columns.Add(new ColumnMetadata
      {
        Name = "valid_to",
        Type = "DATETIME2(7)",
        IsGeneratedAlways = true,
        GeneratedAlwaysType = "RowEnd"
      });
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeTrue();
    task.ValidationWarnings.Should().ContainMatch("*missing history table*");
  }

  // ─── Audit column validation ────────────────────────────────────

  [Fact]
  public void Validate_MissingAuditColumns_ReportsError()
  {
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
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = new PrimaryKeyConstraint { Name = "pk_bare_table", Columns = ["id"] }
      }
    };

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*Missing required 'created_by'*");
    task.ValidationErrors.Should().ContainMatch("*Missing required 'updated_by'*");
  }

  [Fact]
  public void Validate_AppendOnlyWithoutCreatedAt_WarnsButPasses()
  {
    TableMetadata table = CreateTable("logs", t =>
    {
      t.IsAppendOnly = true;
      // Remove updated_by — append-only shouldn't have it
      t.Columns.RemoveAll(c => c.Name == "updated_by");
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeTrue();
    task.ValidationWarnings.Should().ContainMatch("*missing 'created_at'*");
  }

  [Fact]
  public void Validate_AppendOnlyWithUpdatedBy_WarnsButPasses()
  {
    TableMetadata table = CreateTable("logs", t =>
    {
      t.IsAppendOnly = true;
      t.Columns.Add(new ColumnMetadata { Name = "created_at", Type = "DATETIMEOFFSET(7)" });
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeTrue();
    task.ValidationWarnings.Should().ContainMatch("*should not have 'updated_by'*");
  }

  // ─── Naming convention validation ───────────────────────────────

  [Fact]
  public void Validate_SnakeCaseNames_Passes()
  {
    TableMetadata table = CreateTable("user_accounts");
    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeTrue();
    task.ValidationWarnings.Should().BeEmpty();
  }

  [Fact]
  public void Validate_PascalCaseTableName_Warns()
  {
    TableMetadata table = CreateTable("UserAccounts");

    (bool _, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    // Naming is a warning, not an error — validation still passes
    task.ValidationWarnings.Should().ContainMatch("*snake_case*");
  }

  [Fact]
  public void Validate_WrongPkNamingConvention_Warns()
  {
    TableMetadata table = CreateTable("users", t =>
    {
      t.Constraints.PrimaryKey = new PrimaryKeyConstraint
      {
        Name = "PK_Users",
        Columns = ["id"]
      };
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    task.ValidationWarnings.Should().ContainMatch("*PK should be named 'pk_users'*");
  }

  [Fact]
  public void Validate_WrongFkNamingConvention_Warns()
  {
    TableMetadata parent = CreateTable("parent");
    TableMetadata child = CreateTable("child", t =>
    {
      t.Constraints.ForeignKeys.Add(new ForeignKeyConstraint
      {
        Name = "FK_Child_Parent",
        Columns = ["parent_id"],
        ReferencedTable = "parent",
        ReferencedColumns = ["id"]
      });
      t.Columns.Add(new ColumnMetadata { Name = "parent_id", Type = "UNIQUEIDENTIFIER" });
    });

    (bool _, SchemaValidator? task) = RunValidator(CreateMetadata(parent, child));
    task.ValidationWarnings.Should().ContainMatch("*should start with 'fk_child_'*");
  }

  // ─── Primary key validation ─────────────────────────────────────

  [Fact]
  public void Validate_MissingPrimaryKey_ReportsError()
  {
    var table = new TableMetadata
    {
      Name = "no_pk",
      Schema = "test",
      Columns =
        [
            new ColumnMetadata { Name = "data", Type = "VARCHAR(100)" },
                new ColumnMetadata { Name = "created_by", Type = "UNIQUEIDENTIFIER" },
                new ColumnMetadata { Name = "updated_by", Type = "UNIQUEIDENTIFIER" }
        ],
      Constraints = new ConstraintsCollection()
    };

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*no primary key*");
  }

  // ─── Soft delete consistency ────────────────────────────────────

  [Fact]
  public void Validate_SoftDeleteWithoutTemporal_ReportsError()
  {
    TableMetadata table = CreateTable("bad_soft", t =>
    {
      t.HasSoftDelete = true;
      t.HasActiveColumn = true;
      t.HasTemporalVersioning = false;
      t.Triggers = new TriggerConfiguration
      {
        HardDelete = new HardDeleteTrigger { Generate = true, Name = "trg_bad_soft_hard_delete" }
      };
    });

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*HasTemporalVersioning is false*");
  }

  // ─── TreatWarningsAsErrors ──────────────────────────────────────

  [Fact]
  public void Validate_TreatWarningsAsErrors_FailsOnWarning()
  {
    SchemaToolsConfig config = CreateConfig(v => v.TreatWarningsAsErrors = true);

    // PascalCase table will generate a naming warning
    TableMetadata table = CreateTable("BadName");

    (bool success, SchemaValidator _) = RunValidator(CreateMetadata(table), config);
    success.Should().BeFalse("warnings should be treated as errors");
  }

  // ─── Disabling individual validations ───────────────────────────

  [Fact]
  public void Validate_DisabledNamingConventions_IgnoresStyleIssues()
  {
    SchemaToolsConfig config = CreateConfig(v => v.EnforceNamingConventions = false);
    TableMetadata table = CreateTable("PascalCase");

    (bool success, SchemaValidator? task) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
    task.ValidationWarnings.Should().NotContainMatch("*snake_case*");
  }

  [Fact]
  public void Validate_DisabledAuditColumns_IgnoresMissingColumns()
  {
    SchemaToolsConfig config = CreateConfig(v => v.ValidateAuditColumns = false);

    var table = new TableMetadata
    {
      Name = "minimal",
      Schema = "test",
      PrimaryKey = "id",
      Columns = [new ColumnMetadata { Name = "id", Type = "INT", IsPrimaryKey = true }],
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = new PrimaryKeyConstraint { Name = "pk_minimal", Columns = ["id"] }
      }
    };

    (bool success, SchemaValidator _) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
  }

  // ─── Configurable column names ──────────────────────────────────

  [Fact]
  public void Validate_CustomTemporalColumns_ValidatesCorrectNames()
  {
    SchemaToolsConfig config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Columns = new ColumnNamingConfig { ValidFrom = "period_start", ValidTo = "period_end" }
    };

    TableMetadata table = CreateTable("temporal", t =>
    {
      t.HasTemporalVersioning = true;
      t.HistoryTable = "[test].[temporal_history]";
      t.Columns.Add(new ColumnMetadata { Name = "period_start", Type = "DATETIME2", IsGeneratedAlways = true });
      t.Columns.Add(new ColumnMetadata { Name = "period_end", Type = "DATETIME2", IsGeneratedAlways = true });
    });

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
    task.ValidationErrors.Should().NotContainMatch("*missing*period_start*");
  }

  [Fact]
  public void Validate_CustomTemporalColumns_ReportsErrorForMissingColumns()
  {
    SchemaToolsConfig config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Columns = new ColumnNamingConfig { ValidFrom = "period_start", ValidTo = "period_end" }
    };

    TableMetadata table = CreateTable("temporal", t =>
    {
      t.HasTemporalVersioning = true;
      // Table has default valid_from / valid_to but config expects period_start / period_end
      t.Columns.Add(new ColumnMetadata { Name = "valid_from", Type = "DATETIME2", IsGeneratedAlways = true });
      t.Columns.Add(new ColumnMetadata { Name = "valid_to", Type = "DATETIME2", IsGeneratedAlways = true });
    });

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table), config);
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*missing*period_start*");
    task.ValidationErrors.Should().ContainMatch("*missing*period_end*");
  }

  [Fact]
  public void Validate_CustomAuditColumns_ValidatesCorrectNames()
  {
    SchemaToolsConfig config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Columns = new ColumnNamingConfig { CreatedBy = "author", UpdatedBy = "editor" }
    };

    TableMetadata table = new TableMetadata
    {
      Name = "custom_audit",
      Schema = "test",
      PrimaryKey = "id",
      Columns =
      [
          new ColumnMetadata { Name = "id", Type = "INT", IsPrimaryKey = true },
          new ColumnMetadata { Name = "author", Type = "UNIQUEIDENTIFIER" },
          new ColumnMetadata { Name = "editor", Type = "UNIQUEIDENTIFIER" }
      ],
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = new PrimaryKeyConstraint { Name = "pk_custom_audit", Columns = ["id"] }
      }
    };

    (bool success, SchemaValidator _) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
  }

  [Fact]
  public void Validate_CustomActiveColumn_ChecksSoftDeleteCorrectly()
  {
    SchemaToolsConfig config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Columns = new ColumnNamingConfig { Active = "is_enabled" }
    };

    TableMetadata table = CreateTable("soft_del", t =>
    {
      t.HasSoftDelete = true;
      t.HasActiveColumn = true;
      t.HasTemporalVersioning = true;
      t.HistoryTable = "[test].[soft_del_history]";
      t.Triggers = new TriggerConfiguration
      {
        HardDelete = new HardDeleteTrigger
        {
          Generate = true,
          Name = "trg_soft_del_hard_delete",
          ActiveColumnName = "is_enabled"
        }
      };
      t.Columns.Add(new ColumnMetadata { Name = "is_enabled", Type = "BIT", DefaultValue = "1" });
      t.Columns.Add(new ColumnMetadata { Name = "valid_from", Type = "DATETIME2", IsGeneratedAlways = true });
      t.Columns.Add(new ColumnMetadata { Name = "valid_to", Type = "DATETIME2", IsGeneratedAlways = true });
    });

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
    task.ValidationWarnings.Should().NotContainMatch("*Should have DEFAULT 1*");
  }

  // ─── Per-table overrides in validator ───────────────────────────

  [Fact]
  public void Validate_PerTableOverride_SkipsTemporalValidation()
  {
    SchemaToolsConfig config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Overrides = new Dictionary<string, TableOverrideConfig>
      {
        ["special_table"] = new TableOverrideConfig
        {
          Validation = new ValidationOverrideConfig { ValidateTemporal = false }
        }
      }
    };

    TableMetadata table = CreateTable("special_table", t =>
    {
      t.HasTemporalVersioning = true;
      // Deliberately missing temporal columns — should not produce errors
    });

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
    task.ValidationErrors.Should().NotContainMatch("*special_table*Temporal*");
  }

  [Fact]
  public void Validate_PerTableOverride_SkipsAuditValidationForCategory()
  {
    SchemaToolsConfig config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Overrides = new Dictionary<string, TableOverrideConfig>
      {
        ["category:system"] = new TableOverrideConfig
        {
          Validation = new ValidationOverrideConfig { ValidateAuditColumns = false }
        }
      }
    };

    TableMetadata table = new TableMetadata
    {
      Name = "system_config",
      Schema = "test",
      Category = "system",
      PrimaryKey = "id",
      Columns = [new ColumnMetadata { Name = "id", Type = "INT", IsPrimaryKey = true }],
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = new PrimaryKeyConstraint { Name = "pk_system_config", Columns = ["id"] }
      }
    };

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
    task.ValidationErrors.Should().NotContainMatch("*system_config*Missing required*");
  }
}
