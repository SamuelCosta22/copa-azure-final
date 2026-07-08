using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using Fifa2026.V2.Gateway.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Yarp.ReverseProxy.Transforms;

// =============================================================================
// Fifa2026.V2.Gateway — Gateway profissional em código C# com YARP (Story 2.2 / F2)
//
// Substitui o APIM Developer (ADE-004): rate-limit, output cache, CORS, header
// transform e JWT placeholder são MECANISMOS DE CÓDIGO, não policies XML opacas.
// Cada capacidade tem paridade 1:1 com uma policy APIM (ADE-004 Invariante 3).
//
// Pipeline (ORDEM IMPORTA — ADE-004 / story Task 2.6; REORDENADO na Story 4.4):
//   UseForwardedHeaders → UseCors → UseRateLimiter → UseAuthentication
//           → UseAuthorization → XCacheMiddleware (cache 30s, PÓS-AUTH) → MapReverseProxy
//
// Story 4.4 (ADE-009 §Consequences — P1 de segurança): o XCacheMiddleware roda DEPOIS
// de UseAuthentication/UseAuthorization. Antes, um cache HIT fazia short-circuit ANTES do
// auth e servia o status de uma compra SEM token válido por até 30s. Agora TODA request
// (HIT ou MISS) passa por auth primeiro — o cache só é alcançado quando autenticada.
// UseForwardedHeaders precede o rate-limiter (que particiona por IP): atrás do ingress do
// Container Apps o RemoteIpAddress é o do ingress, não do cliente — o header X-Forwarded-For
// devolve o IP real, tornando a partição por-cliente efetiva de novo.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// Constantes de configuração de pipeline.
const string RateLimiterPolicy = "fixed";              // partição fixed-window por IP (AC-5)
const string CorsPolicy = "frontend";                   // origin restrito ao front (AC-7)
const string CorrelationHeader = "X-Correlation-ID";    // ADE-000 Inv 5 / AC-8
const string EntraOidHeader = "X-Entra-OID";            // Story 2.3 AC-7 / ADE-005 Inv 4
// Story 3.5 (ADE-007 v1.2 Inv 8) — claims email/name do token CIAM propagados ADITIVAMENTE
// (mesmo padrão de X-Entra-OID) para o resolve-or-provision do GET /api/v2/me. São PII —
// injetados, NUNCA logados (Inv 8 flag (c)). A Function os usa só no arm de INSERT (nato-CIAM).
const string EntraEmailHeader = "X-Entra-Email";
const string EntraNameHeader = "X-Entra-Name";
// M-1 (code review 2026-07-01) — RouteId (appsettings.json → ReverseProxy:Routes) do ÚNICO
// consumidor de email/name: o GET /api/v2/me. A injeção dessa PII é ESCOPADA a esta rota
// (minimização de PII), diferente do X-Entra-OID (global — contrato existente da Story 2.3).
const string MeRouteId = "me-get";
// EPIC-004 Story 4.6 §Emenda MEDIUM-4 (ADE-009 v1.1) — RouteId e header da exceção
// ROUTE-SCOPED do FlowEvents /api/flow/diploma-summary. O X-Diploma-Key reusa o MESMO valor
// do Gateway:AdminSharedSecret sob header DISTINTO, injetado SÓ nesta rota (não por cluster —
// o cluster flow-events segue fora do X-Gateway-Key). Prova de proveniência que o FQDN direto
// do ca-flow não tem; somada ao Bearer (blanket RequireAuthorization) fecha o buraco MEDIUM-4.
const string DiplomaRouteId = "flow-events-diploma";
const string DiplomaKeyHeader = "X-Diploma-Key";

// Claim names do Microsoft Identity Platform (AC-14 anti-hallucination — validados
// contra docs oficiais "id-token-claims-reference" / "access-token-claims-reference").
//   - "oid": object id estável do usuário no tenant (token v2.0 / endpoint /v2.0).
//   - URI longa: nome do mesmo claim após o mapeamento de inbound claims do
//     JwtBearer handler (System.Security.Claims) — usado como fallback (ADE-005 Inv 4 /
//     story troubleshooting "Claim oid ausente").
const string OidClaim = "oid";
const string OidClaimUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";

// Story 3.5 (ADE-007 Inv 8) — claims padrão OIDC do email/name do cliente CIAM. Como no
// oid, checamos o nome CURTO e a URI LONGA do mapeamento inbound do handler (defensivo p/
// MapInboundClaims true/false).
//
// A-1 (code review 2026-07-01) — "preferred_username" foi REMOVIDO da cadeia de fallback do
// email: é MUTÁVEL e a Microsoft diz explicitamente que "must not be used ... for
// authorization" (id-token-claims-reference). O email do LINK vem SÓ do claim `email` e
// SOMENTE quando `email_verified` for verdadeiro (o IdP atesta posse — OTP/social). Sem isso,
// um atacante que apenas REGISTRA (sem provar posse) o email de uma vítima v1 no CIAM
// dispararia o arm de LINK (UPDATE users SET entra_oid WHERE email AND entra_oid IS NULL),
// tomando a conta da vítima. A verificação passa a estar NO CÓDIGO (defense-in-depth).
const string EmailClaim = "email";
const string EmailClaimUri = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress";
const string EmailVerifiedClaim = "email_verified";
const string NameClaim = "name";
const string NameClaimUri = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";

