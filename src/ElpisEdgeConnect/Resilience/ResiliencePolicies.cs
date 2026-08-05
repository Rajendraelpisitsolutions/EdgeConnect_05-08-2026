// ============================================================================
// File: Resilience/ResiliencePolicies.cs
// Purpose: Builds Polly v8 resilience pipelines for Fanuc API HTTP calls.
//          Each CNC machine gets its OWN pipeline so failures are completely
//          isolated — one sick machine cannot affect polling of healthy ones.
//
// PIPELINE ARCHITECTURE (executed from outer to inner):
//   ┌─────────────────────────────────────────────────────────┐
//   │  RETRY (3 attempts, exponential backoff with jitter)    │
//   │  ┌───────────────────────────────────────────────────┐  │
//   │  │  CIRCUIT BREAKER (trips after 5 failures in 30s)  │  │
//   │  │  ┌─────────────────────────────────────────────┐  │  │
//   │  │  │            HTTP REQUEST                     │  │  │
//   │  │  └─────────────────────────────────────────────┘  │  │
//   │  └───────────────────────────────────────────────────┘  │
//   └─────────────────────────────────────────────────────────┘
//
// HOW IT WORKS:
//   1. HTTP request is attempted
//   2. If it fails → Retry policy waits (exponential backoff) then retries
//   3. After 3 retries → request is considered failed
//   4. If 50% of requests fail within a 30s window (min 5 requests)
//      → Circuit breaker OPENS and all requests fail immediately for 30s
//   5. After 30s → Circuit breaker enters HALF-OPEN state
//   6. One test request is allowed through
//   7. If it succeeds → Circuit CLOSES (back to normal)
//   8. If it fails → Circuit OPENS again for another 30s
//
// WHY PER-MACHINE PIPELINES:
//   If all machines shared one circuit breaker, a single offline machine
//   that causes 5 failures could trip the breaker and stop polling ALL
//   machines. Per-machine pipelines ensure fault isolation.
// ============================================================================

using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace ElpisEdgeConnect.Resilience;

/// <summary>
/// Builds resilience pipelines (retry + circuit breaker) for Fanuc API calls.
/// Each machine gets its own pipeline so failures are fully isolated.
/// 
/// USAGE (in FanucApiClient):
///   var pipeline = ResiliencePolicies.CreateHttpPipeline("CNC-001", logger);
///   var response = await pipeline.ExecuteAsync(async token =>
///       await httpClient.SendAsync(request, token), cancellationToken);
/// </summary>
public static class ResiliencePolicies
{
    /// <summary>
    /// Creates a resilience pipeline tailored for a specific machine's HTTP calls.
    /// 
    /// The pipeline combines two strategies:
    ///   1. RETRY: Handles transient failures by retrying up to 3 times
    ///   2. CIRCUIT BREAKER: Stops retrying entirely when a machine is clearly down
    /// 
    /// Both strategies are configured with logging callbacks that include the
    /// machineId, making it easy to diagnose issues in logs.
    /// </summary>
    /// <param name="machineId">Machine ID for log correlation (e.g., "CNC-001")</param>
    /// <param name="logger">Logger instance for retry/breaker event logging</param>
    /// <returns>A resilience pipeline wrapping HttpResponseMessage results</returns>
    public static ResiliencePipeline<HttpResponseMessage> CreateHttpPipeline(
        string machineId,
        ILogger logger)
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()

            // ── RETRY STRATEGY ──────────────────────────────────────────
            // Retries transient HTTP failures with exponential backoff.
            // This handles brief network glitches, temporary server overload,
            // and rate limiting responses.
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                // Maximum number of retry attempts AFTER the initial failure.
                // Total attempts = 1 (initial) + 3 (retries) = 4 attempts max.
                MaxRetryAttempts = 3,

                // Base delay between retries. With exponential backoff:
                //   Retry 1: ~500ms  (± jitter)
                //   Retry 2: ~1000ms (± jitter)
                //   Retry 3: ~2000ms (± jitter)
                Delay = TimeSpan.FromMilliseconds(500),

