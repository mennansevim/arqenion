using System.Text.Json;
using Arqenion.Api.Models;
using Arqenion.Api.Services;

namespace Arqenion.Api.Tests;

// Kursiyerin hatasına göre dal açılması (Procedures/adaptive-rules.json).
public sealed class FlowAdaptiveTests
{
    private static (SessionRecord Session, FlowProcedureService Service) Start(string mode = "errors",
        Dictionary<string, JsonElement>? conditions = null)
    {
        var session = new SessionRecord { Id = "adaptive-test", Score = 100, RawScore = 100 };
        var service = TestModel.Service();
        service.Start(session, new Scenario
        {
            ScenarioId = "adaptive", AdaptiveMode = mode, SurgeonRequestMode = "off",
            Conditions = conditions ?? new()
        });
        return (session, service);
    }

    private static void AdvanceTo(SessionRecord session, FlowProcedureService service, string nodeId)
    {
        for (var guard = 0; session.CurrentNodeId != nodeId; guard++)
        {
            Assert.True(guard < 200 && session.CurrentNodeId is not null, $"{nodeId} düğümüne ulaşılamadı");
            foreach (var ev in TestModel.CorrectEvents(session).ToList())
                Assert.True(service.Validate(session, ev).Success);
        }
    }

    [Fact]
    public void Wrong_blade_at_incision_opens_the_bleeding_branch_at_step_40()
    {
        var (session, service) = Start();
        var o = service.Validate(session, TestModel.Deliver("SCALPEL_BLADE_11"));   // S01-E1: "kontrolsüz kanama"
        var trigger = Assert.Single(o.Adaptive!);
        Assert.Equal("BLEEDING_DETECTED", trigger.Condition);
        Assert.Equal("40", trigger.EvaluatedAt);
        Assert.Equal("S01-E1", trigger.ScenarioId);
        Assert.Equal("1", o.NextNodeId);                                            // dal hemen değil, karar adımında açılır

        var path = TestModel.PlayCorrectly(session, service);
        Assert.Equal(new[] { "40", "41", "42" }, path.SkipWhile(n => n != "40").Take(3));
        Assert.Equal(1, session.ComplicationsTriggered);
        Assert.Equal(1, session.ComplicationsResolved);
    }

    [Fact]
    public void Exam_mode_keeps_the_scenario_plan()
    {
        var (session, service) = Start(mode: "off");
        Assert.Null(service.Validate(session, TestModel.Deliver("SCALPEL_BLADE_11")).Adaptive);
        Assert.DoesNotContain("41", TestModel.PlayCorrectly(session, service));
    }

    [Fact]
    public void A_condition_fixed_by_the_scenario_is_not_overridden()
    {
        var (session, service) = Start(conditions: new() { ["BLEEDING_DETECTED"] = JsonSerializer.SerializeToElement(false) });
        Assert.Null(service.Validate(session, TestModel.Deliver("SCALPEL_BLADE_11")).Adaptive);
        Assert.DoesNotContain("41", TestModel.PlayCorrectly(session, service));
    }

    [Fact]
    public void Wrong_ligature_renews_the_stump_ligature_once()
    {
        var (session, service) = Start();
        AdvanceTo(session, service, "28");
        var o = service.Validate(session, TestModel.Deliver("NEEDLE_HOLDER_DEBAKEY_18", "SUTURE_SILK_3_0"));   // S28-E1: "bağın güvenliği"
        Assert.Contains(o.Adaptive!, t => t.Condition == "LIGATURE_NOT_SECURE");

        var path = TestModel.PlayCorrectly(session, service);
        Assert.Equal(new[] { "28", "29", "28", "29", "30" }, path.SkipWhile(n => n != "28").Take(5));
    }

    [Fact]
    public void An_error_after_the_decision_point_does_not_change_the_past()
    {
        var (session, service) = Start(conditions: new() { ["LIGATURE_NOT_SECURE"] = JsonSerializer.SerializeToElement(true) });
        AdvanceTo(session, service, "29");
        TestModel.CorrectEvents(session).ToList().ForEach(ev => service.Validate(session, ev));   // 29 → 28 (yenileme)
        Assert.Equal("28", session.CurrentNodeId);
        // Koşul senaryoda sabit; ikinci kez 28'de hata yeniden dal açmaz, döngü sınırı korunur.
        Assert.Null(service.Validate(session, TestModel.Deliver("NEEDLE_HOLDER_DEBAKEY_18", "SUTURE_SILK_3_0")).Adaptive);
        var path = TestModel.PlayCorrectly(session, service);
        Assert.Equal(2, path.Count(n => n == "29"));
    }

    [Fact]
    public void Poor_retraction_opens_the_extra_retraction_branch()
    {
        var (session, service) = Start();
        AdvanceTo(session, service, "11");
        var o = service.Validate(session, TestModel.Deliver("RETRACTOR_LANGENBECK"));   // S11-E2
        Assert.Contains(o.Adaptive!, t => t.Condition == "EXPOSURE_INSUFFICIENT");
        Assert.Equal(new[] { "11", "12", "13" }, TestModel.PlayCorrectly(session, service).SkipWhile(n => n != "11").Take(3));
    }

    [Fact]
    public void Sterile_violation_records_infection_risk_without_changing_the_flow()
    {
        var (session, service) = Start();
        var o = service.Validate(session, new EventRequest { EventType = "sterile_area_violated" });
        var trigger = Assert.Single(o.Adaptive!);
        Assert.Equal("SURGICAL_SITE_INFECTION", trigger.Condition);
        Assert.True(trigger.Outcome);
        var path = TestModel.PlayCorrectly(session, service);
        Assert.Equal(56, path.Count);
        Assert.Single(FlowProcedureService.Load(session).AdaptiveDecisions);
    }

    [Fact]
    public void Each_condition_is_triggered_at_most_once()
    {
        var (session, service) = Start();
        Assert.NotNull(service.Validate(session, TestModel.Deliver("SCALPEL_BLADE_11")).Adaptive);
        Assert.Null(service.Validate(session, TestModel.Deliver("SCALPEL_BLADE_11")).Adaptive);
    }
}