// Quartas / "admin 100% workforce" + EPIC-004 Story 4.2 (ADE-009 Inv 1) — header de
// shared secret (X-Gateway-Key) e os IDs dos clusters que o recebem. O gateway prova a
// cada backend que a request passou pelo guardião (e não é spoof). Lido de
// Gateway:AdminSharedSecret (App Setting Gateway__AdminSharedSecret; agora via KV
// reference resolvida pela MI — Story 4.1 AC-12). Vazio no repo = injeção desligada,
// igual ao backend (compatibilidade retroativa: labs sem gateway — Oitavas/F1).
//
// Story 4.2 (ADE-009 Inv 1): a injeção deixa de ser só no backend-v1 e passa a cobrir
// TAMBÉM os clusters functions-f1 e mcp-server — fechando o P0 do bypass (um curl
// forjando X-Entra-OID direto na Function/McpServer não tem o X-Gateway-Key e é
// rejeitado no destino; via gateway a request carrega o segredo real). O cluster
// flow-events fica FORA: a ADE-009 Inv 1 lista só "v1, Functions F1, McpServer" —
// nenhuma extensão além do escopo decidido pela ADE (Art. IV).
const string GatewayKeyHeader = "X-Gateway-Key";
const string BackendV1ClusterId = "backend-v1";
const string FunctionsF1ClusterId = "functions-f1";
const string McpServerClusterId = "mcp-server";
var adminSharedSecret = builder.Configuration["Gateway:AdminSharedSecret"];

// Conjunto dos clusters CONFIÁVEIS que recebem o X-Gateway-Key injetado (ADE-009 Inv 1).
// OrdinalIgnoreCase preserva a mesma semântica case-insensitive que o StringComparison
// usado antes só para o backend-v1. flow-events NÃO está aqui (fora do escopo da ADE).
var gatewayKeyClusters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    BackendV1ClusterId,
    FunctionsF1ClusterId,
    McpServerClusterId,
};

