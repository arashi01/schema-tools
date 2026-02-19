using SchemaTools.Annotations;
using SchemaTools.Diagnostics;

namespace SchemaTools.Tests.Annotations;

/// <summary>
/// Tests for <see cref="AnnotationParser"/>.
/// </summary>
public class AnnotationParserTests
{
  [Fact]
  public void Parse_SingleLineCategory_ExtractsCategory()
  {
    string sql =
      "-- @category core\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Category.Should().Be("core");
    result.Value.Description.Should().BeNull();
  }

  [Fact]
  public void Parse_SingleLineDescription_ExtractsDescription()
  {
    string sql =
      "-- @description Core user accounts.\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Description.Should().Be("Core user accounts.");
    result.Value.Category.Should().BeNull();
  }

  [Fact]
  public void Parse_BothAnnotations_ExtractsBoth()
  {
    string sql =
      "-- @category core\n" +
      "-- @description Core user accounts.\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Category.Should().Be("core");
    result.Value.Description.Should().Be("Core user accounts.");
  }

  [Fact]
  public void Parse_BlockComment_ExtractsBoth()
  {
    string sql =
      "/* @category audit\n" +
      "   @description Audit trail table. */\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Category.Should().Be("audit");
    result.Value.Description.Should().Be("Audit trail table.");
  }

