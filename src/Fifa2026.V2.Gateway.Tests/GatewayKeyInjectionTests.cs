using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Fifa2026.V2.Gateway.Tests;

/// <summary>
/// Story 4.2 (ADE-009 Inv 1 / AC-2/AC-3) — o transform de X-Gateway-Key, antes escopado só ao
/// cluster backend-v1 (ver <see cref="AdminProxyTests"/>), agora cobre TAMBÉM os clusters
/// functions-f1 e mcp-server. Estes testes provam a METADE "via gateway injeta o segredo" da
/// demo-dinheiro: o gateway anexa o X-Gateway-Key real nas rotas desses clusters, e a
/// semântica anti-spoofing (descartar o header vindo do cliente antes de injetar) é preservada.
/// A metade "destino rejeita sem a chave" é provada em GatewayKeyValidationMiddlewareTests
/// (McpServer) e nos testes do validador (Functions/McpServer).
/// </summary>
public sealed class GatewayKeyInjectionTests : IClassFixture<GatewayTestFixture>
{
    private const string PurchaseBackendPath = "/api/v2/purchase";
    private const string McpBackendPath = "/mcp";

    private readonly GatewayTestFixture _fixture;

    public GatewayKeyInjectionTests(GatewayTestFixture fixture) => _fixture = fixture;

    private List<string> ForwardedGatewayKeys(string path) => _fixture.Backend.LogEntries
        .Where(e => e.RequestMessage.Path == path
                    && e.RequestMessage.Headers!.ContainsKey("X-Gateway-Key"))
        .SelectMany(e => e.RequestMessage.Headers!["X-Gateway-Key"])
        .ToList();

    // Emenda MEDIUM-4 — headers X-Diploma-Key forwardeados a um path do backend flow-events.
    private List<string> ForwardedDiplomaKeys(string path) => _fixture.Backend.LogEntries
        .Where(e => e.RequestMessage.Path == path
                    && e.RequestMessage.Headers!.ContainsKey("X-Diploma-Key"))
        .SelectMany(e => e.RequestMessage.Headers!["X-Diploma-Key"])
        .ToList();

    private void StubPurchase() => _fixture.Backend
        .Given(Request.Create().WithPath(PurchaseBackendPath).UsingPost())
        .RespondWith(Response.Create().WithStatusCode(202)
            .WithHeader("Content-Type", "application/json")
            .WithBody("{\"status\":\"queued\"}"));

    // functions-f1: POST /purchase → path rewrite /api/v2/purchase no backend (cluster
    // functions-f1). Token de CLIENTE (CIAM) satisfaz a DefaultPolicy.
    [Fact]
    public async Task FunctionsF1_Cluster_Receives_Injected_GatewayKey()
    {
        StubPurchase();
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.Create());

