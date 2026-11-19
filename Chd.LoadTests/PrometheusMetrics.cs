using Prometheus;

namespace Chd.LoadTests;

public static class PrometheusMetrics
{
    // Counter'lar - Sürekli artan değerler
    public static readonly Counter LockOperations = Metrics.CreateCounter(
        "chd_lock_operations_total",
        "Total number of lock operations",
        new CounterConfiguration { LabelNames = new[] { "status", "contention" } }
    );

    public static readonly Counter IdempotencyOperations = Metrics.CreateCounter(
        "chd_idempotency_operations_total",
        "Total number of idempotency operations",
        new CounterConfiguration { LabelNames = new[] { "status" } }
    );

    public static readonly Counter SagaOperations = Metrics.CreateCounter(
        "chd_saga_operations_total",
        "Total number of saga operations",
        new CounterConfiguration { LabelNames = new[] { "status" } }
    );

    // Histogram'lar - Latency ölçümü için
    public static readonly Histogram LockLatency = Metrics.CreateHistogram(
        "chd_lock_duration_seconds",
        "Lock acquire, work, and release duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "contention" },
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 10)
        }
    );

    public static readonly Histogram IdempotencyLatency = Metrics.CreateHistogram(
        "chd_idempotency_duration_seconds",
        "Idempotency operation duration in seconds",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 10)
        }
    );

    public static readonly Histogram SagaLatency = Metrics.CreateHistogram(
        "chd_saga_duration_seconds",
        "Saga operation duration in seconds",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 10)
        }
    );

    // Gauge'lar - Anlık değerler
    public static readonly Gauge ActiveTests = Metrics.CreateGauge(
        "chd_active_tests",
        "Number of currently running test scenarios"
    );

    public static readonly Gauge RequestsPerSecond = Metrics.CreateGauge(
        "chd_requests_per_second",
        "Current requests per second",
        new GaugeConfiguration { LabelNames = new[] { "scenario" } }
    );

    // Summary'ler - Percentile hesaplamaları için
    public static readonly Counter MappingItems = Metrics.CreateCounter(
        "chd_mapping_items_total",
        "Total number of mapped items",
        new CounterConfiguration { LabelNames = new[] { "mapper", "status" } }
    );

    public static readonly Histogram MappingItemLatency = Metrics.CreateHistogram(
        "chd_mapping_item_duration_seconds",
        "Average single-item mapping duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "mapper" },
            Buckets = new[]
            {
                1e-8, 2e-8, 5e-8, 1e-7, 2e-7, 5e-7,
                1e-6, 2e-6, 5e-6, 1e-5, 5e-5, 1e-4
            }
        }
    );

    public static readonly Histogram MappingBatchLatency = Metrics.CreateHistogram(
        "chd_mapping_batch_duration_seconds",
        "Batch mapping duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "mapper" },
            Buckets = Histogram.ExponentialBuckets(0.00001, 2, 12)
        }
    );

    public static readonly Counter LoggingCalls = Metrics.CreateCounter(
        "chd_logging_calls_total",
        "Logged method calls",
        new CounterConfiguration { LabelNames = new[] { "library", "event", "status" } }
    );

    public static readonly Histogram LoggingCallLatency = Metrics.CreateHistogram(
        "chd_logging_call_duration_seconds",
        "Duration of one logged method call",
        new HistogramConfiguration
        {
            LabelNames = new[] { "library", "event" },
            Buckets = Histogram.ExponentialBuckets(0.00001, 2, 16)
        }
    );

    public static readonly Histogram LoggingBatchLatency = Metrics.CreateHistogram(
        "chd_logging_batch_duration_seconds",
        "Duration of a 100-call logging batch",
        new HistogramConfiguration
        {
            LabelNames = new[] { "library", "event" },
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 14)
        }
    );

    public static readonly Summary OperationLatency = Metrics.CreateSummary(
        "chd_operation_latency_seconds",
        "Operation latency summary",
        new SummaryConfiguration
        {
            LabelNames = new[] { "operation_type" },
            Objectives = new[]
            {
                new QuantileEpsilonPair(0.5, 0.05),   // P50
                new QuantileEpsilonPair(0.9, 0.01),   // P90
                new QuantileEpsilonPair(0.95, 0.01),  // P95
                new QuantileEpsilonPair(0.99, 0.001)  // P99
            }
        }
    );
}
