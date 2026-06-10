using System.ComponentModel;
using Fifa2026.V2.McpServer.Data;
using ModelContextProtocol.Server;

namespace Fifa2026.V2.McpServer.Tools;

/// <summary>
/// AC-2/3/4/5 — as 3 tools MCP do FIFA 2026 Tickets, expostas via o SDK oficial
/// (ADE-002 Inv 1/2). O atributo <see cref="McpServerToolTypeAttribute"/> faz o
/// SDK descobrir a classe (WithToolsFromAssembly em Program.cs); cada método com
/// <see cref="McpServerToolAttribute"/> vira uma tool listada em <c>tools/list</c>
/// e despachada em <c>tools/call</c> — o framing JSON-RPC 2.0 é do SDK (não
/// implementamos à mão; AC-15 anti-hallucination).
///
/// O JSON Schema de input de cada tool é DERIVADO pelo SDK a partir da assinatura
/// do método + atributos [Description] (System.ComponentModel) — não inventamos
/// schema manual. Dependências (repositório, contexto de oid) são INJETADAS por DI
/// nos parâmetros do método (o SDK resolve do IServiceProvider da request).
///
/// Identidade (AC-9): cada tool lê X-Entra-OID via EntraOidContext SOMENTE para
/// logging mascarado — nunca revalida JWT (gateway é o guardião).
/// </summary>
[McpServerToolType]
public static class FifaTicketTools
{
    [McpServerTool(Name = "consultar_disponibilidade", ReadOnly = true)]
    [Description(
        "Consulta disponibilidade e preços de ingressos para uma partida da Copa 2026. " +
        "Use quando o usuário perguntar se há ingressos para um jogo ou quanto custam. " +
        "Informe matchId (numérico) OU matchDescription (ex.: 'Brasil x Argentina').")]
    public static async Task<AvailabilityResult> ConsultarDisponibilidadeAsync(
        IFifaQueryRepository repository,
        EntraOidContext oidContext,
        ILogger<DiagnosticsCategory> logger,
        [Description("ID numérico da partida (opcional se matchDescription for informado).")]
        int? matchId = null,
        [Description("Descrição da partida, ex.: 'Brasil x Argentina' (opcional se matchId for informado).")]
        string? matchDescription = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "tool=consultar_disponibilidade oid={Oid} matchId={MatchId}",
            oidContext.GetMaskedOidForLog(), matchId);

