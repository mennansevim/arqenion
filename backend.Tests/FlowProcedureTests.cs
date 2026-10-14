using Arqenion.Api.Models;
using Arqenion.Api.Services;

namespace Arqenion.Api.Tests;

// 60 adımlık modelin olay doğrulaması: senaryolar baştan sona, dallar ve hata türleri.
public sealed class FlowProcedureTests
{
    public static IEnumerable<object[]> Scenarios()
        => new[] { "case-basic-01", "case-adhesion-01", "case-bleeding-01", "case-count-01", "case-obese-01" }
            .Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Every_scenario_can_be_played_to_the_end_without_errors(string scenarioId)
    {
        var (session, service) = TestModel.StartSession(scenarioId);
        var branches = new List<FlowBranchDto>();
        var path = TestModel.PlayCorrectly(session, service, o => { if (o.Branch is not null) branches.Add(o.Branch); });

        Assert.Null(session.CurrentNodeId);
        Assert.Equal(0, session.WrongActions);
        Assert.Equal(100, session.Score);
        Assert.Equal("60", path[^1]);
        Assert.Equal(session.ComplicationsTriggered, session.ComplicationsResolved);
        Assert.Equal(session.ComplicationsTriggered, branches.Count(b => !b.Resolved));
    }

    [Fact]
    public void Service_follows_the_same_path_as_the_engine()
    {
        var (session, service) = TestModel.StartSession("case-bleeding-01");
        var path = TestModel.PlayCorrectly(session, service);
        var i = path.IndexOf("40");
        Assert.Equal(new[] { "40", "41", "42", "43", "43a", "42", "43", "44" }, path.Skip(i).Take(8));
        Assert.Equal(2, session.ComplicationsTriggered);
    }

    [Fact]
    public void Basic_case_has_no_branches_and_walks_56_steps()
    {
        var (session, service) = TestModel.StartSession("case-basic-01");
        var path = TestModel.PlayCorrectly(session, service);
        Assert.Equal(56, path.Count);
        Assert.DoesNotContain("41", path);
        Assert.Equal(0, session.ComplicationsTriggered);
    }

    [Fact]
    public void Adhesion_case_enters_and_leaves_the_adhesion_branch()
    {
        var (session, service) = TestModel.StartSession("case-adhesion-01");
        AdvanceTo(session, service, "19");
        var o = service.Validate(session, new EventRequest { EventType = "step_confirmed" });
        Assert.Equal("20", o.NextNodeId);
        Assert.Equal("ADHESIONS_PRESENT", o.Branch?.Condition);
        Assert.True(o.NextStep!.IsBranch);
        Assert.Equal("ADHESIONS_PRESENT", o.NextStep.Condition);
    }

    [Fact]
    public void Wrong_instrument_stays_on_the_step_and_explains_with_the_approved_scenario()
    {
        var (session, service) = TestModel.StartSession("case-basic-01");
        var o = service.Validate(session, TestModel.Deliver("ESU_MONOPOLAR_PEN"));
        Assert.False(o.Success);
        Assert.Equal("WRONG_INSTRUMENT", o.Deviation!.DeviationType);
        Assert.Equal("1", o.NextNodeId);
        Assert.Equal(92, session.Score);

        var text = TestModel.Text().Match("1", "ESU_MONOPOLAR_PEN", new[] { "SCALPEL_BLADE_15" });
        Assert.Equal("S01-E2", text?.Id);
    }

    [Fact]
    public void Consecutive_errors_escalate_penalty_and_severity()
    {
        var (session, service) = TestModel.StartSession("case-basic-01");
        var first = service.Validate(session, TestModel.Deliver("ESU_MONOPOLAR_PEN"));
        var second = service.Validate(session, TestModel.Deliver("ESU_MONOPOLAR_PEN"));
        Assert.Equal(-8, first.ScoreChange);
        Assert.Equal(-11, second.ScoreChange);
        Assert.NotEqual(first.Deviation!.Severity, second.Deviation!.Severity);
    }

    [Fact]
    public void Delivering_on_a_surgeon_only_step_is_an_error()
    {
        var (session, service) = TestModel.StartSession("case-basic-01");
        AdvanceTo(session, service, "35");
        var o = service.Validate(session, TestModel.Deliver("NEEDLE_HOLDER_DEBAKEY_18"));
        Assert.Equal("WRONG_INSTRUMENT", o.Deviation!.DeviationType);
        Assert.Equal("S35-E1", TestModel.Text().Match("35", "NEEDLE_HOLDER_DEBAKEY_18", null)?.Id);
    }

