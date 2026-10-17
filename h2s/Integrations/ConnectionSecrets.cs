using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace h2s.Integrations;

/// <summary>
///   Encrypts and decrypts connections' secret settings (<see cref="Models.Connection.SecretsJson" />) with Data
///   Protection, whose keys live beside the database.
/// </summary>
/// <remarks>
///   Backups never carry secrets, and the keys may be lost (a new data volume, say). A secret that can't be decrypted
///   then reads as not set, and Settings asks for it again; it's never an error.
/// </remarks>
public sealed class ConnectionSecrets
{
  /// <summary>
  ///   The Data Protection purpose for connection secrets.
  /// </summary>
  public const string Purpose = "h2s.connections.secrets";

  private readonly IDataProtector protector;

  /// <summary>
  ///   Initializes a new instance of the <see cref="ConnectionSecrets" /> class.
  /// </summary>
  /// <param name="provider">The app's Data Protection provider.</param>
  public ConnectionSecrets (IDataProtectionProvider provider)
    => this.protector = provider.CreateProtector (ConnectionSecrets.Purpose);

  /// <summary>
  ///   Encrypts secrets for storage.
  /// </summary>
  /// <param name="secrets">The secrets, keyed by option name. Blank values are left out.</param>
  /// <returns>The encrypted secrets, or <c>null</c> when there are none to store.</returns>
  public string? Protect (IReadOnlyDictionary<string, string> secrets)
  {
    Dictionary<string, string> set = secrets.Where (s => !string.IsNullOrWhiteSpace (s.Value))
                                            .ToDictionary (keySelector: s => s.Key,
                                                           elementSelector: s => s.Value,
                                                           comparer: StringComparer.Ordinal);

    return set.Count == 0 ? null : this.protector.Protect (JsonSerializer.Serialize (set));
  }

  /// <summary>
  ///   Decrypts stored secrets.
  /// </summary>
  /// <param name="stored">The encrypted secrets, or <c>null</c> for none.</param>
  /// <returns>The secrets, keyed by option name; empty when there are none, or they can't be decrypted or read.</returns>
  public IReadOnlyDictionary<string, string> Unprotect (string? stored)
  {
    if (string.IsNullOrEmpty (stored))
    {
      return new Dictionary<string, string> ();
    }

    try
    {
      return JsonSerializer.Deserialize<Dictionary<string, string>> (this.protector.Unprotect (stored)) ??
             new Dictionary<string, string> ();
    }
    catch (Exception ex) when (ex is CryptographicException or JsonException)
    {
      return new Dictionary<string, string> ();
    }
  }
}
