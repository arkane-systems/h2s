# Writing an integration

An integration connects h2s to another service, such as Uptime Kuma or Home Assistant. This guide grows with Phase 5. So far it covers integrations and their connections; widgets on tiles and panels come next.

## The idea

The core disposes, integrations propose. An integration declares what it needs, and h2s renders the settings, stores them, checks them, keeps secrets encrypted, and runs the connection test. No integration ships its own settings UI or HTML, which is what will make third-party integrations workable later.

## The contracts

Integrations are written against `h2s.Widgets.Abstractions`, a class library that references nothing of the app: no EF Core, no Razor.

- **`SchemaOption`** is one setting. It has a key, a label, a type (`Text`, `Url`, `Integer`, `Boolean` or `Secret`), and optionally help text, a default, a required flag, integer bounds, and a regular expression `Pattern` for text, with a `PatternDescription` for its error ("may only contain letters, numbers, and hyphens").
- **`OptionValues`** reads stored settings against a schema. A value that's missing or doesn't fit takes the option's default, so an integration never sees bad data. Read values with `GetText`, `GetInteger`, `GetBoolean` and `GetSecret`.
- **`IIntegration`** is the integration:
  - `Key` is stored with every connection, so never change it.
  - `DisplayName` is what Settings shows.
  - `AllowsMultipleConnections` says whether there can be more than one, such as two Home Assistant instances.
  - `ConnectionOptions` is the schema of a connection's settings.
  - `Validate (OptionValues)` (optional) checks rules the schema can't express. It runs on every save and import, so it mustn't touch the network.
  - `TestAsync (ConnectionContext)` (optional) tries the settings against the live service for Settings' Test button, and returns a `ConnectionTestResult` with a level (`Success`, `Warning` or `Danger`) and a message that says how to fix what it found.

`ConnectionContext` carries the settings, secrets included, plus the request's services and a cancellation token. Integrations are singletons, so resolve anything request-scoped (an `IHttpClientFactory` client, say) from `Services`.

## Secrets

Declare a password or token as a `Secret` option. h2s stores secrets apart from the other settings, encrypted with ASP.NET Core Data Protection, and never sends them to a browser or puts them in a backup. Settings can only set, replace or clear them. If the encryption keys are lost, a secret reads as not set (`GetSecret` returns `null`), so handle that as you would an empty field.

## Registering

Integrations are compiled in for now. Register one as a singleton in `Program.cs`:

```csharp
builder.Services.AddSingleton<IIntegration, UptimeKumaIntegration> ();
```

`UptimeKumaIntegration` (`h2s/Integrations/UptimeKuma/`) is the reference: a base-URL check in `Validate`, and a `TestAsync` that probes the server and its status page.

## Outbound requests

Fetch through a named `IHttpClientFactory` client with its own timeout, registered in `Program.cs`, and keep certificate validation on. A homelab CA is trusted by mounting it (see the README), never by accepting any certificate.
