using Dapper;
using Microsoft.Data.SqlClient;

namespace Fifa2026.V2.McpServer.Data;

/// <summary>
/// Implementação Dapper + Microsoft.Data.SqlClient (MESMO padrão de
/// src/Fifa2026.V2.Functions/Data/PurchaseRepository.cs).
///
/// TODAS as queries são PARAMETRIZADAS (sem concatenação de string — anti SQL
/// injection, CodeRabbit focus area). Schema real validado contra
/// fifa2026-api/database/schema.sql + migration knockout-matches (AC-15
/// anti-hallucination): tabelas users, teams, matches, ticket_categories, purchases.
///
/// Acesso SOMENTE leitura — o McpServer nunca grava (as compras são da Function F1).
/// </summary>
public sealed class FifaQueryRepository : IFifaQueryRepository
{
    private readonly string _connectionString;
    private readonly ILogger<FifaQueryRepository> _logger;

    public FifaQueryRepository(IConfiguration configuration, ILogger<FifaQueryRepository> logger)
    {
        _connectionString = configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException(
                "App Setting 'SqlConnectionString' não configurado. Defina a connection string do SQL Server.");
        _logger = logger;
    }

    public async Task<AvailabilityResult> ConsultarDisponibilidadeAsync(
        int? matchId,
        string? matchDescription,
        CancellationToken cancellationToken = default)
    {
        // Resolve a partida (matchId tem prioridade; senão tenta casar por nome dos
        // times via matchDescription "Mandante x Visitante"). Agrega as categorias
        // numa única linha com PIVOT condicional. Schema real:
        //   matches (id, home_team_id, away_team_id) JOIN teams (name)
        //   ticket_categories (match_id, category, price, available_quantity).
        //
        // M-1 (gate S2.5): o PIVOT filtra pelos rótulos REAIS do seed
        // ('VIP Premium'/'Categoria 1'/'Categoria 2' — ver
        // fifa2026-api/database/migrations/2026-05-08-real-fifa-prices.sql), NÃO pelos
        // códigos curtos do contrato (VIP/Cat1/Cat2). Os rótulos são passados como
        // parâmetros (CategoryLabelMapper, fonte única) — mantém a query parametrizada
        // (anti SQL injection) e o contrato externo inalterado.
        const string sql = """
            SELECT TOP (1)
                (ht.name + ' x ' + at.name) AS Partida,
                SUM(CASE WHEN tc.category = @VipLabel  THEN tc.available_quantity ELSE 0 END) AS VipDisponivel,
                SUM(CASE WHEN tc.category = @Cat1Label THEN tc.available_quantity ELSE 0 END) AS Cat1Disponivel,
                SUM(CASE WHEN tc.category = @Cat2Label THEN tc.available_quantity ELSE 0 END) AS Cat2Disponivel,
                MAX(CASE WHEN tc.category = @VipLabel  THEN tc.price END) AS PrecoVip,
                MAX(CASE WHEN tc.category = @Cat1Label THEN tc.price END) AS PrecoCat1,
                MAX(CASE WHEN tc.category = @Cat2Label THEN tc.price END) AS PrecoCat2
            FROM dbo.matches m
            INNER JOIN dbo.teams ht ON ht.id = m.home_team_id
            INNER JOIN dbo.teams at ON at.id = m.away_team_id
            LEFT  JOIN dbo.ticket_categories tc ON tc.match_id = m.id
            WHERE
                (@MatchId IS NOT NULL AND m.id = @MatchId)
                OR (
                    @MatchId IS NULL AND @MatchDescription IS NOT NULL
                    AND (
                        @MatchDescription LIKE '%' + ht.name + '%'
                        AND @MatchDescription LIKE '%' + at.name + '%'
                    )
                )
            GROUP BY ht.name, at.name, m.id
            ORDER BY m.id;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new
            {
                MatchId = matchId,
                MatchDescription = matchDescription,
                VipLabel = CategoryLabelMapper.VipPremium,
                Cat1Label = CategoryLabelMapper.Categoria1,
                Cat2Label = CategoryLabelMapper.Categoria2
            },
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<AvailabilityRow>(command);

        if (row is null)
        {
            _logger.LogInformation(
                "consultar_disponibilidade: nenhuma partida casou (matchId={MatchId}).", matchId);
            return new AvailabilityResult { Encontrado = false };
        }

        return new AvailabilityResult
        {
            Encontrado = true,
            Partida = row.Partida,
            VipDisponivel = row.VipDisponivel,
            Cat1Disponivel = row.Cat1Disponivel,
            Cat2Disponivel = row.Cat2Disponivel,
            PrecoVip = row.PrecoVip,
            PrecoCat1 = row.PrecoCat1,
            PrecoCat2 = row.PrecoCat2
        };
    }

    public async Task<TicketVerificationResult> VerificarIngressoAsync(
        int ingressoId,
        CancellationToken cancellationToken = default)
    {
        // Um "ingresso" é uma linha de purchases (id). Valido = status 'completed'.
        // JOINs para enriquecer: users (comprador), matches+teams (partida),
        // ticket_categories (categoria). Schema real validado contra schema.sql.
        const string sql = """
            SELECT TOP (1)
                p.status                       AS Status,
                u.name                         AS Comprador,
                (ht.name + ' x ' + at.name)    AS Partida,
                tc.category                    AS Categoria,
                p.created_at                   AS DataCompra
            FROM dbo.purchases p
            LEFT JOIN dbo.users u             ON u.id = p.user_id
            LEFT JOIN dbo.ticket_categories tc ON tc.id = p.ticket_category_id
            LEFT JOIN dbo.matches m           ON m.id = tc.match_id
            LEFT JOIN dbo.teams ht            ON ht.id = m.home_team_id
            LEFT JOIN dbo.teams at            ON at.id = m.away_team_id
            WHERE p.id = @IngressoId;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new { IngressoId = ingressoId },
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<TicketRow>(command);

        if (row is null)
        {
            return new TicketVerificationResult { Valido = false };
        }

        return new TicketVerificationResult
        {
            Valido = string.Equals(row.Status, "completed", StringComparison.OrdinalIgnoreCase),
            Comprador = row.Comprador,
            Partida = row.Partida,
            Categoria = row.Categoria,
            DataCompra = row.DataCompra
        };
    }

