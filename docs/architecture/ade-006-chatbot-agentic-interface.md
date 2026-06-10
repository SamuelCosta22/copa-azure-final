# ADE-006 — Chatbot como interface agêntica: split sentidos/ação + n8n como camada de orquestração

> **Tipo:** Architecture Decision Entry (interaction pattern + integration boundary)
> **Status:** ✅ Accepted
> **Date:** 2026-06-08
> **Author:** Aria (Architect)
> **Scope:** EPIC-002 F5 (`src/Fifa2026.V2.McpServer/` — tools MCP + chatbot React) e fases que dependem da camada de ação/orquestração: F4 (n8n) e F6 (Flow Visualizer). Alimenta a próxima story **2.8** (sentidos completos) e registra a decisão das stories futuras das Fases B e C.
> **Supersedes:** N/A — **estende ADE-002** (não a substitui). As 3 tools read-only originais (`consultar_disponibilidade`, `verificar_ingresso`, `consultar_bracket`) permanecem válidas; esta ADE adiciona o catálogo de sentidos e define a camada de ação.
> **Related:** ADE-000 (microsserviço paralelo — Inv 1 backend intocado, Inv 4 idempotência, Inv 5 W3C Trace Context), ADE-002 (MCP SDK 1.4.0 exato, LLM no front, endpoints pinados), ADE-004 (gateway YARP — todo tráfego ao MCP passa pelo gateway), ADE-005 (identidade `oid` propagada como `X-Entra-OID`).

---

## Context

A decisão formalizada aqui foi **tomada pelo owner (Guilherme Prux Campos) em 2026-06-08** — proposta por mim (Aria) na mesma sessão. Esta ADE apenas a registra com o nível de detalhe que a story 2.8 precisa para ser implementada sem invenção (Constitution Art. IV).

O chatbot do v2 (Story 2.5 / F5) hoje expõe **3 tools MCP read-only** sobre o SQL do FIFA 2026 Tickets, via o SDK C# oficial pinado em 1.4.0 exato (ADE-002 Inv 1/2). Verificadas no código real:

| Tool atual | Arquivo | Fonte SQL real |
|---|---|---|
| `consultar_disponibilidade` | `src/Fifa2026.V2.McpServer/Tools/FifaTicketTools.cs` | `matches` + `teams` + `ticket_categories` (PIVOT por rótulo real, ver M-1) |
| `verificar_ingresso` | idem | `purchases` + `users` + `ticket_categories` + `matches` + `teams` |
| `consultar_bracket` | idem | `matches` + `teams` + `stadiums` filtrado por `stage` de mata-mata |

O owner quer que o chat deixe de ser um "balcão de ingressos" e passe a **explorar todo o sistema da Copa** — resultados, classificação, partidas, times, estádios — e que comece a **agir** através do n8n (orquestração / integrações externas), não escrevendo direto no banco.

O problema arquitetural a resolver: como **ampliar a superfície agêntica** (mais tools, e tools que *agem*) sem (a) abrir um vetor de escrita descontrolado a partir de um LLM (que alucina), (b) explodir a contagem de tools a ponto do LLM errar a seleção, e (c) quebrar as invariantes já estabelecidas (ADE-000 backend intocado/idempotência, ADE-002 LLM no front, ADE-004 gateway como guardião, ADE-005 identidade por `oid`).

Esta ADE responde com um **modelo conceitual de "sentidos e mãos"** e o **n8n como única camada de ação/orquestração** para o chatbot.

---

## Decision

Adotamos o pattern **"Agentic Chatbot — Split Sentidos/Mãos com n8n como hub de orquestração"** com 6 invariantes. A peça central é a **regra de ouro** (Invariante 1).

### Invariante 1 — REGRA DE OURO: o chatbot NUNCA escreve direto no banco

O LLM (no front) e o McpServer **jamais executam INSERT/UPDATE/DELETE** disparados por uma conversa. Toda **ação** que mude estado vai pelos **caminhos já existentes e idempotentes** do epic:

- **Compra** → Service Bus → `PurchaseEntryFunction`/`PurchaseConsumerFunction` (F1, ADE-000 Inv 4: UNIQUE + INSERT-catch). O chatbot **não** cria compra direto; no máximo encaminha o usuário ao fluxo de compra v2 existente.
- **Orquestração / integrações externas** (alertas, notificações, consulta a APIs externas) → **webhook n8n** (F4). O n8n é o único componente autorizado a "agir" em nome do chat.