// -----------------------------------------------------------------------------
// YARP reverse proxy (ADE-004 Inv 1 e 2): rotas/clusters do appsettings.json +
// transforms programáticos (X-Correlation-ID, que exige geração de GUID novo).
// O IProxyConfigFilter sobrescreve a destination do cluster com a URL real da
// Function F1 (env FunctionAppF1Url — ADE-003 Inv 3, nunca hardcoded). A
// connection string SQL permanece NAS FUNCTIONS, não aqui.
// -----------------------------------------------------------------------------
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddConfigFilter<FunctionDestinationConfigFilter>()
    // Story 2.5 / F5 — injeta a URL real do McpServer no cluster mcp-server (AC-8).
    .AddConfigFilter<McpServerDestinationConfigFilter>()
    // Story 2.6 / F6 — injeta a URL real do serviço FlowEvents no cluster flow-events
    // (AC-3/AC-5). O gateway permanece o NÓ ZERO: injeta X-Correlation-ID nas requests
    // ao FlowEvents também (mesmo transform global de borda).
    .AddConfigFilter<FlowEventsDestinationConfigFilter>()
    // Quartas / "admin 100% workforce" — injeta a URL real do backend Node/Express v1
    // no cluster backend-v1 (rotas /admin/* proxiadas com a policy AdminOnly).
    .AddConfigFilter<BackendV1DestinationConfigFilter>()
    .AddTransforms(transformBuilderContext =>
    {
        // AC-8 / ADE-000 Inv 5 — injeta X-Correlation-ID (novo GUID se ausente) em
        // CADA requisição encaminhada ao backend. Aplicado em TODAS as rotas
        // (gateway é o nó zero do Flow Visualizer de F6).
        transformBuilderContext.AddRequestTransform(transformContext =>
        {
            var incoming = transformContext.HttpContext.Request.Headers[CorrelationHeader].ToString();
            var correlationId = string.IsNullOrWhiteSpace(incoming)
                ? Guid.NewGuid().ToString()
                : incoming;

            transformContext.ProxyRequest.Headers.Remove(CorrelationHeader);
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);

            // Devolve o mesmo correlationId ao cliente (observabilidade de borda — AC-11).
            transformContext.HttpContext.Response.Headers[CorrelationHeader] = correlationId;

            return ValueTask.CompletedTask;
        });

        // Story 2.3 AC-7 / ADE-005 Inv 4 — propagação de identidade downstream.
        // Após o JWT ser validado pelo AddJwtBearer, extrai o claim `oid` do usuário
        // autenticado e o injeta como header X-Entra-OID na requisição encaminhada à
        // Function F1 (que grava entra_oid em SQL). A Function NUNCA valida o token —
        // confia no header propagado pelo gateway (guardião único de JWT).
        //
        // SEGURANÇA (defense-in-depth): SEMPRE remove qualquer X-Entra-OID que tenha
        // vindo do cliente ANTES de (eventualmente) injetar o valor derivado do token.
        // Isso impede spoofing de identidade — o cliente não consegue forjar o header.
        transformBuilderContext.AddRequestTransform(transformContext =>
        {
            // Anti-spoofing: descarta qualquer X-Entra-OID/Email/Name de origem externa
            // ANTES de (eventualmente) injetar os valores derivados do token. O cliente não
            // consegue forjar identidade (Story 3.5 estende o anti-spoof a email/name).
            transformContext.ProxyRequest.Headers.Remove(EntraOidHeader);
            transformContext.ProxyRequest.Headers.Remove(EntraEmailHeader);
            transformContext.ProxyRequest.Headers.Remove(EntraNameHeader);
            // Emenda MEDIUM-4 (defesa em profundidade) — strip GLOBAL do X-Diploma-Key também:
            // o cliente NUNCA pode forjá-lo em NENHUMA rota. A rota flow-events-diploma reinjeta o
            // valor real logo depois (este transform roda antes, na ordem de registro). Não é
            // explorável hoje (só a rota diploma valida o header), mas fecha o gap por consistência
            // com os headers de identidade acima.
            transformContext.ProxyRequest.Headers.Remove(DiplomaKeyHeader);

            var user = transformContext.HttpContext.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                // Token v2.0 traz o claim "oid"; após o mapeamento inbound do handler
                // o mesmo valor pode aparecer sob a URI longa (fallback — ADE-005 Inv 4).
                var oid = user.FindFirst(OidClaim)?.Value
                    ?? user.FindFirst(OidClaimUri)?.Value;

                if (!string.IsNullOrWhiteSpace(oid))
                {
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation(EntraOidHeader, oid);
                }

                // M-1 (code review 2026-07-01) — a injeção de email/name (PII) NÃO é mais feita
                // aqui: era GLOBAL (todos os clusters recebiam a PII). Foi movida para um
                // transform ESCOPADO à rota me-get (logo abaixo), o único consumidor. O
                // anti-spoof (Remove dos 3 headers, no topo deste transform) PERMANECE global —
                // nenhuma rota encaminha um X-Entra-Email/Name forjado pelo cliente. O
                // X-Entra-OID continua injetado globalmente (contrato existente da Story 2.3).
            }

            // NÃO logamos o token nem o oid/email/name em texto (AC-12 / CodeRabbit focus
            // area — identidade/PII; nunca aparece em log de aplicação).
            return ValueTask.CompletedTask;
        });

        // -----------------------------------------------------------------------------
        // Story 3.5 — injeção de identidade PII (email/name) ESCOPADA à rota me-get.
        //
        // M-1 (code review 2026-07-01, minimização de PII): o único consumidor de email/name é
        // o GET /api/v2/me (resolve-or-provision). Escopamos por ROTA — e não por cluster, como
        // o X-Gateway-Key — porque o cluster functions-f1 é COMPARTILHADO (purchase + me) e só
        // /me precisa da PII. O X-Entra-OID permanece global (contrato Story 2.3); o anti-spoof
        // (Remove) permanece global no transform de identidade acima. O callback de transforms
        // roda por rota, então só ANEXAMOS este transform quando a rota é a me-get.
        // -----------------------------------------------------------------------------
        if (transformBuilderContext.Route?.RouteId is { } routeId &&
            string.Equals(routeId, MeRouteId, StringComparison.OrdinalIgnoreCase))
        {
            transformBuilderContext.AddRequestTransform(transformContext =>
            {
                var user = transformContext.HttpContext.User;
                if (user?.Identity?.IsAuthenticated == true)
                {
                    // A-1 (code review 2026-07-01) — o email do LINK vem SÓ do claim `email` e
                    // SOMENTE quando `email_verified` for verdadeiro (aceita o bool true ou a
                    // string "true"; bool.TryParse cobre ambos — o claim booleano do JWT chega
                    // ao ClaimsPrincipal como string). Sem verificação → header OMITIDO → a
                    // MeFunction cai em 422 (InsufficientClaims) = fail-closed. Isso fecha o
                    // account-takeover: um oid que só REGISTROU o email de uma vítima (sem provar
                    // posse) não dispara o arm de LINK por email. preferred_username foi removido
                    // da cadeia (mutável; "must not be used for authorization").
                    var emailVerified = bool.TryParse(
                        user.FindFirst(EmailVerifiedClaim)?.Value, out var verified) && verified;
                    var email = emailVerified
                        ? user.FindFirst(EmailClaim)?.Value ?? user.FindFirst(EmailClaimUri)?.Value
                        : null;
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        transformContext.ProxyRequest.Headers.TryAddWithoutValidation(EntraEmailHeader, email);
                    }

                    // O NOME não é usado para autorização — só popula a coluna `name` NOT NULL no
                    // arm de INSERT. Mantém o fallback short-claim/URI-longa; só o EMAIL do LINK
                    // precisa ser verificado (A-1 item 2).
                    var name = user.FindFirst(NameClaim)?.Value
                        ?? user.FindFirst(NameClaimUri)?.Value;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        transformContext.ProxyRequest.Headers.TryAddWithoutValidation(EntraNameHeader, name);
                    }
                }

                // PII (email/name): injetada, NUNCA logada (Inv 8 flag (c)).
                return ValueTask.CompletedTask;
            });
        }

        // -----------------------------------------------------------------------------
        // EPIC-004 Story 4.6 §Emenda MEDIUM-4 (ADE-009 v1.1) — injeção ROUTE-SCOPED do
        // X-Diploma-Key SÓ na rota flow-events-diploma (GET /flow-events/api/flow/diploma-summary).
        //
        // Escopamos por ROTA (molde do me-get acima), NÃO por cluster: o cluster flow-events
        // continua FORA do X-Gateway-Key (o teste FlowEvents_Cluster_Does_NOT_Receive_GatewayKey
        // segue verde — header e mecanismo distintos; a injeção é por rota, não por cluster).
        // É a "prova de proveniência" que um chamador no FQDN direto do ca-flow não tem; combinada
        // com o Bearer exigido pelo blanket RequireAuthorization (barra o anônimo-via-gateway),
        // fecha o buraco do MEDIUM-4. Reusa o MESMO segredo (adminSharedSecret) sob header
        // distinto — US$0 — com a MESMA semântica fail-closed/legado da Inv 1 (segredo vazio =
        // injeção desligada = bypass legado no destino, preservando dev local/pré-provisionamento).
        // -----------------------------------------------------------------------------
        if (transformBuilderContext.Route?.RouteId is { } diplomaRouteId &&
            string.Equals(diplomaRouteId, DiplomaRouteId, StringComparison.OrdinalIgnoreCase))
        {
            transformBuilderContext.AddRequestTransform(transformContext =>
            {
                // Anti-spoofing (igual ao X-Gateway-Key/X-Entra-OID): SEMPRE descarta qualquer
                // X-Diploma-Key vindo do cliente antes de injetar o valor real.
                transformContext.ProxyRequest.Headers.Remove(DiplomaKeyHeader);

                // Só injeta quando o segredo está configurado (vazio = injeção desligada, o
                // FlowEvents cai no bypass legado — paridade com a semântica da l.254).
                if (!string.IsNullOrEmpty(adminSharedSecret))
                {
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                        DiplomaKeyHeader, adminSharedSecret);
                }

                return ValueTask.CompletedTask;
            });
        }

        // Quartas + EPIC-004 Story 4.2 (ADE-009 Inv 1) — injeta X-Gateway-Key nas rotas
        // dos clusters CONFIÁVEIS (backend-v1, functions-f1, mcp-server). O escopo é
        // decidido em tempo de CONFIG: o callback de transforms roda por rota, então só
        // ANEXAMOS o transform quando a rota aponta pra um cluster do conjunto. Assim o
        // segredo NUNCA vaza para o cluster flow-events (fora do escopo da ADE-009 Inv 1)
        // — nem é avaliado por request nele.
        if (transformBuilderContext.Cluster?.ClusterId is { } clusterId &&
            gatewayKeyClusters.Contains(clusterId))
        {
            transformBuilderContext.AddRequestTransform(transformContext =>
            {
                // Anti-spoofing (igual ao X-Entra-OID): SEMPRE descarta qualquer
                // X-Gateway-Key vindo do cliente antes de injetar o valor real.
                transformContext.ProxyRequest.Headers.Remove(GatewayKeyHeader);

                // Só injeta quando o segredo está configurado (vazio = injeção desligada,
                // backend cai no fluxo legado — paridade com GATEWAY_SHARED_SECRET vazio).
                if (!string.IsNullOrEmpty(adminSharedSecret))
                {
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                        GatewayKeyHeader, adminSharedSecret);
                }

                return ValueTask.CompletedTask;
            });
        }
    });

