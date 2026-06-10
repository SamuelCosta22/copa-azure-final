namespace Fifa2026.V2.McpServer.Data;

/// <summary>
/// Story 2.5 Dev Notes — abstração de acesso a dados (SOMENTE leitura) para as 3
/// tools MCP. Análoga a <c>IPurchaseRepository</c> de src/Fifa2026.V2.Functions/Data/.
/// Interface para permitir mock do data layer nos testes (AC testing).
///
/// TODAS as implementações usam queries parametrizadas (Dapper) — anti SQL injection.
/// </summary>
public interface IFifaQueryRepository
{
    /// <summary>
    /// AC-3 — disponibilidade/preços de uma partida. Resolve por matchId (preferencial)
    /// ou por matchDescription ("Mandante x Visitante" — match aproximado por nome de time).
    /// Retorna <see cref="AvailabilityResult.Encontrado"/> = false se nada casar.
    /// </summary>
    Task<AvailabilityResult> ConsultarDisponibilidadeAsync(
        int? matchId,
        string? matchDescription,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-4 — verifica um ingresso (compra) por id. Retorna
    /// <see cref="TicketVerificationResult.Valido"/> = false quando o id não existe.
    /// </summary>
    Task<TicketVerificationResult> VerificarIngressoAsync(
        int ingressoId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-5 — jogos de uma rodada do mata-mata (placares + status). A <paramref name="rodada"/>
    /// chega em linguagem natural ("oitavas", "quartas") e é mapeada para o valor de
    /// matches.stage (round_of_16, quarter_final, ...).
    /// </summary>
    Task<IReadOnlyList<BracketMatchResult>> ConsultarBracketAsync(
        string rodada,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Story 2.8 AC-1 — partidas da Copa 2026 com filtros flexíveis (todos opcionais).
    /// <paramref name="fase"/> chega em linguagem natural ("grupos", "oitavas", ...) e é
    /// mapeada para matches.stage. <paramref name="data"/> é string ISO YYYY-MM-DD.
    /// Quando <paramref name="apenasComResultado"/> é true, retorna só jogos disputados
    /// (home_score IS NOT NULL). SOMENTE leitura.
    /// </summary>
    Task<IReadOnlyList<MatchResult>> ConsultarPartidasAsync(
        string? time,
        string? fase,
        string? estadio,
        string? grupo,
        string? data,
        bool? apenasComResultado,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Story 2.8 AC-2 — classificação (tabela de pontos) de um grupo, CALCULADA por
    /// agregação dos resultados de matches (não existe tabela standings). Retorna lista
    /// vazia se nenhum jogo do grupo foi disputado (decisão PO). SOMENTE leitura.
    /// </summary>
    Task<IReadOnlyList<StandingRow>> ConsultarClassificacaoAsync(
        string grupo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Story 2.8 AC-3 — dados de uma seleção por nome ou código (tabela teams).
    /// Retorna <see cref="TeamResult.Encontrado"/> = false quando nada casa. SOMENTE leitura.
    /// </summary>
    Task<TeamResult> ConsultarTimeAsync(
        string nome,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Story 2.8 AC-4 — dados de um estádio por nome ou cidade (tabela stadiums).
    /// Retorna <see cref="StadiumResult.Encontrado"/> = false quando nada casa. SOMENTE leitura.
    /// </summary>
    Task<StadiumResult> ConsultarEstadioAsync(
        string nome,
        CancellationToken cancellationToken = default);
}
