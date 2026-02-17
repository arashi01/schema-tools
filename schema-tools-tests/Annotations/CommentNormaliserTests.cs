using SchemaTools.Annotations;

namespace SchemaTools.Tests.Annotations;

/// <summary>
/// Tests for <see cref="CommentNormaliser"/>.
/// </summary>
public class CommentNormaliserTests
{
  [Fact]
  public void ExtractLeadingComments_SingleLineComments_ExtractsAll()
  {
    string sql =
      "-- @category core\n" +
      "-- @description Some table.\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    (IReadOnlyList<NormalisedCommentLine> lines, bool unterminated) =
      CommentNormaliser.ExtractLeadingComments(sql, "test.sql");

    unterminated.Should().BeFalse();
    lines.Should().HaveCount(2);
    lines[0].Body.Should().Be("@category core");
    lines[0].Style.Should().Be(CommentStyle.SingleLine);
    lines[1].Body.Should().Be("@description Some table.");
  }

  [Fact]
  public void ExtractLeadingComments_BlockComment_ExtractsLines()
  {
    string sql =
      "/* @category core\n" +
      "   @description A block-commented table. */\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    (IReadOnlyList<NormalisedCommentLine> lines, bool unterminated) =
      CommentNormaliser.ExtractLeadingComments(sql, "test.sql");

    unterminated.Should().BeFalse();
    lines.Should().HaveCount(2);
    lines[0].Body.Should().Be("@category core");
    lines[0].Style.Should().Be(CommentStyle.Block);
    lines[1].Body.Should().Be("@description A block-commented table.");
  }

  [Fact]
  public void ExtractLeadingComments_BlockCommentWithStarPrefix_StripsPrefix()
  {
    string sql =
      "/*\n" +
      " * @category audit\n" +
      " * @description Audit trail table.\n" +
      " */\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    (IReadOnlyList<NormalisedCommentLine> lines, bool unterminated) =
      CommentNormaliser.ExtractLeadingComments(sql, "test.sql");

    unterminated.Should().BeFalse();
    lines.Should().HaveCount(2);
    lines[0].Body.Should().Be("@category audit");
    lines[1].Body.Should().Be("@description Audit trail table.");
  }

  [Fact]
  public void ExtractLeadingComments_MixedStyles_ExtractsAll()
  {
    string sql =
      "-- @category core\n" +
      "/* @description Mixed style comment. */\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    (IReadOnlyList<NormalisedCommentLine> lines, bool unterminated) =
      CommentNormaliser.ExtractLeadingComments(sql, "test.sql");

    unterminated.Should().BeFalse();
    lines.Should().HaveCount(2);
    lines[0].Body.Should().Be("@category core");
    lines[0].Style.Should().Be(CommentStyle.SingleLine);
    lines[1].Body.Should().Be("@description Mixed style comment.");
    lines[1].Style.Should().Be(CommentStyle.Block);
  }

  [Fact]
  public void ExtractLeadingComments_UnterminatedBlockComment_SetsFlag()
  {
    string sql =
      "/* @description This block comment never closes\n" +
      "   and continues forever\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    (IReadOnlyList<NormalisedCommentLine> lines, bool unterminated) =
      CommentNormaliser.ExtractLeadingComments(sql, "test.sql");

    unterminated.Should().BeTrue();
    lines.Should().HaveCountGreaterThan(0);
  }

  [Fact]
  public void ExtractLeadingComments_BlankLinesBetweenComments_ContinuesReading()
  {
    string sql =
      "-- @category core\n" +
      "\n" +
      "-- @description After a blank line.\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    (IReadOnlyList<NormalisedCommentLine> lines, bool unterminated) =
      CommentNormaliser.ExtractLeadingComments(sql, "test.sql");

    unterminated.Should().BeFalse();
    lines.Should().HaveCount(2);
  }

  [Fact]
  public void ExtractLeadingComments_NoComments_ReturnsEmpty()
  {
    string sql = "CREATE TABLE [dbo].[t] (id INT);";

    (IReadOnlyList<NormalisedCommentLine> lines, bool unterminated) =
      CommentNormaliser.ExtractLeadingComments(sql, "test.sql");

    unterminated.Should().BeFalse();
    lines.Should().BeEmpty();
  }

