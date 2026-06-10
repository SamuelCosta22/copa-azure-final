using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using Xunit;

namespace Fifa2026.V2.McpServer.Tests;

/// <summary>
/// AC-2 / AC-15 — testes de PROTOCOLO do endpoint /mcp ponta-a-ponta via
/// WebApplicationFactory&lt;Program&gt; (TestServer) + o CLIENTE MCP oficial
/// (ModelContextProtocol.Client 1.4.0). O cliente faz o handshake JSON-RPC 2.0
/// real (initialize → tools/list) sobre o HttpClient do TestServer, provando que
/// o SDK servidor está expondo as tools corretamente — sem reimplementar o
/// protocolo à mão (AC-15). Story 2.8 (Fase A) elevou o contrato de 3 → 7 tools.
///
/// tools/list é metadado (nenhum handler roda) → não exige SqlConnectionString.
/// </summary>
public sealed class McpEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public McpEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_endpoint_responds_healthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", body.GetProperty("status").GetString());
        Assert.Equal("mcp-server", body.GetProperty("service").GetString());
    }

    [Fact]
    public async Task Mcp_client_lists_the_seven_tools_over_streamable_http()
    {
        // HttpClient do TestServer (in-memory) aponta para o /mcp do nosso servidor.
        var httpClient = _factory.CreateClient();

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient,
            loggerFactory: null!,
            ownsHttpClient: false);

        await using var mcpClient = await McpClient.CreateAsync(transport);

        var tools = await mcpClient.ListToolsAsync();

        var names = tools.Select(t => t.Name).ToHashSet();

        // 3 tools herdadas de S2.5
        Assert.Contains("consultar_disponibilidade", names);
        Assert.Contains("verificar_ingresso", names);
        Assert.Contains("consultar_bracket", names);
        // 4 tools da Fase A (Story 2.8)
        Assert.Contains("consultar_partidas", names);
        Assert.Contains("consultar_classificacao", names);
        Assert.Contains("consultar_time", names);
        Assert.Contains("consultar_estadio", names);
        Assert.Equal(7, names.Count);
    }
}
