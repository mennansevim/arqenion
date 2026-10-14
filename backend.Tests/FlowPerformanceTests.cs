using System.Diagnostics;
using Arqenion.Api.Models;
using Xunit.Abstractions;

namespace Arqenion.Api.Tests;

// Karar süresi (HTTP ve veritabanı hariç): motor bir olayı 100 ms bütçesinin çok altında değerlendirmeli.
public sealed class FlowPerformanceTests
{
    private readonly ITestOutputHelper _out;
    public FlowPerformanceTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Decision_time_is_far_below_the_100ms_budget()
    {
        var times = new List<double>();
        var rnd = new Random(7);
        var codes = TestModel.Model.Catalog.Keys.ToArray();
        foreach (var scenario in new[] { "case-basic-01", "case-adhesion-01", "case-bleeding-01", "case-count-01", "case-obese-01" })
            for (var rep = 0; rep < 20; rep++)
            {
                var (session, service) = TestModel.StartSession(scenario);
                while (session.CurrentNodeId is not null)
                {
                    var events = rnd.NextDouble() < 0.2
                        ? new List<EventRequest> { TestModel.Deliver(codes[rnd.Next(codes.Length)]) }
                        : TestModel.CorrectEvents(session).ToList();
                    foreach (var ev in events)
                    {
                        var sw = Stopwatch.StartNew();
                        service.Validate(session, ev);
                        times.Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
            }

        times.Sort();
        double P(double p) => times[Math.Min(times.Count - 1, (int)(p * times.Count))];
        _out.WriteLine($"olay={times.Count} p50={P(.5):F3} p95={P(.95):F3} p99={P(.99):F3} maks={times[^1]:F3} ms");
        Assert.True(P(.99) < 5, $"p99 {P(.99):F3} ms");
    }
}
