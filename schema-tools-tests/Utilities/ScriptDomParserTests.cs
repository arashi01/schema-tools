using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Models;
using SchemaTools.Utilities;

namespace SchemaTools.Tests.Utilities;

public class ScriptDomParserTests
{
  private const SqlServerVersion DefaultVersion = SqlServerVersion.Sql170;

  // ------------------------------------------------------------------
  //  CHECK constraints
  // ------------------------------------------------------------------

  public class ExtractCheckExpressionTests
  {
    [Fact]
    public void AlterTable_ReturnsExpression()
    {
      string script = @"
        ALTER TABLE [dbo].[order_items]
        ADD CONSTRAINT [ck_order_items_quantity]
        CHECK ([quantity] > 0);";

      string? result = ScriptDomParser.ExtractCheckExpression(script, DefaultVersion);

      result.Should().Be("[quantity] > 0");
    }

    [Fact]
    public void InListExpression_ReturnsFullExpression()
    {
      string script = @"
        ALTER TABLE [dbo].[comments]
        ADD CONSTRAINT [ck_comments_owner_type]
        CHECK ([owner_type] IN ('user', 'organisation', 'order'));";

      string? result = ScriptDomParser.ExtractCheckExpression(script, DefaultVersion);

      result.Should().Contain("IN");
      result.Should().Contain("'user'");
      result.Should().Contain("'organisation'");
      result.Should().Contain("'order'");
    }

