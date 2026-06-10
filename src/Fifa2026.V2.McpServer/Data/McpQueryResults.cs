using System.Text.Json.Serialization;

namespace Fifa2026.V2.McpServer.Data;

/// <summary>
/// AC-3 — resultado de <c>consultar_disponibilidade</c>. Disponibilidade e preço por
/// categoria de uma partida (tabela ticket_categories — schema real do projeto).
/// </summary>
public sealed class AvailabilityResult
{
    [JsonPropertyName("encontrado")]
    public bool Encontrado { get; init; }

    [JsonPropertyName("partida")]
    public string? Partida { get; init; }

    [JsonPropertyName("vipDisponivel")]
    public int VipDisponivel { get; init; }

    [JsonPropertyName("cat1Disponivel")]
    public int Cat1Disponivel { get; init; }

    [JsonPropertyName("cat2Disponivel")]
    public int Cat2Disponivel { get; init; }

    [JsonPropertyName("precoVip")]
    public decimal? PrecoVip { get; init; }

    [JsonPropertyName("precoCat1")]
    public decimal? PrecoCat1 { get; init; }

    [JsonPropertyName("precoCat2")]
    public decimal? PrecoCat2 { get; init; }
}

/// <summary>
/// AC-4 — resultado de <c>verificar_ingresso</c>. Validade de um ingresso (compra) e
/// dados associados (tabelas purchases + ticket_categories + matches + users).
/// </summary>
public sealed class TicketVerificationResult
{
    [JsonPropertyName("valido")]
    public bool Valido { get; init; }

    [JsonPropertyName("comprador")]
    public string? Comprador { get; init; }

    [JsonPropertyName("partida")]
    public string? Partida { get; init; }

    [JsonPropertyName("categoria")]
    public string? Categoria { get; init; }

    [JsonPropertyName("dataCompra")]
    public DateTime? DataCompra { get; init; }
}

/// <summary>
/// AC-5 — uma linha do bracket (jogo de uma rodada). Times podem ser NULL no
/// mata-mata até a classificação (ver migration knockout-matches). Placar NULL
/// quando o jogo ainda não foi disputado.
/// </summary>
public sealed class BracketMatchResult
{
    [JsonPropertyName("jogo")]
    public string Jogo { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public DateTime Data { get; init; }

    [JsonPropertyName("horario")]
    public string? Horario { get; init; }

    [JsonPropertyName("estadio")]
    public string? Estadio { get; init; }

    [JsonPropertyName("placarMandante")]
    public int? PlacarMandante { get; init; }

    [JsonPropertyName("placarVisitante")]
    public int? PlacarVisitante { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Story 2.8 AC-1 — uma partida da Copa 2026 (qualquer fase) com filtros flexíveis.
/// Fonte: matches LEFT JOIN teams (mandante/visitante) LEFT JOIN stadiums. Times
/// podem ser NULL no mata-mata → COALESCE 'A definir' no SQL. Placar NULL quando o
/// jogo ainda não foi disputado (home_score IS NULL).
/// </summary>
public sealed class MatchResult
{
    [JsonPropertyName("partida")]
    public string Partida { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public DateTime Data { get; init; }

    [JsonPropertyName("horario")]
    public string? Horario { get; init; }

    [JsonPropertyName("estadio")]
    public string? Estadio { get; init; }

    [JsonPropertyName("fase")]
    public string Fase { get; init; } = string.Empty;

    [JsonPropertyName("grupo")]
    public string? Grupo { get; init; }

    [JsonPropertyName("placarMandante")]
    public int? PlacarMandante { get; init; }

    [JsonPropertyName("placarVisitante")]
    public int? PlacarVisitante { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Story 2.8 AC-2 — uma linha da classificação de um grupo (calculada por agregação
/// dos resultados de matches; NÃO existe tabela standings no schema real). Antes de
/// qualquer jogo disputado no grupo, a lista vem vazia (decisão PO — AC-2).
/// </summary>
public sealed class StandingRow
{
    [JsonPropertyName("posicao")]
    public int Posicao { get; init; }

    [JsonPropertyName("time")]
    public string Time { get; init; } = string.Empty;

    [JsonPropertyName("jogos")]
    public int Jogos { get; init; }

    [JsonPropertyName("vitorias")]
    public int Vitorias { get; init; }

    [JsonPropertyName("empates")]
    public int Empates { get; init; }

    [JsonPropertyName("derrotas")]
    public int Derrotas { get; init; }

    [JsonPropertyName("golsPro")]
    public int GolsPro { get; init; }

    [JsonPropertyName("golsContra")]
    public int GolsContra { get; init; }

    [JsonPropertyName("saldo")]
    public int Saldo { get; init; }

    [JsonPropertyName("pontos")]
    public int Pontos { get; init; }
}

/// <summary>
/// Story 2.8 AC-3 — dados de uma seleção (tabela teams). Encontrado = false quando
/// nenhuma linha casa por nome/código.
/// </summary>
public sealed class TeamResult
{
    [JsonPropertyName("encontrado")]
    public bool Encontrado { get; init; }

    [JsonPropertyName("nome")]
    public string? Nome { get; init; }

    [JsonPropertyName("codigo")]
    public string? Codigo { get; init; }

    [JsonPropertyName("grupo")]
    public string? Grupo { get; init; }

    [JsonPropertyName("confederacao")]
    public string? Confederacao { get; init; }

    [JsonPropertyName("rankingFifa")]
    public int? RankingFifa { get; init; }

    [JsonPropertyName("bandeira")]
    public string? Bandeira { get; init; }
}

/// <summary>
/// Story 2.8 AC-4 — dados de um estádio/sede (tabela stadiums). Encontrado = false
/// quando nenhuma linha casa por nome ou cidade.
/// </summary>
public sealed class StadiumResult
{
    [JsonPropertyName("encontrado")]
    public bool Encontrado { get; init; }

    [JsonPropertyName("nome")]
    public string? Nome { get; init; }

    [JsonPropertyName("cidade")]
    public string? Cidade { get; init; }

    [JsonPropertyName("pais")]
    public string? Pais { get; init; }

    [JsonPropertyName("capacidade")]
    public int? Capacidade { get; init; }

    [JsonPropertyName("descricao")]
    public string? Descricao { get; init; }
}
