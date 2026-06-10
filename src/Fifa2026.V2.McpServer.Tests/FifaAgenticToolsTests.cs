using Fifa2026.V2.McpServer.Data;
using Fifa2026.V2.McpServer.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Fifa2026.V2.McpServer.Tests;

/// <summary>
/// Story 2.8 AC-7/AC-8 — testes das 4 novas tools MCP read-only da Fase A
/// (consultar_partidas, consultar_classificacao, consultar_time, consultar_estadio).
/// Mesmo padrão de <see cref="FifaTicketToolsTests"/>: tools são métodos estáticos que
/// recebem o data layer por DI — mockamos <see cref="IFifaQueryRepository"/>. Cada teste
/// confirma que a tool (a) repassa os parâmetros corretos ao repositório e (b) devolve o
/// resultado do repositório, sem tocar SQL real.
/// </summary>
public sealed class FifaAgenticToolsTests
{
    private static readonly NullLogger<FifaTicketTools.DiagnosticsCategory> Log =
        NullLogger<FifaTicketTools.DiagnosticsCategory>.Instance;

    private static EntraOidContext OidContext(string? oidHeader)
    {
        var ctx = new DefaultHttpContext();
        if (oidHeader is not null)
        {
            ctx.Request.Headers[EntraOidContext.HeaderName] = oidHeader;
        }
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(ctx);
        return new EntraOidContext(accessor.Object);
    }

    [Fact]
    public async Task ConsultarPartidas_passes_args_and_returns_repository_result()
    {
        var expected = new List<MatchResult>
        {
            new() { Partida = "Brasil x Sérvia", Fase = "Fase de Grupos", Grupo = "A", Status = "scheduled" }
        };
        var repo = new Mock<IFifaQueryRepository>();
        repo.Setup(r => r.ConsultarPartidasAsync(
                "Brasil", null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await FifaTicketTools.ConsultarPartidasAsync(
            repo.Object, OidContext("11111111-2222-3333-4444-555555555555"), Log,
            time: "Brasil", fase: null, estadio: null, grupo: null, data: null,
            apenasComResultado: null, CancellationToken.None);

        Assert.Same(expected, result);
        repo.Verify(r => r.ConsultarPartidasAsync(
            "Brasil", null, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarPartidas_forwards_all_filters()
    {
        var repo = new Mock<IFifaQueryRepository>();
        repo.Setup(r => r.ConsultarPartidasAsync(
                null, "semifinal", "Maracanã", "C", "2026-06-15", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchResult>());

        var result = await FifaTicketTools.ConsultarPartidasAsync(
            repo.Object, OidContext(oidHeader: null), Log,
            time: null, fase: "semifinal", estadio: "Maracanã", grupo: "C",
            data: "2026-06-15", apenasComResultado: true, CancellationToken.None);

        Assert.Empty(result);
        repo.Verify(r => r.ConsultarPartidasAsync(
            null, "semifinal", "Maracanã", "C", "2026-06-15", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarClassificacao_passes_grupo_and_returns_list()
    {
        var expected = new List<StandingRow>
        {
            new() { Posicao = 1, Time = "Brasil", Jogos = 3, Vitorias = 3, Pontos = 9, Saldo = 5 }
        };
        var repo = new Mock<IFifaQueryRepository>();
        repo.Setup(r => r.ConsultarClassificacaoAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await FifaTicketTools.ConsultarClassificacaoAsync(
            repo.Object, OidContext("aaaa"), Log, grupo: "A", CancellationToken.None);

        Assert.Same(expected, result);
        Assert.Single(result);
        Assert.Equal("Brasil", result[0].Time);
        repo.Verify(r => r.ConsultarClassificacaoAsync("A", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarClassificacao_returns_empty_when_no_matches_played()
    {
        var repo = new Mock<IFifaQueryRepository>();
        repo.Setup(r => r.ConsultarClassificacaoAsync("B", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StandingRow>());

        var result = await FifaTicketTools.ConsultarClassificacaoAsync(
            repo.Object, OidContext(oidHeader: null), Log, grupo: "B", CancellationToken.None);

        Assert.Empty(result);
        repo.Verify(r => r.ConsultarClassificacaoAsync("B", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarTime_passes_nome_and_returns_result()
    {
        var expected = new TeamResult
        {
            Encontrado = true, Nome = "Brasil", Codigo = "BRA", Grupo = "A", RankingFifa = 5
        };
        var repo = new Mock<IFifaQueryRepository>();
        repo.Setup(r => r.ConsultarTimeAsync("Brasil", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await FifaTicketTools.ConsultarTimeAsync(
            repo.Object, OidContext("aaaa"), Log, nome: "Brasil", CancellationToken.None);

        Assert.Same(expected, result);
        Assert.True(result.Encontrado);
        Assert.Equal("BRA", result.Codigo);
        repo.Verify(r => r.ConsultarTimeAsync("Brasil", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarEstadio_passes_nome_and_returns_result()
    {
        var expected = new StadiumResult
        {
            Encontrado = true, Nome = "Maracanã", Cidade = "Rio de Janeiro", Pais = "Brasil", Capacidade = 78838
        };
        var repo = new Mock<IFifaQueryRepository>();
        repo.Setup(r => r.ConsultarEstadioAsync("Maracanã", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await FifaTicketTools.ConsultarEstadioAsync(
            repo.Object, OidContext(oidHeader: null), Log, nome: "Maracanã", CancellationToken.None);

        Assert.Same(expected, result);
        Assert.Equal("Rio de Janeiro", result.Cidade);
        repo.Verify(r => r.ConsultarEstadioAsync("Maracanã", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarEstadio_returns_not_found_when_repository_says_so()
    {
        var repo = new Mock<IFifaQueryRepository>();
        repo.Setup(r => r.ConsultarEstadioAsync("Inexistente", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StadiumResult { Encontrado = false });

        var result = await FifaTicketTools.ConsultarEstadioAsync(
            repo.Object, OidContext(oidHeader: null), Log, nome: "Inexistente", CancellationToken.None);

        Assert.False(result.Encontrado);
        repo.Verify(r => r.ConsultarEstadioAsync("Inexistente", It.IsAny<CancellationToken>()), Times.Once);
    }
}