  [Fact]
  public void ExtractLeadingComments_TracksLineNumbers()
  {
    string sql =
      "-- @category core\n" +
      "-- @description Line two.\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    (IReadOnlyList<NormalisedCommentLine> lines, _) =
      CommentNormaliser.ExtractLeadingComments(sql, "test.sql");

    lines[0].Location.Line.Should().Be(1);
    lines[1].Location.Line.Should().Be(2);
    lines[0].Location.FilePath.Should().Be("test.sql");
  }

  [Fact]
  public void ExtractLeadingComments_SingleLineBlockComment_ExtractsBody()
  {
    string sql =
      "/* @category lookup */\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    (IReadOnlyList<NormalisedCommentLine> lines, bool unterminated) =
      CommentNormaliser.ExtractLeadingComments(sql, "test.sql");

    unterminated.Should().BeFalse();
    lines.Should().HaveCount(1);
    lines[0].Body.Should().Be("@category lookup");
  }

  [Fact]
  public void ExtractTrailingComments_ColumnWithTrailingComment_ExtractsColumnAndBody()
  {
    string sql =
      "CREATE TABLE [dbo].[t]\n" +
      "(\n" +
      "    [email] VARCHAR(320) NOT NULL, -- @description User email address\n" +
      "    [name]  VARCHAR(200) NOT NULL\n" +
      ");";

    IReadOnlyList<NormalisedCommentLine> comments =
      CommentNormaliser.ExtractTrailingComments(sql, "test.sql");

    comments.Should().HaveCount(1);
    comments[0].AssociatedColumnName.Should().Be("email");
    comments[0].Body.Should().Be("@description User email address");
    comments[0].Location.Line.Should().Be(3);
  }

  [Fact]
  public void ExtractTrailingComments_MultipleColumns_ExtractsAll()
  {
    string sql =
      "CREATE TABLE [dbo].[t]\n" +
      "(\n" +
      "    [email]  VARCHAR(320) NOT NULL, -- @description User email\n" +
      "    [name]   VARCHAR(200) NOT NULL, -- @description Display name\n" +
      "    [age]    INT          NULL\n" +
      ");";

    IReadOnlyList<NormalisedCommentLine> comments =
      CommentNormaliser.ExtractTrailingComments(sql, "test.sql");

    comments.Should().HaveCount(2);
    comments[0].AssociatedColumnName.Should().Be("email");
    comments[1].AssociatedColumnName.Should().Be("name");
  }

  [Fact]
  public void ExtractTrailingComments_NoTrailingComments_ReturnsEmpty()
  {
    string sql =
      "CREATE TABLE [dbo].[t]\n" +
      "(\n" +
      "    [id] INT NOT NULL\n" +
      ");";

    IReadOnlyList<NormalisedCommentLine> comments =
      CommentNormaliser.ExtractTrailingComments(sql, "test.sql");

    comments.Should().BeEmpty();
  }

  [Fact]
  public void ExtractTrailingComments_IgnoresLeadingCommentLines()
  {
    string sql =
      "-- @description This is a leading comment, not a trailing one\n" +
      "CREATE TABLE [dbo].[t] ([id] INT NOT NULL);";

    IReadOnlyList<NormalisedCommentLine> comments =
      CommentNormaliser.ExtractTrailingComments(sql, "test.sql");

    comments.Should().BeEmpty();
  }

  [Fact]
  public void ExtractTrailingComments_BareIdentifier_ExtractsColumnName()
  {
    string sql =
      "CREATE TABLE dbo.t\n" +
      "(\n" +
      "    email VARCHAR(320) NOT NULL, -- @description User email\n" +
      ");";

    IReadOnlyList<NormalisedCommentLine> comments =
      CommentNormaliser.ExtractTrailingComments(sql, "test.sql");

    comments.Should().HaveCount(1);
    comments[0].AssociatedColumnName.Should().Be("email");
  }
}
