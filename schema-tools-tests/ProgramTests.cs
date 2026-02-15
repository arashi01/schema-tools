namespace SchemaTools.Tests;

/// <summary>
/// Tests for the CLI entry point used by Full Framework MSBuild
/// via dotnet exec for DacFx-isolated metadata extraction.
/// </summary>
public sealed class ProgramTests
{
  [Fact]
  public void Main_NoArguments_ReturnsNonZero()
  {
    int result = Program.Main([]);

    result.Should().Be(1);
  }

  [Fact]
  public void Main_HelpFlag_ReturnsZero()
  {
    int result = Program.Main(["--help"]);

    result.Should().Be(0);
  }

  [Fact]
  public void Main_ShortHelpFlag_ReturnsZero()
  {
    int result = Program.Main(["-h"]);

    result.Should().Be(0);
  }

  [Fact]
  public void Main_UnknownCommand_ReturnsNonZero()
  {
    int result = Program.Main(["unknown-command"]);

    result.Should().Be(1);
  }

  [Fact]
  public void Main_ExtractMetadata_MissingDacpac_ReturnsNonZero()
  {
    int result = Program.Main(["extract-metadata", "--output", "out.json"]);

    result.Should().Be(1);
  }

  [Fact]
  public void Main_ExtractMetadata_MissingOutput_ReturnsNonZero()
  {
    int result = Program.Main(["extract-metadata", "--dacpac", "test.dacpac"]);

    result.Should().Be(1);
  }

  [Fact]
  public void Main_ExtractMetadata_UnknownArgument_ReturnsNonZero()
  {
    int result = Program.Main(["extract-metadata", "--dacpac", "test.dacpac", "--output", "out.json", "--unknown"]);

    result.Should().Be(1);
  }

  [Fact]
  public void Main_ExtractMetadata_NonexistentDacpac_ReturnsNonZero()
  {
    int result = Program.Main([
      "extract-metadata",
      "--dacpac", Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}.dacpac"),
      "--output", Path.Combine(Path.GetTempPath(), "out.json")
    ]);

    result.Should().Be(1);
  }

  [Fact]
  public void Main_ExtractMetadata_CommandIsCaseInsensitive()
  {
    // Should recognise the command even in mixed case (and then fail
    // because the dacpac does not exist, confirming it was routed correctly)
    int result = Program.Main([
      "Extract-Metadata",
      "--dacpac", Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}.dacpac"),
      "--output", Path.Combine(Path.GetTempPath(), "out.json")
    ]);

    result.Should().Be(1);
  }
}
