using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;

namespace Arqenion.Api.Tests;

// Swagger üretimde kapalı; Api:Swagger=true ile girişsiz açılır ve VR uçlarını listeler.
public sealed class SwaggerTests : IClassFixture<VrReplayTests.Api>
{
    private readonly VrReplayTests.Api _api;
    public SwaggerTests(VrReplayTests.Api api) => _api = api;

    [Fact]
    public async Task Swagger_is_off_by_default_outside_development()
        => Assert.NotEqual(HttpStatusCode.OK, (await _api.CreateClient().GetAsync("/swagger/v1/swagger.json")).StatusCode);

    [Fact]
    public async Task Swagger_opens_with_the_setting_and_lists_vr_endpoints()
    {
        var http = _api.WithWebHostBuilder(b => b.UseSetting("Api:Swagger", "true")).CreateClient();
        var doc = JsonNode.Parse(await http.GetStringAsync("/swagger/v1/swagger.json"))!;
        var paths = doc["paths"]!.AsObject().Select(p => p.Key).ToList();
        foreach (var p in new[] { "/api/auth/login", "/api/procedure", "/api/assets/manifest", "/api/sessions", "/api/sessions/{sessionId}/events" })
            Assert.Contains(p, paths);
        Assert.NotNull(doc["components"]!["securitySchemes"]!["Bearer"]);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/swagger/index.html")).StatusCode);
    }
}
