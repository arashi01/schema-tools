using SchemaTools.Configuration;
using SchemaTools.Models;
using SchemaTools.Utilities;

namespace SchemaTools.Tests.Utilities;

/// <summary>
/// Tests for the pure <see cref="PatternDetector"/> static class.
/// <see cref="PatternDetector.DetectTablePatterns"/> returns a new
/// <see cref="TableMetadata"/> — all assertions must target the return value.
/// <see cref="PatternDetector.MarkHistoryTables"/> takes and returns
/// <c>IReadOnlyList&lt;TableMetadata&gt;</c>.
/// </summary>
public class PatternDetectorTests
{
  private const SqlServerVersion DefaultSqlVersion = SqlServerVersion.Sql170;

  // Config is mutable -- Action<T> is fine
  private static SchemaToolsConfig CreateConfig(Action<SchemaToolsConfig>? configure = null)
  {
    var config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Features = new FeatureConfig
      {
        EnableSoftDelete = true,
        DetectAppendOnlyTables = true,
        DetectPolymorphicPatterns = true
      },
      Columns = new ColumnNamingConfig
      {
        PolymorphicPatterns =
        [
          new PolymorphicPatternConfig { TypeColumn = "owner_type", IdColumn = "owner_id" }
        ]
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
        new ColumnMetadata { Name = "id", Type = "UNIQUEIDENTIFIER", IsPrimaryKey = true }
      ],
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = new PrimaryKeyConstraint { Name = $"pk_{name}", Columns = ["id"] }
      }
    };
    return configure != null ? configure(table) : table;
  }

  // --- DetectTablePatterns -------------------------------------------------

  public class DetectTablePatternsTests : PatternDetectorTests
  {
    [Theory]
    [InlineData(true, true, true, "both active column and temporal → soft-delete")]
    [InlineData(false, true, false, "no active column → no soft-delete")]
    [InlineData(true, false, false, "no temporal → no soft-delete")]
    public void SoftDelete_RequiresBothActiveColumnAndTemporal(
      bool hasActiveColumn, bool hasTemporal, bool expectedSoftDelete, string because)
    {
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTable("users", t => t with
      {
        HasActiveColumn = hasActiveColumn,
        HasTemporalVersioning = hasTemporal
      });

      TableMetadata result = PatternDetector.DetectTablePatterns(table, config, DefaultSqlVersion);

      result.HasSoftDelete.Should().Be(expectedSoftDelete, because);
      if (hasActiveColumn)
        result.ActiveColumnName.Should().Be("record_active");
    }

    [Fact]
    public void SoftDelete_NotDetectedWhenFeatureDisabled()
    {
      SchemaToolsConfig config = CreateConfig(c => c.Features.EnableSoftDelete = false);
      TableMetadata table = CreateTable("users", t => t with
      {
        HasActiveColumn = true,
        HasTemporalVersioning = true
      });

      TableMetadata result = PatternDetector.DetectTablePatterns(table, config, DefaultSqlVersion);

      result.HasSoftDelete.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false, false, true, "created_at + no updated_by + no temporal → append-only")]
    [InlineData(true, true, false, false, "has updated_by → not append-only")]
    [InlineData(true, false, true, false, "has temporal → not append-only")]
    public void AppendOnly_RequiresCreatedAtAndNoUpdatedByAndNoTemporal(
      bool hasCreatedAt, bool hasUpdatedBy, bool hasTemporal, bool expectedAppendOnly, string because)
    {
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTable("audit_logs", t =>
      {
        var columns = new List<ColumnMetadata>(t.Columns);
        if (hasCreatedAt)
          columns.Add(new ColumnMetadata { Name = "record_created_at", Type = "DATETIMEOFFSET" });
        if (hasUpdatedBy)
          columns.Add(new ColumnMetadata { Name = "record_updated_by", Type = "UNIQUEIDENTIFIER" });
        return t with { HasTemporalVersioning = hasTemporal, Columns = columns };
      });

      TableMetadata result = PatternDetector.DetectTablePatterns(table, config, DefaultSqlVersion);

      result.IsAppendOnly.Should().Be(expectedAppendOnly, because);
    }

    [Fact]
    public void AppendOnly_NotDetectedWhenFeatureDisabled()
    {
      SchemaToolsConfig config = CreateConfig(c => c.Features.DetectAppendOnlyTables = false);
      TableMetadata table = CreateTable("audit_logs", t => t with
      {
        Columns = [.. t.Columns, new ColumnMetadata { Name = "record_created_at", Type = "DATETIMEOFFSET" }]
      });

      TableMetadata result = PatternDetector.DetectTablePatterns(table, config, DefaultSqlVersion);

      result.IsAppendOnly.Should().BeFalse();
    }

    [Fact]
    public void Polymorphic_DetectedWithMatchingColumnsAndCheckConstraint()
    {
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTable("notes", t => t with
      {
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

      TableMetadata result = PatternDetector.DetectTablePatterns(table, config, DefaultSqlVersion);

      result.IsPolymorphic.Should().BeTrue();
      result.PolymorphicOwner.Should().NotBeNull();
      result.PolymorphicOwner!.TypeColumn.Should().Be("owner_type");
      result.PolymorphicOwner.IdColumn.Should().Be("owner_id");
      result.PolymorphicOwner.AllowedTypes.Should().Contain(["user", "company"]);
    }

    [Fact]
    public void Polymorphic_ColumnsMarkedAsPolymorphicFK()
    {
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTable("notes", t => t with
      {
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
              Expression = "[owner_type] IN ('user')"
            }
          ]
        }
      });

      TableMetadata result = PatternDetector.DetectTablePatterns(table, config, DefaultSqlVersion);

      result.Columns.First(c => c.Name == "owner_type").IsPolymorphicForeignKey.Should().BeTrue();
      result.Columns.First(c => c.Name == "owner_id").IsPolymorphicForeignKey.Should().BeTrue();
    }

    [Fact]
    public void Polymorphic_SkippedForHistoryTable()
    {
      SchemaToolsConfig config = CreateConfig();
      TableMetadata table = CreateTable("notes_history", t => t with
      {
        IsHistoryTable = true,
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
              Expression = "[owner_type] IN ('user')"
            }
          ]
        }
      });

      TableMetadata result = PatternDetector.DetectTablePatterns(table, config, DefaultSqlVersion);

      result.IsPolymorphic.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, "feature disabled")]
    [InlineData(true, "only type column present")]
    public void Polymorphic_NotDetectedWhenConditionsMissing(bool hasIdColumn, string because)
    {
      SchemaToolsConfig config = CreateConfig(c =>
      {
        if (!hasIdColumn)
          return;
        c.Features.DetectPolymorphicPatterns = false;
      });

      TableMetadata table = CreateTable("notes", t =>
      {
        var cols = new List<ColumnMetadata>(t.Columns)
        {
          new() { Name = "owner_type", Type = "VARCHAR(20)" }
        };
        if (hasIdColumn)
          cols.Add(new ColumnMetadata { Name = "owner_id", Type = "UNIQUEIDENTIFIER" });
        return t with { Columns = cols };
      });

      TableMetadata result = PatternDetector.DetectTablePatterns(table, config, DefaultSqlVersion);

      result.IsPolymorphic.Should().BeFalse(because);
    }

    [Fact]
    public void Polymorphic_NotDetectedWhenNoPatternsConfigured()
    {
      SchemaToolsConfig config = CreateConfig(c => c.Columns.PolymorphicPatterns = []);
      TableMetadata table = CreateTable("notes", t => t with
      {
        Columns =
        [
          .. t.Columns,
          new ColumnMetadata { Name = "owner_type", Type = "VARCHAR(20)" },
          new ColumnMetadata { Name = "owner_id", Type = "UNIQUEIDENTIFIER" }
        ]
      });

      TableMetadata result = PatternDetector.DetectTablePatterns(table, config, DefaultSqlVersion);

      result.IsPolymorphic.Should().BeFalse();
    }

    [Fact]
    public void PerTableOverride_DisablesSoftDelete()
    {
      SchemaToolsConfig config = CreateConfig(c =>
      {
        c.Overrides = new Dictionary<string, TableOverrideConfig>
        {
          ["excluded_table"] = new TableOverrideConfig
          {
            Features = new FeatureOverrideConfig { EnableSoftDelete = false }
          }
        };
      });

      TableMetadata table = CreateTable("excluded_table", t => t with
      {
        HasActiveColumn = true,
        HasTemporalVersioning = true
      });

      TableMetadata result = PatternDetector.DetectTablePatterns(table, config, DefaultSqlVersion);

      result.HasSoftDelete.Should().BeFalse("per-table override disabled soft-delete");
    }
  }

  // --- MarkHistoryTables ---------------------------------------------------

  public class MarkHistoryTablesTests : PatternDetectorTests
  {
    [Fact]
    public void IdentifiesHistoryTables()
    {
      TableMetadata parent = CreateTable("events", t => t with
      {
        HasTemporalVersioning = true,
        HistoryTable = "[test].[events_history]"
      });
      TableMetadata history = CreateTable("events_history");

      IReadOnlyList<TableMetadata> result =
        PatternDetector.MarkHistoryTables([parent, history]);

      result.First(t => t.Name == "events").IsHistoryTable.Should().BeFalse();
      result.First(t => t.Name == "events_history").IsHistoryTable.Should().BeTrue();
    }

    [Fact]
    public void NoHistoryTables_NoChanges()
    {
      TableMetadata table = CreateTable("users");

      IReadOnlyList<TableMetadata> result = PatternDetector.MarkHistoryTables([table]);

      result.Single().IsHistoryTable.Should().BeFalse();
    }

    [Fact]
    public void CaseInsensitiveMatching()
    {
      TableMetadata parent = CreateTable("events", t => t with
      {
        HasTemporalVersioning = true,
        HistoryTable = "[test].[Events_History]"
      });
      TableMetadata history = CreateTable("events_history");

      IReadOnlyList<TableMetadata> result =
        PatternDetector.MarkHistoryTables([parent, history]);

      result.First(t => t.Name == "events_history").IsHistoryTable
        .Should().BeTrue("matching should be case-insensitive");
    }

    [Fact]
    public void MultipleTemporalTables_AllHistoriesMarked()
    {
      TableMetadata events = CreateTable("events", t => t with
      {
        HasTemporalVersioning = true,
        HistoryTable = "[test].[events_history]"
      });
      TableMetadata orders = CreateTable("orders", t => t with
      {
        HasTemporalVersioning = true,
        HistoryTable = "[test].[orders_history]"
      });
      TableMetadata eventsHistory = CreateTable("events_history");
      TableMetadata ordersHistory = CreateTable("orders_history");

      IReadOnlyList<TableMetadata> result =
        PatternDetector.MarkHistoryTables([events, orders, eventsHistory, ordersHistory]);

      result.First(t => t.Name == "events_history").IsHistoryTable.Should().BeTrue();
      result.First(t => t.Name == "orders_history").IsHistoryTable.Should().BeTrue();
      result.First(t => t.Name == "events").IsHistoryTable.Should().BeFalse();
      result.First(t => t.Name == "orders").IsHistoryTable.Should().BeFalse();
    }

    [Fact]
    public void EmptyHistoryTable_NotMatched()
    {
      TableMetadata parent = CreateTable("events", t => t with
      {
        HasTemporalVersioning = true,
        HistoryTable = ""
      });
      TableMetadata other = CreateTable("other");

      IReadOnlyList<TableMetadata> result =
        PatternDetector.MarkHistoryTables([parent, other]);

      result.First(t => t.Name == "other").IsHistoryTable.Should().BeFalse();
    }
  }
}