    [Fact]
    public void Confirming_a_delivery_step_is_wrong_order()
    {
        var (session, service) = TestModel.StartSession("case-basic-01");
        var o = service.Validate(session, new EventRequest { EventType = "step_confirmed" });
        Assert.Equal("WRONG_ORDER", o.Deviation!.DeviationType);
    }

    [Fact]
    public void Fascia_requires_0_vicryl_not_prolene()
    {
        var (session, service) = TestModel.StartSession("case-basic-01");
        AdvanceTo(session, service, "52");
        var wrong = service.Validate(session, TestModel.Deliver("NEEDLE_HOLDER_DEBAKEY_18", "SUTURE_POLYPROPYLENE_0"));
        Assert.Equal("WRONG_INSTRUMENT", wrong.Deviation!.DeviationType);
        Assert.Equal("SUTURE_POLYGLACTIN_0", wrong.Deviation.Expected);
        Assert.Equal("S52-E6", TestModel.Text().Match("52", "SUTURE_POLYPROPYLENE_0", new[] { "SUTURE_POLYGLACTIN_0" })?.Id);

        var missing = service.Validate(session, TestModel.Deliver("NEEDLE_HOLDER_DEBAKEY_18"));
        Assert.Equal("WRONG_INSTRUMENT", missing.Deviation!.DeviationType);

        var right = service.Validate(session, TestModel.Deliver("NEEDLE_HOLDER_DEBAKEY_18", "SUTURE_POLYGLACTIN_0"));
        Assert.True(right.Success);
        Assert.Equal("53", right.NextNodeId);
    }

    [Fact]
    public void Suture_preparation_accepts_the_suture_alone_or_on_the_needle_holder()
    {
        var (session, service) = TestModel.StartSession("case-basic-01");
        AdvanceTo(session, service, "26");
        Assert.True(service.Validate(session, TestModel.Deliver("SUTURE_SILK_3_0")).Success);

        (session, service) = TestModel.StartSession("case-basic-01");
        AdvanceTo(session, service, "26");
        Assert.True(service.Validate(session, TestModel.Deliver("NEEDLE_HOLDER_DEBAKEY_18", "SUTURE_SILK_3_0")).Success);
    }

    [Fact]
    public void Obese_patient_needs_long_retractors_and_aesthetic_closure()
    {
        var (session, service) = TestModel.StartSession("case-obese-01");
        AdvanceTo(session, service, "11");
        Assert.Equal("WRONG_INSTRUMENT", service.Validate(session, TestModel.Deliver("RETRACTOR_FARABEUF_15_SET")).Deviation?.DeviationType);
        Assert.True(service.Validate(session, TestModel.Deliver("RETRACTOR_FARABEUF_LONG_SET")).Success);

        AdvanceTo(session, service, "56");
        Assert.Equal("WRONG_INSTRUMENT",
            service.Validate(session, TestModel.Deliver("NEEDLE_HOLDER_DEBAKEY_18", "SUTURE_POLYPROPYLENE_3_0")).Deviation?.DeviationType);
        Assert.True(service.Validate(session, TestModel.Deliver("NEEDLE_HOLDER_DEBAKEY_18", "SUTURE_POLIGLECAPRONE_4_0")).Success);
    }

    [Theory]
    [InlineData("case-obese-01", "RETRACTOR_ROUX_14")]
    [InlineData("case-obese-01", "RETRACTOR_ROUX_15")]
    [InlineData("case-obese-01", "RETRACTOR_FARABEUF_15_SET")]
    [InlineData("case-basic-01", "RETRACTOR_ROUX_LONG")]
    [InlineData("case-basic-01", "RETRACTOR_FARABEUF_LONG_SET")]
    public void Step_4_retractor_length_follows_body_type(string scenarioId, string retractor)
    {
        // Q-B2: kilolu hastada 15 cm'den uzun Farabeuf, gerekirse 15 cm'den uzun Roux; 15 cm uzatmak hata.
        var (session, service) = TestModel.StartSession(scenarioId);
        AdvanceTo(session, service, "4");
        var o = service.Validate(session, TestModel.Deliver(retractor));
        Assert.Equal("WRONG_INSTRUMENT", o.Deviation?.DeviationType);
    }