// -----------------------------------------------------------------------------
// AC-5 — Rate limiting em código (paridade com APIM rate-limit-by-key).
// Fixed window por IP. UMA única policy "fixed" (aplicada em todas as rotas do proxy),
// mas o LIMITE é sensível ao PATH:
//   - rotas de cliente (ex.: /purchase): 5 req/min por IP (comportamento original — AC-5).
//   - rotas admin (/admin/*): 60 req/min por IP.
//
// DECISÃO (Quartas / "admin 100% workforce"): o Dashboard dispara várias chamadas
// (stats + sales, recarregamentos, paginação) e estouraria o limite apertado de 5/min
// (HTTP 429). Em vez de um 2º policy + per-route config no YARP (que duplicaria metadata
// de rate-limit junto à blanket RequireRateLimiting e tornaria a resolução ambígua),
// mantemos UMA policy com PARTIÇÕES SEPARADAS por path: "admin:{ip}" (60/min) e "{ip}"
// (5/min). Os contadores não se misturam, a rota /purchase continua 5/min (teste verde)
// e as rotas admin ganham folga sem afrouxar o resto do gateway.
// -----------------------------------------------------------------------------
// -----------------------------------------------------------------------------
// Story 4.4 (ADE-009 §Consequences) — Forwarded Headers. O gateway roda atrás do
// ingress do Azure Container Apps: sem tratar X-Forwarded-For, HttpContext.Connection.
// RemoteIpAddress é o IP do INGRESS (sempre o mesmo / pool pequeno), não do cliente —
// então a partição "por IP" do rate-limiter abaixo colapsa num único bucket (todo o
// tráfego compete pelos mesmos 5/min). ForwardedHeadersMiddleware reescreve o
// RemoteIpAddress a partir do X-Forwarded-For antes do rate-limiter (ver ordem do
// pipeline no fim do arquivo — UseForwardedHeaders vem primeiro).
//
// KnownNetworks/KnownProxies LIMPOS de propósito: por padrão o middleware só confia em
// X-Forwarded-For vindo da rede loopback — o ingress do Container Apps NÃO é loopback,
// então o header seria ignorado. Com as duas listas vazias, o middleware confia no
// X-Forwarded-For de QUALQUER origem imediata (comportamento documentado do ASP.NET Core).
// DECISÃO consciente e segura no contexto do CAE: o ingress é a borda confiável do
// ambiente e APÕE o IP real do cliente à DIREITA do X-Forwarded-For; com ForwardLimit=1
// (default) o middleware lê a entrada mais à direita = o IP que o ingress anexou, então um
// X-Forwarded-For forjado pelo cliente (à esquerda) NÃO é lido. Não há proxy adicional
// não-confiável entre o ingress e este gateway.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    // SEGURANCA (gate @architect, Story 4.4): pina ForwardLimit=1 explicitamente. Com as listas
    // limpas acima, este e o UNICO guardrail contra spoofing de IP — le apenas a entrada mais a
    // direita (o IP que o ingress CAE anexou), ignorando qualquer X-Forwarded-For forjado pelo
    // cliente. NAO aumentar sem reavaliar o rate-limit por IP (particiona por RemoteIpAddress).
    options.ForwardLimit = 1;
});

