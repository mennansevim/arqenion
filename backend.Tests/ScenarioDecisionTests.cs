using System.Text.Json;
using Arqenion.Api.Models;
using Arqenion.Api.Services;

namespace Arqenion.Api.Tests;

// Klinik ekibin onayladığı hata senaryoları ↔ motor kararı. "İstenen A yerine B" başlığında motor B'yi reddetmeli, A'yı kabul etmeli.
// Kodlar senaryo metninden elle eşlendi; hekim tercihine bağlı olanlarda cerrahın istemi senaryodaki A'ya sabitlenir.
public sealed class ScenarioDecisionTests
{
    // (senaryo, düğüm, istenen/kabul, verilen/ret, iplik mi, hasta parametreleri)
    public static IEnumerable<object[]> Cases() => new[]
    {
        C("S01-E1", "1", "SCALPEL_BLADE_10", "SCALPEL_BLADE_11"),
        C("S01-E2", "1", "SCALPEL_BLADE_15", "ESU_MONOPOLAR_PEN"),
        C("S03-E1", "3", "FORCEPS_STILLE_NON_TOOTHED_15", "FORCEPS_TOOTHED_15"),
        C("S04-E1", "4", "RETRACTOR_FARABEUF_15_SET", "RETRACTOR_RICHARDSON"),
        C("S05-E1", "5", "SCALPEL_BLADE_15", "SCALPEL_BLADE_11"),
        C("S05-E2", "5", "SCALPEL_BLADE_15", "SCALPEL_BLADE_10"),
        C("S06-E1", "6", "CLAMP_CRILE_STRAIGHT_14", "SCISSORS_METZENBAUM"),
        C("S06-E2", "6", "CLAMP_CRILE_STRAIGHT_14", "ESU_MONOPOLAR_PEN"),
        C("S06-E3", "6", "CLAMP_CRILE_STRAIGHT_14", "CLAMP_CRILE_CURVED_14"),
        C("S07-E2", "7", "CLAMP_CRILE_CURVED_14", "CLAMP_CRILE_STRAIGHT_14"),
        C("S08-E2", "8", "CLAMP_CRILE_CURVED_14", "CLAMP_CRILE_STRAIGHT_14"),
        C("S08-E3", "8", "CLAMP_CRILE_CURVED_14", "CLAMP_OCHSNER_KOCHER_CURVED_20"),
        C("S09-E1", "9", "SCALPEL_BLADE_15", "SCALPEL_BLADE_11"),
        C("S09-E2", "9", "SCALPEL_BLADE_15", "SCALPEL_BLADE_10"),
        C("S10-E1", "10", "SCISSORS_METZENBAUM", "SCISSORS_MAYO"),
        C("S10-E2", "10", "SCISSORS_METZENBAUM", "ESU_MONOPOLAR_PEN"),
        C("S10-E3", "10", "SCISSORS_METZENBAUM", "SCALPEL_BLADE_15"),
        C("S11-E1", "11", "RETRACTOR_FARABEUF_15_SET", "RETRACTOR_ROUX_14"),
        C("S11-E2", "11", "RETRACTOR_FARABEUF_15_SET", "RETRACTOR_LANGENBECK"),
        C("S12-E1", "12", "RETRACTOR_ROUX_14", "RETRACTOR_ROUX_15"),
        C("S12-E2", "12", "RETRACTOR_ROUX_15", "RETRACTOR_ROUX_14"),
        C("S12-E3", "12", "RETRACTOR_ROUX_14", "RETRACTOR_LANGENBECK"),
        C("S13-E1", "13", null, "RETRACTOR_ROUX_14"),
        C("S15-E1", "15", "CLAMP_BABCOCK_16", "CLAMP_ALLIS_15"),
        C("S18-E1", "18", null, "CLAMP_ALLIS_15"),
        C("S20-E1", "20", "CLAMP_GEMINI_CURVED_20", "CLAMP_OVERHOLT_GEISSENDOERFER_21"),
        C("S21-E1", "21", "SCISSORS_METZENBAUM_CURVED", "SCISSORS_MAYO", explained: false),
        C("S21-E1", "21", "SCISSORS_METZENBAUM_CURVED", "ESU_MONOPOLAR_PEN", explained: false),   // istem: makas; koter uzatıldı
        C("S21-E1", "21", "ESU_MONOPOLAR_PEN", "SCISSORS_METZENBAUM_CURVED", explained: false),   // istem: koter; makas uzatıldı
        C("S23-E1", "23", "CLAMP_CRILE_CURVED_14", "CLAMP_CRILE_STRAIGHT_14"),
        C("S24-E2", "24", "SCISSORS_METZENBAUM_CURVED", "SCISSORS_MAYO"),
        C("S26-E1", "26", "SUTURE_SILK_3_0", "SUTURE_POLYGLACTIN_3_0", suture: true),
        C("S27-E1", "27", "NEEDLE_HOLDER_DEBAKEY_18", "FORCEPS_TOOTHED_15"),
        C("S28-E1", "28", "SUTURE_POLYGLACTIN_2_0", "SUTURE_SILK_3_0", suture: true),
        C("S30-E1", "30", "CLAMP_CRILE_STRAIGHT_14", "CLAMP_CRILE_CURVED_14"),
        C("S30-E2", "30", "CLAMP_CRILE_STRAIGHT_14", "CLAMP_OCHSNER_KOCHER_CURVED_20"),
        C("S31-E1", "31", "SCALPEL_BLADE_15", "SCALPEL_BLADE_12"),
        C("S32-E1", "32", null, "SCISSORS_METZENBAUM_CURVED"),
        C("S34-E2", "34", "SCALPEL_BLADE_15", "SCALPEL_BLADE_10"),
        C("S35-E1", "35", null, "NEEDLE_HOLDER_DEBAKEY_18"),
        C("S36-E2", "36", "CLAMP_CRILE_STRAIGHT_14", "CLAMP_ALLIS_15"),
        C("S37-E2", "37", null, "NEEDLE_HOLDER_DEBAKEY_18"),
        C("S41-E1", "41", "CLAMP_MOSQUITO", "ESU_MONOPOLAR_PEN"),
        C("S41-E2", "41", "CLAMP_PEAN", "CLAMP_CRILE_STRAIGHT_14"),
        C("S42-E1", "42", "ISOTONIC_SALINE", "CHLORHEXIDINE_SOLUTION"),
        C("S48-E1", "48", "CLAMP_CRILE_CURVED_14", "CLAMP_ALLIS_15"),
        C("S49-E1", "49", "SUTURE_POLYGLACTIN_1", "SUTURE_POLYGLACTIN_2_0", suture: true),
        C("S49-E2", "49", "SUTURE_POLYGLACTIN_1", "SUTURE_POLYPROPYLENE_0", suture: true),
        C("S50-E3", "50", "SUTURE_POLYGLACTIN_1", "SUTURE_POLYGLACTIN_2_0", suture: true),
        C("S50-E6", "50", "SUTURE_POLYGLACTIN_0", "SUTURE_POLYPROPYLENE_0", suture: true),
        C("S50-E7", "50", "SUTURE_POLYGLACTIN_0", "SUTURE_POLYGLACTIN_RAPIDE", suture: true),
        C("S51-E1", "51", "CLAMP_ALLIS_ATRAUMATIC_19", "CLAMP_OCHSNER_KOCHER_CURVED_20"),
        C("S51-E2", "51", "CLAMP_ALLIS_ATRAUMATIC_19", "CLAMP_ALLIS_15"),
        C("S52-E2", "52", "SUTURE_POLYGLACTIN_0", "SUTURE_POLYGLACTIN_2_0", suture: true),
        C("S52-E6", "52", "SUTURE_POLYGLACTIN_0", "SUTURE_POLYPROPYLENE_0", suture: true),
        C("S52-E7", "52", "SUTURE_POLYGLACTIN_0", "SUTURE_POLYGLACTIN_RAPIDE", suture: true),
        C("S53-E1", "53", "ISOTONIC_SALINE", "CHLORHEXIDINE_SOLUTION"),
        C("S54-E1", "54", "SUTURE_POLYGLACTIN_2_0", "SUTURE_POLYPROPYLENE_2_0", suture: true),
        C("S54-E2", "54", "SUTURE_POLYGLACTIN_2_0", "SUTURE_POLYGLACTIN_0", suture: true),
        C("S55-E2", "55", "FORCEPS_STILLE_NON_TOOTHED_15", "FORCEPS_TOOTHED_15"),
        C("S56-E2", "56", "SUTURE_POLYPROPYLENE_4_0", "SUTURE_POLYPROPYLENE_2_0", suture: true),
        C("S56-E5", "56", "SUTURE_POLIGLECAPRONE_4_0", "SUTURE_POLYPROPYLENE_4_0", suture: true, closure: "AESTHETIC"),
        C("S56-E6", "56", "SUTURE_POLYAMIDE_4_0", "SUTURE_POLIGLECAPRONE_4_0", suture: true),
        C("S59-E1", "59", "TAPE_SILK", "TAPE_WATERPROOF", explained: false),
    };

