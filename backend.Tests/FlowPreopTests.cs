using System.Text.Json;
using Arqenion.Api.Models;
using Arqenion.Api.Services;
using Xunit.Abstractions;

namespace Arqenion.Api.Tests;

// Ameliyat öncesi bölümler (PRE01–PRE09): kontrol listesi adımları.
public sealed class FlowPreopTests
{
    private readonly ITestOutputHelper _out;
    public FlowPreopTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Every_preop_section_has_a_clean_checklist()
    {
        var all = TestModel.Model.Checklists;
        Assert.Equal(9, all.Count);
        foreach (var (section, items) in all)
        {
            _out.WriteLine($"{section}: {string.Join(" | ", items.Select(i => $"{i.Id}{(i.Optional ? "?" : "")} {i.Name}"))}");
            Assert.NotEmpty(items);
            Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
            foreach (var i in items)
            {
                Assert.DoesNotContain(i.Task ?? "\u0000", i.Risks);   // görev metni risk listesine karışmamalı
                Assert.False(i.Name == i.Name.ToUpper(new System.Globalization.CultureInfo("tr-TR")), $"başlık madde olmuş: {i.Name}");
                Assert.False(char.IsLower(i.Name[0]) && !i.Name.EndsWith('.'), $"bölünmüş satır: {i.Name}");
            }
        }
        Assert.True(all["PRE01"].Single(i => i.Id == "PRE01.3").Optional);   // taraf işaretlemesi: koşullu
        Assert.Contains(all["PRE01"], i => i.Risks.Any(r => r.Contains("onam", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Full_case_starts_with_the_preop_checklists_then_the_60_steps()
    {
        var (session, service) = TestModel.StartSession("case-full-01");
        Assert.Equal("PRE01", session.CurrentNodeId);
        var view = service.View("PRE01", FlowProcedureService.Load(session))!;
        Assert.Equal("checklist", view.Kind);
        Assert.Equal("checklist_confirmed", view.ExpectedEvent);
        Assert.Equal(4, view.Checklist!.Count);

        var path = TestModel.PlayCorrectly(session, service);
        Assert.Equal(new[] { "PRE01", "PRE02", "PRE03", "PRE04", "PRE05", "PRE06", "PRE07", "PRE08", "PRE09", "1", "2" }, path.Take(11));
        Assert.Equal(65, path.Count);
        Assert.Equal(100, session.Score);
    }

    [Fact]
    public void A_skipped_checklist_item_is_an_error_explained_with_its_risk()
    {
        var (session, service) = TestModel.StartSession("case-full-01");
        var o = service.Validate(session, new EventRequest { EventType = "checklist_confirmed", CheckedItems = new() { "PRE01.1", "PRE01.2" } });
        Assert.Equal("CHECKLIST_INCOMPLETE", o.Deviation!.DeviationType);
        Assert.Equal("PRE01.4", o.Deviation.Expected);
        Assert.Equal("PRE01", o.NextNodeId);
        Assert.Equal(-10, o.ScoreChange);
        var entry = TestModel.Text().Entry("PRE01", "CHECKLIST_INCOMPLETE", null, new[] { "PRE01.4" });
        Assert.Contains("Hasta formlarının kontrolü", entry.Explanation);
        Assert.Contains("onam", entry.PossibleRisk, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(entry.RecommendedAction, entry.PossibleRisk);   // risk, görev metninin tekrarı değil

        // Koşullu madde (taraf işaretlemesi) zorunlu değil.
        Assert.True(service.Validate(session, new EventRequest { EventType = "checklist_confirmed",
            CheckedItems = new() { "PRE01.1", "PRE01.2", "PRE01.4" } }).Success);
        Assert.Equal("PRE02", session.CurrentNodeId);
    }

    [Fact]
    public void Delivering_an_instrument_during_preop_is_wrong_order()
    {
        var (session, service) = TestModel.StartSession("case-full-01");
        Assert.Equal("WRONG_ORDER", service.Validate(session, TestModel.Deliver("SCALPEL_BLADE_10")).Deviation?.DeviationType);
    }

    [Fact]
    public void Contract_requires_known_checklist_items()
    {
        var service = TestModel.Service();
        Assert.Equal("checkedItems", service.ContractError(new EventRequest { EventType = "checklist_confirmed" })?.Field);
        Assert.Equal("checkedItems", service.ContractError(new EventRequest { EventType = "checklist_confirmed", CheckedItems = new() { "PRE99.1" } })?.Field);
        Assert.Null(service.ContractError(new EventRequest { EventType = "checklist_confirmed", CheckedItems = new() { "PRE01.1" } }));
    }
}