const int ClientPermitLimit = 5;    // AC-5 original (rotas de cliente).
const int AdminPermitLimit = 60;    // rotas /admin/* (Dashboard faz N chamadas).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimiterPolicy, httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var isAdmin = httpContext.Request.Path.StartsWithSegments("/admin");
        return RateLimitPartition.GetFixedWindowLimiter(
            // Partição namespaceada: admin e cliente nunca compartilham contador.
            partitionKey: isAdmin ? $"admin:{ip}" : ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isAdmin ? AdminPermitLimit : ClientPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

// -----------------------------------------------------------------------------
// AC-6 — Cache de borda (30s) em código (paridade com APIM cache-lookup/cache-store).
// Implementado pelo XCacheMiddleware (IMemoryCache) — NÃO pelo OutputCache nativo, que
// não captura respostas proxied pelo YARP (o forwarder chama DisableBuffering). Ver a
// documentação no próprio XCacheMiddleware.
// -----------------------------------------------------------------------------
builder.Services.AddMemoryCache();

// -----------------------------------------------------------------------------
// AC-7 — CORS restrito ao domínio do frontend (paridade com APIM cors).
// -----------------------------------------------------------------------------
var frontendOrigin = builder.Configuration["Gateway:FrontendOrigin"]
    ?? "https://fifa2026-web.azurewebsites.net";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(frontendOrigin)
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// =============================================================================
// Story 2.11 / Quartas — IDENTIDADE DOIS-MUNDOS: cliente CIAM + admin workforce
// (ADE-007 Inv 1/2/5/7, SUPERSEDE ADE-005). O gateway YARP valida DOIS issuers de
// forma issuer-agnóstica (ADE-004 Inv 4 preservada): o cliente final pelo Microsoft
// Entra External ID (CIAM, ciamlogin.com) e o admin/operador pelo Entra ID workforce
// (login.microsoftonline.com).
//
// ⚠️ NÃO é "só trocar a string" da authority. Para aceitar DOIS issuers num mesmo
// pipeline, registramos DOIS handlers JwtBearer concretos ("Ciam" e "Admin") e um
// PolicyScheme "Selector" que, a cada request, inspeciona o issuer NÃO-validado do
// bearer e ENCAMINHA (ForwardDefaultSelector) ao handler concreto correto. O handler
// escolhido então valida iss/aud/assinatura/lifetime do jeito normal (fail-closed).
//
// Por que selector por issuer (e não dois schemes "tentados em sequência")?
//   Encadear schemes faria o 1º handler logar erro de validação para todo token do
//   2º issuer (ruído + risco de challenge ambíguo). O selector roteia 1:1 pelo issuer,
//   então cada token é validado pelo handler do SEU mundo — limpo e determinístico.
//
// CARRY-FORWARD M-1 (gate S2.2) — FAIL-CLOSED em AMBOS os mundos: nenhum tenant tem
// default "common" (aceitaria tokens de qualquer tenant). Tenant E client são
// configuração OBRIGATÓRIA; ausência → a app não sobe. iss/aud validados
// EXPLICITAMENTE (ValidIssuer/ValidAudiences), não só inferidos do Authority.
//
// Config requerida (App Settings do Container App, sem valores reais no repo):
//   Jwt:CiamTenantId  — GUID do tenant CIAM (Entra External ID)
//   Jwt:CiamClientId  — Application (client) ID da App Reg SPA no CIAM (= aud do token cliente)
//   Jwt:CiamAuthority — (opcional) authority CIAM completa override; default derivado:
//                       https://<CiamTenantId>.ciamlogin.com/<CiamTenantId>/v2.0
//   Jwt:AdminTenantId — GUID do tenant workforce (admin)
//   Jwt:AdminClientId — Application (client) ID da App Reg admin (= aud do token admin)
// =============================================================================
const string CiamScheme = "Ciam";    // cliente final (Entra External ID / CIAM)
const string AdminScheme = "Admin";  // admin/operador (Entra ID workforce + App Roles)
const string SelectorScheme = "Selector"; // PolicyScheme que roteia pelo issuer

// --- Config CIAM (cliente) — fail-closed ---
var ciamTenantId = builder.Configuration["Jwt:CiamTenantId"];
var ciamClientId = builder.Configuration["Jwt:CiamClientId"];
var ciamAuthorityOverride = builder.Configuration["Jwt:CiamAuthority"];

if (string.IsNullOrWhiteSpace(ciamTenantId) ||
    string.Equals(ciamTenantId, "common", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Configuração de identidade do CLIENTE ausente/insegura: defina 'Jwt:CiamTenantId' " +
        "com o GUID do tenant CIAM (Entra External ID; não use 'common'). " +
        "Story 2.11 AC-5 / ADE-007 Inv 1.");
}

if (string.IsNullOrWhiteSpace(ciamClientId))
{
    throw new InvalidOperationException(
        "Configuração de identidade do CLIENTE ausente: defina 'Jwt:CiamClientId' com o " +
        "Application (client) ID da App Registration SPA no tenant CIAM (= audience do " +
        "access token do cliente). Story 2.11 AC-5.");
}

// Authority CIAM v2.0. Para Entra External ID o host é <tenant>.ciamlogin.com (NÃO
// login.microsoftonline.com — esse é o erro clássico das Quartas, ADE-007 Consequências).
// Discovery: https://<tenant>.ciamlogin.com/<tenantId>/v2.0/.well-known/openid-configuration
// (validado contra docs Microsoft Entra External ID — AC-19). O issuer emitido pelo
// CIAM em tokens v2.0 tem a forma https://<tenantId>.ciamlogin.com/<tenantId>/v2.0.
// Permitimos override completo via Jwt:CiamAuthority quando o tenant do instrutor
// usar um subdomínio nomeado (ex.: contoso.ciamlogin.com) em vez do GUID.
var ciamAuthority = !string.IsNullOrWhiteSpace(ciamAuthorityOverride)
    ? ciamAuthorityOverride
    : $"https://{ciamTenantId}.ciamlogin.com/{ciamTenantId}/v2.0";
var ciamIssuerV2 = ciamAuthority.TrimEnd('/');

// --- Config Admin (workforce) — fail-closed ---
var adminTenantId = builder.Configuration["Jwt:AdminTenantId"];
var adminClientId = builder.Configuration["Jwt:AdminClientId"];

if (string.IsNullOrWhiteSpace(adminTenantId) ||
    string.Equals(adminTenantId, "common", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Configuração de identidade do ADMIN ausente/insegura: defina 'Jwt:AdminTenantId' " +
        "com o GUID do tenant workforce do admin (não use 'common'). " +
        "Story 2.11 AC-12/AC-13 / ADE-007 Inv 5.");
}

if (string.IsNullOrWhiteSpace(adminClientId))
{
    throw new InvalidOperationException(
        "Configuração de identidade do ADMIN ausente: defina 'Jwt:AdminClientId' com o " +
        "Application (client) ID da App Registration admin (= audience do token admin). " +
        "Story 2.11 AC-12.");
}

var adminAuthority = $"https://login.microsoftonline.com/{adminTenantId}/v2.0";
var adminIssuerV2 = $"https://login.microsoftonline.com/{adminTenantId}/v2.0";

// Hosts de issuer usados pelo selector para rotear o token ao handler do seu mundo.
const string CiamIssuerHost = "ciamlogin.com";
const string WorkforceIssuerHost = "login.microsoftonline.com";

builder.Services
    // O DEFAULT é o PolicyScheme "Selector": toda autenticação passa por ele, que
    // decide (por issuer) qual handler concreto vai validar o token de fato.
    .AddAuthentication(SelectorScheme)
    .AddJwtBearer(CiamScheme, options =>
    {
        // CLIENTE — discovery do CIAM (ciamlogin.com). JWKS validam a assinatura RS256;
        // iss/aud/lifetime EXPLÍCITOS abaixo (fail-closed, não confia só no metadata).
        options.Authority = ciamAuthority;
        options.Audience = ciamClientId;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = ciamIssuerV2,
            ValidateAudience = true,
            // aud do CIAM pode ser o client id ou o App ID URI (api://<client-id>).
            ValidAudiences = new[] { ciamClientId, $"api://{ciamClientId}" },
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            // Sem tolerância de relógio: token expirado → 401 (AC-6).
            ClockSkew = TimeSpan.Zero
        };
    })
    .AddJwtBearer(AdminScheme, options =>
    {
        // ADMIN — discovery do workforce (login.microsoftonline.com). Mesma mecânica de
        // validação (ADE-004 issuer-agnóstico), só muda a authority/issuer/aud.
        options.Authority = adminAuthority;
        options.Audience = adminClientId;
        options.RequireHttpsMetadata = true;
        // Mantém os nomes de claim originais do token (não renomeia "roles" para a URI
        // longa do WS-Federation). Combinado com RoleClaimType="roles" abaixo, faz o
        // RequireRole("Admin") casar a App Role emitida pelo Entra na claim "roles".
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = adminIssuerV2,
            ValidateAudience = true,
            ValidAudiences = new[] { adminClientId, $"api://{adminClientId}" },
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            // O Entra emite App Roles na claim "roles" (id-token-claims-reference). Mapeia
            // "roles" como o role claim type para que RequireRole("Admin") seja satisfeito.
            RoleClaimType = "roles",
            ClockSkew = TimeSpan.Zero
        };
    })
    .AddPolicyScheme(SelectorScheme, "Bearer issuer selector", options =>
    {
        // Para CADA request: lê o issuer NÃO-validado do bearer e encaminha ao handler
        // concreto do mundo correto. A validação real (assinatura/iss/aud/lifetime) é
        // feita pelo handler escolhido — o selector NÃO confia no issuer lido aqui, só
        // o usa para ROTEAR. Se nada casar, default = caminho do cliente (CIAM).
        options.ForwardDefaultSelector = httpContext =>
        {
            var authHeader = httpContext.Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader["Bearer ".Length..].Trim();
                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var issuer = handler.ReadJwtToken(token).Issuer ?? string.Empty;
                    if (issuer.Contains(CiamIssuerHost, StringComparison.OrdinalIgnoreCase))
                    {
                        return CiamScheme;
                    }
                    if (issuer.Contains(WorkforceIssuerHost, StringComparison.OrdinalIgnoreCase))
                    {
                        return AdminScheme;
                    }
                }
            }

            // Default: caminho do cliente (CIAM). Token ausente/ilegível cai aqui e o
            // handler CIAM responde 401 (fail-closed — nenhuma rota fica anônima).
            return CiamScheme;
        };
    });

