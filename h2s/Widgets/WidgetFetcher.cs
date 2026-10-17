using System.Collections.Concurrent;
using h2s.Widgets.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Widgets;

/// <summary>
///   Reads widgets through their kinds, keeping each result for its refresh interval and sharing one read among
///   concurrent requests for the same widget. A read that fails or takes too long gives an unknown result rather than an
///   error.
/// </summary>
/// <remarks>
///   A singleton, so the cache and the reads in flight are shared by every request. Cache keys include a hash of the
///   widget's and its connection's settings, so changing either takes effect at once.
/// </remarks>
public sealed partial class WidgetFetcher
{
  /// <summary>
  ///   How long a widget's read may take before it counts as failed.
  /// </summary>
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds (10);

  private readonly IMemoryCache                                           cache;
  private readonly ConcurrentDictionary<string, Lazy<Task<WidgetResult>>> inFlight = new (StringComparer.Ordinal);
  private readonly ILogger<WidgetFetcher>                                 logger;

  /// <summary>
  ///   Initializes a new instance of the <see cref="WidgetFetcher" /> class.
  /// </summary>
  /// <param name="cache">The cache that keeps widgets' results.</param>
  /// <param name="logger">The logger for reads that fail.</param>
  public WidgetFetcher (IMemoryCache cache, ILogger<WidgetFetcher> logger)
  {
    this.cache  = cache;
    this.logger = logger;
  }

  /// <summary>
  ///   Gets a widget's result, if one was read recently.
  /// </summary>
  /// <param name="key">The widget's cache key.</param>
  /// <returns>The result, or <c>null</c> when there's none.</returns>
  public WidgetResult? GetCached (string key) => this.cache.TryGetValue (key: key, value: out WidgetResult? result) ? result : null;

  /// <summary>
  ///   Gets a widget's result, reading it when there's none recent. Concurrent calls for one key share a single read.
  /// </summary>
  /// <param name="key">The widget's cache key.</param>
  /// <param name="widgetId">The widget's ID, for the log.</param>
  /// <param name="lifetime">How long the result is kept.</param>
  /// <param name="fetch">Reads the widget, given a token that cancels it after <see cref="Timeout" />.</param>
  /// <returns>The result.</returns>
  public async Task<WidgetResult> GetAsync (string                                      key,
                                            int                                         widgetId,
                                            TimeSpan                                    lifetime,
                                            Func<CancellationToken, Task<WidgetResult>> fetch)
  {
    if (this.GetCached (key) is { } cached)
    {
      return cached;
    }

    Lazy<Task<WidgetResult>> read =
      this.inFlight.GetOrAdd (key: key,
                              valueFactory: _ => new Lazy<Task<WidgetResult>> (() => this.ReadAsync (key: key,
                                                                                                     widgetId: widgetId,
                                                                                                     lifetime: lifetime,
                                                                                                     fetch: fetch)));

    return await read.Value;
  }

  /// <summary>
  ///   Reads a widget and keeps its result.
  /// </summary>
  /// <param name="key">The widget's cache key.</param>
  /// <param name="widgetId">The widget's ID, for the log.</param>
  /// <param name="lifetime">How long the result is kept.</param>
  /// <param name="fetch">Reads the widget.</param>
  /// <returns>The result.</returns>
  private async Task<WidgetResult> ReadAsync (string                                      key,
                                              int                                         widgetId,
                                              TimeSpan                                    lifetime,
                                              Func<CancellationToken, Task<WidgetResult>> fetch)
  {
    try
    {
      using var    timeout = new CancellationTokenSource (WidgetFetcher.Timeout);
      WidgetResult result;

      try
      {
        result = await fetch (timeout.Token);
      }
      catch (OperationCanceledException) when (timeout.IsCancellationRequested)
      {
        result = WidgetResult.Unknown ("The service didn't answer in time.");
      }
      catch (Exception ex)
      {
        this.LogWidgetFailed (exception: ex, widgetId: widgetId);
        result = WidgetResult.Unknown ("This widget couldn't be read; the server log has the details.");
      }

      _ = this.cache.Set (key: key, value: result, absoluteExpirationRelativeToNow: lifetime);

      return result;
    }
    finally
    {
      _ = this.inFlight.TryRemove (key: key, value: out _);
    }
  }

  /// <summary>
  ///   Logs a widget whose kind failed to read it.
  /// </summary>
  /// <param name="exception">What went wrong.</param>
  /// <param name="widgetId">The widget's ID.</param>
  [LoggerMessage (Level = LogLevel.Warning, Message = "Widget {WidgetId} couldn't be read.")]
  private partial void LogWidgetFailed (Exception exception, int widgetId);
}
