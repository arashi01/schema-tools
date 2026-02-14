using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaTools.Visitors;

namespace SchemaTools.Tests.Visitors;

public class ViewDiscoveryVisitorTests
{
  private static List<DiscoveredView> ParseAndDiscoverViews(string sql)
  {
    var parser = new TSql170Parser(initialQuotedIdentifiers: true);
    using var reader = new StringReader(sql);
    TSqlFragment fragment = parser.Parse(reader, out _);

    var visitor = new ViewDiscoveryVisitor();
    fragment.Accept(visitor);
    return visitor.Views;
  }

  // ===========================================================================
  // CREATE VIEW
  // ===========================================================================

  [Fact]
  public void Visit_CreateView_SimpleView_DiscoversView()
  {
    const string sql = @"
CREATE VIEW [dbo].[vw_users]
AS
    SELECT * FROM [dbo].[users] WHERE active = 1;
GO";

    List<DiscoveredView> views = ParseAndDiscoverViews(sql);

    views.Should().ContainSingle();
    DiscoveredView view = views[0];
    view.Name.Should().Be("vw_users");
    view.Schema.Should().Be("dbo");
  }

  [Fact]
  public void Visit_CreateView_NoSchema_DiscoversViewWithNullSchema()
  {
    const string sql = @"
CREATE VIEW vw_products
AS
    SELECT * FROM products WHERE active = 1;
GO";

    List<DiscoveredView> views = ParseAndDiscoverViews(sql);

    views.Should().ContainSingle();
    DiscoveredView view = views[0];
    view.Name.Should().Be("vw_products");
    view.Schema.Should().BeNull();
  }

  [Fact]
  public void Visit_CreateView_CustomSchema_DiscoversSchema()
  {
    const string sql = @"
CREATE VIEW [inventory].[vw_stock]
AS
    SELECT * FROM [inventory].[products];
GO";

    List<DiscoveredView> views = ParseAndDiscoverViews(sql);

    views.Should().ContainSingle();
    DiscoveredView view = views[0];
    view.Name.Should().Be("vw_stock");
    view.Schema.Should().Be("inventory");
  }

  // ===========================================================================
  // CREATE OR ALTER VIEW
  // ===========================================================================

  [Fact]
  public void Visit_CreateOrAlterView_DiscoversView()
  {
    const string sql = @"
CREATE OR ALTER VIEW [dbo].[vw_orders]
AS
    SELECT * FROM [dbo].[orders] WHERE active = 1;
GO";

    List<DiscoveredView> views = ParseAndDiscoverViews(sql);

    views.Should().ContainSingle();
    DiscoveredView view = views[0];
    view.Name.Should().Be("vw_orders");
    view.Schema.Should().Be("dbo");
  }

  // ===========================================================================
  // ALTER VIEW
  // ===========================================================================

  [Fact]
  public void Visit_AlterView_DiscoversView()
  {
    const string sql = @"
ALTER VIEW [dbo].[vw_customers]
AS
    SELECT id, name FROM [dbo].[customers] WHERE active = 1;
GO";

    List<DiscoveredView> views = ParseAndDiscoverViews(sql);

    views.Should().ContainSingle();
    DiscoveredView view = views[0];
    view.Name.Should().Be("vw_customers");
    view.Schema.Should().Be("dbo");
  }

  // ===========================================================================
  // Multiple views
  // ===========================================================================

  [Fact]
  public void Visit_MultipleViews_DiscoversAll()
  {
    const string sql = @"
CREATE VIEW [dbo].[vw_users]
AS
    SELECT * FROM [dbo].[users];
GO

CREATE VIEW [dbo].[vw_orders]
AS
    SELECT * FROM [dbo].[orders];
GO

CREATE VIEW [sales].[vw_products]
AS
    SELECT * FROM [sales].[products];
GO";

    List<DiscoveredView> views = ParseAndDiscoverViews(sql);

    views.Should().HaveCount(3);
    views.Select(v => v.Name).Should().BeEquivalentTo(["vw_users", "vw_orders", "vw_products"]);
    views.Single(v => v.Name == "vw_products").Schema.Should().Be("sales");
  }

  // ===========================================================================
  // Complex views
  // ===========================================================================

  [Fact]
  public void Visit_ViewWithJoin_DiscoversView()
  {
    const string sql = @"
CREATE VIEW [dbo].[vw_order_details]
AS
    SELECT o.id, o.order_date, c.name AS customer_name
    FROM [dbo].[orders] o
    INNER JOIN [dbo].[customers] c ON o.customer_id = c.id
    WHERE o.active = 1;
GO";

    List<DiscoveredView> views = ParseAndDiscoverViews(sql);

    views.Should().ContainSingle();
    DiscoveredView view = views[0];
    view.Name.Should().Be("vw_order_details");
    view.Schema.Should().Be("dbo");
  }

  [Fact]
  public void Visit_ViewWithCte_DiscoversView()
  {
    const string sql = @"
CREATE VIEW [dbo].[vw_top_customers]
AS
WITH ranked AS (
    SELECT customer_id, COUNT(*) as order_count,
           ROW_NUMBER() OVER (ORDER BY COUNT(*) DESC) as rn
    FROM [dbo].[orders]
    GROUP BY customer_id
)
SELECT c.id, c.name, r.order_count
FROM ranked r
INNER JOIN [dbo].[customers] c ON r.customer_id = c.id
WHERE r.rn <= 10;
GO";

    List<DiscoveredView> views = ParseAndDiscoverViews(sql);

    views.Should().ContainSingle();
    DiscoveredView view = views[0];
    view.Name.Should().Be("vw_top_customers");
  }

  // ===========================================================================
  // No views
  // ===========================================================================

  [Fact]
  public void Visit_NoViews_ReturnsEmpty()
  {
    const string sql = @"
CREATE TABLE [dbo].[users]
(
    [id] UNIQUEIDENTIFIER PRIMARY KEY,
    [name] NVARCHAR(100)
);
GO";

    List<DiscoveredView> views = ParseAndDiscoverViews(sql);

    views.Should().BeEmpty();
  }

  [Fact]
  public void Visit_OnlyTriggers_ReturnsEmpty()
  {
    const string sql = @"
CREATE TRIGGER [dbo].[trg_users_soft_delete]
ON [dbo].[users]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
END;
GO";

    List<DiscoveredView> views = ParseAndDiscoverViews(sql);

    views.Should().BeEmpty();
  }
}