    // explained: false → başlık aleti adıyla anmıyor ("farklı ayırma aracı", "farklı ürün"); kart cerrah istemi/genel metni gösterir.
    private static object[] C(string id, string node, string? wanted, string given, bool suture = false, string closure = "STANDARD",
        bool explained = true)
        => new object[] { id, node, wanted!, given, suture, closure, explained };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Engine_rejects_the_approved_error_and_accepts_the_requested_choice(
        string scenarioId, string node, string? wanted, string given, bool suture, string closure, bool explained)
    {
        _ = explained;   // açıklama ayrı testte denetlenir
        var model = TestModel.Model;
        Assert.True(model.Catalog.ContainsKey(given), $"{scenarioId}: {given} tepside yok, hata yapılamaz");

        var wrong = Play(node, wanted, closure, given, suture);
        Assert.False(wrong.Success, $"{scenarioId}: {given} kabul edildi");
        Assert.Equal("WRONG_INSTRUMENT", wrong.Deviation!.DeviationType);

        if (wanted is null) return;
        var right = Play(node, wanted, closure, wanted, suture);
        Assert.True(right.Success, $"{scenarioId}: istenen {wanted} reddedildi: {right.Message}");
    }

    // Hata yapıldığında klinik kart tam o onaylı senaryoyu anlatmalı (yanlış senaryo = yanlış eğitim).
    [Theory]
    [MemberData(nameof(Cases))]
    public void The_error_is_explained_with_its_own_approved_scenario(
        string scenarioId, string node, string? wanted, string given, bool suture, string closure, bool explained)
    {
        _ = suture; _ = closure;
        var wantedList = wanted is null ? null : new[] { wanted };
        var matched = TestModel.Text().Match(node, given, wantedList);
        if (explained)
        {
            Assert.True(matched?.Id == scenarioId, $"{scenarioId}: {given} → {matched?.Id ?? "senaryo yok"} ({matched?.Title})");
            return;
        }
        // Başlık aleti anmıyor: yanlış bir senaryo da seçilmemeli; kart istemi ya da genel metni gösterir.
        Assert.Null(matched);
        Assert.False(string.IsNullOrWhiteSpace(TestModel.Text().Entry(node, "WRONG_INSTRUMENT", given, wantedList).Explanation));
    }

