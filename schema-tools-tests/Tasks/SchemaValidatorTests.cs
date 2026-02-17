using SchemaTools.Configuration;
using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

/// <summary>
/// Integration tests for <see cref="SchemaValidator"/> MSBuild task.
/// These tests exercise the full Execute() pipeline including configuration
/// loading, metadata injection, validation, and MSBuild error/warning reporting.
/// Pure validation logic is tested in <see cref="SchemaValidationTests"/>.
/// </summary>
public class SchemaValidatorTests
{
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

  private static (bool success, SchemaValidator task) RunValidator(
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

  // --- FK integration through Execute() ------------------------------------

  [Fact]
  public void ValidFK_Passes()
  {
    TableMetadata parent = CreateTable("parent");
    TableMetadata child = CreateTable("child", t => t with
    {
      Columns = [.. t.Columns, new ColumnMetadata { Name = "parent_id", Type = "UNIQUEIDENTIFIER" }],
      Constraints = t.Constraints with
      {
        ForeignKeys =
        [
          new ForeignKeyConstraint
          {
            Name = "fk_child_parent",
            Columns = ["parent_id"],
            ReferencedTable = "parent",
            ReferencedColumns = ["id"]
          }
        ]
      }
    });

    (bool success, _) = RunValidator(CreateMetadata(parent, child));
    success.Should().BeTrue();
  }

  [Fact]
  public void FKToMissingTable_FailsWithError()
  {
    TableMetadata table = CreateTable("orphan", t => t with
    {
      Columns = [.. t.Columns, new ColumnMetadata { Name = "ref_id", Type = "UNIQUEIDENTIFIER" }],
      Constraints = t.Constraints with
      {
        ForeignKeys =
        [
          new ForeignKeyConstraint
          {
            Name = "fk_orphan_missing",
            Columns = ["ref_id"],
            ReferencedTable = "nonexistent",
            ReferencedColumns = ["id"]
          }
        ]
      }
    });

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*references non-existent table*");
  }

  // --- Circular FK through Execute() ---------------------------------------

  [Fact]
  public void CircularFKs_FailsWithError()
  {
    TableMetadata a = CreateTable("table_a", t => t with
    {
      Columns = [.. t.Columns, new ColumnMetadata { Name = "b_id", Type = "UNIQUEIDENTIFIER" }],
      Constraints = t.Constraints with
      {
        ForeignKeys =
        [
          new ForeignKeyConstraint
          {
            Name = "fk_table_a_b",
            Columns = ["b_id"],
            ReferencedTable = "table_b",
            ReferencedColumns = ["id"]
          }
        ]
      }
    });

    TableMetadata b = CreateTable("table_b", t => t with
    {
      Columns = [.. t.Columns, new ColumnMetadata { Name = "a_id", Type = "UNIQUEIDENTIFIER" }],
      Constraints = t.Constraints with
      {
        ForeignKeys =
        [
          new ForeignKeyConstraint
          {
            Name = "fk_table_b_a",
            Columns = ["a_id"],
            ReferencedTable = "table_a",
            ReferencedColumns = ["id"]
          }
        ]
      }
    });

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(a, b));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*Circular foreign key*");
  }

  // --- Polymorphic through Execute() ---------------------------------------

  [Fact]
  public void PolymorphicWithCheckConstraint_Passes()
  {
    TableMetadata table = CreateTable("notes", t => t with
    {
      IsPolymorphic = true,
      PolymorphicOwner = new PolymorphicOwnerInfo
      {
        TypeColumn = "owner_type",
        IdColumn = "owner_id",
        AllowedTypes = ["user", "company"]
      },
      Columns =
      [
        .. t.Columns,
        new ColumnMetadata { Name = "owner_type", Type = "VARCHAR(20)" },
        new ColumnMetadata { Name = "owner_id", Type = "UNIQUEIDENTIFIER" }
      ],
      Constraints = t.Constraints with
      {
        CheckConstraints =
        [
          new CheckConstraint
          {
            Name = "ck_notes_owner_type",
            Expression = "[owner_type] IN ('user', 'company')"
          }
        ]
      }
    });

    (bool success, _) = RunValidator(CreateMetadata(table));
    success.Should().BeTrue();
  }

  [Fact]
  public void PolymorphicWithoutCheckConstraint_FailsWithError()
  {
    TableMetadata table = CreateTable("notes", t => t with
    {
      IsPolymorphic = true,
      PolymorphicOwner = new PolymorphicOwnerInfo
      {
        TypeColumn = "owner_type",
        IdColumn = "owner_id",
        AllowedTypes = ["user"]
      },
      Columns =
      [
        .. t.Columns,
        new ColumnMetadata { Name = "owner_type", Type = "VARCHAR(20)" },
        new ColumnMetadata { Name = "owner_id", Type = "UNIQUEIDENTIFIER" }
      ]
    });

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*missing CHECK constraint*");
  }

  // --- Temporal through Execute() ------------------------------------------

