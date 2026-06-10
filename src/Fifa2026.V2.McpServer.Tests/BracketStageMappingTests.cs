using Fifa2026.V2.McpServer.Data;
using Xunit;

namespace Fifa2026.V2.McpServer.Tests;

/// <summary>
/// AC-5 / AC-15 — o mapeamento de rodada em linguagem natural para o valor real de
/// matches.stage (round_of_32, round_of_16, quarter_final, semi_final, third_place,
/// final — validados contra a migration knockout-matches). Garante que entradas
/// não reconhecidas retornam null (a tool devolve lista vazia, sem inventar stage).
/// </summary>
public sealed class BracketStageMappingTests
{
    [Theory]
    [InlineData("oitavas", "round_of_16")]
    [InlineData("Oitavas de final", "round_of_16")]
    [InlineData("round of 16", "round_of_16")]
    [InlineData("quartas", "quarter_final")]
    [InlineData("quarter finals", "quarter_final")]
    [InlineData("semifinal", "semi_final")]
    [InlineData("semi-final", "semi_final")]
    [InlineData("final", "final")]
    [InlineData("32 avos", "round_of_32")]
    [InlineData("round of 32", "round_of_32")]
    [InlineData("terceiro lugar", "third_place")]
    public void Maps_known_rounds_to_real_stage(string input, string expectedStage)
    {
        Assert.Equal(expectedStage, FifaQueryRepository.MapRodadaToStage(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("grupo A")]
    [InlineData("rodada inexistente")]
    public void Returns_null_for_unknown_or_empty(string? input)
    {
        Assert.Null(FifaQueryRepository.MapRodadaToStage(input));
    }

    /// <summary>
    /// Story 2.8 AC-1 — MapFaseToStage estende MapRodadaToStage adicionando apenas o
    /// caso "grupos" → 'Fase de Grupos' (valor real com acento da migration
    /// 2026-05-08-group-stage-72.sql) e delegando o mata-mata. Diferente de
    /// MapRodadaToStage, aqui "grupos" NÃO é null (é a fase de grupos).
    /// </summary>
    [Theory]
    [InlineData("grupos", "Fase de Grupos")]
    [InlineData("Fase de Grupos", "Fase de Grupos")]
    [InlineData("grupo A", "Fase de Grupos")]
    [InlineData("oitavas", "round_of_16")]
    [InlineData("quartas", "quarter_final")]
    [InlineData("semifinal", "semi_final")]
    [InlineData("final", "final")]
    [InlineData("round of 32", "round_of_32")]
    public void MapFaseToStage_maps_groups_and_delegates_knockout(string input, string expectedStage)
    {
        Assert.Equal(expectedStage, FifaQueryRepository.MapFaseToStage(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("fase inexistente")]
    public void MapFaseToStage_returns_null_for_unknown_or_empty(string? input)
    {
        Assert.Null(FifaQueryRepository.MapFaseToStage(input));
    }
}
