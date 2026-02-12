using SchemaTools.Utilities;

namespace SchemaTools.Tests.Utilities;

public class SqlCommentParserTests
{
  [Fact]
  public void ExtractDescription_FromValidComment_ReturnsDescription()
  {
    const string sql = """
            -- @description User accounts table
            CREATE TABLE [dbo].[users] (id INT);
            """;

    SqlCommentParser.ExtractDescription(sql).Should().Be("User accounts table");
  }

  [Fact]
  public void ExtractDescription_CaseInsensitive_ReturnsDescription()
  {
    const string sql = """
            -- @Description Core lookup values
            CREATE TABLE [dbo].[lookups] (id INT);
            """;

    SqlCommentParser.ExtractDescription(sql).Should().Be("Core lookup values");
  }

  [Fact]
  public void ExtractDescription_WithoutAnnotation_ReturnsNull()
  {
    const string sql = """
            -- This table stores users
            CREATE TABLE [dbo].[users] (id INT);
            """;

    SqlCommentParser.ExtractDescription(sql).Should().BeNull();
  }

  [Fact]
  public void ExtractCategory_FromValidComment_ReturnsCategory()
  {
    const string sql = """
            -- @category core
            -- @description Core table
            CREATE TABLE [dbo].[users] (id INT);
            """;

    SqlCommentParser.ExtractCategory(sql).Should().Be("core");
  }

  [Fact]
  public void ExtractCategory_WithoutAnnotation_ReturnsNull()
  {
    const string sql = "CREATE TABLE [dbo].[users] (id INT);";

    SqlCommentParser.ExtractCategory(sql).Should().BeNull();
  }

  [Fact]
  public void ExtractDescription_TrimsWhitespace()
  {
    const string sql = "-- @description   Padded description   \nCREATE TABLE x (id INT);";

    SqlCommentParser.ExtractDescription(sql).Should().Be("Padded description");
  }

  [Fact]
  public void ExtractCategory_WithMultipleAnnotations_ReturnsFirst()
  {
    const string sql = """
            -- @category first
            -- @category second
            CREATE TABLE x (id INT);
            """;

    SqlCommentParser.ExtractCategory(sql).Should().Be("first");
  }
}