O McpServer continua **somente leitura** (já é: `FifaQueryRepository` abre `SqlConnection` apenas para `SELECT`; comentário de cabeçalho "o McpServer nunca grava"). As tools de ação (Fase B) **não** ganham acesso de escrita ao SQL — elas disparam webhook n8n e retornam o resultado da orquestração.

> **Por quê é a regra de ouro:** um LLM alucina argumentos. Se uma tool pudesse escrever no banco, uma alucinação viraria corrupção de dados irreversível. Roteando ações por Service Bus (idempotente) e n8n (orquestração observável, com auth própria), o blast radius de uma alucinação fica contido — no pior caso, um webhook é chamado com payload inócuo, nunca um `DELETE FROM purchases`.

### Invariante 2 — Split "Sentidos" (ler) vs "Mãos" (agir)

A superfície de tools MCP é dividida em duas naturezas explícitas e auditáveis:

| Natureza | Marca | Caminho de execução | Efeito colateral | Exemplos |
|---|---|---|---|---|
| **Sentidos** (ler) | `[McpServerTool(ReadOnly = true)]` | tool → `IFifaQueryRepository` → **SQL SELECT parametrizado** (Dapper) | Nenhum | as 3 atuais + as 5 da Fase A |
| **Mãos** (agir) | `[McpServerTool]` (ReadOnly = false) | tool → **webhook n8n** (HttpClient server-side) | Orquestração externa (e-mail/alerta/integração) | `criar_alerta_ingresso` (Fase B) |

As duas naturezas convivem no mesmo McpServer e são listadas juntas em `tools/list`. O atributo `ReadOnly` do SDK 1.4.0 é o discriminador formal — o LLM e o front podem (opcionalmente) usar o hint para tratar ações com confirmação do usuário. **Sentidos nunca chamam n8n; Mãos nunca tocam SQL.** Essa separação é o invariante estrutural que torna a regra de ouro verificável por inspeção do código.

### Invariante 3 — Sentidos = SQL SELECT direto, seguindo o padrão existente (sem desvio)

As novas tools read-only (Fase A) seguem **exatamente** o padrão já implementado e validado em F5 (ADE-002, gate S2.5):

1. Método estático em uma classe `[McpServerToolType]` (descoberta por `WithToolsFromAssembly()` em `Program.cs`), cada um com `[McpServerTool(Name = "...", ReadOnly = true)]` + `[Description]` em português (o SDK deriva o JSON Schema da assinatura + `[Description]` — **não se escreve schema à mão**).
2. Dependências injetadas por DI nos parâmetros: `IFifaQueryRepository`, `EntraOidContext`, `ILogger`. Identidade lida via `EntraOidContext.GetMaskedOidForLog()` **só para log mascarado** — nunca revalida JWT (gateway é o guardião, ADE-004/ADE-005).
3. Acesso a dados via `FifaQueryRepository` (Dapper + `Microsoft.Data.SqlClient`), **100% parametrizado** (anti SQL injection — CodeRabbit focus). Cada nova tool ganha um método na interface `IFifaQueryRepository` (mockável nos testes).
4. Parâmetros opcionais recebem **default explícito** (`= null`) — lição do gate S2.5: o SDK 1.4.0 marca nullable sem default como `required` e `tools/call` com argumentos parciais quebra.
5. DTOs de retorno em `McpQueryResults.cs` com `[JsonPropertyName]` (contrato JSON estável e em português, como os atuais).

### Invariante 4 — Mãos = webhook n8n, reusando o contrato de webhook já existente (F4)

A primeira "mão" (`criar_alerta_ingresso`, Fase B) dispara o **webhook n8n** exatamente no padrão que F4 já estabeleceu para a notificação pós-compra (Story 2.4 AC-5/AC-6):

- `HttpClient` server-side no McpServer (já há `AddHttpClient` em `Program.cs`), `POST` num endpoint de webhook n8n com URL em **App Setting** (`N8N_WEBHOOK_URL` ou um setting dedicado por workflow) — **nunca hardcoded, nunca no bundle do front**.
- Body JSON inclui **`correlationId`** (ADE-000 Inv 5 — W3C Trace Context; o n8n loga o `correlationId` em cada node, essencial para o Flow Visualizer F6) e **`entraOid`** (identidade do usuário do chat, propagada pelo gateway como `X-Entra-OID`).
- A tool **não orquestra** — ela só dispara o webhook e devolve "alerta registrado". A lógica (criar alerta, agendar checagem, notificar) vive no **workflow n8n**, não no McpServer. Isso prova o padrão `chatbot → MCP → n8n` sem mover lógica de negócio para dentro da tool.

