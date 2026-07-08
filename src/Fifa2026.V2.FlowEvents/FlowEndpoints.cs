using System.Security.Cryptography;
using System.Text;
using Fifa2026.V2.FlowEvents.Data;
using Fifa2026.V2.FlowEvents.Hubs;

namespace Fifa2026.V2.FlowEvents;

/// <summary>
/// AC-3/AC-5/AC-6 — endpoints HTTP do FlowEvents service:
///   GET  /api/flow/recent           → últimas N compras (lista do front, AC-5)
///   GET  /api/flow/{correlationId}  → timeline de eventos (fallback polling 2s, AC-6)
///   POST /api/flow/{correlationId}/replay → relê a timeline e a empurra via SignalR
///                                            (anima a bolinha em tempo real, AC-6)
///   GET  /api/flow/diploma-summary  → Story 4.6 (Diploma vivo): correlation-IDs ESCOPADOS
///                                     ao aluno (customDimensions.UserId) + região do
///                                     ambiente (backend-resolved) — infalsificável (AC-2/3/6)
///
/// O serviço fica ATRÁS do gateway YARP (rota nova flow-events) — o gateway valida o
/// Bearer Entra (ADE-004/ADE-005). Este serviço não revalida o JWT.
/// </summary>
public static class FlowEndpoints
{
    // EPIC-004 Story 4.6 §Emenda MEDIUM-4 (ADE-009 v1.1) — header/config da prova de proveniência
    // route-scoped do diploma-summary. O X-Diploma-Key reusa o MESMO valor do GATEWAY_SHARED_SECRET
    // (segredo simétrico da Fase 9), injetado pelo gateway sob header DISTINTO SÓ nesta rota.
    private const string DiplomaKeyHeader = "X-Diploma-Key";
    private const string DiplomaSecretConfigKey = "DiplomaSharedSecret";

    public static void MapFlowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/flow");

        // AC-5 — lista das últimas compras (default 50, máx 200).
        group.MapGet("/recent", async (
            IFlowEventRepository repository,
            CancellationToken cancellationToken,
            int? top) =>
        {
            var limit = Math.Clamp(top ?? 50, 1, 200);
            var purchases = await repository.GetRecentPurchasesAsync(limit, cancellationToken);
            return Results.Ok(purchases);
        });

