namespace SchemaTools.Tests.Fixtures;

/// <summary>
/// Creates and manages a temporary directory for tests that require file I/O.
/// The directory and all contents are deleted when disposed.
/// </summary>
internal sealed class TempDirectoryFixture : IDisposable
{
  public string RootPath { get; }

  public TempDirectoryFixture()
  {
    RootPath = Path.Combine(
      Path.GetTempPath(),
      "schema-tools-tests",
      Guid.NewGuid().ToString());
    Directory.CreateDirectory(RootPath);
  }

  /// <summary>
  /// Create a subdirectory within the temp root and return its path.
  /// </summary>
  public string CreateSubdirectory(string relativePath)
  {
    string fullPath = Path.Combine(RootPath, relativePath);
    Directory.CreateDirectory(fullPath);
    return fullPath;
  }

  /// <summary>
  /// Write a file within the temp root and return its path.
  /// Creates parent directories as needed.
  /// </summary>
  public string WriteFile(string relativePath, string content)
  {
    string fullPath = Path.Combine(RootPath, relativePath);
    string? directory = Path.GetDirectoryName(fullPath);
    if (directory is not null)
      Directory.CreateDirectory(directory);
    File.WriteAllText(fullPath, content);
    return fullPath;
  }

  public void Dispose()
  {
    if (Directory.Exists(RootPath))
    {
      try
      {
        Directory.Delete(RootPath, recursive: true);
      }
      catch (IOException)
      {
        // Best-effort cleanup; temp directory will be cleaned by OS eventually.
      }
    }
  }
}
