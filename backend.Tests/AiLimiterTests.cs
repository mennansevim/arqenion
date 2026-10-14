using Arqenion.Api.Models;
using Arqenion.Api.Services;
using Microsoft.Extensions.Configuration;

namespace Arqenion.Api.Tests;

public sealed class AiLimiterTests
{
    private static AiLimiter Limiter(int perSession = 25, int perMinute = 60, int concurrent = 4)
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiGateway:MaxPerSession"] = perSession.ToString(),
            ["AiGateway:MaxPerMinute"] = perMinute.ToString(),
            ["AiGateway:MaxConcurrent"] = concurrent.ToString()
        }).Build());

    private static AiFeedbackRequest Req(string session, string actual = "SCALPEL_BLADE_11")
        => new(session, "e1", 1, new DeviationDto { DeviationType = "WRONG_INSTRUMENT", Expected = "SCALPEL_BLADE_10", Actual = actual },
            new PatientProfile());

    [Fact]
    public void Session_limit_stops_further_calls_for_that_session_only()
    {
        var l = Limiter(perSession: 2);
        Assert.Null(l.TryAcquire("s1")); l.Release();
        Assert.Null(l.TryAcquire("s1")); l.Release();
        Assert.Equal("seans sınırı", l.TryAcquire("s1"));
        Assert.Null(l.TryAcquire("s2"));
    }

    [Fact]
    public void Minute_limit_is_global()
    {
        var l = Limiter(perMinute: 3);
        for (var i = 0; i < 3; i++) { Assert.Null(l.TryAcquire($"s{i}")); l.Release(); }
        Assert.Equal("dakika sınırı", l.TryAcquire("s9"));
    }

    [Fact]
    public void Concurrent_calls_do_not_queue()
    {
        var l = Limiter(concurrent: 1);
        Assert.Null(l.TryAcquire("a"));
        Assert.Equal("eşzamanlı çağrı sınırı", l.TryAcquire("b"));
        l.Release();
        Assert.Null(l.TryAcquire("b"));
    }

    [Fact]
    public void Same_error_in_the_same_session_reuses_only_ai_answers()
    {
        var l = Limiter();
        l.Remember(Req("s1"), new AiFeedbackResult { Source = "table" });
        Assert.Null(l.Cached(Req("s1")));
        var ai = new AiFeedbackResult { Source = "llm", Explanation = "x" };
        l.Remember(Req("s1"), ai);
        Assert.Same(ai, l.Cached(Req("s1")));
        Assert.Null(l.Cached(Req("s2")));                         // başka seans
        Assert.Null(l.Cached(Req("s1", "SCALPEL_BLADE_12")));    // başka hata
    }
}