  [Fact]
  public void Parse_MultiLineDescription_JoinsContinuationLines()
  {
    string sql =
      "-- @description This is a long description\n" +
      "-- that spans multiple lines\n" +
      "-- and keeps going.\n" +
      "-- @category core\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Description.Should().Be(
      "This is a long description that spans multiple lines and keeps going.");
    result.Value.Category.Should().Be("core");
  }

  [Fact]
  public void Parse_CategoryDoesNotGetContinuation_OnlyDescriptionContinues()
  {
    string sql =
      "-- @category core\n" +
      "-- extra text that is not part of category\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    // The continuation should not append to category since only description supports it.
    result.IsSuccess.Should().BeTrue();
    result.Value.Category.Should().Be("core");
  }

  [Fact]
  public void Parse_CaseInsensitive_AcceptsMixedCase()
  {
    string sql =
      "-- @Category CORE\n" +
      "-- @Description Core user accounts.\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Category.Should().Be("CORE");
    result.Value.Description.Should().Be("Core user accounts.");
  }

  [Fact]
  public void Parse_UnknownAnnotation_ProducesWarning()
  {
    string sql =
      "-- @unknown something\n" +
      "-- @category core\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.HasWarnings.Should().BeTrue();
    result.Diagnostics.Should().ContainSingle(d => d.Code == "ST1001");
    result.Value.Category.Should().Be("core");
  }

  [Fact]
  public void Parse_DuplicateDescription_ProducesWarningAndUsesLast()
  {
    string sql =
      "-- @description First description.\n" +
      "-- @description Second description.\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.HasWarnings.Should().BeTrue();
    result.Diagnostics.Should().ContainSingle(d => d.Code == "ST1004");
    result.Value.Description.Should().Be("Second description.");
  }

  [Fact]
  public void Parse_DuplicateCategory_ProducesWarningAndUsesLast()
  {
    string sql =
      "-- @category first\n" +
      "-- @category second\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.HasWarnings.Should().BeTrue();
    result.Diagnostics.Should().ContainSingle(d => d.Code == "ST1004");
    result.Value.Category.Should().Be("second");
  }

  [Fact]
  public void Parse_UnterminatedBlockComment_FailsWithError()
  {
    string sql =
      "/* @description This block never closes\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.HasErrors.Should().BeTrue();
    result.Diagnostics.Should().ContainSingle(d => d.Code == "ST1003");
  }

  [Fact]
  public void Parse_NoAnnotations_ReturnsSuccessWithNulls()
  {
    string sql = "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Description.Should().BeNull();
    result.Value.Category.Should().BeNull();
    result.Value.ColumnAnnotations.Should().BeEmpty();
  }

  [Fact]
  public void Parse_ColumnTrailingDescription_ExtractsColumnAnnotation()
  {
    string sql =
      "-- @category core\n" +
      "CREATE TABLE [dbo].[t]\n" +
      "(\n" +
      "    [email] VARCHAR(320) NOT NULL, -- @description User email address\n" +
      "    [name]  VARCHAR(200) NOT NULL\n" +
      ");";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.ColumnAnnotations.Should().HaveCount(1);
    result.Value.ColumnAnnotations[0].ColumnName.Should().Be("email");
    result.Value.ColumnAnnotations[0].Description.Should().Be("User email address");
  }

  [Fact]
  public void Parse_MultipleColumnTrailingDescriptions_ExtractsAll()
  {
    string sql =
      "CREATE TABLE [dbo].[t]\n" +
      "(\n" +
      "    [email] VARCHAR(320) NOT NULL, -- @description User email\n" +
      "    [name]  VARCHAR(200) NOT NULL, -- @description Display name\n" +
      "    [age]   INT          NULL\n" +
      ");";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.ColumnAnnotations.Should().HaveCount(2);
    result.Value.ColumnAnnotations[0].ColumnName.Should().Be("email");
    result.Value.ColumnAnnotations[1].ColumnName.Should().Be("name");
  }

  [Fact]
  public void Parse_UnknownColumnAnnotation_ProducesWarning()
  {
    string sql =
      "CREATE TABLE [dbo].[t]\n" +
      "(\n" +
      "    [email] VARCHAR(320) NOT NULL, -- @unknown something\n" +
      ");";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.HasWarnings.Should().BeTrue();
    result.Diagnostics.Should().ContainSingle(d => d.Code == "ST1001");
    result.Value.ColumnAnnotations.Should().BeEmpty();
  }

  [Fact]
  public void Parse_BlockCommentWithStarPrefix_ParsesCorrectly()
  {
    string sql =
      "/*\n" +
      " * @category audit\n" +
      " * @description Audit trail\n" +
      " *              for all changes.\n" +
      " */\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Category.Should().Be("audit");
    result.Value.Description.Should().StartWith("Audit trail");
  }

  [Fact]
  public void Parse_MixedCommentStyles_ExtractsBoth()
  {
    string sql =
      "-- @category core\n" +
      "/* @description Mixed style table. */\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Category.Should().Be("core");
    result.Value.Description.Should().Be("Mixed style table.");
  }

  [Fact]
  public void Parse_BlankCommentLineDoesNotBreakContinuation()
  {
    // Blank -- lines are not emitted by the normaliser, so continuation persists
    string sql =
      "-- @description First part.\n" +
      "--\n" +
      "-- and the rest.\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Description.Should().Be("First part. and the rest.");
  }

  [Fact]
  public void Parse_NewAnnotationBreaksContinuation()
  {
    // A new annotation line breaks the previous continuation
    string sql =
      "-- @description First part.\n" +
      "-- @category core\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Description.Should().Be("First part.");
    result.Value.Category.Should().Be("core");
  }

  [Fact]
  public void Parse_BareAnnotationWithNoValue_ReturnsEmptyString()
  {
    string sql =
      "-- @description\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Description.Should().BeEmpty();
  }

  [Fact]
  public void Parse_SingleLineBlockComment_Extracts()
  {
    string sql =
      "/* @category lookup */\n" +
      "CREATE TABLE [dbo].[t] (id INT);";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.Category.Should().Be("lookup");
  }

  [Fact]
  public void Parse_TrailingCommentInsideStringLiteral_NotExtracted()
  {
    // A -- inside a string literal should not be treated as a comment
    string sql =
      "CREATE TABLE [dbo].[t]\n" +
      "(\n" +
      "    [val] VARCHAR(100) DEFAULT '--test' NOT NULL\n" +
      ");";

    OperationResult<ParsedAnnotations> result = AnnotationParser.Parse(sql, "test.sql");

    result.IsSuccess.Should().BeTrue();
    result.Value.ColumnAnnotations.Should().BeEmpty();
  }
}
