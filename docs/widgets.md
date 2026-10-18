# Writing an integration

An integration connects h2s to another service, such as Uptime Kuma or Home Assistant. This guide grows with Phase 5. So far it covers integrations, their connections and Settings cards, and widgets on links' tiles and status bars.

## The idea

The core disposes, integrations propose. An integration declares what it needs, and h2s renders the settings, stores them, checks them, keeps secrets encrypted, and runs the connection test. No integration ships its own settings UI or HTML, which is what will make third-party integrations workable later.

## The contracts

Integrations are written against `h2s.Widgets.Abstractions`, a class library that references nothing of the app: no EF Core, no Razor.

- **`SchemaOption`** is one setting. It has a key, a label, a type (`Text`, `Url`, `Integer`, `Boolean`, `Secret` or `Select`, with its `Choices`), and optionally help text, a placeholder, a default, a required flag, integer bounds, and a regular expression `Pattern` for text, with a `PatternDescription` for its error ("may only contain letters, numbers, and hyphens"). The browser checks the pattern too, as an HTML `pattern` with the `v` flag, so write it to work there as well: escape a hyphen in a character class (`[A-Za-z0-9\-]+`). A widget kind's text or integer option may also name one of its integration's choice lists in `ChoicesFrom` (see Choice lists below).
- **`OptionValues`** reads stored settings against a schema. A value that's missing or doesn't fit takes the option's default, so an integration never sees bad data. Read values with `GetText`, `GetInteger`, `GetBoolean` and `GetSecret`.
- **`IIntegration`** is the integration:
  - `Key` is stored with every connection, so never change it.
  - `DisplayName` is what Settings shows, and `Description` is a sentence or two under it saying what the integration adds.
  - `AllowsMultipleConnections` says whether there can be more than one, such as two Home Assistant instances.
  - `ConnectionOptions` is the schema of a connection's settings.
  - `Validate (OptionValues)` (optional) checks rules the schema can't express. It runs on every save and import, so it mustn't touch the network.
  - `TestAsync (ConnectionContext)` (optional) tries the settings against the live service for Settings' Test button, and returns a `ConnectionTestResult` with a level (`Success`, `Warning` or `Danger`) and a message that says how to fix what it found.
  - `GetChoicesAsync (listKey, ConnectionContext)` (optional) reads one of the integration's choice lists from the service (see Choice lists below).

`ConnectionContext` carries the settings, secrets included, plus the request's services and a cancellation token. Integrations are singletons, so resolve anything request-scoped (an `IHttpClientFactory` client, say) from `Services`.

## Settings

Settings → Integrations gives every registered integration a card, built from `ConnectionOptions`: Save, Test connection and, once there's a connection, Remove (after a confirmation). You write no UI. Each integration has one connection there for now; integrations that allow several get a list when the first one arrives.

## Secrets

Declare a password or token as a `Secret` option. h2s stores secrets apart from the other settings, encrypted with ASP.NET Core Data Protection, and never sends them to a browser or puts them in a backup. Settings shows only whether each is set, and can replace it or, when it's optional, clear it. A secret left blank keeps its stored value, for Test as well as Save, so you can test without typing a token again. If the encryption keys are lost, a secret reads as not set (`GetSecret` returns `null`), so handle that as you would an empty field.

## Widgets

An integration offers widget kinds through `WidgetKinds`. A kind (`IWidgetKind`) declares:

- `Key`, stored with each widget and unique within the integration, so never change it;
- `DisplayName`, shown in the link dialog as "Integration: Name";
- `Role`: `App` for a widget that belongs to links of one app (at most one per link, on the left of the tile), or `Status` for one that suits any link (compact pills on the right, as many as you like);
- `Placements` (optional): where its widgets make sense, as flags of `WidgetPlacements`: `Tile` (a link's tile) and `StatusBar` (a status bar's items, each under a label). By default a status widget allows both and an app widget only tiles; declare it when yours belong in only one place, and h2s will offer and accept them only there;
- `Options`, a schema like a connection's;
- `DefaultRefresh`, how often its widgets are read again (a widget may choose its own, from 15 seconds).

`FetchAsync (WidgetContext)` reads the service. The context carries the connection's settings (secrets included), the widget's own options, the request's services and a cancellation token, which the core cancels after 10 seconds. Return a `WidgetResult`: a `WidgetStatus` for the pill's colour, a few `WidgetStat`s (label, value, unit, and a severity when one figure deserves a colour of its own), and a `Description` sentence, which is the tooltip and what screen readers hear. Return `WidgetResult.Unknown (reason)` when the service can't be read; an exception or a timeout gives an unknown result too, so a widget never breaks the page.

The core keeps each result for the widget's interval and shares one read among everyone looking, so read freely; if your service reports many things in one request, cache that request yourself (Uptime Kuma's client keeps `/metrics` for 30 seconds, so a dashboard of monitor widgets costs one request). Widgets with the same settings share their result, including the link dialog's live preview of a widget being edited: the preview is how people test a widget, so make the `Description` say what's wrong when the service can't be read ("Uptime Kuma doesn't report a monitor 99."). `KumaMonitorWidgetKind` is the reference; `JsonApiValueWidgetKind` (`h2s/Integrations/JsonApi/`) is a second, for an integration with no fixed service: it takes a path and a value path as options, caches its own HTTP reads in `JsonApiClient` (15 seconds, so several widgets on one endpoint cost one request), and checks in `FetchAsync` that its path stays on its connection's host before it sends credentials anywhere. It also shows how a kind turns a problem into the right status: an unreadable service is `Unknown`, a service that answers badly is `Error`, and a value that isn't there is a `Warning`, each with a sentence that says which.

## Choice lists

When an option's value is something the service knows about, such as a monitor or an entity, let people pick it by name. Publish a choice list from the integration: `GetChoicesAsync (listKey, ConnectionContext)` returns a `ChoiceList` of `OptionChoice`s (value, label and an optional `Group` heading; keep a group's choices together), or `ChoiceList.Unavailable (reason)` when the service can't be read, or `null` for a list it doesn't have. Then point options at it with `ChoicesFrom = "<list>"`. A list belongs to the integration, not to a widget kind, so every kind that picks the same thing shares it, and the link dialog reads it once each time it opens, through the integration's first connection.

The option keeps its own type and checks: the form offers the list as a dropdown, with "Another value…" for typing a value the list doesn't hold, and falls back to the plain field (with your reason) when the list can't be read. So a service that's down never stops anyone saving, and nothing stored changes if a list is added to an existing option later. Uptime Kuma's `monitors` list is the reference: group monitors under "Groups", the rest under "Monitors", each labelled with its name and ID.

## Registering

Integrations are compiled in for now. Register one as a singleton in `Program.cs`:

```csharp
builder.Services.AddSingleton<IIntegration, UptimeKumaIntegration> ();
```

`UptimeKumaIntegration` (`h2s/Integrations/UptimeKuma/`) is the reference: a required API key secret, a select for the uptime window, a base-URL check in `Validate`, and a `TestAsync` that probes the server and reads its `/metrics` with the key. `JsonApiIntegration` (`h2s/Integrations/JsonApi/`) is a second example, with a select (the authentication method) whose other options only matter for some choices, an optional secret, and rules in `Validate` that depend on the select. `Validate` also runs on import, where secrets are never present, so it checks only the settings; a missing secret is for `TestAsync` and the widgets to report.

## Outbound requests

Fetch through a named `IHttpClientFactory` client with its own timeout, registered in `Program.cs`, and keep certificate validation on. A homelab CA is trusted by mounting it (see the README), never by accepting any certificate.
