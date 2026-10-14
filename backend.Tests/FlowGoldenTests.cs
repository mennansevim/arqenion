using System.Text.Json;
using Arqenion.Api.Models;
using Arqenion.Api.Services;

namespace Arqenion.Api.Tests;

// C# motoru, Python motorunun (procedure-model/tools/flow_engine.py) sonucunu birebir üretmeli.
// Golden dosyası: procedure-model/dist/open-appendectomy-v2.4.golden.json (make_golden.py / build.sh üretir).
public sealed class FlowGoldenTests
{
    private static readonly JsonElement Golden =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(TestModel.Dist, "open-appendectomy-v2.4.golden.json"))).RootElement;

    public static IEnumerable<object[]> Cases()
        => Golden.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("id").GetString()! });

    [Fact]
    public void Golden_file_matches_the_loaded_model()
        => Assert.Equal(TestModel.Model.Version, Golden.GetProperty("procedureVersion").GetString());

    [Theory]
    [MemberData(nameof(Cases))]
    public void Engine_walks_the_same_path_with_the_same_instruments(string caseId)
    {
        var c = Golden.GetProperty("cases").EnumerateArray().First(x => x.GetProperty("id").GetString() == caseId);
        var conditions = c.GetProperty("conditions").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(v => v.GetBoolean()).ToList());
        var parameters = c.GetProperty("parameters").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var expected = c.GetProperty("expectedInstruments");

        var model = TestModel.Model;
        var engine = new FlowEngine(model);
        var includePreop = c.TryGetProperty("includePreop", out var ip) && ip.GetBoolean();
        var state = engine.Start(conditions, parameters, includePreop);
        var node = state.Path[0];
        for (var guard = 0; node is not null; guard++)
        {
            Assert.True(guard < 500, "akış bitmedi");
            if (expected.TryGetProperty(node, out var golden))   // ameliyat öncesi düğümlerin alet listesi yok
                AssertSameInstruments(caseId, node, golden, model.Expect(node, state.Parameters));
            node = engine.Advance(state, node).Next;
        }

        var goldenPath = c.GetProperty("path").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(goldenPath, state.Path);
    }

    private static void AssertSameInstruments(string caseId, string node, JsonElement golden, NodeExpectation actual)
    {
        var where = $"{caseId} / düğüm {node}";
        Assert.True(golden.GetProperty("accepted").EnumerateArray().Select(x => x.GetString()).SequenceEqual(actual.Accepted),
            $"{where}: accepted farklı");

        if (golden.TryGetProperty("byParameter", out var bp))
        {
            Assert.True(actual.ByParameter is not null, $"{where}: byParameter eksik");
            var (parameter, value, instruments) = actual.ByParameter!.Value;
            Assert.Equal(bp.GetProperty("parameter").GetString(), parameter);
            Assert.Equal(bp.GetProperty("value").GetString(), value);
            Assert.Equal(bp.GetProperty("instruments").EnumerateArray().Select(x => x.GetString()!), instruments);
        }
        else Assert.True(actual.ByParameter is null, $"{where}: fazladan byParameter");

        var clamps = golden.TryGetProperty("clamps", out var cl)
            ? cl.EnumerateArray().Select(x => (x.GetProperty("code").GetString()!, x.GetProperty("qty").GetInt32())).ToList()
            : new();
        Assert.Equal(clamps, actual.Clamps.Select(x => (x.Code, x.Qty)).ToList());
        Assert.False(golden.TryGetProperty("pendingConfirmation", out _), $"{where}: teyit bekleyen adım motorda desteklenmiyor");
    }
}
