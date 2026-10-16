using h2s.Integrations;
using Microsoft.AspNetCore.DataProtection;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="ConnectionSecrets" />.
/// </summary>
public class ConnectionSecretsTests
{
  [Fact]
  public void ProtectThenUnprotect_RoundTrips ()
  {
    var secrets = new ConnectionSecrets (new EphemeralDataProtectionProvider ());

    string? stored = secrets.Protect (new Dictionary<string, string> { ["token"] = "s3cret", ["blank"] = " " });

    Assert.NotNull (stored);
    Assert.DoesNotContain (expectedSubstring: "s3cret", actualString: stored);
    Assert.Equal (expected: new Dictionary<string, string> { ["token"] = "s3cret" }, actual: secrets.Unprotect (stored));
  }

  [Fact]
  public void Protect_StoresNothingWithoutSecrets ()
    => Assert.Null (new ConnectionSecrets (new EphemeralDataProtectionProvider ()).Protect (new Dictionary<string, string>
                                                                                            {
                                                                                              ["token"] = "",
                                                                                            }));

  [Theory]
  [InlineData (null)]
  [InlineData ("")]
  [InlineData ("not protected")]
  public void Unprotect_ReadsWhatItCantDecryptAsNoSecrets (string? stored)
    => Assert.Empty (new ConnectionSecrets (new EphemeralDataProtectionProvider ()).Unprotect (stored));

  [Fact]
  public void Unprotect_ReadsSecretsFromLostKeysAsNoSecrets ()
  {
    // Another provider has other keys, as after the keys directory is lost.
    string? stored =
      new ConnectionSecrets (new EphemeralDataProtectionProvider ()).Protect (new Dictionary<string, string>
                                                                              {
                                                                                ["token"] = "s3cret",
                                                                              });

    Assert.Empty (new ConnectionSecrets (new EphemeralDataProtectionProvider ()).Unprotect (stored));
  }
}
