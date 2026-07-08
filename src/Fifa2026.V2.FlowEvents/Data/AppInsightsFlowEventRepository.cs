using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Fifa2026.V2.FlowEvents.Models;

namespace Fifa2026.V2.FlowEvents.Data;

/// <summary>
/// AC-3/AC-4 — implementação real que consulta o App Insights (Log Analytics workspace
/// que recebe a telemetria dos 6 componentes) por correlationId via SDK OFICIAL
/// Azure.Monitor.Query (LogsQueryClient). A query Kusto filtra
/// customDimensions.CorrelationId == &lt;id&gt; (ADE-000 Inv 5 — BeginScope grava o
/// CorrelationId como customDimension em todas as Functions/Gateway).
///
/// AUTENTICAÇÃO: DefaultAzureCredential (Managed Identity no Container App; az CLI/VS em
/// dev). O workspace id vem do App Setting LogAnalyticsWorkspaceId (nunca hardcoded —
/// ADE-003 Inv 3).
///
/// AC-13 anti-hallucination: LogsQueryClient.QueryWorkspaceAsync(workspaceId, query,
/// timeRange) e LogsQueryResult.Table.Rows são APIs reais do Azure SDK for .NET.
/// </summary>
public sealed class AppInsightsFlowEventRepository : IFlowEventRepository
{
    private readonly LogsQueryClient _client;
    private readonly string _workspaceId;
    private readonly ILogger<AppInsightsFlowEventRepository> _logger;

    public AppInsightsFlowEventRepository(IConfiguration configuration, ILogger<AppInsightsFlowEventRepository> logger)
    {
        _workspaceId = configuration["LogAnalyticsWorkspaceId"]
            ?? throw new InvalidOperationException(
                "App Setting 'LogAnalyticsWorkspaceId' não configurado. Defina o GUID do workspace " +
                "Log Analytics que recebe a telemetria do App Insights (Story 2.6 AC-3).");
        _logger = logger;
        _client = new LogsQueryClient(new DefaultAzureCredential());
    }

    /// <summary>Query Kusto da timeline de um correlationId (validada contra docs.microsoft.com).</summary>
    private const string TimelineQuery = """
        AppTraces
        | where tostring(Properties.CorrelationId) == correlationId
        | project timestamp = TimeGenerated, message = Message, severityLevel = SeverityLevel, cloud_RoleName = AppRoleName, customDimensions = Properties
        | order by timestamp asc
        | limit 100
        """;

    /// <summary>Query Kusto das últimas N compras (traces de entrada do gateway/entry).</summary>
    private const string RecentQuery = """
        AppTraces
        | where isnotempty(tostring(Properties.CorrelationId))
        | summarize timestamp = min(TimeGenerated), maxSeverity = max(SeverityLevel)
            by correlationId = tostring(Properties.CorrelationId)
        | order by timestamp desc
        | limit topN
        """;

    /// <summary>
    /// Story 4.6 (Diploma vivo) — últimas N compras de UM aluno. Filtra
    /// <c>customDimensions.UserId</c> (gravado pelo PurchaseEntryFunction em cada trace de
    /// entrada) além do CorrelationId. É a MESMA query da lista recente, só escopada ao
    /// usuário — nenhuma nova fonte de verdade (reúso do farol F6, AC-2).
    /// </summary>
    private const string ByUserQuery = """
        AppTraces
        | where isnotempty(tostring(Properties.CorrelationId))
        | where tostring(Properties.UserId) == userId
        | summarize timestamp = min(TimeGenerated), maxSeverity = max(SeverityLevel)
            by correlationId = tostring(Properties.CorrelationId)
        | order by timestamp desc
        | limit topN
        """;

    public async Task<IReadOnlyList<FlowEvent>> GetTimelineAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        // Parametrização Kusto via "declare" prefix evita injeção (correlationId é input externo).
        var query = $"declare query_parameters(correlationId:string = '{Sanitize(correlationId)}');\n{TimelineQuery}";

        var response = await _client.QueryWorkspaceAsync(
            _workspaceId,
            query,
            new QueryTimeRange(TimeSpan.FromHours(1)),
            cancellationToken: cancellationToken);