        var response = await client.PostAsJsonAsync("/purchase",
            new { matchId = 1, category = "VIP", userId = 1, quantity = 1 });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains(GatewayTestFixture.AdminSharedSecret, ForwardedGatewayKeys(PurchaseBackendPath));
    }

    [Fact]
    public async Task FunctionsF1_Cluster_AntiSpoofing_ClientKey_IsOverwritten()
    {
        StubPurchase();
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.Create());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Gateway-Key", "forged-evil-key");

        var response = await client.PostAsJsonAsync("/purchase",
            new { matchId = 1, category = "VIP", userId = 1, quantity = 1 });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var keys = ForwardedGatewayKeys(PurchaseBackendPath);
        Assert.Contains(GatewayTestFixture.AdminSharedSecret, keys);
        Assert.DoesNotContain("forged-evil-key", keys);
    }

    // mcp-server: aponta o cluster mcp-server pro MESMO WireMock (McpServerUrl) e stuba /mcp.
    [Fact]
    public async Task McpServer_Cluster_Receives_Injected_GatewayKey()
    {
        _fixture.Backend
            .Given(Request.Create().WithPath(McpBackendPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"ok\":true}"));

        // WithWebHostBuilder deriva um host novo que aponta o cluster mcp-server pro WireMock.
        var factory = _fixture.WithWebHostBuilder(builder =>
            builder.UseSetting("McpServerUrl", _fixture.Backend.Url));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.Create());

        var response = await client.PostAsync("/mcp",
            new StringContent("{\"jsonrpc\":\"2.0\"}", Encoding.UTF8, "application/json"));

        // O gateway roteou e injetou o segredo real no cluster mcp-server (independe do stub).
        Assert.Contains(GatewayTestFixture.AdminSharedSecret, ForwardedGatewayKeys(McpBackendPath));
    }

    // Emenda MEDIUM-4 (ADE-009 v1.1) — a rota ESPECÍFICA flow-events-diploma injeta o
    // X-Diploma-Key (route-scoped, molde do me-get) reusando o AdminSharedSecret sob header
    // DISTINTO. Prova as duas metades do design: (a) o X-Diploma-Key É injetado nesta rota;
    // (b) o X-Gateway-Key NUNCA vaza (cluster flow-events segue fora do escopo cluster-scoped).
    [Fact]
    public async Task DiplomaRoute_Injects_XDiplomaKey_ButNever_XGatewayKey()
    {
        const string diplomaBackendPath = "/api/flow/diploma-summary";
        _fixture.Backend
            .Given(Request.Create().WithPath(diplomaBackendPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"region\":null,\"correlationIds\":[],\"count\":0}"));

        var factory = _fixture.WithWebHostBuilder(builder =>
            builder.UseSetting("FlowEventsUrl", _fixture.Backend.Url));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.Create());
        // Anti-spoofing: um X-Diploma-Key forjado pelo cliente deve ser descartado antes da injeção.
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Diploma-Key", "forged-evil-key");

        var response = await client.GetAsync("/flow-events/api/flow/diploma-summary?userId=42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // (a) o segredo real foi injetado nesta rota, o forjado descartado.
        var diplomaKeys = ForwardedDiplomaKeys(diplomaBackendPath);
        Assert.Contains(GatewayTestFixture.AdminSharedSecret, diplomaKeys);
        Assert.DoesNotContain("forged-evil-key", diplomaKeys);
        // (b) o X-Gateway-Key NUNCA vaza para o cluster flow-events (retro-compat da Inv 1).
        Assert.Empty(ForwardedGatewayKeys(diplomaBackendPath));
    }

    // Emenda MEDIUM-4 (N-1 — âncora de review) — a rota flow-events-diploma está sob o blanket
    // RequireAuthorization (prova de IDENTIDADE do design Opção F): SEM Bearer → 401. Este teste
    // FALHARIA se alguém tornasse a rota anônima (AllowAnonymous / removesse o RequireAuthorization),
    // reabrindo o vetor "anônimo via gateway". O backend NUNCA é chamado (auth barra antes do proxy),
    // por isso não precisa stub de FlowEventsUrl. Espelha CiamOnlyFenceTests.NoToken_OnMe_Returns401.
    [Fact]
    public async Task NoToken_OnDiplomaSummary_Returns401()
    {
        var client = _fixture.CreateClient();
        var response = await client.GetAsync("/flow-events/api/flow/diploma-summary?userId=1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // flow-events: FORA do conjunto de clusters confiáveis (AC-2 — a ADE-009 Inv 1 lista só
    // v1/functions-f1/mcp-server). O segredo NUNCA é injetado nele. Prova que a extensão do
    // transform não vazou além do escopo decidido pela ADE (Art. IV).
    [Fact]
    public async Task FlowEvents_Cluster_Does_NOT_Receive_GatewayKey()
    {
        const string flowBackendPath = "/api/ping";
        _fixture.Backend
            .Given(Request.Create().WithPath(flowBackendPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"ok\":true}"));

        var factory = _fixture.WithWebHostBuilder(builder =>
            builder.UseSetting("FlowEventsUrl", _fixture.Backend.Url));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.Create());

        var response = await client.GetAsync("/flow-events/api/ping");

        // A request CHEGOU ao backend flow-events (rewrite /flow-events/api/ping → /api/ping)...
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_fixture.Backend.LogEntries, e => e.RequestMessage.Path == flowBackendPath);
        // ...mas SEM o X-Gateway-Key (cluster não-confiável — segredo não vaza).
        Assert.Empty(ForwardedGatewayKeys(flowBackendPath));
    }
}
