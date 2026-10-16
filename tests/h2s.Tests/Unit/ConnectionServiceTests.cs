using h2s.Data;
using h2s.Integrations;
using h2s.Integrations.UptimeKuma;
using h2s.Models;
using h2s.Tests.Infrastructure;
using h2s.Widgets.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="ConnectionService" /> and the Uptime Kuma integration's rules.
/// </summary>
public class ConnectionServiceTests
{
  [Fact]
  public async Task SaveOnlyAsync_CreatesTheConnectionThenUpdatesIt_KeepingItsSecrets ()
  {
    await using var              database = await TestDatabase.CreateAsync ();
    await using DashboardContext context  = database.CreateContext ();
    using var                    cache    = new MemoryCache (new MemoryCacheOptions ());
    ConnectionService            service  = TestConnections.CreateService (context: context, cache: cache);
    var                          kuma     = new UptimeKumaIntegration ();

    Connection created = await service.SaveOnlyAsync (integration: kuma,
                                                      optionsJson: """{"serverUrl":"https://a.home.arpa"}""",
                                                      cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: "Uptime Kuma", actual: created.Name);

    await context.Connections.ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression: c => c.SecretsJson,
                                                                                        valueExpression: "secret"),
                                                  cancellationToken: TestContext.Current.CancellationToken);
    context.ChangeTracker.Clear ();

    Connection updated = await service.SaveOnlyAsync (integration: kuma,
                                                      optionsJson: """{"serverUrl":"https://b.home.arpa"}""",
                                                      cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: created.Id, actual: updated.Id);
    Assert.Equal (expected: "secret",   actual: updated.SecretsJson);
    Assert.Equal (expected: "https://b.home.arpa",
                  actual: (await UptimeKumaSettings.LoadAsync (connections: service,
                                                               cancellationToken: TestContext.Current.CancellationToken))!
                 .ServerUrl);
  }

  [Fact]
  public async Task DeleteAllAsync_TurnsTheIntegrationOff ()
  {
    await using var              database = await TestDatabase.CreateAsync ();
    await using DashboardContext context  = database.CreateContext ();
    using var                    cache    = new MemoryCache (new MemoryCacheOptions ());
    ConnectionService            service  = TestConnections.CreateService (context: context, cache: cache);

    context.Connections.Add (TestConnections.UptimeKuma ());
    await context.SaveChangesAsync (TestContext.Current.CancellationToken);

    // Read once, so the delete must clear the cache.
    Assert.NotNull (await UptimeKumaSettings.LoadAsync (connections: service,
                                                        cancellationToken: TestContext.Current.CancellationToken));

    await service.DeleteAllAsync (integrationKey: UptimeKumaIntegration.IntegrationKey,
                                  cancellationToken: TestContext.Current.CancellationToken);

    Assert.Null (await UptimeKumaSettings.LoadAsync (connections: service,
                                                     cancellationToken: TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task ReadOptions_DecryptsSecrets_AndKnowsOnlyRegisteredIntegrations ()
  {
    await using var              database = await TestDatabase.CreateAsync ();
    await using DashboardContext context  = database.CreateContext ();
    using var                    cache    = new MemoryCache (new MemoryCacheOptions ());
    var                          provider = new EphemeralDataProtectionProvider ();
    var                          secrets  = new ConnectionSecrets (provider);
    var service = new ConnectionService (context: context,
                                         integrations: new IntegrationCatalog ([new SecretIntegration ()]),
                                         secrets: secrets,
                                         cache: cache);

    OptionValues? options = service.ReadOptions (new Connection
                                                 {
                                                   IntegrationKey = SecretIntegration.IntegrationKey,
                                                   OptionsJson    = """{"server":"https://ha.home.arpa","token":"not here"}""",
                                                   SecretsJson =
                                                     secrets.Protect (new Dictionary<string, string> { ["token"] = "abc" }),
                                                 });

    Assert.Equal (expected: "https://ha.home.arpa", actual: options!.GetText ("server"));
    Assert.Equal (expected: "abc",                  actual: options.GetSecret ("token"));
    Assert.False (options.Values.ContainsKey ("token"));
    Assert.Null (service.ReadOptions (new Connection { IntegrationKey = "unknown" }));
  }

  [Theory]
  [InlineData ("https://status.home.arpa",           true)]
  [InlineData ("https://status.home.arpa/",          true)]
  [InlineData ("http://status.home.arpa:3001",       true)]
  [InlineData ("https://status.home.arpa/dashboard", false)]
  [InlineData ("https://status.home.arpa/?x=1",      false)]
  public void UptimeKumaValidate_WantsABaseUrl (string serverUrl, bool valid)
  {
    var kuma = new UptimeKumaIntegration ();

    Assert.Equal (expected: valid ? null : UptimeKumaIntegration.InvalidServerUrl,
                  actual: kuma.Validate (new OptionValues (schema: kuma.ConnectionOptions,
                                                           json: $$"""{"serverUrl":"{{serverUrl}}"}""")));
  }

  /// <summary>
  ///   An integration with a secret, like Home Assistant's token.
  /// </summary>
  private sealed class SecretIntegration : IIntegration
  {
    /// <summary>
    ///   The integration's key.
    /// </summary>
    public const string IntegrationKey = "secret";

    /// <inheritdoc />
    public string Key => SecretIntegration.IntegrationKey;

    /// <inheritdoc />
    public string DisplayName => "Secret";

    /// <inheritdoc />
    public bool AllowsMultipleConnections => true;

    /// <inheritdoc />
    public IReadOnlyList<SchemaOption> ConnectionOptions { get; } =
      [
        new (Key: "server", Label: "Server", Type: OptionType.Url), new (Key: "token", Label: "Token", Type: OptionType.Secret),
      ];
  }
}
