using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace SawmillService.Services;

/// <summary>
/// Shared hardening for SawmillService's Kafka producers (QA bug: the service died with
/// an uncatchable StackOverflow while the broker was unreachable). Two hazards are
/// addressed here:
///
/// 1. Native-callback safety — Confluent.Kafka's default log handler writes to
///    Console directly from librdkafka's native callback threads. If that write
///    throws (dead redirect pipe during shutdown) or any handler throws, the
///    exception crosses the native/managed boundary and takes the whole process
///    down. All handlers below are wrapped so no exception can ever escape into
///    the native callback context.
///
/// 2. Outage behaviour — fail-fast timeouts bound how long a publish against a
///    dead broker can linger (default message.timeout.ms is 5 minutes), and the
///    explicit error/log handlers replace the raw stderr flood with rate-bounded
///    ILogger output.
/// </summary>
internal static class KafkaProducerGuard
{
    /// <summary>Producer config with bounded timeouts for broker-outage resilience.</summary>
    public static ProducerConfig BuildConfig(string bootstrapServers) => new()
    {
        BootstrapServers = bootstrapServers,

        // Fail a publish against a dead broker within ~15s instead of the default
        // 5 minutes, so fire-and-forget tasks cannot pile up during an outage.
        MessageTimeoutMs = 15_000,

        // Bound connection setup / API-version negotiation during broker flaps.
        SocketTimeoutMs = 10_000,
        ApiVersionRequestTimeoutMs = 5_000,

        // Short backoff between reconnect attempts — reconnects stay cheap without
        // spinning the error log every tick.
        RetryBackoffMs = 1_000,
        SocketConnectionSetupTimeoutMs = 10_000,
    };

    /// <summary>
    /// Attaches exception-safe log/error handlers to a producer builder. Handlers run on
    /// librdkafka's native threads — every body is try/catch-guarded so a logging failure
    /// can never propagate across the native/managed boundary.
    /// </summary>
    public static ProducerBuilder<string, string> AttachGuardedHandlers(
        this ProducerBuilder<string, string> builder, ILogger? logger)
    {
        builder.SetLogHandler((_, log) =>
        {
            try
            {
                // Only errors and worse — avoids replaying the reconnect flood at
                // warning level during an outage. SyslogLevel: lower is worse.
                if (log.Level <= SyslogLevel.Error)
                {
                    logger?.LogWarning("Kafka [{Facility}]: {Message}", log.Name, log.Message);
                }
            }
            catch
            {
                // Swallowed by design: throwing here would crash the process.
            }
        });

        builder.SetErrorHandler((_, err) =>
        {
            try
            {
                logger?.LogWarning("Kafka error {Code}: {Reason} (fatal: {IsFatal})", err.Code, err.Reason, err.IsFatal);
            }
            catch
            {
                // Swallowed by design: throwing here would crash the process.
            }
        });

        return builder;
    }

    /// <summary>
    /// Safe shutdown flush: pumping a dead broker during dispose must not fail the
    /// process while it is already stopping.
    /// </summary>
    public static void SafeDispose(IProducer<string, string> producer)
    {
        try
        {
            producer.Flush(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Best-effort flush — the broker may be gone; never block shutdown.
        }
        finally
        {
            try { producer.Dispose(); } catch { /* already gone */ }
        }
    }
}