### Invariante 5 — Princípio de design: poucas tools bem parametrizadas > muitas tools estreitas

A ampliação de "sentidos" é feita **consolidando** capacidades em poucas tools flexíveis e bem parametrizadas, não criando uma tool por pergunta. Motivo empírico: quanto mais tools (e mais parecidas entre si), mais o LLM erra a seleção e os argumentos. Uma `consultar_partidas` parametrizada por `time | fase | estadio | grupo | data` cobre "jogos do Brasil", "jogos nas oitavas", "jogos no Maracanã", "jogos do grupo C" e "jogos de 15/06" — cinco perguntas, uma tool. Quando o schema suporta, **resultado/placar é um campo da mesma partida** (não uma tool separada): `consultar_partidas` retorna o placar quando o jogo já foi disputado, dispensando uma `consultar_resultados` independente.

### Invariante 6 — Tudo continua passando pelo gateway YARP; identidade continua sendo o `oid`

Nenhuma mudança no perímetro: o front (LLM no browser) chama o gateway YARP com Bearer Entra; o gateway valida o JWT, injeta `X-Correlation-ID` (ADE-004) e propaga `X-Entra-OID` (ADE-005) ao McpServer. As novas tools read-only e a tool de ação **herdam** esse perímetro — o McpServer permanece com ingress interno, atrás do gateway, sem revalidar JWT. A tool de ação, ao chamar o n8n, **repassa** `correlationId` e `entraOid` no body (Invariante 4), mantendo a cadeia de rastreabilidade intacta até o n8n (e, em F6, até o SignalR).

---

## Catálogo de tools da Fase A (Sentidos completos) — escopo da Story 2.8

Todas read-only, `[McpServerTool(ReadOnly = true)]`, SQL SELECT parametrizado (Dapper). As tabelas/colunas abaixo foram **verificadas contra o schema real** (`fifa2026-api/database/schema.sql`) e o seed real (`migrations/2026-05-07-update-48-teams.sql`, `2026-05-07-update-16-stadiums.sql`, `2026-05-08-group-stage-72.sql`, `2026-05-07-knockout-matches.sql`, `2026-05-08-real-fifa-prices.sql`).

> **Schema real relevante (fonte da verdade, não inventar):**
> - `matches(id, home_team_id, away_team_id, stadium_id, date DATE, time NVARCHAR(5), stage NVARCHAR(50), group_name NVARCHAR(1), home_score INT NULL, away_score INT NULL, status NVARCHAR(20) DEFAULT 'scheduled')`
> - `teams(id, name, code NVARCHAR(3), flag, group_name NVARCHAR(1), confederation, fifa_ranking)`
> - `stadiums(id, name, city, country, capacity, image, description, address, latitude, longitude)`
> - `ticket_categories(id, match_id, category NVARCHAR(50), price, total_quantity, available_quantity, description)`
> - **`matches.stage` tem valores REAIS:** `'Fase de Grupos'` (grupos), `'round_of_32'`, `'round_of_16'`, `'quarter_final'`, `'semi_final'`, `'third_place'`, `'final'`. ⚠️ O grupo é `'Fase de Grupos'` (com acento/espaços) — **não** um valor `round_of_*`. A migration knockout-matches insere `'round_of_32'`; a group-stage-72 insere `'Fase de Grupos'`.
> - **`matches.group_name`** só é preenchido na fase de grupos (A–L); NULL no mata-mata.
> - **Não existe tabela `standings`/classificação** — a classificação é **calculada por agregação** dos jogos de grupo (ver `consultar_classificacao`).

### A.1 — `consultar_partidas` (a tool-âncora; consolida "jogos" + "resultados")