  [Fact]
  public void TemporalWithCorrectStructure_Passes()
  {
    TableMetadata table = CreateTable("events", t => t with
    {
      HasTemporalVersioning = true,
      HistoryTable = "[test].[events_history]",
      Columns =
      [
        .. t.Columns,
        new ColumnMetadata
        {
          Name = "record_valid_from",
          Type = "DATETIME2(7)",
          IsGeneratedAlways = true,
          GeneratedAlwaysType = GeneratedAlwaysType.RowStart
        },
        new ColumnMetadata
        {
          Name = "record_valid_until",
          Type = "DATETIME2(7)",
          IsGeneratedAlways = true,
          GeneratedAlwaysType = GeneratedAlwaysType.RowEnd
        }
      ]
    });

    (bool success, _) = RunValidator(CreateMetadata(table));
    success.Should().BeTrue();
  }

  [Fact]
  public void TemporalMissingValidFrom_FailsWithError()
  {
    TableMetadata table = CreateTable("events", t => t with
    {
      HasTemporalVersioning = true,
      HistoryTable = "[test].[events_history]",
      Columns =
      [
        .. t.Columns,
        new ColumnMetadata
        {
          Name = "record_valid_until",
          Type = "DATETIME2(7)",
          IsGeneratedAlways = true
        }
      ]
    });

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*missing 'record_valid_from'*");
  }

  // --- Audit column through Execute() --------------------------------------

  [Fact]
  public void MissingAuditColumns_FailsWithError()
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

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table));
    success.Should().BeFalse();
    task.ValidationErrors.Should().ContainMatch("*record_created_by*");
    task.ValidationErrors.Should().ContainMatch("*record_updated_by*");
  }

  // --- TreatWarningsAsErrors through Execute() -----------------------------

  [Fact]
  public void TreatWarningsAsErrors_FailsOnWarning()
  {
    SchemaToolsConfig config = CreateConfig(c => c.Validation.TreatWarningsAsErrors = true);
    TableMetadata table = CreateTable("BadName");

    (bool success, _) = RunValidator(CreateMetadata(table), config);
    success.Should().BeFalse("warnings treated as errors should fail");
  }

  // --- Disabling individual validations through Execute() ------------------

  [Fact]
  public void DisabledNamingConventions_NoPascalCaseWarning()
  {
    SchemaToolsConfig config = CreateConfig(c => c.Validation.EnforceNamingConventions = false);
    TableMetadata table = CreateTable("PascalCase");

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
    task.ValidationWarnings.Should().NotContainMatch("*snake_case*");
  }

  [Fact]
  public void DisabledAuditColumns_IgnoresMissing()
  {
    SchemaToolsConfig config = CreateConfig(c => c.Validation.ValidateAuditColumns = false);
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

    (bool success, _) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
  }

  // --- Configurable column names through Execute() -------------------------

  [Theory]
  [InlineData("period_start", "period_end", true, "custom temporal columns accepted")]
  [InlineData("record_valid_from", "record_valid_until", false, "default temporal columns rejected when custom expected")]
  public void CustomTemporalColumns_ValidatesAgainstConfig(
    string validFrom, string validTo, bool expectSuccess, string because)
  {
    SchemaToolsConfig config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Columns = new ColumnNamingConfig { ValidFrom = "period_start", ValidTo = "period_end" }
    };

    TableMetadata table = CreateTable("temporal", t => t with
    {
      HasTemporalVersioning = true,
      HistoryTable = "[test].[temporal_history]",
      Columns =
      [
        .. t.Columns,
        new ColumnMetadata { Name = validFrom, Type = "DATETIME2", IsGeneratedAlways = true },
        new ColumnMetadata { Name = validTo, Type = "DATETIME2", IsGeneratedAlways = true }
      ]
    });

    (bool success, _) = RunValidator(CreateMetadata(table), config);
    success.Should().Be(expectSuccess, because);
  }

  [Fact]
  public void CustomAuditColumns_ValidatesCorrectNames()
  {
    SchemaToolsConfig config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Columns = new ColumnNamingConfig { CreatedBy = "author", UpdatedBy = "editor" }
    };

    var table = new TableMetadata
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

    (bool success, _) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
  }

  [Fact]
  public void CustomActiveColumn_SoftDeleteValidatedCorrectly()
  {
    SchemaToolsConfig config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Columns = new ColumnNamingConfig { Active = "is_enabled" }
    };

    TableMetadata table = CreateTable("soft_del", t => t with
    {
      HasSoftDelete = true,
      HasActiveColumn = true,
      HasTemporalVersioning = true,
      HistoryTable = "[test].[soft_del_history]",
      ActiveColumnName = "is_enabled",
      Columns =
      [
        .. t.Columns,
        new ColumnMetadata { Name = "is_enabled", Type = "BIT", DefaultValue = "1" },
        new ColumnMetadata { Name = "record_valid_from", Type = "DATETIME2", IsGeneratedAlways = true },
        new ColumnMetadata { Name = "record_valid_until", Type = "DATETIME2", IsGeneratedAlways = true }
      ]
    });

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
    task.ValidationWarnings.Should().NotContainMatch("*Should have DEFAULT 1*");
  }

  // --- Per-table overrides through Execute() -------------------------------

  [Fact]
  public void PerTableOverride_SkipsTemporalValidation()
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

    TableMetadata table = CreateTable("special_table", t => t with
    {
      HasTemporalVersioning = true
    });

    (bool success, SchemaValidator task) = RunValidator(CreateMetadata(table), config);
    success.Should().BeTrue();
    task.ValidationErrors.Should().NotContainMatch("*special_table*Temporal*");
  }

  [Fact]
  public void PerTableOverride_SkipsAuditForCategory()
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

    var table = new TableMetadata
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