    public async Task<IReadOnlyList<BracketMatchResult>> ConsultarBracketAsync(
        string rodada,
        CancellationToken cancellationToken = default)
    {
        var stage = MapRodadaToStage(rodada);
        if (stage is null)
        {
            _logger.LogInformation("consultar_bracket: rodada não reconhecida ({Rodada}).", rodada);
            return Array.Empty<BracketMatchResult>();
        }

        // Jogos do mata-mata por stage. Times podem ser NULL (mata-mata antes da
        // classificação — ver migration knockout-matches) → COALESCE para rótulo
        // "A definir". Placar NULL = jogo não disputado. Schema real validado.
        const string sql = """
            SELECT
                (COALESCE(ht.name, 'A definir') + ' x ' + COALESCE(at.name, 'A definir')) AS Jogo,
                m.date        AS Data,
                m.time        AS Horario,
                s.name        AS Estadio,
                m.home_score  AS PlacarMandante,
                m.away_score  AS PlacarVisitante,
                m.status      AS Status
            FROM dbo.matches m
            LEFT JOIN dbo.teams    ht ON ht.id = m.home_team_id
            LEFT JOIN dbo.teams    at ON at.id = m.away_team_id
            LEFT JOIN dbo.stadiums s  ON s.id  = m.stadium_id
            WHERE m.stage = @Stage
            ORDER BY m.date, m.time;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new { Stage = stage },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<BracketMatchResult>(command);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<MatchResult>> ConsultarPartidasAsync(
        string? time,
        string? fase,
        string? estadio,
        string? grupo,
        string? data,
        bool? apenasComResultado,
        CancellationToken cancellationToken = default)
    {
        // Story 2.8 AC-1 — partidas com filtros flexíveis. WHERE dinâmico com o padrão
        // (@p IS NULL OR condição) por filtro — query parametrizada (sem concatenação).
        // 'fase' em linguagem natural é traduzida para matches.stage via MapFaseToStage
        // (delega a MapRodadaToStage + adiciona 'Fase de Grupos'); o valor real vai por
        // @Stage (nunca hardcoded na string SQL). Times NULL no mata-mata → COALESCE.
        var stage = MapFaseToStage(fase);

        // Parse da data ISO no app (não na string SQL) — passa como DateTime? por @Data.
        DateTime? dataFiltro = null;
        if (!string.IsNullOrWhiteSpace(data)
            && DateTime.TryParse(data, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            dataFiltro = parsed.Date;
        }

        var timeLike = string.IsNullOrWhiteSpace(time) ? null : "%" + time + "%";
        var timeUpper = string.IsNullOrWhiteSpace(time) ? null : time.ToUpperInvariant();
        var estadioLike = string.IsNullOrWhiteSpace(estadio) ? null : "%" + estadio + "%";

        const string sql = """
            SELECT
                (COALESCE(ht.name, 'A definir') + ' x ' + COALESCE(at.name, 'A definir')) AS Partida,
                m.date        AS Data,
                m.time        AS Horario,
                s.name        AS Estadio,
                m.stage       AS Fase,
                m.group_name  AS Grupo,
                m.home_score  AS PlacarMandante,
                m.away_score  AS PlacarVisitante,
                m.status      AS Status
            FROM dbo.matches m
            LEFT JOIN dbo.teams    ht ON ht.id = m.home_team_id
            LEFT JOIN dbo.teams    at ON at.id = m.away_team_id
            LEFT JOIN dbo.stadiums s  ON s.id  = m.stadium_id
            WHERE
                (@Time IS NULL OR ht.name LIKE @TimeLike OR at.name LIKE @TimeLike
                                 OR ht.code = @TimeUpper OR at.code = @TimeUpper)
                AND (@Stage IS NULL OR m.stage = @Stage)
                AND (@Estadio IS NULL OR s.name LIKE @EstadioLike OR s.city LIKE @EstadioLike)
                AND (@Grupo IS NULL OR m.group_name = @Grupo)
                AND (@Data IS NULL OR m.date = @Data)
                AND (@ApenasComResultado = 0 OR m.home_score IS NOT NULL)
            ORDER BY m.date, m.time;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new
            {
                Time = time,
                TimeLike = timeLike,
                TimeUpper = timeUpper,
                Stage = stage,
                Estadio = estadio,
                EstadioLike = estadioLike,
                Grupo = grupo,
                Data = dataFiltro,
                ApenasComResultado = (apenasComResultado ?? false) ? 1 : 0
            },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<MatchResult>(command);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<StandingRow>> ConsultarClassificacaoAsync(
        string grupo,
        CancellationToken cancellationToken = default)
    {
        // Story 2.8 AC-2 — classificação CALCULADA por agregação (não existe tabela
        // standings). UNION ALL expande cada partida disputada do grupo em duas linhas
        // (perspectiva do mandante e do visitante), soma 3/1/0 pontos e agrega por time.
        // 'Fase de Grupos' (com acento) vai por @Stage (parametrizado, nunca hardcoded).
        // Se nenhum jogo foi disputado (home_score IS NULL em todos), retorna vazio.
        const string sql = """
            WITH PartidasDoGrupo AS (
                SELECT
                    m.home_team_id AS team_id,
                    m.home_score   AS gols_marcados,
                    m.away_score   AS gols_sofridos,
                    CASE WHEN m.home_score > m.away_score THEN 3
                         WHEN m.home_score = m.away_score THEN 1
                         ELSE 0 END AS pontos
                FROM dbo.matches m
                WHERE m.group_name = @Grupo
                  AND m.stage = @Stage
                  AND m.home_score IS NOT NULL
                  AND m.away_score IS NOT NULL
                UNION ALL
                SELECT
                    m.away_team_id,
                    m.away_score,
                    m.home_score,
                    CASE WHEN m.away_score > m.home_score THEN 3
                         WHEN m.away_score = m.home_score THEN 1
                         ELSE 0 END
                FROM dbo.matches m
                WHERE m.group_name = @Grupo
                  AND m.stage = @Stage
                  AND m.home_score IS NOT NULL
                  AND m.away_score IS NOT NULL
            )
            SELECT
                ROW_NUMBER() OVER (
                    ORDER BY SUM(p.pontos) DESC,
                             SUM(p.gols_marcados - p.gols_sofridos) DESC,
                             SUM(p.gols_marcados) DESC)                AS Posicao,
                t.name                                                AS Time,
                COUNT(*)                                              AS Jogos,
                SUM(CASE WHEN p.pontos = 3 THEN 1 ELSE 0 END)         AS Vitorias,
                SUM(CASE WHEN p.pontos = 1 THEN 1 ELSE 0 END)         AS Empates,
                SUM(CASE WHEN p.pontos = 0 THEN 1 ELSE 0 END)         AS Derrotas,
                SUM(p.gols_marcados)                                  AS GolsPro,
                SUM(p.gols_sofridos)                                  AS GolsContra,
                SUM(p.gols_marcados - p.gols_sofridos)                AS Saldo,
                SUM(p.pontos)                                         AS Pontos
            FROM PartidasDoGrupo p
            JOIN dbo.teams t ON t.id = p.team_id
            GROUP BY t.id, t.name
            ORDER BY Pontos DESC, Saldo DESC, GolsPro DESC;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new { Grupo = grupo, Stage = StageFaseDeGrupos },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<StandingRow>(command);
        return rows.AsList();
    }

    public async Task<TeamResult> ConsultarTimeAsync(
        string nome,
        CancellationToken cancellationToken = default)
    {
        // Story 2.8 AC-3 — seleção por nome (LIKE) ou código exato (uppercase).
        const string sql = """
            SELECT TOP (1)
                t.name          AS Nome,
                t.code          AS Codigo,
                t.group_name    AS Grupo,
                t.confederation AS Confederacao,
                t.fifa_ranking  AS RankingFifa,
                t.flag          AS Bandeira
            FROM dbo.teams t
            WHERE t.name LIKE @NomeLike OR t.code = @NomeUpper
            ORDER BY t.name;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new { NomeLike = "%" + nome + "%", NomeUpper = nome.ToUpperInvariant() },
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<TeamRow>(command);

        if (row is null)
        {
            return new TeamResult { Encontrado = false };
        }

        return new TeamResult
        {
            Encontrado = true,
            Nome = row.Nome,
            Codigo = row.Codigo,
            Grupo = row.Grupo,
            Confederacao = row.Confederacao,
            RankingFifa = row.RankingFifa,
            Bandeira = row.Bandeira
        };
    }

    public async Task<StadiumResult> ConsultarEstadioAsync(
        string nome,
        CancellationToken cancellationToken = default)
    {
        // Story 2.8 AC-4 — estádio por nome OU cidade (LIKE).
        const string sql = """
            SELECT TOP (1)
                s.name        AS Nome,
                s.city        AS Cidade,
                s.country     AS Pais,
                s.capacity    AS Capacidade,
                s.description AS Descricao
            FROM dbo.stadiums s
            WHERE s.name LIKE @NomeLike OR s.city LIKE @NomeLike
            ORDER BY s.name;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new { NomeLike = "%" + nome + "%" },
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<StadiumRow>(command);

        if (row is null)
        {
            return new StadiumResult { Encontrado = false };
        }

        return new StadiumResult
        {
            Encontrado = true,
            Nome = row.Nome,
            Cidade = row.Cidade,
            Pais = row.Pais,
            Capacidade = row.Capacidade,
            Descricao = row.Descricao
        };
    }

    /// <summary>Valor real de <c>matches.stage</c> da fase de grupos (com acento e
    /// espaços — migration 2026-05-08-group-stage-72.sql). Passado sempre por parâmetro
    /// SQL, nunca concatenado.</summary>
    internal const string StageFaseDeGrupos = "Fase de Grupos";

    /// <summary>
    /// Story 2.8 AC-1 — mapeia a "fase" em linguagem natural para o valor de
    /// <c>matches.stage</c>. Adiciona apenas o caso "grupos" → 'Fase de Grupos' e
    /// DELEGA o mata-mata a <see cref="MapRodadaToStage"/> (não duplica os mapeamentos
    /// de oitavas/quartas/etc.). Retorna null quando não reconhece (sem filtro de fase).
    /// </summary>
    internal static string? MapFaseToStage(string? fase)
    {
        if (string.IsNullOrWhiteSpace(fase))
        {
            return null;
        }

        var f = fase.Trim().ToLowerInvariant();
        if (f.Contains("grupo"))
        {
            return StageFaseDeGrupos;
        }

        return MapRodadaToStage(fase);
    }

    /// <summary>
    /// Mapeia a rodada em linguagem natural para o valor de <c>matches.stage</c>
    /// (valores reais da migration knockout-matches: round_of_32, round_of_16,
    /// quarter_final, semi_final, third_place, final). Retorna null se não reconhecer.
    /// </summary>
    internal static string? MapRodadaToStage(string? rodada)
    {
        if (string.IsNullOrWhiteSpace(rodada))
        {
            return null;
        }

        var r = rodada.Trim().ToLowerInvariant();

        if (r.Contains("32") || r.Contains("trinta e dois") || r.Contains("round of 32"))
        {
            return "round_of_32";
        }
        if (r.Contains("oitava") || r.Contains("16") || r.Contains("round of 16"))
        {
            return "round_of_16";
        }
        if (r.Contains("quarta") || r.Contains("quarter"))
        {
            return "quarter_final";
        }
        if (r.Contains("semi"))
        {
            return "semi_final";
        }
        if (r.Contains("terceiro") || r.Contains("3") || r.Contains("third"))
        {
            return "third_place";
        }
        if (r.Contains("final"))
        {
            // Avaliado por último: "semi_final"/"third_place" já tratados acima.
            return "final";
        }

        return null;
    }

    private sealed record AvailabilityRow(
        string? Partida,
        int VipDisponivel,
        int Cat1Disponivel,
        int Cat2Disponivel,
        decimal? PrecoVip,
        decimal? PrecoCat1,
        decimal? PrecoCat2);

    private sealed record TicketRow(
        string? Status,
        string? Comprador,
        string? Partida,
        string? Categoria,
        DateTime? DataCompra);

    private sealed record TeamRow(
        string? Nome,
        string? Codigo,
        string? Grupo,
        string? Confederacao,
        int? RankingFifa,
        string? Bandeira);

    private sealed record StadiumRow(
        string? Nome,
        string? Cidade,
        string? Pais,
        int? Capacidade,
        string? Descricao);
}