// Autorização: a policy default exige usuário autenticado por QUALQUER um dos dois
// handlers concretos (o selector já roteou). Uma rota administrativa separada usa a
// policy "AdminOnly", que exige o esquema Admin E a claim de role "Admin" (App Role
// construída hands-on no Bloco 3 — ADE-007 Inv 5; decisão do owner: única role "Admin").
const string AdminRole = "Admin";
const string AdminOnlyPolicy = "AdminOnly";
const string CiamOnlyPolicy = "CiamOnly";  // Story 3.5 / ADE-007 v1.3 Inv 8.2 — fence do /api/v2/me
builder.Services.AddAuthorization(options =>
{
    // Default (rotas v2 do cliente): basta estar autenticado (CIAM ou Admin).
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
            CiamScheme, AdminScheme)
        .RequireAuthenticatedUser()
        .Build();

    // AdminOnly: rota administrativa. Exige usuário autenticado E a App Role "Admin".
    // NÃO fixamos o esquema aqui de propósito: deixamos o PolicyScheme selector
    // autenticar (CIAM→Ciam, workforce→Admin). Assim:
    //   - token workforce com role "Admin"  → autenticado + role presente → 200;
    //   - token CIAM (cliente) válido        → AUTENTICADO mas sem a role → 403 (não 401);
    //   - sem token / token inválido         → não autenticado → 401.
    // Só o esquema Admin (workforce) carrega a claim "roles"; um cliente CIAM nunca a
    // tem, então a separação dos dois mundos é preservada (a App Role só existe no
    // workforce — ADE-007 Inv 5). Fixar AddAuthenticationSchemes(Admin) daria 401 (e
    // não 403) ao cliente CIAM, perdendo a distinção autenticado-mas-não-autorizado.
    options.AddPolicy(AdminOnlyPolicy, policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole(AdminRole));

    // Story 3.5 (ADE-007 v1.3 Invariante 8.2) — FENCE CiamOnly: o /api/v2/me
    // (resolve-or-provision) DEVE ser autorizado SÓ para o MUNDO CIAM (cliente), NUNCA para o
    // Admin (workforce). Espelha a estratégia da AdminOnly — que discrimina por um requisito
    // INDEPENDENTE DE ESQUEMA (RequireRole), não fixando o esquema.
    //
    // Por que NÃO basta fixar o esquema (AddAuthenticationSchemes(Ciam)): a rota /me também
    // herda a DefaultPolicy do blanket MapReverseProxy().RequireAuthorization(), e o ASP.NET
    // COMBINA as duas policies UNINDO os esquemas de autenticação — o AdminScheme volta pela
    // DefaultPolicy e um token workforce autentica por ele, satisfazendo RequireAuthenticatedUser.
    // Fixar o esquema, portanto, não exclui o admin.
    //
    // O discriminador correto é o ISSUER — a MESMA distinção que o PolicyScheme selector usa
    // (ciamlogin.com vs login.microsoftonline.com). CiamOnly exige que a identidade autenticada
    // tenha sido emitida pelo issuer CIAM: um token workforce (mesmo autenticado pelo esquema
    // Admin via o merge) tem issuer login.microsoftonline.com → NÃO satisfaz → 403. Um token
    // CIAM válido tem issuer ciamlogin.com → satisfaz → 200.
    //
    // POR QUE o fence importa: o transform que injeta X-Entra-OID é GLOBAL. Sem CiamOnly, um
    // token de admin satisfaria /api/v2/me, o gateway injetaria o oid do OPERADOR como
    // X-Entra-OID, e o resolve-or-provision criaria/vincularia uma linha de CLIENTE com a
    // identidade do admin — corrompendo a base (e, pelo arm de LINK por email, potencialmente
    // atando a linha de um cliente real ao operador).
    options.AddPolicy(CiamOnlyPolicy, policy =>
        policy.RequireAuthenticatedUser()
            .RequireAssertion(ctx =>
            {
                // O issuer aparece como o claim "iss" (surfaçado pelo handler) e, de forma
                // redundante, taggeado em CADA claim validada (Claim.Issuer = issuer do token).
                // Checamos os dois — só o mundo CIAM (ciamlogin.com) passa.
                var user = ctx.User;
                var iss = user.FindFirst("iss")?.Value;
                if (!string.IsNullOrEmpty(iss))
                {
                    return iss.Contains(CiamIssuerHost, StringComparison.OrdinalIgnoreCase);
                }
                return user.Claims.Any(c =>
                    c.Issuer.Contains(CiamIssuerHost, StringComparison.OrdinalIgnoreCase));
            }));
});

