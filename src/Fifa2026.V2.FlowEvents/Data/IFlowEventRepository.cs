using Fifa2026.V2.FlowEvents.Models;

namespace Fifa2026.V2.FlowEvents.Data;

/// <summary>
/// Abstração da fonte de eventos do fluxo. A implementação real consulta o App
/// Insights (Log Analytics) por correlationId; os testes injetam um fake.
/// </summary>
public interface IFlowEventRepository
{
    /// <summary>
    /// AC-3/AC-4 — retorna a timeline de eventos (ordenada por timestamp asc) para um
    /// correlationId, derivada dos traces do App Insights de TODOS os 6 componentes.
    /// </summary>
    Task<IReadOnlyList<FlowEvent>> GetTimelineAsync(string correlationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-5 — últimas N compras v2 (correlationId, timestamp, status) para a lista do front.
    /// </summary>
    Task<IReadOnlyList<RecentPurchase>> GetRecentPurchasesAsync(int top, CancellationToken cancellationToken = default);

    /// <summary>
    /// Story 4.6 (Diploma vivo) AC-2/AC-6 — últimas N compras ESCOPADAS a um único aluno.
    /// Escopo = <c>customDimensions.UserId</c> (o userId v1 que o <c>PurchaseEntryFunction</c>
    /// já grava como parâmetro estruturado em cada trace de entrada) — nenhuma nova fonte de
    /// verdade: é a MESMA telemetria App Insights/F6, só filtrada pela identidade do aluno.
    /// Retorna somente correlation-IDs (identificadores técnicos, não PII) do próprio usuário;
    /// nenhum dado de OUTRO aluno é acessível por esta consulta (AC-6).
    /// </summary>
    Task<IReadOnlyList<RecentPurchase>> GetPurchasesByUserAsync(string userId, int top, CancellationToken cancellationToken = default);
}