        // AC-6 — timeline completa (usada como fallback de polling se o WebSocket falhar).
        group.MapGet("/{correlationId}", async (
            string correlationId,
            IFlowEventRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!IsValidCorrelationId(correlationId))
            {
                return Results.BadRequest(new { error = "correlationId inválido (esperado GUID)." });
            }

            var timeline = await repository.GetTimelineAsync(correlationId, cancellationToken);
            return Results.Ok(timeline);
        });

        // AC-6 — relê a timeline e empurra cada evento via SignalR ao grupo correlation-<id>,
        // disparando a animação da bolinha nos clientes assinantes em tempo real.
        group.MapPost("/{correlationId}/replay", async (
            string correlationId,
            IFlowEventRepository repository,
            IFlowEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            if (!IsValidCorrelationId(correlationId))
            {
                return Results.BadRequest(new { error = "correlationId inválido (esperado GUID)." });
            }

            var timeline = await repository.GetTimelineAsync(correlationId, cancellationToken);
            foreach (var flowEvent in timeline)
            {
                await publisher.PublishAsync(flowEvent, cancellationToken);
            }

            return Results.Ok(new { correlationId, pushed = timeline.Count });
        });

        // Story 4.6 (Diploma vivo) AC-2/AC-3/AC-6 — resumo da telemetria de UM aluno:
        //   region          → resolvida no backend (env do ambiente) — nunca fabricada no cliente
        //   correlationIds  → filtrados por customDimensions.UserId (reúso da MESMA fonte App
        //                     Insights do F6 — AC-2); a RESPOSTA não contém PII de terceiros
        //                     (só GUIDs opacos + region + count)
        //   count           → correlationIds.Length (a "narrativa é o número", spec UX §1.4)
        // deployTime NÃO nasce aqui: é injetado no BUILD do frontend (VITE_BUILD_TIME) — outro
        // metadado de origem confiável (o runner do CI), não o navegador (AC-3).
        //
        // POSTURA (protegida — EPIC-004 Story 4.6 §Emenda MEDIUM-4 / ADE-009 v1.1): diferente do
        // resto do FlowEvents (recent/timeline/replay/SignalR, que seguem anônimos como MEDIUM-1
        // aceito), ESTE endpoint exige DUAS provas que um chamador anônimo da internet não tem:
        //   (1) IDENTIDADE — via gateway o blanket RequireAuthorization barra o anônimo (Bearer);
        //   (2) PROVENIÊNCIA — o gateway injeta X-Diploma-Key (reuso do GATEWAY_SHARED_SECRET sob
        //       header distinto, route-scoped) e aqui validamos em tempo constante.
        // Semântica fail-closed/legado idêntica ao GatewayKeyValidator (Story 4.2): DiplomaSharedSecret
        // configurado → header ausente/divergente = 401; VAZIO → bypass legado (dev local + estado
        // pré-provisionamento). O cluster flow-events continua FORA do X-Gateway-Key (header distinto;
        // FlowEvents_Cluster_Does_NOT_Receive_GatewayKey segue verde). Residual aceito: aluno
        // AUTENTICADO pode passar ?userId=<outro> e ler GUIDs opacos+count (zero PII) — igual/menor
        // que o /recent; escopo infalsificável-por-identidade = Opção D (fast-follow). Detalhe em
        // docs/security/final-security-debt.md (MEDIUM-4, fechado pela Opção F).
        group.MapGet("/diploma-summary", async (
            string userId,
            HttpRequest request,
            IFlowEventRepository repository,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (!IsValidUserId(userId))
            {
                return Results.BadRequest(new { error = "userId inválido (esperado inteiro positivo do usuário v1)." });
            }

            // Emenda MEDIUM-4 — prova de proveniência route-scoped (só o gateway conhece o segredo).
            var diplomaSecret = configuration[DiplomaSecretConfigKey];
            if (!string.IsNullOrEmpty(diplomaSecret)
                && !IsTrustedDiplomaKey(request.Headers[DiplomaKeyHeader].ToString(), diplomaSecret))
            {
                return Results.Unauthorized();
            }

            var purchases = await repository.GetPurchasesByUserAsync(userId, top: 50, cancellationToken);
            var correlationIds = purchases.Select(p => p.CorrelationId).ToArray();

            // Região do ambiente (App Setting explícito do deploy, ou a convenção REGION_NAME do
            // App Service). Ausente → null: o front degrada graciosamente (nunca inventa um valor).
            var region = configuration["DeployRegion"] ?? configuration["REGION_NAME"];

            return Results.Ok(new
            {
                region,
                correlationIds,
                count = correlationIds.Length
            });
        });
    }

    /// <summary>O userId v1 é sempre um inteiro positivo (1..12 dígitos). Guard anti-abuso.</summary>
    private static bool IsValidUserId(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 12
           && value.All(char.IsAsciiDigit);

    /// <summary>O correlationId é sempre um GUID (gerado pelo gateway YARP — nó zero).</summary>
    private static bool IsValidCorrelationId(string value) => Guid.TryParse(value, out _);

    /// <summary>
    /// Emenda MEDIUM-4 — compara o X-Diploma-Key recebido com o segredo configurado em TEMPO
    /// CONSTANTE (anti timing-attack), MESMA semântica do GatewayKeyValidator das Functions
    /// (Story 4.2). <see cref="CryptographicOperations.FixedTimeEquals"/> retorna false para
    /// tamanhos diferentes SEM lançar. Header ausente/vazio → false (fail-closed quando o
    /// segredo está armado). Só é chamado quando o segredo NÃO está vazio (o bypass legado é
    /// decidido no handler antes desta chamada).
    /// </summary>
    private static bool IsTrustedDiplomaKey(string? incomingKey, string configuredSecret)
    {
        if (string.IsNullOrEmpty(incomingKey))
        {
            return false;
        }

        var incomingBytes = Encoding.UTF8.GetBytes(incomingKey);
        var secretBytes = Encoding.UTF8.GetBytes(configuredSecret);
        return CryptographicOperations.FixedTimeEquals(incomingBytes, secretBytes);
    }
}