// Observabilidade de borda (AC-11 / ADE-000 Inv 5) — App Insights se a connection
// string estiver presente (APPLICATIONINSIGHTS_CONNECTION_STRING). No-op sem ela.
builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();

// Pipeline na ORDEM correta (Task 2.6 / ADE-004; REORDENADO na Story 4.4 — cache PÓS-AUTH):
app.UseForwardedHeaders();        // 0. Reescreve RemoteIpAddress a partir do X-Forwarded-For
                                  //    do ingress ANTES do rate-limiter (que particiona por IP).
app.UseCors(CorsPolicy);          // 1. CORS
app.UseRateLimiter();             // 2. Rate limiter (429) — agora vê o IP real do cliente
app.UseAuthentication();          // 3. Authentication (selector roteia CIAM vs Admin)
app.UseAuthorization();           // 4. Authorization
// 5. Cache de borda (30s) + X-Cache HIT/MISS (AC-6) — DEPOIS de auth (Story 4.4): um HIT
//    só é servido/armazenado após UseAuthentication/UseAuthorization validarem a request,
//    fechando o bypass em que um HIT servia o status de uma compra sem token (ADE-009).
app.UseMiddleware<XCacheMiddleware>();

// Endpoint de saúde para smoke test / Container App health probe.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "gateway-yarp" }));

