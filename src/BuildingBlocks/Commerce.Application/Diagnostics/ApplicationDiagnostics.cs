using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Commerce.Application.Diagnostics;

internal static class ApplicationDiagnostics
{
    public const string InstrumentationName =
        "Commerce.Application";

    public static ActivitySource ActivitySource { get; } =
        new(InstrumentationName);

    public static Meter Meter { get; } =
        new(InstrumentationName);

    public static Counter<long> CommandExecutions { get; } =
        Meter.CreateCounter<long>(
            "commerce.application.command.executions",
            description:
            "Number of Application commands grouped by command and outcome.");

    public static Histogram<double> CommandDuration { get; } =
        Meter.CreateHistogram<double>(
            "commerce.application.command.duration",
            unit: "s",
            description:
            "Application command execution duration in seconds.");
}
