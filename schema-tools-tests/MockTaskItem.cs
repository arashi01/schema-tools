using System.Collections;
using Microsoft.Build.Framework;

namespace SchemaTools.Tests;

/// <summary>
/// Minimal ITaskItem implementation for testing TableFiles support
/// </summary>
internal class MockTaskItem : ITaskItem
{
  private readonly Dictionary<string, string> _metadata;

  public MockTaskItem(string itemSpec)
  {
    ItemSpec = itemSpec;
    _metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["FullPath"] = Path.GetFullPath(itemSpec)
    };
  }

  public string ItemSpec { get; set; }
  public int MetadataCount => _metadata.Count;
  public ICollection MetadataNames => _metadata.Keys;

  public IDictionary CloneCustomMetadata() => new Dictionary<string, string>(_metadata);
  public string GetMetadata(string metadataName) =>
      _metadata.TryGetValue(metadataName, out string? value) ? value : string.Empty;
  public void RemoveMetadata(string metadataName) => _metadata.Remove(metadataName);
  public void SetMetadata(string metadataName, string metadataValue) =>
      _metadata[metadataName] = metadataValue;
  public void CopyMetadataTo(ITaskItem destinationItem)
  {
    foreach (KeyValuePair<string, string> kvp in _metadata)
      destinationItem.SetMetadata(kvp.Key, kvp.Value);
  }
}