    [Fact]
    public void NullScript_ReturnsNull()
    {
      ScriptDomParser.ExtractCheckExpression(null, DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void EmptyScript_ReturnsNull()
    {
      ScriptDomParser.ExtractCheckExpression("", DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void NoCheckConstraint_ReturnsNull()
    {
      string script = "ALTER TABLE [dbo].[t] ADD COLUMN [x] INT;";

      ScriptDomParser.ExtractCheckExpression(script, DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void LenExpression_ReturnsExpression()
    {
      string script = @"
        ALTER TABLE [dbo].[member_permissions]
        ADD CONSTRAINT [ck_member_permissions_permission]
        CHECK (LEN([permission]) > 0);";

      string? result = ScriptDomParser.ExtractCheckExpression(script, DefaultVersion);

      result.Should().Contain("LEN");
      result.Should().Contain("[permission]");
      result.Should().Contain("> 0");
    }
  }

  public class ExtractCheckExpressionByNameTests
  {
    private const string TableScript = @"
      CREATE TABLE [dbo].[orders] (
        [id] UNIQUEIDENTIFIER NOT NULL,
        [status] VARCHAR(20) NOT NULL,
        [quantity] INT NOT NULL,
        CONSTRAINT [ck_orders_status] CHECK ([status] IN ('pending', 'shipped', 'delivered')),
        CONSTRAINT [ck_orders_quantity] CHECK ([quantity] > 0)
      );";

    [Fact]
    public void FindsByName_ReturnsCorrectExpression()
    {
      string? result = ScriptDomParser.ExtractCheckExpressionByName(
        TableScript, "ck_orders_status", DefaultVersion);

      result.Should().Contain("IN");
      result.Should().Contain("'pending'");
    }

    [Fact]
    public void FindsSecondConstraint_ReturnsCorrectExpression()
    {
      string? result = ScriptDomParser.ExtractCheckExpressionByName(
        TableScript, "ck_orders_quantity", DefaultVersion);

      result.Should().Contain("[quantity]");
      result.Should().Contain("> 0");
    }

    [Fact]
    public void NonExistentName_ReturnsNull()
    {
      ScriptDomParser.ExtractCheckExpressionByName(
        TableScript, "ck_nonexistent", DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void NullScript_ReturnsNull()
    {
      ScriptDomParser.ExtractCheckExpressionByName(
        null, "ck_x", DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void NullName_ReturnsNull()
    {
      ScriptDomParser.ExtractCheckExpressionByName(
        TableScript, null!, DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void ColumnLevelCheck_IsFound()
    {
      string script = @"
        CREATE TABLE [dbo].[t] (
          [age] INT NOT NULL CONSTRAINT [ck_age] CHECK ([age] >= 0)
        );";

      string? result = ScriptDomParser.ExtractCheckExpressionByName(
        script, "ck_age", DefaultVersion);

      result.Should().Contain("[age]");
      result.Should().Contain(">= 0");
    }
  }

  // ------------------------------------------------------------------
  //  DEFAULT constraints
  // ------------------------------------------------------------------

  public class ExtractDefaultExpressionTests
  {
    [Fact]
    public void NamedDefault_ReturnsExpression()
    {
      string script = @"
        ALTER TABLE [dbo].[t]
        ADD CONSTRAINT [df_t_active] DEFAULT ((1)) FOR [active];";

      string? result = ScriptDomParser.ExtractDefaultExpression(script, DefaultVersion);

      result.Should().Be("((1))");
    }

    [Fact]
    public void UnnamedDefault_ReturnsExpression()
    {
      string script = @"
        ALTER TABLE [dbo].[t]
        ADD DEFAULT ((0)) FOR [col];";

      string? result = ScriptDomParser.ExtractDefaultExpression(script, DefaultVersion);

      result.Should().Be("((0))");
    }

    [Fact]
    public void NewIdDefault_ReturnsExpression()
    {
      string script = @"
        ALTER TABLE [dbo].[t]
        ADD CONSTRAINT [df_t_id] DEFAULT (NEWID()) FOR [id];";

      string? result = ScriptDomParser.ExtractDefaultExpression(script, DefaultVersion);

      result.Should().Be("(NEWID())");
    }

    [Fact]
    public void GetDateDefault_ReturnsExpression()
    {
      string script = @"
        ALTER TABLE [dbo].[t]
        ADD CONSTRAINT [df_t_created] DEFAULT (GETUTCDATE()) FOR [created_at];";

      string? result = ScriptDomParser.ExtractDefaultExpression(script, DefaultVersion);

      result.Should().Contain("GETUTCDATE()");
    }

    [Fact]
    public void NullScript_ReturnsNull()
    {
      ScriptDomParser.ExtractDefaultExpression(null, DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void EmptyScript_ReturnsNull()
    {
      ScriptDomParser.ExtractDefaultExpression("", DefaultVersion).Should().BeNull();
    }
  }

  // ------------------------------------------------------------------
  //  Computed columns
  // ------------------------------------------------------------------

  public class ExtractComputedColumnTests
  {
    [Fact]
    public void SimpleMultiply_ReturnsExpression()
    {
      string script = @"
        CREATE TABLE [dbo].[t] (
          [a] INT NOT NULL,
          [b] INT NOT NULL,
          [c] AS ([a] * [b])
        );";

      ScriptDomParser.ComputedColumnInfo? info =
        ScriptDomParser.ExtractComputedColumn(script, "c", DefaultVersion);

      info.Should().NotBeNull();
      info!.Expression.Should().Contain("[a] * [b]");
      info.IsPersisted.Should().BeFalse();
    }

    [Fact]
    public void PersistedColumn_SetsFlag()
    {
      string script = @"
        CREATE TABLE [dbo].[t] (
          [sort_order] INT NOT NULL,
          [category_id] UNIQUEIDENTIFIER NOT NULL,
          [sort_key] AS (CONCAT(RIGHT('0000' + CAST([sort_order] AS VARCHAR(4)), 4), '-', CAST([category_id] AS VARCHAR(36)))) PERSISTED
        );";

      ScriptDomParser.ComputedColumnInfo? info =
        ScriptDomParser.ExtractComputedColumn(script, "sort_key", DefaultVersion);

      info.Should().NotBeNull();
      info!.IsPersisted.Should().BeTrue();
      info.Expression.Should().Contain("CONCAT");
    }

    [Fact]
    public void NonComputedColumn_ReturnsNull()
    {
      string script = @"
        CREATE TABLE [dbo].[t] (
          [a] INT NOT NULL,
          [b] INT NOT NULL
        );";

      ScriptDomParser.ExtractComputedColumn(script, "a", DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void NonExistentColumn_ReturnsNull()
    {
      string script = @"
        CREATE TABLE [dbo].[t] (
          [a] INT NOT NULL
        );";

      ScriptDomParser.ExtractComputedColumn(script, "nonexistent", DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void NullInputs_ReturnsNull()
    {
      ScriptDomParser.ExtractComputedColumn(null, "c", DefaultVersion).Should().BeNull();
      ScriptDomParser.ExtractComputedColumn("", "c", DefaultVersion).Should().BeNull();
      ScriptDomParser.ExtractComputedColumn("CREATE TABLE t (x INT)", null!, DefaultVersion).Should().BeNull();
    }
  }

  public class ExtractAllComputedColumnsTests
  {
    [Fact]
    public void MultipleComputedColumns_ReturnsAll()
    {
      string script = @"
        CREATE TABLE [dbo].[product_categories] (
          [product_id] UNIQUEIDENTIFIER NOT NULL,
          [sort_order] INT NOT NULL,
          [weight] DECIMAL(8,2) NOT NULL,
          [weighted_sort] AS ([sort_order] * [weight]),
          [sort_key] AS (CONCAT(RIGHT('0000' + CAST([sort_order] AS VARCHAR(4)), 4), '-', CAST([product_id] AS VARCHAR(36)))) PERSISTED
        );";

      Dictionary<string, ScriptDomParser.ComputedColumnInfo> result =
        ScriptDomParser.ExtractAllComputedColumns(script, DefaultVersion);

      result.Should().HaveCount(2);
      result.Should().ContainKey("weighted_sort");
      result.Should().ContainKey("sort_key");

      result["weighted_sort"].IsPersisted.Should().BeFalse();
      result["sort_key"].IsPersisted.Should().BeTrue();
    }

    [Fact]
    public void NoComputedColumns_ReturnsEmptyDictionary()
    {
      string script = @"
        CREATE TABLE [dbo].[t] (
          [id] INT NOT NULL,
          [name] VARCHAR(100) NOT NULL
        );";

      Dictionary<string, ScriptDomParser.ComputedColumnInfo> result =
        ScriptDomParser.ExtractAllComputedColumns(script, DefaultVersion);

      result.Should().BeEmpty();
    }

    [Fact]
    public void NullScript_ReturnsEmptyDictionary()
    {
      ScriptDomParser.ExtractAllComputedColumns(null, DefaultVersion).Should().BeEmpty();
    }

    [Fact]
    public void CaseInsensitiveLookup()
    {
      string script = @"
        CREATE TABLE [dbo].[t] (
          [Col] AS (1 + 2)
        );";

      Dictionary<string, ScriptDomParser.ComputedColumnInfo> result =
        ScriptDomParser.ExtractAllComputedColumns(script, DefaultVersion);

      result.Should().ContainKey("Col");
      result.Should().ContainKey("col");
      result.Should().ContainKey("COL");
    }
  }

  // ------------------------------------------------------------------
  //  FK referential actions
  // ------------------------------------------------------------------

  public class ExtractFKActionsTests
  {
    [Fact]
    public void CascadeDelete_Detected()
    {
      string script = @"
        ALTER TABLE [dbo].[order_items]
        ADD CONSTRAINT [fk_order_items_order]
        FOREIGN KEY ([order_id])
        REFERENCES [dbo].[orders] ([id])
        ON DELETE CASCADE;";

      ScriptDomParser.ForeignKeyActionInfo info =
        ScriptDomParser.ExtractFKActions(script, DefaultVersion);

      info.OnDelete.Should().Be(ForeignKeyAction.Cascade);
      info.OnUpdate.Should().Be(ForeignKeyAction.NoAction);
    }

    [Fact]
    public void CascadeDeleteAndUpdate_BothDetected()
    {
      string script = @"
        ALTER TABLE [dbo].[t]
        ADD CONSTRAINT [fk_t]
        FOREIGN KEY ([col])
        REFERENCES [dbo].[ref] ([id])
        ON DELETE CASCADE
        ON UPDATE CASCADE;";

      ScriptDomParser.ForeignKeyActionInfo info =
        ScriptDomParser.ExtractFKActions(script, DefaultVersion);

      info.OnDelete.Should().Be(ForeignKeyAction.Cascade);
      info.OnUpdate.Should().Be(ForeignKeyAction.Cascade);
    }

    [Fact]
    public void SetNull_Detected()
    {
      string script = @"
        ALTER TABLE [dbo].[t]
        ADD CONSTRAINT [fk_t]
        FOREIGN KEY ([col])
        REFERENCES [dbo].[ref] ([id])
        ON DELETE SET NULL;";

      ScriptDomParser.ForeignKeyActionInfo info =
        ScriptDomParser.ExtractFKActions(script, DefaultVersion);

      info.OnDelete.Should().Be(ForeignKeyAction.SetNull);
    }

    [Fact]
    public void SetDefault_Detected()
    {
      string script = @"
        ALTER TABLE [dbo].[t]
        ADD CONSTRAINT [fk_t]
        FOREIGN KEY ([col])
        REFERENCES [dbo].[ref] ([id])
        ON DELETE SET DEFAULT;";

      ScriptDomParser.ForeignKeyActionInfo info =
        ScriptDomParser.ExtractFKActions(script, DefaultVersion);

      info.OnDelete.Should().Be(ForeignKeyAction.SetDefault);
    }

    [Fact]
    public void NoAction_IsDefault()
    {
      string script = @"
        ALTER TABLE [dbo].[t]
        ADD CONSTRAINT [fk_t]
        FOREIGN KEY ([col])
        REFERENCES [dbo].[ref] ([id]);";

      ScriptDomParser.ForeignKeyActionInfo info =
        ScriptDomParser.ExtractFKActions(script, DefaultVersion);

      info.OnDelete.Should().Be(ForeignKeyAction.NoAction);
      info.OnUpdate.Should().Be(ForeignKeyAction.NoAction);
    }

    [Fact]
    public void NullScript_ReturnsDefaults()
    {
      ScriptDomParser.ForeignKeyActionInfo info =
        ScriptDomParser.ExtractFKActions(null, DefaultVersion);

      info.OnDelete.Should().Be(ForeignKeyAction.NoAction);
      info.OnUpdate.Should().Be(ForeignKeyAction.NoAction);
    }
  }

  public class ExtractFKActionsByNameTests
  {
    private const string TableScript = @"
      CREATE TABLE [dbo].[order_items] (
        [id] UNIQUEIDENTIFIER NOT NULL,
        [order_id] UNIQUEIDENTIFIER NOT NULL,
        [product_id] UNIQUEIDENTIFIER NOT NULL,
        CONSTRAINT [fk_order_items_order] FOREIGN KEY ([order_id])
          REFERENCES [dbo].[orders] ([id]) ON DELETE CASCADE,
        CONSTRAINT [fk_order_items_product] FOREIGN KEY ([product_id])
          REFERENCES [dbo].[products] ([id])
      );";

    [Fact]
    public void FindsCascadeByName()
    {
      ScriptDomParser.ForeignKeyActionInfo info =
        ScriptDomParser.ExtractFKActionsByName(TableScript, "fk_order_items_order", DefaultVersion);

      info.OnDelete.Should().Be(ForeignKeyAction.Cascade);
    }

    [Fact]
    public void FindsNoActionByName()
    {
      ScriptDomParser.ForeignKeyActionInfo info =
        ScriptDomParser.ExtractFKActionsByName(TableScript, "fk_order_items_product", DefaultVersion);

      info.OnDelete.Should().Be(ForeignKeyAction.NoAction);
    }

    [Fact]
    public void NonExistentName_ReturnsDefaults()
    {
      ScriptDomParser.ForeignKeyActionInfo info =
        ScriptDomParser.ExtractFKActionsByName(TableScript, "fk_nonexistent", DefaultVersion);

      info.OnDelete.Should().Be(ForeignKeyAction.NoAction);
      info.OnUpdate.Should().Be(ForeignKeyAction.NoAction);
    }
  }

  // ------------------------------------------------------------------
  //  Index filter predicates
  // ------------------------------------------------------------------

  public class ExtractFilterClauseTests
  {
    [Fact]
    public void SimpleFilter_ReturnsWhereClause()
    {
      string script = @"
        CREATE UNIQUE NONCLUSTERED INDEX [ix_orders_active]
        ON [dbo].[orders] ([user_id], [order_date])
        WHERE ([active] = 1);";

      string? result = ScriptDomParser.ExtractFilterClause(script, DefaultVersion);

      result.Should().StartWith("WHERE");
      result.Should().Contain("[active]");
      result.Should().Contain("= 1");
    }

    [Fact]
    public void NoFilter_ReturnsNull()
    {
      string script = @"
        CREATE NONCLUSTERED INDEX [ix_orders_date]
        ON [dbo].[orders] ([order_date]);";

      ScriptDomParser.ExtractFilterClause(script, DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void NullScript_ReturnsNull()
    {
      ScriptDomParser.ExtractFilterClause(null, DefaultVersion).Should().BeNull();
    }

    [Fact]
    public void CompoundFilter_ReturnsWhereClause()
    {
      string script = @"
        CREATE NONCLUSTERED INDEX [ix_filtered]
        ON [dbo].[t] ([a])
        WHERE ([active] = 1 AND [status] = 'open');";

      string? result = ScriptDomParser.ExtractFilterClause(script, DefaultVersion);

      result.Should().StartWith("WHERE");
      result.Should().Contain("[active]");
      result.Should().Contain("[status]");
    }
  }

  // ------------------------------------------------------------------
  //  Polymorphic types
  // ------------------------------------------------------------------

  public class ExtractAllowedTypesFromExpressionTests
  {
    [Fact]
    public void InList_ExtractsAllValues()
    {
      string expression = "([owner_type] IN ('user', 'organisation', 'order'))";

      List<string> result = ScriptDomParser.ExtractAllowedTypesFromExpression(
        expression, DefaultVersion);

      result.Should().BeEquivalentTo(new[] { "user", "organisation", "order" });
    }

    [Fact]
    public void SingleValue_ExtractsSingleItem()
    {
      string expression = "([type] IN ('admin'))";

      List<string> result = ScriptDomParser.ExtractAllowedTypesFromExpression(
        expression, DefaultVersion);

      result.Should().ContainSingle().Which.Should().Be("admin");
    }

    [Fact]
    public void NullExpression_ReturnsEmptyList()
    {
      ScriptDomParser.ExtractAllowedTypesFromExpression(null, DefaultVersion)
        .Should().BeEmpty();
    }

    [Fact]
    public void EmptyExpression_ReturnsEmptyList()
    {
      ScriptDomParser.ExtractAllowedTypesFromExpression("", DefaultVersion)
        .Should().BeEmpty();
    }

    [Fact]
    public void NonInExpression_ReturnsEmptyList()
    {
      string expression = "([quantity] > 0)";

      ScriptDomParser.ExtractAllowedTypesFromExpression(expression, DefaultVersion)
        .Should().BeEmpty();
    }
  }

  // ------------------------------------------------------------------
  //  Internal helpers
  // ------------------------------------------------------------------

  public class ConvertDeleteUpdateActionTests
  {
    [Theory]
    [InlineData(DeleteUpdateAction.Cascade, ForeignKeyAction.Cascade)]
    [InlineData(DeleteUpdateAction.SetNull, ForeignKeyAction.SetNull)]
    [InlineData(DeleteUpdateAction.SetDefault, ForeignKeyAction.SetDefault)]
    [InlineData(DeleteUpdateAction.NoAction, ForeignKeyAction.NoAction)]
    [InlineData(DeleteUpdateAction.NotSpecified, ForeignKeyAction.NoAction)]
    public void ConvertsCorrectly(DeleteUpdateAction action, ForeignKeyAction expected)
    {
      ScriptDomParser.ConvertDeleteUpdateAction(action).Should().Be(expected);
    }
  }

  public class ParseStatementsTests
  {
    [Fact]
    public void ValidSql_ReturnsStatements()
    {
      string sql = "SELECT 1; SELECT 2;";

      List<TSqlStatement> stmts = ScriptDomParser.ParseStatements(sql, DefaultVersion).ToList();

      stmts.Should().HaveCount(2);
    }

    [Fact]
    public void InvalidSql_ReturnsWhatItCan()
    {
      // ScriptDom is resilient -- partial parse still returns some statements
      string sql = "THIS IS NOT SQL";

      // Should not throw
      List<TSqlStatement> stmts = ScriptDomParser.ParseStatements(sql, DefaultVersion).ToList();

      // Result depends on parser resilience -- just verify no exception
      stmts.Should().NotBeNull();
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
      List<TSqlStatement> stmts = ScriptDomParser.ParseStatements("", DefaultVersion).ToList();

      stmts.Should().BeEmpty();
    }
  }
}