| Campo | Valor |
|---|---|
| **Nome** | `consultar_partidas` |
| **ReadOnly** | `true` (Sentido) |
| **Descrição (`[Description]`)** | "Consulta partidas da Copa 2026 com filtros flexíveis. Use para perguntas como 'jogos do Brasil', 'jogos nas oitavas', 'jogos no Maracanã', 'jogos do grupo C' ou 'jogos do dia 15/06'. Retorna o placar quando o jogo já foi disputado." |
| **Parâmetros** (todos opcionais, default `= null`) | `time` (string — nome OU código do time, ex.: "Brasil"/"BRA"), `fase` (string em linguagem natural — "grupos", "oitavas", "quartas", "semifinal", "final" — mapeada para `stage`), `estadio` (string — nome/cidade do estádio), `grupo` (string — "A".."L"), `data` (string ISO `YYYY-MM-DD`), `apenasComResultado` (bool — só jogos com placar) |
| **Retorno** | `IReadOnlyList<MatchResult>` com `{ partida, data, horario, estadio, fase, grupo, placarMandante?, placarVisitante?, status }`. Placar `null` quando não disputado. |
| **Fontes SQL** | `matches m` LEFT JOIN `teams ht`/`teams at` (home/away) LEFT JOIN `stadiums s`. Filtros: `time` → `ht.name/at.name LIKE` ou `ht.code/at.code =`; `fase` → mapeia para `m.stage` (reusar `MapRodadaToStage` + adicionar `'Fase de Grupos'`); `grupo` → `m.group_name`; `estadio` → `s.name/s.city LIKE`; `data` → `m.date`; `apenasComResultado` → `m.home_score IS NOT NULL`. Times NULL no mata-mata → `COALESCE(ht.name,'A definir')` (padrão já usado em `consultar_bracket`). |

> **Decisão de consolidação:** `consultar_resultados(time|rodada|data)` do pedido **NÃO vira tool separada** — vira o filtro `apenasComResultado=true` (e os mesmos `time`/`fase`/`data`) de `consultar_partidas`. Placar e partida são a mesma linha em `matches` (`home_score`/`away_score`); separar duplicaria a query e confundiria a seleção do LLM (Invariante 5). Resultado: **menos uma tool**, mesma capacidade.

### A.2 — `consultar_classificacao`

| Campo | Valor |
|---|---|
| **Nome** | `consultar_classificacao` |
| **ReadOnly** | `true` (Sentido) |
| **Descrição** | "Consulta a classificação (tabela de pontos) de um grupo da fase de grupos da Copa 2026. Calculada a partir dos resultados dos jogos do grupo." |
| **Parâmetros** | `grupo` (string obrigatório — "A".."L") |
| **Retorno** | `IReadOnlyList<StandingRow>` com `{ posicao, time, jogos, vitorias, empates, derrotas, golsPro, golsContra, saldo, pontos }` |
| **Fontes SQL** | **Calculado** (não há tabela `standings`): agregação dos `matches` onde `m.group_name = @grupo AND m.stage = 'Fase de Grupos' AND m.home_score IS NOT NULL`, expandindo cada partida em duas linhas (mandante/visitante) via `UNION ALL`, somando 3/1/0 pontos por resultado, agrupando por `team_id`, ordenando por `pontos DESC, saldo DESC, golsPro DESC`. JOIN `teams` para o nome. |

> **Honestidade de schema (Art. IV):** classificação **não existe como tabela** no schema real. A estratégia é **agregação dos jogos do grupo**. Antes dos jogos serem disputados (`home_score` NULL no seed atual), a classificação retorna todos com 0 ponto — comportamento correto e documentado, não um bug. Cabe ao `@dev` (Story 2.8) implementar a agregação; o `@data-engineer` pode ser consultado para otimização da query se necessário (delegação ADE-004 da matriz de autoridade).

### A.3 — `consultar_time`

| Campo | Valor |
|---|---|
| **Nome** | `consultar_time` |
| **ReadOnly** | `true` (Sentido) |
| **Descrição** | "Consulta informações de uma seleção da Copa 2026 (grupo, confederação, ranking FIFA, código)." |
| **Parâmetros** | `nome` (string obrigatório — nome OU código, ex.: "Brasil"/"BRA") |
| **Retorno** | `TeamResult` com `{ encontrado, nome, codigo, grupo, confederacao, rankingFifa, bandeira }` |
| **Fontes SQL** | `teams` (`name`, `code`, `group_name`, `confederation`, `fifa_ranking`, `flag`). Match por `name LIKE @nome OR code = @nome` (case-insensitive). |

### A.4 — `consultar_estadio`

