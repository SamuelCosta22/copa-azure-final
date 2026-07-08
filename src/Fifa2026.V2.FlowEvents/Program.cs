using Fifa2026.V2.FlowEvents;
using Fifa2026.V2.FlowEvents.Data;
using Fifa2026.V2.FlowEvents.Hubs;

// =============================================================================
// Fifa2026.V2.FlowEvents — Flow Visualizer backend (Story 2.6 / F6).
//
// Serviço .NET 8 dedicado (ASP.NET Core, host de longa duração em Container App —
// MESMO padrão de src/Fifa2026.V2.McpServer/) que:
//   1. Consulta o App Insights por correlationId via SDK Azure.Monitor.Query (AC-3).
//   2. Expõe o Hub SignalR FlowHub (Service Mode: Default — Hub clássico, AC-2/AC-3)
//      ligado ao Azure SignalR Service via AddAzureSignalR.
//   3. Empurra os 5 eventos do fluxo (GATEWAY_YARP_RECEIVED → SQL_INSERTED) para o
//      grupo correlation-<id> (AC-6). ADE-008 Inv 5: o nó do n8n saiu (6 → 5 nós).
//
// É um Function-equivalente de longa duração: SignalR Default mode exige um Hub
// clássico hospedado (incompatível com o runtime serverless do Functions), por isso
// o host é ASP.NET Core em Container App (decisão @dev — alinhar com @architect).
//
// O NÓ ZERO do fluxo é o Gateway YARP (ADE-004) — NUNCA APIM (APIM não existe no
// EPIC-002). O gateway injeta X-Correlation-ID; este serviço só LÊ a telemetria.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// AC-3 — repositório que consulta o App Insights (Log Analytics) por correlationId.
builder.Services.AddSingleton<IFlowEventRepository, AppInsightsFlowEventRepository>();

// AC-3/AC-6 — publisher SignalR (push do FlowEvent ao grupo correlation-<id>).
builder.Services.AddSingleton<IFlowEventPublisher, SignalRFlowEventPublisher>();

// AC-2/AC-3 — SignalR + Azure SignalR Service (Service Mode: Default). A connection
// string vem do App Setting AzureSignalRConnectionString (nunca hardcoded). Sem ela,
// AddAzureSignalR lança no startup — fail-fast (config obrigatória, como o gateway).
var signalRConnection = builder.Configuration["AzureSignalRConnectionString"]
    ?? builder.Configuration.GetConnectionString("AzureSignalR");

var signalRBuilder = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(signalRConnection))
{
    signalRBuilder.AddAzureSignalR(signalRConnection);
}
// Sem connection string (ex.: dev local sem SignalR Service), o Hub roda self-hosted
// (in-proc) — útil para smoke local; em produção a connection string é obrigatória.

// AC-9/AC-6 — CORS para o frontend (origin restrito; o WebSocket SignalR exige credentials).
var frontendOrigin = builder.Configuration["FrontendOrigin"]
    ?? "https://fifa2026-web.azurewebsites.net";
const string CorsPolicy = "frontend";
builder.Services.AddCors(options =>
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(frontendOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

// Observabilidade de borda (ADE-000 Inv 5) — no-op sem APPLICATIONINSIGHTS_CONNECTION_STRING.
builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();

// Emenda MEDIUM-4 (ADE-009 v1.1) — SINAL de configuração esquecível. Em produção (não-Development),
// um `DiplomaSharedSecret` VAZIO significa que o `/api/flow/diploma-summary` cai no BYPASS legado
// (anônimo) — reabrindo o MEDIUM-4 SEM nenhum sinal (via gateway tudo aparenta ok). O owner
// rejeitou aceitar esse débito → aqui emitimos um WARNING alto no startup (visível nos logs do
// Container App). Fail-LOUD, não fail-closed: NÃO derrubamos o serviço (recent/timeline/SignalR
// seguem, e o pré-provisionamento pode ainda não ter armado o segredo). Em Development o vazio é
// intencional (dev local) — nenhum aviso. Não expomos isso no /health público (ingress externo)
// para não sinalizar a um atacante que o endpoint está aberto — o sinal é operator-facing (log).
if (!app.Environment.IsDevelopment()
    && string.IsNullOrEmpty(app.Configuration["DiplomaSharedSecret"]))
{
    app.Logger.LogWarning(
        "DiplomaSharedSecret VAZIO em ambiente '{Environment}': GET /api/flow/diploma-summary está "
        + "ANÔNIMO (bypass legado — débito MEDIUM-4 reaberto). Configure o App Setting "
        + "DiplomaSharedSecret no ca-flow como Key Vault reference ao gateway-admin-shared-secret "
        + "(guia final-portal-guide.md, Fase 7.4) para armar a validação X-Diploma-Key.",
        app.Environment.EnvironmentName);
}

app.UseCors(CorsPolicy);

// Health endpoint (paridade com gateway/McpServer — smoke test / Container App probe).
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "flow-events" }));

// AC-3/AC-6 — Hub SignalR na rota /hubs/flow (o front conecta aqui com fallback polling).
app.MapHub<FlowHub>("/hubs/flow");

// AC-3/AC-5/AC-6 — endpoints REST (timeline, recent, replay).
app.MapFlowEndpoints();

app.Run();

// Necessário para WebApplicationFactory<Program> nos testes de integração.
public partial class Program { }