        var table = response.Value.Table;
        var events = new List<FlowEvent>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            var timestamp = row.GetDateTimeOffset("timestamp") ?? DateTimeOffset.UtcNow;
            var message = row.GetString("message");
            var severity = (int)(row.GetInt32("severityLevel") ?? 1);
            var role = row.GetString("cloud_RoleName");

            var eventType = TraceEventMapper.Classify(role, message);
            if (eventType is null)
            {
                continue;
            }

            events.Add(new FlowEvent
            {
                CorrelationId = correlationId,
                EventType = eventType.Value,
                Timestamp = timestamp,
                Status = TraceEventMapper.StatusFromSeverity(severity),
                Message = message
            });
        }

        // Ordena por nó (mantém a ordem visual do diagrama mesmo se a ingestão chegar fora de ordem).
        events.Sort((a, b) => a.NodeIndex.CompareTo(b.NodeIndex));
        _logger.LogInformation("Timeline montada para correlationId com {Count} eventos.", events.Count);
        return events;
    }

    public async Task<IReadOnlyList<RecentPurchase>> GetRecentPurchasesAsync(int top, CancellationToken cancellationToken = default)
    {
        var query = $"declare query_parameters(topN:int = {top});\n{RecentQuery}";

        var response = await _client.QueryWorkspaceAsync(
            _workspaceId,
            query,
            new QueryTimeRange(TimeSpan.FromDays(1)),
            cancellationToken: cancellationToken);

        var table = response.Value.Table;
        var purchases = new List<RecentPurchase>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            var correlationId = row.GetString("correlationId");
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                continue;
            }

            purchases.Add(new RecentPurchase
            {
                CorrelationId = correlationId,
                Timestamp = row.GetDateTimeOffset("timestamp") ?? DateTimeOffset.UtcNow,
                Status = TraceEventMapper.StatusFromSeverity((int)(row.GetInt32("maxSeverity") ?? 1))
            });
        }

        return purchases;
    }

    public async Task<IReadOnlyList<RecentPurchase>> GetPurchasesByUserAsync(string userId, int top, CancellationToken cancellationToken = default)
    {
        // userId v1 é sempre um inteiro positivo; sanitizamos para dígitos antes de interpolar
        // no declare (defesa em profundidade — a parametrização Kusto já isola).
        var query = $"declare query_parameters(topN:int = {top}, userId:string = '{SanitizeDigits(userId)}');\n{ByUserQuery}";

        var response = await _client.QueryWorkspaceAsync(
            _workspaceId,
            query,
            new QueryTimeRange(TimeSpan.FromDays(1)),
            cancellationToken: cancellationToken);

        var table = response.Value.Table;
        var purchases = new List<RecentPurchase>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            var correlationId = row.GetString("correlationId");
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                continue;
            }

            purchases.Add(new RecentPurchase
            {
                CorrelationId = correlationId,
                Timestamp = row.GetDateTimeOffset("timestamp") ?? DateTimeOffset.UtcNow,
                Status = TraceEventMapper.StatusFromSeverity((int)(row.GetInt32("maxSeverity") ?? 1))
            });
        }

        _logger.LogInformation("Resumo do Diploma: {Count} compra(s) do aluno.", purchases.Count);
        return purchases;
    }

    /// <summary>Mantém apenas dígitos [0-9] (o userId v1 é um inteiro positivo).</summary>
    private static string SanitizeDigits(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var n = 0;
        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                buffer[n++] = c;
            }
        }
        return new string(buffer[..n]);
    }

    /// <summary>
    /// Defesa em profundidade: o correlationId é sempre um GUID; removemos qualquer
    /// caractere fora de [0-9a-fA-F-] antes de interpolar no declare (a parametrização
    /// Kusto já isola, mas mantemos sanitização explícita).
    /// </summary>
    private static string Sanitize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var n = 0;
        foreach (var c in value)
        {
            if (Uri.IsHexDigit(c) || c == '-')
            {
                buffer[n++] = c;
            }
        }
        return new string(buffer[..n]);
    }
}