                // Exponential backoff multiplies the delay by 2 each retry.
                // This prevents hammering a struggling server with rapid retries.
                BackoffType = DelayBackoffType.Exponential,

                // Jitter adds randomness (±25%) to the delay to prevent
                // "thundering herd" — many machines retrying at the exact same time.
                // Without jitter: 100 machines all retry at 500ms → server spike
                // With jitter:    retries spread across 375-625ms → smoother load
                UseJitter = true,

                // WHICH FAILURES TRIGGER A RETRY:
                // We retry on network errors, timeouts, and specific HTTP status codes
                // that indicate transient server-side issues.
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    // Network-level failures (DNS, connection refused, etc.)
                    .Handle<HttpRequestException>()
                    // Request cancelled due to timeout
                    .Handle<TaskCanceledException>()
                    // HTTP status codes that are typically transient:
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.RequestTimeout or        // 408 — server took too long
                        HttpStatusCode.TooManyRequests or       // 429 — rate limited
                        HttpStatusCode.ServiceUnavailable or    // 503 — server temporarily down
                        HttpStatusCode.GatewayTimeout),         // 504 — upstream timeout

                // Callback fired on each retry — logs the attempt for diagnostics.
                // Includes the machine ID, attempt number, delay, and failure reason.
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Machine {MachineId}: Retry {Attempt} after {Delay}ms — {Outcome}",
                        machineId,
                        args.AttemptNumber,          // 1, 2, or 3
                        args.RetryDelay.TotalMilliseconds,
                        // Log either the exception message or the HTTP status code
                        args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                    return ValueTask.CompletedTask;
                }
            })

            // ── CIRCUIT BREAKER STRATEGY ────────────────────────────────
            // Prevents wasting resources on a machine that's clearly down.
            // After enough failures in a time window, the breaker "trips" and
            // immediately fails all requests without making HTTP calls.
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                // Trip the breaker when 50% or more requests fail.
                // This means if 3 out of 5 requests fail → circuit opens.
                FailureRatio = 0.5,

                // Time window for measuring the failure ratio.
                // Only failures within the last 30 seconds count.
                // Older failures are forgotten (sliding window).
                SamplingDuration = TimeSpan.FromSeconds(30),

                // Minimum number of requests before the failure ratio is evaluated.
                // This prevents the breaker from tripping on the very first failure
                // (1 out of 1 = 100% failure rate). We need at least 5 data points.
                MinimumThroughput = 5,

                // How long the circuit stays OPEN before allowing a test request.
                // During this time, all requests fail immediately with
                // BrokenCircuitException — no HTTP calls are made.
                BreakDuration = TimeSpan.FromSeconds(30),

                // WHICH FAILURES COUNT TOWARD THE FAILURE RATIO:
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => !r.IsSuccessStatusCode),

                // ── State Change Callbacks ──

                // OPENED: Circuit just tripped — machine is considered DOWN.
                // Log as ERROR because this means the machine has been consistently
                // failing and we're stopping all communication attempts.
                OnOpened = args =>
                {
                    logger.LogError(
                        "Machine {MachineId}: Circuit OPENED — pausing polls for {Duration}s. " +
                        "Machine appears to be offline or unresponsive.",
                        machineId,
                        args.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                },

                // CLOSED: Circuit recovered — machine is back to normal.
                // Log as INFO because this is good news — polling is working again.
                OnClosed = args =>
                {
                    logger.LogInformation(
                        "Machine {MachineId}: Circuit CLOSED — machine recovered, resuming normal polling",
                        machineId);
                    return ValueTask.CompletedTask;
                },

                // HALF-OPEN: Break duration expired — allowing one test request.
                // If this request succeeds → circuit closes (recovered).
                // If this request fails → circuit opens again (still down).
                OnHalfOpened = args =>
                {
                    logger.LogInformation(
                        "Machine {MachineId}: Circuit HALF-OPEN — sending test request to check recovery",
                        machineId);
                    return ValueTask.CompletedTask;
                }
            })

            // Build the final immutable pipeline
            .Build();
    }
}
