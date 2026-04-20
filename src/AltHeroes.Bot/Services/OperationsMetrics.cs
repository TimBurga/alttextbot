using System.Diagnostics.Metrics;

namespace AltHeroes.Bot.Services;

public sealed class OperationsMetrics : IDisposable
{
    private readonly Meter _meter;
    public Counter<int> LabelsApplied { get; }
    public Counter<int> LabelsNegated { get; }
    public Counter<int> SubscribersEnrolled { get; }
    public Counter<int> SubscribersUnenrolled { get; }
    public Counter<int> PostsScored { get; }
    public Counter<int> ApiErrors { get; }
    public UpDownCounter<int> ActiveSubscribers { get; }

    public OperationsMetrics()
    {
        _meter = new Meter("AltHeroes.Bot", "1.0.0");

        LabelsApplied = _meter.CreateCounter<int>(
            "altheroes.labels.applied",
            description: "Number of labels applied to subscribers");

        LabelsNegated = _meter.CreateCounter<int>(
            "altheroes.labels.negated",
            description: "Number of labels negated/removed");

        SubscribersEnrolled = _meter.CreateCounter<int>(
            "altheroes.subscribers.enrolled",
            description: "Number of subscribers enrolled");

        SubscribersUnenrolled = _meter.CreateCounter<int>(
            "altheroes.subscribers.unenrolled",
            description: "Number of subscribers unenrolled");

        PostsScored = _meter.CreateCounter<int>(
            "altheroes.posts.scored",
            description: "Number of posts scored");

        ApiErrors = _meter.CreateCounter<int>(
            "altheroes.api.errors",
            description: "Number of API errors encountered");

        ActiveSubscribers = _meter.CreateUpDownCounter<int>(
            "altheroes.subscribers.active",
            description: "Current number of active subscribers");
    }

    public void Dispose() => _meter.Dispose();
}