    [Fact]
    public void Clamp_count_follows_the_vessels()
    {
        var (session, service) = TestModel.StartSession("case-obese-01");
        AdvanceTo(session, service, "23");
        var view = service.View("23", FlowProcedureService.Load(session))!;
        Assert.Equal(new[] { "CLAMP_MOSQUITO", "CLAMP_PEAN" }, view.Clamps!.Select(c => c.Code));

        var first = service.Validate(session, TestModel.Deliver("CLAMP_MOSQUITO"));
        Assert.True(first.Success);
        Assert.Equal("23", first.NextNodeId);
        Assert.Equal(new[] { "CLAMP_PEAN" }, first.NextStep!.Clamps!.Select(c => c.Code));

        Assert.Equal("WRONG_INSTRUMENT", service.Validate(session, TestModel.Deliver("CLAMP_CRILE_CURVED_14")).Deviation?.DeviationType);
        Assert.Equal("24", service.Validate(session, TestModel.Deliver("CLAMP_PEAN")).NextNodeId);
    }

    [Fact]
    public void Ligature_is_renewed_once_when_not_secure()
    {
        var (session, service) = TestModel.StartSession("case-obese-01");
        var path = TestModel.PlayCorrectly(session, service);
        var i = path.IndexOf("28");
        Assert.Equal(new[] { "28", "29", "28", "29", "30" }, path.Skip(i).Take(5));
    }

    [Fact]
    public void Count_mismatch_needs_the_real_count_then_opens_the_discrepancy_subflow()
    {
        var (session, service) = TestModel.StartSession("case-count-01");
        AdvanceTo(session, service, "44");
        var view = service.View("44", FlowProcedureService.Load(session))!;
        Assert.Equal(9, view.TableCount);
        Assert.Equal(10, view.RecordedCount);

        var wrong = service.Validate(session, new EventRequest { EventType = "count_confirmed", Counts = new() { ["COMPRESS"] = 10 } });
        Assert.Equal("COUNT_MISMATCH", wrong.Deviation!.DeviationType);
        Assert.True(service.Validate(session, new EventRequest { EventType = "count_confirmed", Counts = new() { ["COMPRESS"] = 9 } }).Success);

        AdvanceTo(session, service, "47");
        var verify = service.Validate(session, new EventRequest { EventType = "step_confirmed" });
        Assert.Equal("47a", verify.NextNodeId);
        Assert.Equal(47, verify.NextStepId);
        Assert.Equal("COUNT_MISMATCH", verify.Branch?.Condition);

        var path = TestModel.PlayCorrectly(session, service);
        Assert.Equal(new[] { "47a", "47b", "47c", "47", "48" }, path.SkipWhile(n => n != "47a").Take(5));
        Assert.Equal(1, session.ComplicationsResolved);
    }

    [Fact]
    public void Errors_stay_in_the_final_score()
    {
        // Kesinti modeli: doğru adımlar puanı geri toplamaz; son puan yapılan hataları gösterir.
        var (session, service) = TestModel.StartSession("case-basic-01");
        Assert.Equal(-8, service.Validate(session, TestModel.Deliver("ESU_MONOPOLAR_PEN")).ScoreChange);
        TestModel.PlayCorrectly(session, service);
        Assert.Equal(92, session.Score);
        Assert.Equal(1, session.WrongActions);
    }

    [Fact]
    public void Hint_costs_points_and_names_what_is_expected()
    {
        var (session, service) = TestModel.StartSession("case-basic-01");
        var o = service.Validate(session, new EventRequest { EventType = "hint_requested" });
        Assert.Equal(-2, o.ScoreChange);
        var requested = TestModel.Model.InstrumentName(o.NextStep!.AllowedInstruments.Single());
        Assert.Contains($"Cerrah istiyor: {requested}", o.Message);
        Assert.Equal(o.NextStep.SurgeonRequest, requested);
    }

    [Fact]
    public void Every_playable_node_has_an_instruction()
    {
        var state = new FlowState { Parameters = TestModel.Model.DefaultParameters() };
        var service = TestModel.Service();
        foreach (var n in TestModel.Model.PlayableNodes)
        {
            var v = service.View(n.Id, state)!;
            Assert.False(string.IsNullOrWhiteSpace(v.Instruction), n.Id);
            Assert.Contains(v.Kind, new[] { "delivery", "confirm", "count", "checklist" });
        }
        Assert.Equal(60, TestModel.Model.MainStepCount);
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
}