        return await repository.ConsultarDisponibilidadeAsync(matchId, matchDescription, cancellationToken);
    }

    [McpServerTool(Name = "verificar_ingresso", ReadOnly = true)]
    [Description(
        "Verifica se um ingresso é válido e retorna dados da compra (comprador, partida, " +
        "categoria, data). Use quando o usuário perguntar se um ingresso/ID é válido.")]
    public static async Task<TicketVerificationResult> VerificarIngressoAsync(
        IFifaQueryRepository repository,
        EntraOidContext oidContext,
        ILogger<DiagnosticsCategory> logger,
        [Description("ID numérico do ingresso (compra) a verificar.")]
        int ingressoId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "tool=verificar_ingresso oid={Oid} ingressoId={IngressoId}",
            oidContext.GetMaskedOidForLog(), ingressoId);

        return await repository.VerificarIngressoAsync(ingressoId, cancellationToken);
    }

    [McpServerTool(Name = "consultar_bracket", ReadOnly = true)]
    [Description(
        "Consulta os jogos de uma rodada do mata-mata (oitavas, quartas, semifinal, final) " +
        "com placares e classificados. Use quando o usuário perguntar sobre confrontos/resultados de uma fase.")]
    public static async Task<IReadOnlyList<BracketMatchResult>> ConsultarBracketAsync(
        IFifaQueryRepository repository,
        EntraOidContext oidContext,
        ILogger<DiagnosticsCategory> logger,
        [Description("Rodada do mata-mata, ex.: 'oitavas', 'quartas', 'semifinal', 'final'.")]
        string rodada,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "tool=consultar_bracket oid={Oid} rodada={Rodada}",
            oidContext.GetMaskedOidForLog(), rodada);

        return await repository.ConsultarBracketAsync(rodada, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Story 2.8 — Fase A "Sentidos completos": 4 novas tools read-only que
    // permitem ao chatbot explorar todo o sistema da Copa (partidas, classificação,
    // times e estádios). Mesmo padrão das 3 acima: método estático, dependências por
    // DI, [Description] PT-BR rica (Gemini usa o texto p/ escolher a tool), ReadOnly
    // = true (regra de ouro ADE-006 Inv 1 — o McpServer nunca grava), oid só p/ log.
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "consultar_partidas", ReadOnly = true)]
    [Description(
        "Consulta partidas da Copa 2026 com filtros flexíveis. Use para perguntas como " +
        "'jogos do Brasil', 'jogos nas oitavas', 'jogos no Maracanã', 'jogos do grupo C' " +
        "ou 'jogos do dia 15/06'. Retorna o placar quando o jogo já foi disputado.")]
    public static async Task<IReadOnlyList<MatchResult>> ConsultarPartidasAsync(
        IFifaQueryRepository repository,
        EntraOidContext oidContext,
        ILogger<DiagnosticsCategory> logger,
        [Description("Nome ou código do time, ex.: 'Brasil'/'BRA' (opcional).")]
        string? time = null,
        [Description("Fase em linguagem natural: 'grupos', 'oitavas', 'quartas', 'semifinal', 'final' (opcional).")]
        string? fase = null,
        [Description("Nome do estádio ou cidade-sede, ex.: 'Maracanã'/'Rio de Janeiro' (opcional).")]
        string? estadio = null,
        [Description("Letra do grupo da fase de grupos, ex.: 'A'..'L' (opcional).")]
        string? grupo = null,
        [Description("Data no formato ISO YYYY-MM-DD, ex.: '2026-06-15' (opcional).")]
        string? data = null,
        [Description("Se true, retorna apenas jogos já disputados (com placar) (opcional).")]
        bool? apenasComResultado = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "tool=consultar_partidas oid={Oid} time={Time} fase={Fase} estadio={Estadio} grupo={Grupo} data={Data}",
            oidContext.GetMaskedOidForLog(), time, fase, estadio, grupo, data);

        return await repository.ConsultarPartidasAsync(
            time, fase, estadio, grupo, data, apenasComResultado, cancellationToken);
    }

    [McpServerTool(Name = "consultar_classificacao", ReadOnly = true)]
    [Description(
        "Consulta a classificação (tabela de pontos) de um grupo da fase de grupos da " +
        "Copa 2026. Calculada a partir dos resultados dos jogos do grupo. Grupos cujos " +
        "jogos ainda não foram disputados retornam lista vazia (ainda sem jogos disputados).")]
    public static async Task<IReadOnlyList<StandingRow>> ConsultarClassificacaoAsync(
        IFifaQueryRepository repository,
        EntraOidContext oidContext,
        ILogger<DiagnosticsCategory> logger,
        [Description("Letra do grupo, ex.: 'A', 'B', ... 'L'.")]
        string grupo,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "tool=consultar_classificacao oid={Oid} grupo={Grupo}",
            oidContext.GetMaskedOidForLog(), grupo);

        return await repository.ConsultarClassificacaoAsync(grupo, cancellationToken);
    }

    [McpServerTool(Name = "consultar_time", ReadOnly = true)]
    [Description(
        "Consulta informações de uma seleção da Copa 2026 (grupo, confederação, ranking FIFA, código). " +
        "Use para perguntas como 'fala sobre o Brasil' ou 'em que grupo está a Argentina?'.")]
    public static async Task<TeamResult> ConsultarTimeAsync(
        IFifaQueryRepository repository,
        EntraOidContext oidContext,
        ILogger<DiagnosticsCategory> logger,
        [Description("Nome ou código da seleção, ex.: 'Brasil' ou 'BRA'.")]
        string nome,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "tool=consultar_time oid={Oid} nome={Nome}",
            oidContext.GetMaskedOidForLog(), nome);

        return await repository.ConsultarTimeAsync(nome, cancellationToken);
    }

    [McpServerTool(Name = "consultar_estadio", ReadOnly = true)]
    [Description(
        "Consulta informações de um estádio/sede da Copa 2026 (cidade, país, capacidade, descrição). " +
        "Use para perguntas como 'me fala do Maracanã' ou 'qual a capacidade do estádio do Rio?'.")]
    public static async Task<StadiumResult> ConsultarEstadioAsync(
        IFifaQueryRepository repository,
        EntraOidContext oidContext,
        ILogger<DiagnosticsCategory> logger,
        [Description("Nome do estádio OU cidade-sede, ex.: 'Maracanã' ou 'Rio de Janeiro'.")]
        string nome,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "tool=consultar_estadio oid={Oid} nome={Nome}",
            oidContext.GetMaskedOidForLog(), nome);

        return await repository.ConsultarEstadioAsync(nome, cancellationToken);
    }

    /// <summary>Marcador de categoria para o ILogger das tools (mantém logs agrupados).</summary>
    public sealed class DiagnosticsCategory { }
}