    [Fact]
    public void Approved_scenario_explains_the_matching_error()
    {
        // Yanıltıcı aletle yapılan hata, kendi onaylı senaryosuyla açıklanır.
        var text = TestModel.Text();
        Assert.Equal("S01-E1", text.Match("1", "SCALPEL_BLADE_11", new[] { "SCALPEL_BLADE_10" })?.Id);
        Assert.Equal("S20-E1", text.Match("20", "CLAMP_OVERHOLT_GEISSENDOERFER_21", new[] { "CLAMP_GEMINI_CURVED_20" })?.Id);
        Assert.Equal("S51-E1", text.Match("51", "CLAMP_OCHSNER_KOCHER_CURVED_20", new[] { "CLAMP_ALLIS_ATRAUMATIC_19" })?.Id);
        Assert.Equal("S51-E2", text.Match("51", "CLAMP_ALLIS_15", new[] { "CLAMP_ALLIS_ATRAUMATIC_19" })?.Id);
        Assert.Equal("S56-E2", text.Match("56", "SUTURE_POLYPROPYLENE_2_0", new[] { "SUTURE_POLYPROPYLENE_4_0" })?.Id);
        Assert.Equal("S26-E1", text.Match("26", "SUTURE_POLYGLACTIN_3_0", new[] { "SUTURE_SILK_3_0" })?.Id);   // "yanlış sütür"
        Assert.Equal("S42-E1", text.Match("42", "CHLORHEXIDINE_SOLUTION", new[] { "ISOTONIC_SALINE" })?.Id); // "yanlış solüsyon"
    }

    [Fact]
    public void Numbers_in_the_scenario_title_must_match_the_given_instrument()
    {
        // No:15 verildiğinde "11 numaralı bistüri" senaryosu anlatılmamalı; cerrah istemi metni gelir.
        var text = TestModel.Text();
        Assert.Null(text.Match("1", "SCALPEL_BLADE_15", new[] { "SCALPEL_BLADE_10" }));
        var entry = text.Entry("1", "WRONG_INSTRUMENT", "SCALPEL_BLADE_15", new[] { "SCALPEL_BLADE_10" });
        Assert.Contains("Cerrah No:10 bistüri ucu istedi", entry.Explanation);
        Assert.DoesNotContain("11", entry.Explanation);
    }