// Story 2.11 / Quartas (AC-12/AC-13) — rota administrativa demonstrativa protegida pela
// policy "AdminOnly" (esquema Admin/workforce + App Role "Admin"). Mostra a separação
// dos dois mundos NO PRÓPRIO gateway: um token CIAM (cliente), mesmo válido, é roteado
// pelo selector ao esquema Ciam e NÃO satisfaz AdminOnly → 403. Só um token workforce
// com role "Admin" passa. As rotas de cliente (proxy abaixo) seguem na DefaultPolicy
// (qualquer um dos dois esquemas autenticado). Em produção, rotas admin reais do proxy
// usariam .RequireAuthorization(AdminOnlyPolicy) no cluster correspondente.
app.MapGet("/admin/ping", () => Results.Ok(new { status = "ok", scope = "admin" }))
    .RequireAuthorization(AdminOnlyPolicy);

// 6. MapReverseProxy com rate-limit em todas as rotas, cache na rota GET e EXIGÊNCIA
//    de JWT válido (CIAM OU workforce — DefaultPolicy) em todas as rotas v2.
//    Sem Bearer válido → 401 (UseAuthentication/UseAuthorization rejeitam antes do
//    proxy). Token expirado/issuer errado/aud errado → 401 (AC-6). O selector escolhe
//    o handler do issuer do token (issuer-agnóstico — ADE-004 Inv 4 / ADE-007 Inv 2).
app.MapReverseProxy()
    .RequireRateLimiting(RateLimiterPolicy)
    .RequireAuthorization();

app.Run();

// Necessário para WebApplicationFactory<Program> nos testes de integração (Task de testes).
public partial class Program { }
