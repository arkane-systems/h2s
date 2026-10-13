using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace h2s.Tests.Infrastructure;

/// <summary>
///   Holds Data Protection keys in memory, so a test host never writes key files to disk.
/// </summary>
public sealed class InMemoryXmlRepository : IXmlRepository
{
  private readonly ConcurrentQueue<XElement> elements = new ();

  /// <inheritdoc />
  public IReadOnlyCollection<XElement> GetAllElements () => this.elements.Select (e => new XElement (e)).ToList ();

  /// <inheritdoc />
  public void StoreElement (XElement element, string friendlyName) => this.elements.Enqueue (new XElement (element));
}