| Campo | Valor |
|---|---|
| **Nome** | `consultar_estadio` |
| **ReadOnly** | `true` (Sentido) |
| **Descrição** | "Consulta informações de um estádio/sede da Copa 2026 (cidade, país, capacidade, descrição)." |
| **Parâmetros** | `nome` (string obrigatório — nome do estádio OU cidade) |
| **Retorno** | `StadiumResult` com `{ encontrado, nome, cidade, pais, capacidade, descricao }` |
| **Fontes SQL** | `stadiums` (`name`, `city`, `country`, `capacity`, `description`). Match por `name LIKE @nome OR city LIKE @nome`. (Nota: Rose Bowl está soft-disabled como "Rose Bowl (legacy)" — não filtrar especialmente; aparece se buscado.) |

### Catálogo final consolidado (entrada para a Story 2.8 / @sm)

| # | Tool | Natureza | Params | Retorno (resumo) | Fontes SQL |
|---|---|---|---|---|---|
| (existente) | `consultar_disponibilidade` | Sentido | `matchId?`, `matchDescription?` | disponibilidade+preço por categoria | `matches`+`teams`+`ticket_categories` |
| (existente) | `verificar_ingresso` | Sentido | `ingressoId` | validade da compra + dados | `purchases`+`users`+`ticket_categories`+`matches`+`teams` |
| (existente) | `consultar_bracket` | Sentido | `rodada` | jogos do mata-mata por stage | `matches`+`teams`+`stadiums` |
| **A.1** | `consultar_partidas` | Sentido | `time? fase? estadio? grupo? data? apenasComResultado?` | lista de partidas (+placar quando houver) | `matches`+`teams`+`stadiums` |
| **A.2** | `consultar_classificacao` | Sentido | `grupo` | tabela de pontos do grupo (**calculada**) | `matches`+`teams` (agregação) |
| **A.3** | `consultar_time` | Sentido | `nome` | dados da seleção | `teams` |
| **A.4** | `consultar_estadio` | Sentido | `nome` | dados do estádio | `stadiums` |

**Total Fase A = 4 tools novas** (não 5 — `consultar_resultados` consolidada em `consultar_partidas`, Invariante 5). Superfície de sentidos do chat passa de 3 → 7 tools, cobrindo disponibilidade, ingresso, mata-mata, partidas (com resultado), classificação, times e estádios — "explorar todo o sistema da Copa".

---

## Fase B — Primeira "mão" via n8n (decisão registrada; implementação em story futura)

**Fora do escopo da Story 2.8.** Decisão registrada agora; story própria depois.

### B.1 — `criar_alerta_ingresso` (action tool)

| Campo | Valor |
|---|---|
| **Nome** | `criar_alerta_ingresso` |
| **ReadOnly** | `false` (Mão) |
| **Descrição** | "Cria um alerta para avisar o usuário quando houver ingressos disponíveis para uma partida. Aciona uma automação de orquestração." |
| **Parâmetros** | `matchId` (int) OU `matchDescription` (string), `categoria` (string — VIP/Cat1/Cat2, opcional) |
| **Efeito** | Dispara **webhook n8n** (`POST`, URL em App Setting), body `{ correlationId, entraOid, matchId, matchDescription, categoria, requestedAt }`. **Não escreve no SQL** (regra de ouro). Retorna `{ registrado: true, alertaId? }`. |
| **Orquestração** | Vive no **workflow n8n** (criar/agendar/notificar) — análogo ao `post-purchase-notification` (F4). O McpServer só dispara e devolve. |

**Por que prova o padrão:** é o menor incremento que demonstra `chatbot → gateway → MCP (mão) → n8n` sem mover lógica de negócio para a tool nem violar a regra de ouro. Reusa 100% o contrato de webhook de F4 (URL em App Setting, `correlationId` no body, auth no n8n).

---

## Fase C — n8n externo + F6 (decisão registrada; implementação em story futura)

**Fora do escopo da Story 2.8.** Decisão registrada agora.

A "mão" evolui para o n8n **consultar uma API externa** (placar ao vivo / notícias) e **emitir um FlowEvent**, fechando a narrativa agêntica no Flow Visualizer (F6). A jornada visualizada:

