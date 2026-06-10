# F5 — Inteligência Conversacional: MCP Server + Chatbot React + Gemini 2.0 Flash

> **Leitura prévia obrigatória** · Workshop "Living Lab Azure-Native" (40h) · Fase 5 de 6
> **Tempo estimado de leitura:** 40-50 min · **Faça ANTES da aula** (esta é uma das duas fases longas — 8h).
> **Story:** [2.5](../../stories/2.5.story.md) · **Decisão de arquitetura:** [ADE-002](../../architecture/ade-002-mcp-pinning.md) (MCP C# SDK `1.4.0` exato + integração LLM no front + endpoints LLM pinados) + [ADE-004](../../architecture/ade-004-gateway-yarp.md) (gateway YARP — todo tráfego ao MCP passa por ele) + [ADE-005](../../architecture/ade-005-identity-easy-auth.md) (identidade `oid` propagada como `X-Entra-OID`)
> **Continuidade:** parte cumulativa da [F1](../phase-01/README.md), [F2](../phase-02/README.md), [F3](../phase-03/README.md) e [F4](../phase-04/README.md) — o chatbot consulta o **mesmo SQL** da F1, passa pelo **gateway YARP da F2**, usa o **Bearer token Entra da F3** e segue o fio "tudo passa pelo gateway" que você já conhece.

---

## 0. Por que você está lendo isto antes da aula

Até aqui, o seu sistema FIFA 2026 Tickets faz coisas: compra ingresso (F1), protege o caminho com um gateway profissional (F2), sabe **quem** está chamando via identidade Entra (F3) e dispara automações pós-compra (F4). Mas tudo isso é **transacional** — o usuário clica, preenche, envia. Para descobrir se "tem ingresso para Brasil x Argentina", ele teria que navegar telas, filtrar, ler tabelas.

A Fase 5 muda a forma como o usuário **conversa** com o sistema. Você vai construir um **chatbot** que entende perguntas em português ("Tem ingresso para Brasil x Argentina?", "Esse ingresso ID 123 é válido?", "Quem está nas oitavas?") e responde com **dados reais do seu SQL** — não com texto inventado. O segredo está em três peças que se encaixam:

1. Um **MCP Server** (.NET) que expõe 3 **tools** sobre o seu banco, num protocolo padrão (MCP — Model Context Protocol).
2. Um **chatbot React** no frontend que conversa com um **LLM** (Gemini 2.0 Flash, por padrão).
3. O **LLM decide sozinho** quando precisa de dados, chama a tool certa via o gateway YARP, recebe o resultado e gera a resposta em linguagem natural.

> **A frase âncora da fase:** "O LLM raciocina; o MCP Server tem os fatos." O modelo nunca inventa disponibilidade ou preço — ele **pergunta ao seu banco** através de uma tool. Esse desacoplamento é o que permite, no bônus, trocar o Gemini por Groq ou Mistral **sem mudar uma linha das tools**.

Esta leitura cobre:

1. O que é **function calling / tool use** em LLMs (a ideia central)
2. O que é o **MCP** (protocolo, JSON-RPC 2.0, `tools/list` / `tools/call`) e por que ele existe
3. Como o **MCP Server .NET** desta fase é construído (SDK oficial `1.4.0`, 3 tools, `MapMcp`)
4. Por que a **chave da LLM fica num proxy server-side** — e nunca no navegador
5. O que é o **Gemini 2.0 Flash** e como ele faz function calling
6. **Bônus: portabilidade entre LLMs** (Gemini → Groq → Mistral trocando uma env var)
7. Onde tudo se encaixa no fluxo v2 e os **contratos exatos**
8. Glossário e checklist de pré-aula

> **Pré-requisitos de conhecimento:** você fez a F1 (compra → fila → consumer → SQL), a F2 (gateway YARP), a F3 (JWT Entra validado, `oid` propagado como `X-Entra-OID`) e a F4 (n8n). Você programa em qualquer linguagem; **não exigimos** experiência prévia com LLMs, MCP ou function calling. Se você já usou um chatbot que "faz coisas" (consulta clima, agenda reunião), já viveu a ideia — aqui você a constrói por dentro.

---

## 1. A ideia central: function calling (tool use)

Um LLM "puro" só sabe gerar texto a partir do que aprendeu no treino. Pergunte "Tem ingresso para Brasil x Argentina?" a um LLM sem ferramentas e ele vai **inventar** uma resposta plausível — porque ele não tem como saber o estoque do **seu** banco. Isso é alucinação, e num produto real é inaceitável.

**Function calling** (também chamado *tool use*) resolve isso. Você entrega ao LLM, junto com a pergunta, um **catálogo de ferramentas** que ele pode usar — cada uma com nome, descrição e os parâmetros que aceita. O LLM então:

1. Lê a pergunta do usuário e o catálogo de tools.
2. Decide: "preciso de um dado que não tenho — vou chamar a tool `consultar_disponibilidade` com `matchDescription='Brasil x Argentina'`".
3. **Não executa nada** — ele só **devolve a intenção** de chamada (o nome da tool + os argumentos).
4. **Você** (o seu código) executa a tool de verdade (consulta o SQL) e devolve o resultado ao LLM.
5. O LLM recebe o dado real e **gera a resposta natural**: "Sim! Para Brasil x Argentina há 12 ingressos VIP a R$ 4.500, ...".

> **A intuição:** o LLM é um ótimo **gerente** — ele entende a pergunta, sabe qual ferramenta usar e redige a resposta. Mas ele não tem acesso ao banco; quem tem é a **ferramenta**. O LLM delega o trabalho braçal (buscar o fato) e fica com o trabalho intelectual (entender e responder).

Esse loop "LLM pede tool → código executa → resultado volta ao LLM" pode repetir algumas vezes numa mesma pergunta (o LLM pode precisar de mais de um dado). No nosso código, o limite é **4 rodadas por mensagem** (`MAX_TOOL_ITERS` no hook `useLlmChat`), o suficiente para evitar loops infinitos.

---

## 2. O que é o MCP (Model Context Protocol)

Function calling resolve "como o LLM pede uma ferramenta". Mas falta combinar **como a ferramenta é descrita e chamada** de forma padronizada, para que qualquer LLM (ou qualquer cliente) saiba conversar com qualquer servidor de ferramentas. É aí que entra o **MCP — Model Context Protocol**.

O MCP é um **protocolo aberto** (spec em [modelcontextprotocol.io](https://modelcontextprotocol.io/)) que padroniza como um **servidor** expõe capacidades (tools, resources, prompts) e como um **cliente** as descobre e usa. Pense no MCP como o "USB-C das ferramentas de IA": um padrão de conector que faz o seu servidor de tools funcionar com qualquer cliente que fale MCP.

### 2.1 JSON-RPC 2.0 — o "envelope" das mensagens

O MCP usa **JSON-RPC 2.0** como formato de mensagem. JSON-RPC é um padrão simples de "chamada de procedimento remoto" em JSON. Cada requisição tem quatro campos:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": { "name": "consultar_disponibilidade", "arguments": { "matchDescription": "Brasil x Argentina" } }
}
```

- `jsonrpc`: sempre `"2.0"`.
- `id`: um identificador da chamada (para casar request e response).
- `method`: o que você quer fazer — no MCP, os principais são `tools/list` (liste suas tools) e `tools/call` (execute uma tool).
- `params`: os argumentos do método.

A resposta vem no mesmo envelope, com `result` (sucesso) ou `error` (falha). **Você não escreve esse envelope à mão** — o SDK oficial do MCP cuida do framing (ver seção 3).

### 2.2 Os dois métodos que importam nesta fase

| Método JSON-RPC | O que faz | Quem provê |
|---|---|---|
| `tools/list` | Devolve o catálogo de tools (nome, descrição, JSON Schema de input) | O SDK MCP, automaticamente |
| `tools/call` | Executa uma tool com argumentos e devolve o resultado | O SDK despacha para o seu handler |

> **Por que isso é ouro didático:** sem MCP, você teria que (a) inventar um formato de descrição de tools, (b) escrever o parser do JSON-RPC, (c) garantir que o formato bate com o que o LLM espera. Com o SDK oficial, você só **decora um método C# com um atributo** e o resto vem pronto. Menos código no lugar errado, menos superfície para alucinação (princípio "No Invention" — você não inventa métodos JSON-RPC que não existem na spec).

### 2.3 Streamable HTTP — o transporte

O MCP define vários **transportes** (como as mensagens trafegam). O que usamos é o **Streamable HTTP**: o cliente faz um `POST` para um endpoint (`/mcp`) com a mensagem JSON-RPC, e a resposta pode vir como JSON puro **ou** como evento SSE (`text/event-stream`). Por isso o cliente do front envia `Accept: application/json, text/event-stream` e sabe ler os dois formatos.

Escolhemos HTTP (e não stdio, outro transporte do MCP) porque o nosso MCP Server é um **serviço de rede** que fica **atrás do gateway YARP** — exatamente o mesmo padrão de hospedagem que você já usou no gateway da F2.

---

## 3. O MCP Server .NET desta fase (`src/Fifa2026.V2.McpServer/`)

O MCP Server é um **microserviço .NET 8 separado** das Functions de compra (`src/Fifa2026.V2.Functions/`). Ele só faz uma coisa: **expor 3 tools de leitura** sobre o SQL do FIFA 2026 Tickets. Ele **não** chama o LLM (isso é do front — ver seção 4).

### 3.1 SDK oficial pinado em 1.4.0 exato

Ele usa o **SDK C# oficial do MCP** (repo `modelcontextprotocol/csharp-sdk`, mantido em colaboração com a Microsoft), pinado em **versão exata `1.4.0`** (ADE-002 Invariante 1):

| Pacote NuGet | Versão | Papel |
|---|---|---|
| `ModelContextProtocol.AspNetCore` | `1.4.0` | Server HTTP — expõe `/mcp` via `MapMcp()` (Streamable HTTP) |
| `ModelContextProtocol` | `1.4.0` | Pacote principal (hosting + DI) |
| `ModelContextProtocol.Core` | `1.4.0` | Núcleo client/server low-level |

> **Por que `1.4.0` exato e não `latest`?** O SDK MCP é recente e evolui rápido. Um minor inesperado poderia mudar a assinatura de `tools/list`/`tools/call` ou de `MapMcp()` **no meio do workshop**. Pinar exato garante que duas turmas em datas diferentes rodam o mesmo código. (O n8n da F4 é a única exceção — roda `latest` por decisão de owner.)

### 3.2 Como o servidor é montado (`Program.cs`)

O registro do MCP Server cabe em três linhas:

```csharp
builder.Services
    .AddMcpServer()
    .WithHttpTransport()        // Streamable HTTP (ADE-002 Inv 2)
    .WithToolsFromAssembly();   // descobre [McpServerTool] no assembly
```

E o endpoint é uma linha:

```csharp
app.MapMcp("/mcp");             // POST /mcp JSON-RPC; GET/DELETE p/ streaming em modo stateful
```

Há também um `/health` (para o smoke test e o health probe do Container App) e o proxy de LLM (`app.MapLlmProxy()` — ver seção 5).

### 3.3 As 3 tools (`Tools/FifaTickerTools.cs`)

Cada tool é um **método C# estático** decorado com `[McpServerTool]` dentro de uma classe `[McpServerToolType]`. O JSON Schema de input é **derivado pelo SDK** a partir da assinatura do método e dos atributos `[Description]` — você não escreve schema à mão.

| Tool | Parâmetros | Retorna | O que consulta |
|---|---|---|---|
| `consultar_disponibilidade` | `matchId` (int, opcional) **ou** `matchDescription` (string, opcional) | `{ encontrado, partida, vipDisponivel, cat1Disponivel, cat2Disponivel, precoVip, precoCat1, precoCat2 }` | `matches` + `teams` + `ticket_categories` (PIVOT VIP/Cat1/Cat2) |
| `verificar_ingresso` | `ingressoId` (int, obrigatório) | `{ valido, comprador, partida, categoria, dataCompra }` | `purchases` + `users` + `ticket_categories` + `matches` + `teams` (válido = status `completed`) |
| `consultar_bracket` | `rodada` (string, obrigatório: "oitavas", "quartas", "semifinal", "final") | lista de `{ jogo, data, horario, estadio, placarMandante, placarVisitante, status }` | `matches` + `teams` + `stadiums`, mapeando a rodada para `matches.stage` |

Exemplo de uma tool, exatamente como está no código:

```csharp
[McpServerTool(Name = "consultar_disponibilidade", ReadOnly = true)]
[Description("Consulta disponibilidade e preços de ingressos para uma partida da Copa 2026. ...")]
public static async Task<AvailabilityResult> ConsultarDisponibilidadeAsync(
    IFifaQueryRepository repository,
    EntraOidContext oidContext,
    ILogger<DiagnosticsCategory> logger,
    [Description("ID numérico da partida (opcional se matchDescription for informado).")]
    int? matchId = null,
    [Description("Descrição da partida, ex.: 'Brasil x Argentina' (opcional se matchId for informado).")]
    string? matchDescription = null,
    CancellationToken cancellationToken = default)
```

> **Detalhe que vale ouro (bug real corrigido):** o SDK 1.4.0 marca um parâmetro nullable como **required** a menos que ele tenha um valor default. Como `matchId` e `matchDescription` são **ambos opcionais** (AC-3), a tool quebrava no `tools/call` quando só um era informado. A correção foi dar **default `= null`** aos dois parâmetros. Isso está coberto por um teste de integração (`McpToolCallIntegrationTests`). Lição: leia o comportamento do SDK, não assuma.

### 3.4 Acesso a dados: Dapper parametrizado (mesmo padrão do projeto)

As queries vivem em `Data/FifaQueryRepository.cs`, com **Dapper + Microsoft.Data.SqlClient** — o mesmo padrão de `src/Fifa2026.V2.Functions/Data/`. **Todas as queries são parametrizadas** (`@MatchId`, `@IngressoId`, `@Stage`), sem concatenação de string — defesa contra SQL injection. O acesso é **somente leitura**: o MCP Server nunca grava (compras são da Function F1).

### 3.5 Fase A — Sentidos completos (tools 4-7) — Story 2.8

A primeira versão do MCP Server (S2.5) tinha **3 tools**. Na **Fase A** (Story 2.8), o chatbot ganha "sentidos completos": passamos de 3 para **7 tools read-only**, permitindo "explorar todo o sistema da Copa" — partidas com placar, classificação calculada de grupos, dados de times e de estádios. As 4 novas tools seguem **exatamente** o mesmo padrão das 3 originais (método estático, dependências por DI, `[Description]` PT-BR rica, `ReadOnly = true`) e **não abrem nenhum vetor de escrita** no banco (regra de ouro: o MCP Server nunca grava).

| Tool | Parâmetros | Retorna | O que consulta | Pergunta canônica |
|---|---|---|---|---|
| `consultar_partidas` | `time`, `fase`, `estadio`, `grupo`, `data` (YYYY-MM-DD), `apenasComResultado` — **todos opcionais** | lista de `{ partida, data, horario, estadio, fase, grupo, placarMandante, placarVisitante, status }` | `matches` + `teams` (mandante/visitante) + `stadiums`; `fase` em linguagem natural → `matches.stage` | "Quando o Brasil joga?" |
| `consultar_classificacao` | `grupo` (obrigatório) | lista de `{ posicao, time, jogos, vitorias, empates, derrotas, golsPro, golsContra, saldo, pontos }` | **calculada por agregação** de `matches` (não existe tabela `standings`); grupos sem jogos disputados → lista vazia | "Como está o grupo A?" |
| `consultar_time` | `nome` (obrigatório — nome ou código) | `{ encontrado, nome, codigo, grupo, confederacao, rankingFifa, bandeira }` | `teams` | "Em que grupo está a Argentina?" |
| `consultar_estadio` | `nome` (obrigatório — estádio ou cidade) | `{ encontrado, nome, cidade, pais, capacidade, descricao }` | `stadiums` | "Me fala do Maracanã." |

> **Detalhe de schema (anti-alucinação):** o valor real de `matches.stage` para a fase de grupos é **`'Fase de Grupos'`** (com acento e espaços — migration `2026-05-08-group-stage-72.sql`), enquanto o mata-mata usa `round_of_32`/`round_of_16`/`quarter_final`/`semi_final`/`third_place`/`final`. A função `MapFaseToStage` traduz a linguagem natural ("grupos", "oitavas", ...) para esses valores, **delegando** o mata-mata à `MapRodadaToStage` já existente — sem duplicar lógica.

> **Classificação sem tabela:** não há tabela `standings` no banco. A `consultar_classificacao` **calcula** a tabela de pontos por agregação (`UNION ALL` expandindo cada jogo na perspectiva de mandante e visitante, somando 3/1/0 pontos). Antes de qualquer jogo do grupo ser disputado, a tool retorna **lista vazia** — e o chatbot responde "ainda sem jogos disputados". Isso é correto, não um bug.

---

## 4. Onde vive a integração LLM: no frontend React

Um ponto de arquitetura que evita confusão: **a integração com o LLM é do frontend React, não do MCP Server** (ADE-002 Invariante 3). O MCP Server só expõe tools; quem conversa com o Gemini/Groq/Mistral é o navegador.

O fluxo, peça por peça (todos os arquivos em `Lovable/World Cup Tickets Hub/src/`):

```
[Chatbot.tsx]  componente React (Sheet lateral, Input, histórico)
     │ send("Tem ingresso pra Brasil x Argentina?")
     ▼
[useLlmChat.ts]  hook que orquestra o loop function calling
     │ chama llm.chat(history, MCP_TOOLS, toolResults)
     ▼
[lib/llm/gemini.ts]  adapter Gemini (monta o body no formato do Gemini)
     │ llmFetch('gemini', '/models/gemini-2.0-flash:generateContent', body)
     ▼
[lib/llm/proxy.ts]  envia ao PROXY server-side (NUNCA conhece a key)
     │ POST {VITE_LLM_PROXY_URL}/llm/gemini/... (Bearer Entra)
     ▼
[Gateway YARP] → [McpServer LlmProxyEndpoints.cs] injeta a key → Gemini oficial
     │ Gemini decide chamar consultar_disponibilidade
     ▼
[lib/mcpClient.ts]  callMcpTool → POST {VITE_GATEWAY_V2_URL}/mcp (JSON-RPC tools/call)
     │ via gateway YARP (Bearer Entra; X-Entra-OID propagado)
     ▼
[McpServer /mcp]  executa a tool → Dapper SELECT no SQL → resultado
     ▼
[Gemini] recebe o resultado → gera a resposta natural → [Chatbot UI]
```

O catálogo de tools que o front entrega ao LLM (`lib/mcpTools.ts`) **espelha** as 3 tools reais do MCP Server, mantido em sincronia manual. O hook (`useLlmChat.ts`) executa o loop: enquanto o LLM pedir tools, ele as executa via `callMcpTool` e devolve os resultados; quando o LLM responde só com texto, ele exibe e encerra.

> **Por que o LLM no front (e não no .NET)?** Porque o MCP **desacopla** o LLM dos dados. O MCP Server não sabe nem se importa qual LLM está chamando suas tools. Isso é exatamente o que permite a demo de portabilidade (seção 6): você troca o LLM no front e o MCP Server nem percebe.

---

## 5. A decisão de segurança: a key da LLM fica no proxy server-side

Esta é a decisão de arquitetura **mais importante** da fase do ponto de vista de segurança, e ela vale para qualquer produto que use LLM no frontend.

### 5.1 O problema: a key NÃO pode ir no navegador

Se você embutir a API key do Gemini no código React, ela vai parar no **bundle JavaScript** que é baixado pelo navegador de **todo usuário**. Qualquer um abre o DevTools, procura a string e **rouba a sua key** — e passa a gastar a sua cota (ou a sua conta, se houver cobrança). Isso vale para **qualquer** credencial: nunca embuta um segredo num bundle de browser.

### 5.2 A solução: proxy server-side mínimo

O frontend **nunca conhece a key**. Em vez de falar direto com o Gemini, ele chama um **proxy** (`lib/llm/proxy.ts` → `VITE_LLM_PROXY_URL`), que é o próprio MCP Server (via gateway YARP). O proxy (`Llm/LlmProxyEndpoints.cs`):

1. Recebe o corpo que o adapter do front já montou no formato do provider.
2. **Injeta a key** (lida de App Setting server-side: `GEMINI_API_KEY` / `GROQ_API_KEY` / `MISTRAL_API_KEY`).
3. Encaminha ao endpoint **oficial** pinado de cada provider.
4. Devolve a resposta crua ao front — **a key nunca aparece** na resposta nem em log.

As rotas do proxy (em `Program.cs` via `MapLlmProxy()`):

```
POST /llm/gemini/{*path}   → https://generativelanguage.googleapis.com/v1beta/{path}?key=KEY
POST /llm/groq/{*path}     → https://api.groq.com/openai/v1/{path}    (Authorization: Bearer KEY)
POST /llm/mistral/{*path}  → https://api.mistral.ai/v1/{path}         (Authorization: Bearer KEY)
```

Note que **Gemini** passa a key na query string (`?key=...`) enquanto **Groq/Mistral** usam header `Authorization: Bearer` — porque Groq e Mistral são **OpenAI-compatible** e o Gemini não.

### 5.3 Fail-safe, nunca fail-open

- Sem `VITE_LLM_PROXY_URL` configurado no front → `proxy.ts` **lança um erro explicativo** em vez de tentar embutir a key. Não existe caminho "key no bundle".
- Sem a key configurada no servidor → o proxy responde **503** (provider indisponível), sem vazar nada.
- O **workflow de CI** (`deploy-phase-05.yml`) tem um *guard*: se algum prefixo de key (`gsk_` do Groq, `AIza` do Google) ou nome de App Setting aparecer no `dist/assets/*.js`, o build **falha**. Você não consegue, nem por acidente, publicar a key no bundle.

> **A regra que você leva para a vida:** segredo de servidor mora **no servidor**. O frontend só conhece a **URL** do proxy, nunca a credencial. Esse padrão (proxy/BFF que injeta o segredo) é o jeito certo de consumir qualquer API paga a partir de um browser.

---

## 6. Gemini 2.0 Flash e a portabilidade entre LLMs (bônus)

### 6.1 Gemini 2.0 Flash

O provider **default** é o **Gemini 2.0 Flash** do Google — um modelo rápido e barato, com **tier gratuito sem exigir cartão de crédito**, ideal para workshop. Endpoint e modelo são pinados (ADE-002 Inv 3):

- Base: `https://generativelanguage.googleapis.com/v1beta`
- Modelo: `models/gemini-2.0-flash`, método `:generateContent`
- Function calling: campo `tools[].functionDeclarations` + `tool_config.function_calling_config.mode: "AUTO"`
- Fonte oficial: https://ai.google.dev/api/generate-content

A API version é **`v1beta`** porque é a que expõe function calling para os modelos 2.x (confirmado em https://ai.google.dev/gemini-api/docs/api-versions).

### 6.2 Portabilidade: trocar o LLM com UMA env var

Aqui está a recompensa do desacoplamento via MCP. O front tem uma **factory de provider** (`lib/llm/index.ts`) que lê a env var `VITE_LLM_PROVIDER`:

```
VITE_LLM_PROVIDER=gemini    → GeminiProvider          (default)
VITE_LLM_PROVIDER=groq      → OpenAiCompatProvider('groq', 'llama-3.3-70b-versatile')
VITE_LLM_PROVIDER=mistral   → OpenAiCompatProvider('mistral', 'mistral-large-latest')
```

Trocar de provider **não muda nenhuma linha** dos componentes, do hook, das tools ou do MCP Server. As mesmas 3 perguntas produzem respostas equivalentes — porque os fatos vêm sempre do **mesmo** MCP Server; só o "cérebro" que redige muda.

| Provider | Base URL oficial | Formato | Modelo default | Cartão? |
|---|---|---|---|---|
| `gemini` (default) | `generativelanguage.googleapis.com/v1beta` | `functionDeclarations` + `tool_config` | `gemini-2.0-flash` | Não |
| `groq` | `api.groq.com/openai/v1` | OpenAI-compat (`tools`/`tool_calls`) | `llama-3.3-70b-versatile` | Não |
| `mistral` | `api.mistral.ai/v1` | OpenAI-compat (`tools`/`tool_calls`) | `mistral-large-latest` | Não |

> **Por que Groq e Mistral compartilham um adapter?** Ambos expõem o **mesmo** formato OpenAI-compatible (`/chat/completions` com `tools`/`tool_calls`). Um único `OpenAiCompatProvider` cobre os dois, parametrizado pelo nome do provider e pelo modelo. Só o Gemini, com seu formato próprio, tem adapter dedicado.

Essa portabilidade não é só elegância: é **resiliência**. Se o Gemini ficar fora do ar ou estourar a cota durante a aula, você troca `VITE_LLM_PROVIDER=groq` e segue (procedimento de fallback detalhado no SPEAKER-NOTES — AC-12).

---

## 7. Onde tudo se encaixa no fluxo v2 + contratos exatos

### 7.1 Identidade: tudo passa pelo gateway (herança F2/F3)

O chatbot **não fala direto** com o MCP Server. Ele fala com o **gateway YARP** (rota `/mcp`), que:

1. Valida o **Bearer token Entra** (obtido via MSAL.js na F3).
2. Extrai o claim `oid` do token e o injeta como header **`X-Entra-OID`** na requisição encaminhada (anti-spoofing: remove qualquer `X-Entra-OID` que o cliente tentar forjar).

O MCP Server **lê** o `X-Entra-OID` via `EntraOidContext` apenas para **logging mascarado** (8 primeiros chars + `…`) — **nunca revalida o JWT** (o gateway é o guardião único) e **nunca loga o oid completo** (é PII). É o mesmo padrão de identidade da F3, agora aplicado às tools.

### 7.2 Contratos exatos

| Contrato | Valor | Onde |
|---|---|---|
| Endpoint MCP | `POST {VITE_GATEWAY_V2_URL}/mcp` (JSON-RPC 2.0) | `mcpClient.ts` |
| Endpoint proxy LLM | `POST {VITE_LLM_PROXY_URL}/llm/{provider}/{path}` | `proxy.ts` |
| Header de auth | `Authorization: Bearer <token Entra>` (MSAL F3) | ambos |
| Header de identidade | `X-Entra-OID` (injetado pelo gateway, lido pelo MCP Server) | gateway → MCP Server |
| App Settings da key (server-side) | `GEMINI_API_KEY`, `GROQ_API_KEY`, `MISTRAL_API_KEY` | Container App do MCP Server |
| App Setting do SQL | `SqlConnectionString` | Container App do MCP Server |
| Env vars do front | `VITE_GATEWAY_V2_URL`, `VITE_LLM_PROXY_URL`, `VITE_LLM_PROVIDER` | build do Vite |

---

## 8. Glossário

| Termo | Significado |
|---|---|
| **LLM** | Large Language Model — modelo de linguagem (Gemini, Groq/Llama, Mistral) |
| **Function calling / tool use** | Capacidade do LLM de pedir a execução de uma ferramenta externa, devolvendo nome + argumentos (não executa, só decide) |
| **MCP** | Model Context Protocol — protocolo aberto que padroniza como tools são expostas e chamadas ([modelcontextprotocol.io](https://modelcontextprotocol.io/)) |
| **JSON-RPC 2.0** | Formato de mensagem (envelope JSON com `method`/`params`/`id`) usado pelo MCP |
| **`tools/list`** | Método MCP que devolve o catálogo de tools (provido pelo SDK) |
| **`tools/call`** | Método MCP que executa uma tool (despachado pelo SDK ao seu handler) |
| **Streamable HTTP** | Transporte MCP sobre HTTP; resposta pode vir como JSON ou SSE |
| **`MapMcp()`** | Extensão do SDK que registra o endpoint `/mcp` no ASP.NET Core |
| **Proxy server-side** | Endpoint do servidor que injeta a key e encaminha ao provider — mantém o segredo fora do browser |
| **`X-Entra-OID`** | Header com o `oid` do usuário, propagado pelo gateway, lido pelo MCP Server p/ log mascarado |
| **Gemini 2.0 Flash** | Modelo default; tier gratuito sem cartão; function calling via `functionDeclarations` |
| **OpenAI-compatible** | Formato de API (`/chat/completions` com `tools`/`tool_calls`) usado por Groq e Mistral |
| **Portabilidade** | Trocar de LLM mudando só `VITE_LLM_PROVIDER`, sem alterar código |

---

## 9. Checklist de pré-aula

Antes de entrar na F5, confirme:

- [ ] Li esta página inteira e entendi o loop **function calling** (LLM pede tool → código executa → resultado volta ao LLM).
- [ ] Entendi por que o LLM **não inventa** disponibilidade/preço — ele **pergunta ao SQL** via tool.
- [ ] Entendi por que a **key da LLM fica no proxy server-side** e nunca no navegador.
- [ ] Sei que as tools originais (S2.5) são `consultar_disponibilidade`, `verificar_ingresso`, `consultar_bracket`, e que a Fase A (S2.8) adiciona `consultar_partidas`, `consultar_classificacao`, `consultar_time`, `consultar_estadio` — total **7 tools** read-only.
- [ ] Sei que o chatbot fala com o **gateway YARP** (não direto com o MCP Server), com **Bearer Entra** (F3).
- [ ] Tenho as F1-F4 funcionando (compra → fila → consumer → SQL; gateway; identidade; n8n).
- [ ] Criei contas gratuitas (sem cartão) no **Google AI Studio** (Gemini) e, opcionalmente, **Groq** e **Mistral** para a demo de portabilidade.
- [ ] Tenho o repositório na branch `phase-05-ai-mcp`.

> **Próximo passo:** na aula, você provisiona o MCP Server como Container App, configura as App Settings (keys + SQL), faz o build do front com as `VITE_*` e roda os 3 smoke tests ao vivo. O passo-a-passo está no [PORTAL-GUIDE](./PORTAL-GUIDE.md).
