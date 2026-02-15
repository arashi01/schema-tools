using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Visitors;

namespace SchemaTools.Tests.Visitors;

public class TriggerDiscoveryVisitorTests
{
  private static List<DiscoveredTrigger> ParseAndDiscoverTriggers(string sql)
  {
    var parser = new TSql170Parser(initialQuotedIdentifiers: true);
    using var reader = new StringReader(sql);
    TSqlFragment fragment = parser.Parse(reader, out _);

    var visitor = new TriggerDiscoveryVisitor();
    fragment.Accept(visitor);
    return visitor.Triggers;
  }

  // --- CREATE TRIGGER -------------------------------------------------------

  [Fact]
  public void Visit_CreateTrigger_ExtractsNameAndTable()
  {
    const string sql = @"
CREATE TRIGGER [dbo].[tr_users_soft_delete]
ON [dbo].[users]
AFTER UPDATE
AS
BEGIN
    PRINT 'Triggered';
END;
";

    List<DiscoveredTrigger> triggers = ParseAndDiscoverTriggers(sql);

    triggers.Should().ContainSingle();
    DiscoveredTrigger trigger = triggers[0];
    trigger.Name.Should().Be("tr_users_soft_delete");
    trigger.Schema.Should().Be("dbo");
    trigger.TargetTable.Should().Be("users");
    trigger.TargetSchema.Should().Be("dbo");
  }

  [Fact]
  public void Visit_CreateTrigger_WithoutSchema_ExtractsNameOnly()
  {
    const string sql = @"
CREATE TRIGGER tr_simple
ON users
AFTER INSERT
AS
BEGIN
    PRINT 'No schema';
END;
";

    List<DiscoveredTrigger> triggers = ParseAndDiscoverTriggers(sql);

    triggers.Should().ContainSingle();
    DiscoveredTrigger trigger = triggers[0];
    trigger.Name.Should().Be("tr_simple");
    trigger.Schema.Should().BeNull();
    trigger.TargetTable.Should().Be("users");
    trigger.TargetSchema.Should().BeNull();
  }

  // --- CREATE OR ALTER TRIGGER ----------------------------------------------

  [Fact]
  public void Visit_CreateOrAlterTrigger_ExtractsDetails()
  {
    const string sql = @"
CREATE OR ALTER TRIGGER [sales].[tr_orders_audit]
ON [sales].[orders]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
END;
";

    List<DiscoveredTrigger> triggers = ParseAndDiscoverTriggers(sql);

    triggers.Should().ContainSingle();
    DiscoveredTrigger trigger = triggers[0];
    trigger.Name.Should().Be("tr_orders_audit");
    trigger.Schema.Should().Be("sales");
    trigger.TargetTable.Should().Be("orders");
    trigger.TargetSchema.Should().Be("sales");
  }

  // --- ALTER TRIGGER --------------------------------------------------------

  [Fact]
  public void Visit_AlterTrigger_TracksAsExisting()
  {
    const string sql = @"
ALTER TRIGGER [dbo].[tr_existing]
ON [dbo].[products]
AFTER UPDATE
AS
BEGIN
    PRINT 'Modified';
END;
";

    List<DiscoveredTrigger> triggers = ParseAndDiscoverTriggers(sql);

    triggers.Should().ContainSingle();
    DiscoveredTrigger trigger = triggers[0];
    trigger.Name.Should().Be("tr_existing");
    trigger.TargetTable.Should().Be("products");
  }

  // --- Multiple triggers in one file ----------------------------------------

  [Fact]
  public void Visit_MultipleTriggersInFile_DiscoversAll()
  {
    const string sql = @"
CREATE TRIGGER [dbo].[tr_first]
ON [dbo].[table1]
AFTER INSERT
AS
BEGIN
    PRINT 'First';
END;
GO

CREATE TRIGGER [dbo].[tr_second]
ON [dbo].[table2]
AFTER UPDATE
AS
BEGIN
    PRINT 'Second';
END;
GO

CREATE TRIGGER [dbo].[tr_third]
ON [dbo].[table1]
AFTER DELETE
AS
BEGIN
    PRINT 'Third';
END;
";

    List<DiscoveredTrigger> triggers = ParseAndDiscoverTriggers(sql);

    triggers.Should().HaveCount(3);
    triggers.Select(t => t.Name).Should().BeEquivalentTo(["tr_first", "tr_second", "tr_third"]);
  }

  // --- No triggers in file --------------------------------------------------

  [Fact]
  public void Visit_NoTriggers_ReturnsEmptyList()
  {
    const string sql = @"
CREATE TABLE [dbo].[users]
(
    [id] UNIQUEIDENTIFIER PRIMARY KEY,
    [name] NVARCHAR(100)
);
";

    List<DiscoveredTrigger> triggers = ParseAndDiscoverTriggers(sql);

    triggers.Should().BeEmpty();
  }

  // --- INSTEAD OF trigger ---------------------------------------------------

  [Fact]
  public void Visit_InsteadOfTrigger_ExtractsDetails()
  {
    const string sql = @"
CREATE TRIGGER [dbo].[tr_view_insert]
ON [dbo].[user_view]
INSTEAD OF INSERT
AS
BEGIN
    INSERT INTO [dbo].[users] SELECT * FROM inserted;
END;
";

    List<DiscoveredTrigger> triggers = ParseAndDiscoverTriggers(sql);

    triggers.Should().ContainSingle();
    triggers[0].Name.Should().Be("tr_view_insert");
    triggers[0].TargetTable.Should().Be("user_view");
  }

  // --- Mixed schemas --------------------------------------------------------

  [Fact]
  public void Visit_DifferentSchemas_PreservesSchemaInfo()
  {
    const string sql = @"
CREATE TRIGGER [sales].[tr_sales_trigger]
ON [inventory].[products]
AFTER UPDATE
AS
BEGIN
    PRINT 'Cross-schema trigger';
END;
";

    List<DiscoveredTrigger> triggers = ParseAndDiscoverTriggers(sql);

    triggers.Should().ContainSingle();
    DiscoveredTrigger trigger = triggers[0];
    trigger.Schema.Should().Be("sales", "trigger schema should be 'sales'");
    trigger.TargetSchema.Should().Be("inventory", "target table schema should be 'inventory'");
  }

  // --- Trigger with complex body --------------------------------------------

  [Fact]
  public void Visit_TriggerWithComplexBody_StillExtractsMetadata()
  {
    const string sql = @"
CREATE TRIGGER [dbo].[tr_cascade_deactivate]
ON [dbo].[orders]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF UPDATE(active)
    BEGIN
        DECLARE @deactivated_ids TABLE (id UNIQUEIDENTIFIER);

        INSERT INTO @deactivated_ids
        SELECT i.id FROM inserted i
        INNER JOIN deleted d ON i.id = d.id
        WHERE i.active = 0 AND d.active = 1;

        UPDATE [dbo].[order_items]
        SET active = 0, updated_by = (SELECT TOP 1 updated_by FROM inserted)
        WHERE order_id IN (SELECT id FROM @deactivated_ids);
    END
END;
";

    List<DiscoveredTrigger> triggers = ParseAndDiscoverTriggers(sql);

    triggers.Should().ContainSingle();
    triggers[0].Name.Should().Be("tr_cascade_deactivate");
    triggers[0].TargetTable.Should().Be("orders");
  }
}