```
chat (LLM no front)
  → gateway YARP (valida JWT, injeta X-Correlation-ID, propaga X-Entra-OID)
    → McpServer (tool de ação)
      → n8n (webhook → node de integração com API externa → emite evento com correlationId)
        → SignalR (FlowHub.SendFlowEvent) → Flow Visualizer /flow (bolinha animada)
```

Isso reusa a infra de F6 já desenhada (Story 2.6): `FlowEventsFunction` lê App Insights por `correlationId`, `FlowHub` empurra `SendFlowEvent(correlationId, eventType, ...)` para o grupo `correlation-<id>`; o n8n já é nó da timeline do visualizer (nó 5). A novidade da Fase C é o **disparo a partir do chat** (não só pós-compra) e a **integração externa dentro do n8n** — sem nenhuma nova invariante de perímetro (Invariante 6 cobre).

> **Honestidade (Art. IV):** o Flow Visualizer atual (F6) rastreia a jornada **de compra** (Gateway → Entry → Service Bus → Consumer → n8n → SQL). A Fase C adiciona uma jornada **de chat** à mesma malha SignalR/App Insights. A reutilização é real (mesmos componentes), mas o caminho do chat precisa que a tool de ação propague `correlationId`/`entraOid` ao n8n (Invariante 4) — pré-requisito já garantido pelo design da Fase B.

---

## Invariantes anti-alucinação (modelo ADE-002, estendido para a interface agêntica)

1. **SDK oficial faz o framing** (ADE-002 Inv 1/2): `tools/list`/`tools/call` e o JSON Schema de input vêm do SDK 1.4.0 a partir da assinatura + `[Description]`. Não se escreve schema/JSON-RPC à mão.
2. **Sentidos só leem; SQL só parametrizado** (Inv 1/3): zero concatenação de string; `IFifaQueryRepository` é a única porta de dados, somente `SELECT`.
3. **Mãos só agem por caminhos idempotentes/observáveis** (Inv 1/4): nenhuma escrita direta no SQL a partir de tool; ações vão por Service Bus (compra) ou webhook n8n (orquestração). Uma alucinação de argumento não corrompe dados.
4. **Schema é fonte da verdade** (Art. IV): rótulos de categoria são `'VIP Premium'`/`'Categoria 1'`/`'Categoria 2'` (via `CategoryLabelMapper`, fonte única); `stage` de grupo é `'Fase de Grupos'` (não `round_of_*`); classificação é **calculada** (não há tabela). Tools que citarem dados inexistentes são rejeitadas em review.
5. **Identidade nunca é inventada nem revalidada na tool** (ADE-004/005): `oid` chega via `X-Entra-OID` propagado pelo gateway; a tool só o usa para log mascarado e para repassar ao n8n. A tool nunca decide autorização — o gateway é o guardião.
6. **Defaults explícitos em parâmetros opcionais** (gate S2.5): todo parâmetro opcional tem `= null`/default, senão o SDK 1.4.0 o marca `required` e `tools/call` parcial quebra ao vivo.

---

## Rationale

### Por que o split "sentidos/mãos" (vs tools genéricas sem natureza)?

- **Auditabilidade da regra de ouro:** com sentidos (`ReadOnly=true` → SQL) e mãos (`ReadOnly=false` → n8n) fisicamente separados, "o chat não escreve no banco" é verificável por inspeção — não é uma promessa, é uma propriedade estrutural.
- **UX/segurança:** o hint `ReadOnly` permite ao front exigir confirmação só nas ações (mãos), deixando leitura fluida.
- **Didático:** ensina a distinção entre *retrieval* e *actuation* num agente — conceito central de sistemas agênticos, transferível para a vida pós-workshop.

### Por que n8n como única camada de ação (vs tool escrevendo no SQL)?

- **Blast radius:** LLM alucina; SQL direto a partir de alucinação = corrupção. n8n/Service Bus contêm o dano.
- **Reuso real:** o contrato de webhook (URL em App Setting, `correlationId` no body, auth no n8n) já existe e foi validado em F4. A Fase B não inventa nada — replica o padrão.
- **Observabilidade fecha em F6:** ação via n8n já é nó do Flow Visualizer; a jornada agêntica vira visível "de graça".

### Por que consolidar em poucas tools (vs uma tool por pergunta)?

- **Acerto de seleção do LLM:** muitas tools estreitas e parecidas aumentam erro de roteamento. `consultar_partidas` parametrizada cobre 5+ intenções com 1 tool (Invariante 5).
- **Manutenção:** menos superfície, menos schema, menos testes — coerente com 8h de workshop por fase.