    [Fact]
    public void Requests_are_fixed_per_session_and_can_be_turned_off()
    {
        var a = TestModel.StartSession("case-basic-01");
        var reqA = FlowProcedureService.Load(a.Session).Requests;
        Assert.Contains("41", reqA.Keys);
        Assert.Contains("51", reqA.Keys);
        Assert.DoesNotContain("23", reqA.Keys);   // klemp adedi damara bağlı adımda istem yok
        Assert.Contains("21", reqA.Keys);         // modeldeki SEPARATION_TOOL varyantı: makas ya da koter
        Assert.DoesNotContain("52", reqA.Keys);   // portegü + penset birlikte kullanılır, alternatif değil
        Assert.DoesNotContain("3", reqA.Keys);    // Stille / Semken eşdeğer dişsiz penset: ikisi de doğru
        Assert.DoesNotContain("36", reqA.Keys);   // Crile düz / Kelly-Rankin eşdeğer
        Assert.Equal(new[] { "1", "4", "8", "12", "21", "41", "48", "49", "50", "51", "56", "59" }.Where(TestModel.Model.PlayableNodes.Select(n => n.Id).Contains).Where(reqA.ContainsKey).Count(),
            reqA.Keys.Count);

        var off = new Scenario { ScenarioId = "off", SurgeonRequestMode = "off" };
        var session = new SessionRecord { Score = 100, RawScore = 100 };
        TestModel.Service().Start(session, off);
        Assert.Empty(FlowProcedureService.Load(session).Requests);
    }

    [Theory]
    [InlineData("8", "FORCEPS_STILLE_NON_TOOTHED_15", "FORCEPS_DEBAKEY")]      // aynı seçenek: iki dişsiz penset
    [InlineData("51", "FORCEPS_STILLE_NON_TOOTHED_15", "FORCEPS_SEMKEN_NON_TOOTHED_15_5")]
    [InlineData("41", "CLAMP_PEAN", "CLAMP_ROCHESTER_PEAN_CURVED_14")]       // Pean ve Rochester-Pean aynı tip
    public void Equivalent_instruments_of_the_requested_option_are_all_correct(string node, string requested, string equivalent)
        => Assert.True(Play(node, requested, "STANDARD", equivalent, suture: false).Success);

    [Theory]
    [InlineData("3", "FORCEPS_SEMKEN_NON_TOOTHED_15_5")]
    [InlineData("36", "CLAMP_KELLY_RANKIN_14")]
    public void Steps_without_a_surgeon_choice_accept_every_alternative(string node, string code)
        => Assert.True(Play(node, null, "STANDARD", code, suture: false).Success);

    private static ValidationOutcome Play(string node, string? wanted, string closure, string code, bool suture)
    {
        var scenario = new Scenario
        {
            ScenarioId = "decision",
            Conditions = new() { ["EXPOSURE_INSUFFICIENT"] = JsonSerializer.SerializeToElement(true),
                                 ["ADHESIONS_PRESENT"] = JsonSerializer.SerializeToElement(true),
                                 ["BLEEDING_DETECTED"] = JsonSerializer.SerializeToElement(true) },
            Parameters = new() { ["SKIN_CLOSURE_TYPE"] = JsonSerializer.SerializeToElement(closure) },
            SurgeonRequests = wanted is null ? new() : new() { [node] = wanted }
        };
        var session = new SessionRecord { Score = 100, RawScore = 100 };
        var service = TestModel.Service();
        service.Start(session, scenario);
        for (var guard = 0; session.CurrentNodeId != node; guard++)
        {
            Assert.True(guard < 200 && session.CurrentNodeId is not null, $"{node} düğümüne ulaşılamadı");
            foreach (var ev in TestModel.CorrectEvents(session).ToList())
                Assert.True(service.Validate(session, ev).Success);
        }

        var state = FlowProcedureService.Load(session);
        var (deliverable, sutures, _) = service.Allowed(node, TestModel.Model.Expect(node, state.Parameters), state);
        var x = TestModel.Model.Expect(node, state.Parameters);
        if (x.Kind != "delivery") return service.Validate(session, TestModel.Deliver(code));
        return suture
            ? service.Validate(session, TestModel.Deliver(deliverable.FirstOrDefault(), code))
            : service.Validate(session, TestModel.Deliver(code, sutures.FirstOrDefault()));
    }
}
