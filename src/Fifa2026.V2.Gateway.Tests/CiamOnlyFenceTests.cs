using System.Net;
using System.Net.Http.Headers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Fifa2026.V2.Gateway.Tests;

/// <summary>
/// Story 3.5 (ADE-007 v1.3 Invariante 8.2) — FENCE <c>CiamOnly</c> na rota <c>/me</c>
/// (proxy → <c>/api/v2/me</c>, cluster functions-f1). O item de maior risco de segurança
/// da story: o endpoint resolve-or-provision DEVE ser autorizado SÓ para o esquema CIAM
/// (cliente), NUNCA para o Admin (workforce). Sem o fence, como o transform X-Entra-OID é
/// GLOBAL, um token de admin faria o gateway injetar o oid do OPERADOR e o resolve-or-provision
/// criaria uma linha de CLIENTE com identidade de admin — corrompendo a base.
///
/// Cobertura (mesmo padrão de DualIssuerTests/AdminProxyTests, WebApplicationFactory + WireMock):
///   - Token CIAM válido → 200 + X-Entra-OID/Email/Name propagados ao backend (AC-6).
///   - Token Admin (workforce) → 401/403 E o backend /api/v2/me NUNCA é chamado (a prova
///     mais forte do fence: o resolve-or-provision jamais corre com um oid de admin).
///   - Sem token → 401.
///
/// NOTA de rate limiter (herdada de DualIssuerTests): /me usa a partição de cliente
/// (5/min por IP, compartilhada na instância). Esta classe faz ≤ 5 requisições.
/// </summary>
public sealed class CiamOnlyFenceTests : IClassFixture<GatewayTestFixture>
{
    private readonly GatewayTestFixture _fixture;

    public CiamOnlyFenceTests(GatewayTestFixture fixture) => _fixture = fixture;

    private void StubMe()
    {
        _fixture.Backend
            .Given(Request.Create().WithPath("/api/v2/me").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"userId\":123}"));
    }

    private int BackendMeHitCount() => _fixture.Backend.LogEntries
        .Count(e => e.RequestMessage.Path == "/api/v2/me");

    [Fact]
    public async Task CiamToken_OnMe_Returns200_And_Forwards_Identity_Headers()
    {
        // Token do cliente (issuer CIAM) satisfaz CiamOnly → 200; o gateway propaga a
        // identidade (oid) e os claims aditivos email/name (AC-6) ao backend /api/v2/me.
        // A-1 (code review): email/name só são propagados com email_verified=true.
        StubMe();
        const string oid = "cccccccc-1111-2222-3333-444444444444";
        const string email = "cliente@example.com";
        const string name = "Cliente CIAM";

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.Create(oid: oid, email: email, name: name, emailVerified: true));

        var response = await client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = _fixture.Backend.LogEntries.Last(e => e.RequestMessage.Path == "/api/v2/me");
        var headers = log.RequestMessage.Headers!;
        Assert.True(headers.ContainsKey("X-Entra-OID"));
        Assert.Contains(oid, headers["X-Entra-OID"]);
        Assert.True(headers.ContainsKey("X-Entra-Email"));
        Assert.Contains(email, headers["X-Entra-Email"]);
        Assert.True(headers.ContainsKey("X-Entra-Name"));
        Assert.Contains(name, headers["X-Entra-Name"]);
        // Story 3.5 fix — o flag de verificação acompanha o email (aqui: verificado).
        Assert.True(headers.ContainsKey("X-Entra-Email-Verified"));
        Assert.Contains("true", headers["X-Entra-Email-Verified"]);
    }

    [Fact]
    public async Task CiamToken_OnMe_EmailNotVerified_ForwardsEmail_WithVerifiedFalseFlag()
    {
        // A-1 re-layer (Story 3.5 fix) — o email agora é propagado SEMPRE, junto de
        // X-Entra-Email-Verified refletindo o claim email_verified. A decisão de segurança
        // migra para a MeFunction: o arm de LINK (vincular a uma conta v1 EXISTENTE) exige
        // verified=true (anti-takeover); o INSERT nato-CIAM não. Aqui o email NÃO é verificado
        // → header presente + X-Entra-Email-Verified: false. O gating do LINK é coberto no nível
        // da Function por MeFunctionTests.LinkByEmail_SkippedWhenEmailNotVerified_FallsToInsertNotLink.
        StubMe();
        const string oid = "cccccccc-1111-2222-3333-444444444444";
        const string email = "vitima@example.com"; // presente no token, mas NÃO verificado
        const string name = "Cliente CIAM";

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.Create(oid: oid, email: email, name: name, emailVerified: false));

        var response = await client.GetAsync("/me");

        // O stub responde 200; o que importa é o que o gateway ENCAMINHOU ao backend.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var log = _fixture.Backend.LogEntries.Last(e => e.RequestMessage.Path == "/api/v2/me");
        var headers = log.RequestMessage.Headers!;
        Assert.True(headers.ContainsKey("X-Entra-OID"));             // identidade validada — sempre propagada
        Assert.True(headers.ContainsKey("X-Entra-Email"));           // A-1 re-layer: email propagado SEMPRE
        Assert.Contains(email, headers["X-Entra-Email"]);
        Assert.True(headers.ContainsKey("X-Entra-Email-Verified"));  // flag acompanha o email
        Assert.Contains("false", headers["X-Entra-Email-Verified"]); // NÃO verificado → false
        Assert.True(headers.ContainsKey("X-Entra-Name"));            // nome não exige verificação
    }

    [Fact]
    public async Task AdminToken_OnMe_IsRejected_And_Backend_Never_Hit()
    {
        // O CORAÇÃO do fence: um token workforce (admin) — mesmo válido e com role Admin —
        // NÃO satisfaz CiamOnly (é forçado a validar pelo handler Ciam, falha no issuer).
        // 401/403 (ambos fecham o buraco) E o resolve-or-provision JAMAIS corre: o backend
        // /api/v2/me não recebe a request, então o oid do admin nunca vira uma linha de cliente.
        StubMe();
        var before = BackendMeHitCount();

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateAdmin());

        var response = await client.GetAsync("/me");

        // B-1 (code review 2026-07-01) — 403 EXATO (não "401 OU 403"): o admin AUTENTICA (o
        // selector roteia o token workforce ao AdminScheme e o merge com a DefaultPolicy o
        // aprova no RequireAuthenticatedUser), mas a assertion de ISSUER do CiamOnly o barra →
        // 403 (autenticado-mas-não-autorizado), NUNCA 401. Fixar o status prova que foi o FENCE
        // que barrou — não uma falha de autenticação genérica.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, BackendMeHitCount()); // backend NUNCA chamado (fence bloqueou antes do proxy)
    }

    [Fact]
    public async Task NoToken_OnMe_Returns401()
    {
        var client = _fixture.CreateClient();
        var response = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