---

## Consequences

### Positivas

- ✅ Chat passa a "explorar todo o sistema da Copa" (7 sentidos) sem abrir vetor de escrita (regra de ouro).
- ✅ Padrão `chatbot → MCP → n8n` provado com o menor incremento (Fase B) e fechado no visualizer (Fase C).
- ✅ Zero desvio de padrões: novas tools reusam `IFifaQueryRepository`/Dapper/`EntraOidContext`/DTOs; ação reusa o webhook de F4; perímetro reusa gateway/`oid`.
- ✅ Amarra didaticamente F5 (tools) + F4 (n8n) + F6 (visualizer) numa narrativa agêntica única — ouro pedagógico.
- ✅ `-1` tool por consolidação (`consultar_resultados` → filtro de `consultar_partidas`): menos superfície, menos erro de LLM.

### Negativas / Trade-offs aceitos

- ⚠️ **Classificação calculada custa uma query de agregação** (vs SELECT trivial de uma tabela `standings` inexistente). Mitigado: agregação é simples (UNION ALL + GROUP BY); `@data-engineer` pode otimizar/indexar se necessário. Honestamente documentado — não se inventa tabela.
- ⚠️ **Ação assíncrona via n8n** não devolve resultado de negócio imediato à tool (a tool retorna "registrado", a orquestração roda depois). Mitigado: é o comportamento correto de uma "mão" (fire-and-forth orquestrado); o usuário é notificado pelo workflow, não pela resposta síncrona.
- ⚠️ **`consultar_partidas` flexível tem SQL com vários filtros opcionais** (mais complexo que as 3 atuais). Mitigado: padrão `(@p IS NULL OR coluna = @p)` parametrizado; consolidar reduz o número total de queries a manter.
- ⚠️ **Mais tools = mais tokens no `tools/list`** enviado ao LLM. Mitigado: 7 tools é folgado para os modelos-alvo; a consolidação (Inv 5) mantém a contagem baixa.

---

## Alternatives Considered (rejeitadas)

### Alt 1 — LLM com SQL livre (text-to-SQL / tool genérica `executar_query`)

- **Rejected porque:** dar SQL arbitrário a um LLM é o oposto da regra de ouro — alucinação vira `DROP`/`DELETE`/exfiltração de dados. Quebra ADE-000 Inv 1 (backend intocado) e a postura anti-injection do projeto. Tools tipadas e parametrizadas (sentidos) entregam a mesma capacidade de leitura com superfície de ataque/alucinação controlada.

### Alt 2 — Muitas tools estreitas (uma por pergunta: `jogos_do_time`, `jogos_do_dia`, `jogos_da_fase`, `resultados`, ...)

- **Rejected porque:** explode a contagem de tools parecidas, piora a seleção do LLM e multiplica schema/testes. Consolidado em `consultar_partidas` parametrizada (Invariante 5).

### Alt 3 — Tool de ação escrevendo direto no SQL (sem n8n)

- **Rejected porque:** viola a regra de ouro (Inv 1). Mesmo "só inserir um alerta" abriria um caminho de escrita a partir do LLM. Roteando por n8n, a ação fica idempotente, observável e com auth própria — e fecha no Flow Visualizer.

### Alt 4 — Mover a orquestração da ação para dentro da tool (McpServer faz e-mail/agendamento)

- **Rejected porque:** transforma o McpServer (servidor de tools) num orquestrador, acoplando lógica de negócio à camada de tools. n8n é a camada de orquestração do epic (F4); a tool só dispara o webhook (Inv 4). Mantém responsabilidades separadas.

### Alt 5 — `consultar_resultados` como tool separada de `consultar_partidas`

- **Rejected porque:** placar e partida são a mesma linha em `matches` (`home_score`/`away_score`). Duas tools sobre a mesma tabela confundem a seleção do LLM e duplicam query. Consolidado como filtro `apenasComResultado` (Invariante 5).

---

## Validation

Esta decisão é considerada **validada** (Fase A / Story 2.8) quando:

- [ ] 4 novas tools (`consultar_partidas`, `consultar_classificacao`, `consultar_time`, `consultar_estadio`) listadas em `tools/list` com `ReadOnly=true` e JSON Schema derivado pelo SDK.
- [ ] Cada nova tool tem método em `IFifaQueryRepository` com SQL **parametrizado** (sem concatenação), mockado nos testes; nenhum acesso de escrita.
- [ ] `consultar_partidas` resolve "jogos do Brasil" (filtro `time`), "jogos nas oitavas" (`fase`→`round_of_16`), "jogos do grupo C" (`grupo`), "jogos no <estádio>" (`estadio`) e retorna placar quando `home_score IS NOT NULL`.
- [ ] `consultar_classificacao('C')` retorna tabela calculada por agregação dos `matches` de grupo (não lê tabela `standings` — que não existe).
- [ ] `stage` da fase de grupos tratado como `'Fase de Grupos'` (não `round_of_*`); rótulos de categoria via `CategoryLabelMapper`.
- [ ] Smoke no chat: "Quais os jogos do Brasil?", "Como está o grupo C?", "Fale do Maracanã" respondidos via tool, não alucinação.
- [ ] Nenhuma tool de Fase A é `ReadOnly=false`; nenhuma escreve no SQL (regra de ouro preservada).

Para Fase B/C (stories futuras): `criar_alerta_ingresso` dispara webhook n8n com `correlationId`+`entraOid` no body (sem escrita SQL); a jornada de chat aparece no Flow Visualizer via SignalR.

---

## Relação com ADEs anteriores

- **ADE-002 (estende, não substitui):** as 3 tools read-only e o pin do SDK 1.4.0, a posição do LLM no front e os endpoints LLM pinados permanecem **inalterados**. Esta ADE adiciona 4 sentidos e a camada de ação — todos sob as mesmas invariantes de ADE-002 (SDK faz o framing, defaults explícitos, identidade só para log mascarado). A correção M-1 do gate S2.5 (rótulos de categoria reais) é **invariante herdada** (anti-alucinação Inv 4).
- **ADE-000:** regra de ouro materializa a Inv 1 (backend intocado) e a Inv 4 (idempotência) — ações por Service Bus/n8n, nunca escrita ad-hoc. Webhook n8n propaga `correlationId` (Inv 5, W3C Trace Context).
- **ADE-004:** todo tráfego (sentidos e mãos) entra pelo gateway YARP, que injeta `X-Correlation-ID` e é o guardião do JWT.
- **ADE-005:** identidade é o `oid` (`X-Entra-OID`); a tool de ação repassa `entraOid` ao n8n sem revalidar.

---

## Impact on EPIC-002

| Story | Impacto | Ação (executor) |
|---|---|---|
| **2.8 (nova — F5+)** | **Cria as 4 tools de sentidos** desta ADE (catálogo Fase A). O catálogo acima é o contrato de entrada. | Story a draftar — **@sm** (este ADE alimenta o draft; @architect já satisfeito). |
| **Story futura (Fase B)** | Implementa `criar_alerta_ingresso` (mão via webhook n8n) + workflow n8n correspondente. Decisão registrada aqui. | **@sm**/@pm quando priorizado. |
| **Story futura (Fase C)** | n8n consulta API externa + emite FlowEvent; jornada de chat no Flow Visualizer (reusa F6). | **@sm**/@pm quando priorizado. |
| **2.5 (F5)** | Sem re-draft. As 3 tools atuais permanecem; esta ADE referencia-as como baseline. | Nenhuma — referência apenas. |

> **NÃO altero código nem stories** (autoridade de @dev/@sm/@po). Esta ADE fecha a decisão de interação/integração e entrega o catálogo Fase A para o @sm draftar a Story 2.8.

---

## Change Log

| Date | Author | Description |
|---|---|---|
| 2026-06-08 | @architect (Aria) | ADE-006 criada — split sentidos/mãos + n8n como hub de orquestração + regra de ouro (chatbot nunca escreve direto no banco). Catálogo Fase A (4 tools novas, `consultar_resultados` consolidada em `consultar_partidas`) verificado contra schema/seed reais. Fases B (action tool via webhook n8n) e C (n8n externo + F6) registradas para stories futuras. Estende ADE-002 sem substituir. |

**Authority:** Aria (Architect) — designado por @aiox-master para padrões de integração, design de API/tools e seleção de tecnologia. Detalhe de DDL/otimização de query da classificação calculada pode ser delegado a @data-engineer (matriz de autoridade).
**Review cycle:** Imutável durante EPIC-002. Mudanças → nova ADE que a supersede.
