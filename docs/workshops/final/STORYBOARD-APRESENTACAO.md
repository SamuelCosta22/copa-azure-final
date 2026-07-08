# Storyboard da Apresentação — A Grande Final (F5/F6)

> **O que é este arquivo:** um **prompt pronto para colar no Claude for PowerPoint**. Ele gera o deck da Grande Final (`.pptx`) no **mesmo DNA visual do deck real das Quartas** ("Quartas de final - Apresentação.pptx"): **10 slides**, tema escuro, uma tecnologia por slide, selos "TECNOLOGIA N DE 4", footer fixo.
>
> **Como usar:**
> 1. Abra o Claude for PowerPoint (ou Claude com a skill de PowerPoint).
> 2. **Cole todo o bloco abaixo** (da linha `═══ INÍCIO DO PROMPT ═══` até `═══ FIM DO PROMPT ═══`). O prompt é **auto-contido** — todo o texto de cada slide já está escrito por extenso; não é preciso dar acesso ao repositório.
> 3. Gere o `.pptx`. Ele **não** entra no git versionado (mesmo padrão de Oitavas/Quartas: o repo guarda o storyboard + o deck reveal `slides.md`; o binário é exportado à parte).
>
> **Fidelidade técnica (Art. IV — No Invention):** todo número, nome de tool, nó e componente já está fixado no prompt e bate com o código/guia reais: **7 tools read-only**, **5 nós**, notificação pós-compra **inline** na Function Consumer, **`gemini-2.5-flash`**, chave Gemini **só no proxy server-side**, `X-Gateway-Key` fechando o bypass ao McpServer. Não altere esses números ao gerar o deck. **O aluno da Final nunca usou n8n/orquestração externa/PostgreSQL — NÃO cite nenhum deles em slide algum.**

---

```
═══ INÍCIO DO PROMPT (cole tudo a partir daqui no Claude for PowerPoint) ═══

Gere uma apresentação PowerPoint (.pptx) de EXATAMENTE 10 SLIDES para o encerramento
de um workshop técnico ("A Grande Final" da "Copa do Mundo Azure 2026"). Siga com
precisão o layout e o DNA descritos abaixo — este deck é o gêmeo, na fase seguinte,
de um deck já existente das "Quartas de Final".

────────────────────────────────────────────────────────────────────────
DIRETRIZES VISUAIS GLOBAIS (valem para TODOS os slides)
────────────────────────────────────────────────────────────────────────
• Tema ESCURO (fundo quase preto), tipografia sem serifa, tom "produto de nuvem".
• Cores de acento:
   - VERDE-AZULADO (teal) → tudo de F5 / voz / leitura / read-only.
   - ROXO → tudo de F6 / visão / observabilidade.
   - VERMELHO → usar SÓ no realce de segurança (X-Gateway-Key / "por construção").
• FOOTER FIXO em todos os 10 slides (rodapé, uma linha, discreto):
   COPA DO MUNDO AZURE 2026   |   A GRANDE FINAL · F5/F6 — VOZ & VISÃO
• Nos slides de tecnologia (4, 5, 6, 7): um SELO no canto — "TECNOLOGIA N DE 4".
• Diagramas SEMPRE como caixas + setas simples (estilo "COMO FUNCIONA"), nunca
   screenshots. Sem blocos de código longos (o código vive no runbook do aluno).
• UMA tecnologia por slide. Densidade moderada: título forte, um diagrama, quatro
   recursos, uma caixa "▸ NESTA ETAPA". Frases curtas.
• Slides de CONCEITO-CHAVE (3, 8): destaque visual — uma frase de efeito grande,
   entre aspas, como âncora do slide.

════════════════════════════════════════════════════════════════════════
SLIDE 1 — CAPA
════════════════════════════════════════════════════════════════════════
• Etiqueta (topo): A GRANDE FINAL · COPA DO MUNDO AZURE
• Título grande: Voz & Visão — a Copa que te responde
• Subtítulo narrativo (UMA frase): "Um chatbot que lê o estado real da Copa e uma
   tela onde cada compra se acende em cinco nós — com segurança por construção."
• Faixa de jornada (chips na base): Oitavas (F1) → Quartas (F2/F3) → **Final (F5/F6)**
• Footer padrão.

════════════════════════════════════════════════════════════════════════
SLIDE 2 — STACK DA FASE · "As tecnologias que vamos usar"
════════════════════════════════════════════════════════════════════════
• Rótulo grande: STACK DA FASE
• Subtítulo: As tecnologias que vamos usar (só o que é NOVO nesta fase)
• Cinco linhas (ícone + nome em destaque + uma frase cada):
   1. MCP (McpServer .NET) — protocolo que dá "ferramentas" ao LLM
      (tools/list / tools/call); server .NET, ingress INTERNO, atrás do gateway.
   2. RAG por tool-use (Gemini + 7 tools) — o chatbot RECUPERA o fato real via
      tool antes de responder; não inventa.
   3. Google Gemini (gemini-2.5-flash) — o LLM que decide QUAL tool chamar
      (function calling AUTO); chave server-side.
   4. Managed Identity + Key Vault — os apps leem os segredos do cofre e a telemetria
      do Log Analytics SEM chave em claro (roles Secrets User / Log Analytics Reader).
   5. Azure SignalR (Free / Service Mode Default) — empurra eventos ao browser em
      tempo real (WebSocket) → o Flow Visualizer (5 nós).
• Nota de rodapé do slide (pequena): as fases anteriores já deram compra async,
   gateway, identidade, Container Apps e SQL — este deck NÃO os reexplica.
• Footer padrão.

════════════════════════════════════════════════════════════════════════
SLIDE 3 — CONCEITO-CHAVE · A regra de ouro (agora trivial)
════════════════════════════════════════════════════════════════════════
• Rótulo: CONCEITO-CHAVE
• Título: A regra de ouro, agora por CONSTRUÇÃO — o chatbot nunca escreve no banco
• Corpo (duas ideias curtas, contrastadas):
   - ANTES: a segurança de uma ação dependia de ROTEAR a ação por um caminho seguro
      (fila, orquestração, validação).
   - AGORA: o McpServer é SÓ SENTIDOS — não existe um vetor de escrita para o LLM
      chamar. A regra vale por construção, não por roteamento.
• Mini-visual (faixa horizontal): os 7 sentidos (todos de LEITURA) de um lado —
   consultar_disponibilidade · verificar_ingresso · consultar_bracket ·
   consultar_partidas · consultar_classificacao · consultar_time · consultar_estadio —
   e, do outro lado, um "✗ nenhuma tool de escrita" em vermelho.
• FRASE DE EFEITO (grande, entre aspas, âncora do slide):
   "O que não existe não pode ser chamado."
• Nuance honesta (linha pequena, importante): o LLM pode DIZER em texto "criei o
   alerta" — isso é alucinação de TEXTO, não uma tool call; nada é gravado.
• Footer padrão.

════════════════════════════════════════════════════════════════════════
SLIDE 4 — TECNOLOGIA 1 DE 4 · MCP (Model Context Protocol)
════════════════════════════════════════════════════════════════════════
• Título da tecnologia: MCP · MODEL CONTEXT PROTOCOL
• "O que é MCP?" (2-3 frases): Um protocolo padrão para expor "ferramentas" (tools)
   que um LLM DESCOBRE e CHAMA em runtime — o "USB-C das integrações de IA". Aqui, um
   McpServer .NET fica ATRÁS do gateway, com ingress interno; o browser nunca o alcança.
• Bloco COMO FUNCIONA (mini-fluxo com setas, caixas):
   Chatbot → [tools/list] descobre → [tools/call] executa (JSON-RPC 2.0) →
   McpServer (interno, atrás do gateway) → SELECT no Azure SQL
• Bloco PRINCIPAIS RECURSOS (4 itens, nome + frase):
   - tools/list — o LLM descobre, em runtime, as 7 ferramentas disponíveis.
   - tools/call (JSON-RPC 2.0) — a chamada tipada de uma ferramenta.
   - McpServer .NET (SDK oficial) — Container App de ingress INTERNO; sem URL pública.
   - 7 tools read-only — todas [McpServerTool(ReadOnly = true)]; SELECT parametrizado
      (Dapper). Nenhuma escreve.
• Caixa "▸ NESTA ETAPA": subir o McpServer (Container App interno, porta 8080) e ver
   tools/list retornar EXATAMENTE 7 sentidos, todos readOnly: true.
• Selo: TECNOLOGIA 1 DE 4. Acento teal. Footer padrão.

════════════════════════════════════════════════════════════════════════
SLIDE 5 — TECNOLOGIA 2 DE 4 · RAG por tool-use (Gemini + 7 tools)
════════════════════════════════════════════════════════════════════════
• Título da tecnologia: RAG POR TOOL-USE
• "O que é RAG?" (2-3 frases): Retrieval-Augmented Generation — o modelo RECUPERA um
   fato de uma fonte externa ANTES de responder, em vez de "lembrar" (e inventar). Aqui
   NÃO é vector store: é grounding via MCP (SELECT no banco, não embeddings). Mesmo
   princípio — recuperar antes de gerar — implementação diferente.
• Bloco COMO FUNCIONA (mini-fluxo):
   "Quando o Brasil joga?" → Gemini decide a tool (function calling AUTO) →
   consultar_partidas → SQL real → resposta FUNDAMENTADA
• Bloco PRINCIPAIS RECURSOS (4 itens):
   - Function calling (AUTO) — o Gemini decide QUAL das 7 tools chamar.
   - Grounding — a resposta vem do BANCO, não do conhecimento paramétrico do modelo.
   - Chave server-side — a GEMINI_API_KEY fica no PROXY /llm, nunca no browser; um
      guard falha o build se a key vazar no bundle.
   - gemini-2.5-flash — o modelo do lab.
• Caixa "▸ NESTA ETAPA": fazer ≥3 perguntas ao chatbot e ver, no painel, QUAL tool o
   Gemini chamou em cada uma.
• Selo: TECNOLOGIA 2 DE 4. Acento teal. Footer padrão.

════════════════════════════════════════════════════════════════════════
SLIDE 6 — TECNOLOGIA 3 DE 4 · Managed Identity
════════════════════════════════════════════════════════════════════════
• Título da tecnologia: MANAGED IDENTITY
• "O que é Managed Identity?" (2-3 frases): Uma identidade do Azure AD gerenciada pela
   plataforma — o serviço se autentica SEM senha e SEM segredo no código. Aqui, o
   FlowEvents usa a MID para ler a telemetria da compra.
• Bloco COMO FUNCIONA (mini-fluxo):
   FlowEvents (System-assigned MI) → role Log Analytics Reader no workspace →
   LogsQueryClient consulta os traces (Kusto)
• Bloco PRINCIPAIS RECURSOS (4 itens):
   - System-assigned — a identidade nasce e morre com o Container App.
   - RBAC — recebe o papel Log Analytics Reader (o mínimo necessário).
   - Sem credencial — nenhuma connection string de telemetria a guardar/rotacionar.
   - Fail-visível — sem o papel, o LogsQueryClient toma 403 e os nós NUNCA acendem.
• Caixa "▸ NESTA ETAPA": ligar a Managed Identity do ca-flow (FlowEvents) e conceder
   Log Analytics Reader no workspace.
• Faixa "🔐 BLINDAR · a MESMA identidade abre o cofre": essa Managed Identity (role Key
   Vault Secrets User) também LÊ os segredos do Key Vault — as chaves (SQL, Gemini,
   SignalR, o segredo do gateway) SAEM do claro e vão para o cofre já existente, in-place,
   sem downtime. Uma identidade sem-senha para telemetria E segredos.
• Selo: TECNOLOGIA 3 DE 4. Acento roxo. Footer padrão.

════════════════════════════════════════════════════════════════════════
SLIDE 7 — TECNOLOGIA 4 DE 4 · Azure SignalR + observabilidade (Flow Visualizer)
════════════════════════════════════════════════════════════════════════
• Título da tecnologia: AZURE SIGNALR · OBSERVABILIDADE AO VIVO
• "O que é?" (2-3 frases): A MESMA telemetria — App Insights + Log Analytics, já no ar —
   alimenta duas coisas: (1) o Flow Visualizer AO VIVO (FlowEvents + Azure SignalR
   empurram eventos ao browser por WebSocket, uma "bolinha" atravessa 5 nós) e (2) a
   observabilidade NÍVEL-PRODUÇÃO da jornada da compra. Nada recriado, ~US$0.
• Bloco COMO FUNCIONA (mini-fluxo):
   traces (correlationId) → FlowEvents lê via Kusto → TraceEventMapper classifica cada
   trace num nó → SignalR → a rota /flow ACENDE os 5 nós
• Bloco PRINCIPAIS RECURSOS (4 itens):
   - Trace-driven — o motor lê traces CORRELACIONADOS; é agnóstico a quem os emitiu.
   - Azure SignalR (Free_F1) — Service Mode DEFAULT (⚠ não Serverless — o FlowHub é
      hospedado pelo serviço).
   - CORS restrito — o WebSocket usa credentials → origin EXATO do front, nunca "*".
   - 5 nós — a bolinha atravessa a jornada em < 30s, pelo mesmo correlationId.
• Faixa "▸ TAMBÉM · Observabilidade nível-produção (~US$0, reusa App Insights + Log
   Analytics)": tracing ponta-a-ponta por correlationId (Transaction Search / Application
   Map) · Workbook da jornada da compra (latência por hop, falhas, backlog do Service
   Bus) · alertas úteis (5xx no gateway · dead-letter no Service Bus). Amarração: a MI
   que lê o Log Analytics é IRMÃ da MI que lê o Key Vault — mesmo princípio zero-segredo.
• Caixa "▸ NESTA ETAPA": criar o SignalR (Free/Default) + o FlowEvents (Container App
   EXTERNO, transport Auto), fazer uma compra real e ver /flow acender os 5 nós; e ligar
   o App Insights para ver o trace ponta-a-ponta por correlationId.
• Selo: TECNOLOGIA 4 DE 4. Acento roxo. Footer padrão.

════════════════════════════════════════════════════════════════════════
SLIDE 8 — CONCEITO-CHAVE · Identidade unificada: modernizar sem destruir
════════════════════════════════════════════════════════════════════════
• Rótulo: CONCEITO-CHAVE
• Título: Identidade unificada — o login novo ADOTA o usuário antigo
• Corpo (o vínculo, não a substituição):
   - O usuário que já existia no v1 (senha bcrypt) ganha um entra_oid do CIAM vinculado
      NA MESMA LINHA users — VÍNCULO, não substituição. O bcrypt NÃO migra (a Microsoft
      gerencia a credencial do CIAM); os dois COEXISTEM na mesma linha.
   - O JIT /api/v2/me faz resolve-or-provision: EAGER (a migração em lote das Quartas) +
      LAZY (link por EMAIL no 1º login/compra de quem chega nato-CIAM).
   - O fence CiamOnly blinda o endpoint: um token admin/workforce NUNCA provisiona um
      cliente (segurança da unificação).
• Mini-visual (uma linha users, duas credenciais):
   [users.id] ── password: bcrypt (v1, INTACTO) ── + ── entra_oid: <guid CIAM> (adicionado)
   → resultado: COEXISTE (o mesmo humano, uma linha)
• INSIGHT DE NEGÓCIO (destaque): o usuário nato-CIAM ANTES não conseguia comprar (o
   checkout exigia um users.id do v1). A unificação o torna CIDADÃO DE PRIMEIRA CLASSE
   na base (visível ao dashboard admin, que lê users).
• FRASE DE EFEITO (grande, entre aspas):
   "O login novo não apaga o usuário antigo — ele o ADOTA."
• Moldura (linha pequena): é o gêmeo, na Final, do "modernizar sem destruir" das Quartas.
   Este slide ADICIONA o vínculo — nada é apagado. Story 3.5 / ADE-007 (fence CiamOnly,
   bcrypt+entra_oid).
• Acento teal/roxo. Footer padrão.

════════════════════════════════════════════════════════════════════════
SLIDE 9 — ARQUITETURA · A foto completa (tudo que você construiu)
════════════════════════════════════════════════════════════════════════
• Rótulo: ARQUITETURA
• Título: A foto completa — tudo que você construiu (Oitavas → Quartas → Final)
• IMPORTANTE: este NÃO é só o pedaço da Final — é a JORNADA INTEIRA num quadro só (última
   aula). É DENSO: use AGRUPAMENTO VISUAL em caixas rotuladas + uma faixa transversal na
   base, para não poluir. Caixas + setas simples, estilo "COMO FUNCIONA".
• DIAGRAMA (3 grupos da esquerda p/ direita + faixa transversal embaixo):

   ┌─ CAIXA "IDENTIDADE" (Quartas F2/F3) ─┐   ┌─ CAIXA "BORDA — NÓ 0" ─┐
   │ Cliente (Browser SPA) → Entra         │   │      GATEWAY YARP        │
   │   External ID (CIAM)                  │──▶│  guardião único          │
   │ Admin → Entra ID workforce            │   │  JWT DUAL-ISSUER         │
   │ (dois emissores)                      │   │  injeta X-Entra-OID +    │
   └───────────────────────────────────────┘   │  X-Gateway-Key           │
                                                │  cache pós-auth ·        │
                                                │  rate-limit · CORS       │
                                                └───────────┬─────────────┘
                                                            │ (tudo passa por ele)
              ┌─ CAIXA "SERVIÇOS" ─────────────────────────▼──────────────────────────┐
              │ • IDENTIDADE UNIFICADA (Final 3.5): /api/v2/me (JIT resolve-or-provision)│
              │      → [Azure SQL · users]  (bcrypt v1 + entra_oid CIAM na mesma linha) │
              │ • COMPRA — 5 NÓS (Oitavas F1): [Function Entry]→[Service Bus]           │
              │      →[Function Consumer · notificação INLINE]→[Azure SQL]              │
              │ • VOZ (Final F5): [McpServer interno · 7 tools read-only]→SQL (SELECT); │
              │      proxy /llm →[Gemini] (chave server-side)                           │
              │ • VISÃO (Final F6): [FlowEvents](Managed Identity→Kusto por correlationId)│
              │      →[Azure SignalR]→ rota /flow                                       │
              └────────────────────────────────────────────────────────────────────────┘
   ══ FAIXA TRANSVERSAL (Final EPIC-004), atravessa todos os serviços ══
      🔐 SEGURANÇA (Blindar): Managed Identity + Key Vault — segredos lidos do cofre via
         MI, sem chave em claro; o X-Gateway-Key fecha o bypass ao McpServer.
      📈 OBSERVABILIDADE: App Insights + Log Analytics — traces por correlationId
         (é o que a VISÃO consome via Kusto).
      🗄 DADOS: Azure SQL compartilhado (users + purchases).

• LEGENDA NUMERADA das fases (marque no diagrama, DNA das Quartas):
   ① F1 — a COMPRA assíncrona (5 nós, um correlationId, notificação inline no nó 3).
   ② F2/F3 — IDENTIDADE + gateway (dual-issuer; cliente CIAM, admin workforce).
   ③ F5 — a VOZ (McpServer só LÊ, 7 tools; Gemini decide a tool).
   ④ F6 — a VISÃO (traces → SignalR → /flow acende os 5 nós).
   ⑤ Blindar — segredos no cofre (MI+KV) + observabilidade correlacionada.
• Linha-resumo (destaque): um sistema Azure-native COMPLETO, construído do ZERO, camada
   por camada — segredos NO COFRE — e retro-compatível (nada das fases anteriores quebrou).
• Nota (rodapé pequeno): o draw.io de topologia completa é a Story 4.6 (ainda não feita);
   por ora, este slide é a foto.
• Acento: teal (F5) + roxo (F6) + vermelho só no realce de segurança. Footer padrão.

════════════════════════════════════════════════════════════════════════
SLIDE 10 — ENCERRAMENTO DA JORNADA
════════════════════════════════════════════════════════════════════════
• Rótulo: ENCERRAMENTO DA JORNADA
• Título: Você concluiu a Copa do Mundo Azure
• Quatro bullets (o que foi construído — cada um com um ícone/acento):
   • VOZ (F5) — um chatbot MCP + RAG que consulta o estado real da Copa: 7 sentidos,
      zero escrita, segurança por construção.
   • VISÃO (F6) — observabilidade ao vivo: uma compra animada em 5 nós por correlationId
      (Azure SignalR + Managed Identity).
   • BLINDAR — o gateway é o guardião único (X-Gateway-Key fecha o bypass ao McpServer);
      os segredos vão para o Key Vault, lidos por Managed Identity, não em claro; a chave
      do Gemini nunca vai no bundle.
   • UNIFICAR — base v1 (bcrypt) ↔ CIAM na mesma linha users; o JIT /api/v2/me torna o
      cliente nato-CIAM cidadão de primeira classe.
• Fala de fechamento (uma frase, destaque): "Você começou com uma compra de ingresso e
   terminou com um sistema Azure-native completo — construído do zero, com as próprias
   mãos. Isso é uma Grande Final."
• Footer padrão.

════════════════════════════════════════════════════════════════════════
LEMBRETES FINAIS PARA A GERAÇÃO
════════════════════════════════════════════════════════════════════════
• São 10 slides — nem mais, nem menos.
• Slides 4, 5, 6, 7 são as tecnologias, cada um com o selo "TECNOLOGIA N DE 4",
   diagrama "COMO FUNCIONA", 4 recursos e a caixa "▸ NESTA ETAPA".
• Slides 3 e 8 são os CONCEITOS-CHAVE — dê o maior destaque visual às frases de efeito.
• Slide 9 é a arquitetura (diagrama + legenda numerada); slide 10 é o encerramento
   celebrativo da jornada INTEIRA (não só da fase).
• NÃO invente tools, nós ou números: são 7 tools read-only, 5 nós, notificação inline,
   gemini-2.5-flash, chave Gemini no proxy, X-Gateway-Key. E, na identidade
   (Slide 8): bcrypt + entra_oid na MESMA linha users, JIT GET /api/v2/me
   (resolve-or-provision), fence CiamOnly — não invente rotas/claims além destes.
• NÃO cite n8n, "automação no-code", orquestração externa nem PostgreSQL em NENHUM slide —
   o aluno da Final nunca os viu. Os 5 nós são 5 (não explique "por que não 6").

═══ FIM DO PROMPT ═══
```

---

## Nota de manutenção (para o time, NÃO faz parte do prompt)

- **Origem do DNA:** o layout acima replica o `.pptx` real das Quartas ("Quartas de final -
  Apresentação.pptx", 11 slides) mostrado pelo owner: capa → stack → conceito-chave central →
  4× tecnologia → 2× conceito-chave de aprofundamento → arquitetura → encerramento.
- **Mapeamento Quartas → Final (por posição de slide):**

  | Quartas # | Quartas (real) | Final # | Final (este prompt) |
  |---|---|---|---|
  | 1 | Capa "Identidade dois mundos" | 1 | Capa "Voz & Visão — a Copa que te responde" |
  | 2 | Stack (Gateway/CIAM/workforce/Container Apps/SQL) | 2 | Stack (MCP/RAG/Gemini/Managed Identity/SignalR) |
  | 3 | Conceito central: desambiguação de identidade | 3 | Conceito central: **regra de ouro por construção** |
  | 4 | Tec 1: Gateway YARP | 4 | Tec 1: **MCP** |
  | 5 | Tec 2: Entra External ID | 5 | Tec 2: **RAG por tool-use** |
  | 6 | Tec 3: Entra ID workforce | 6 | Tec 3: **Managed Identity** |
  | 7 | Tec 4: Azure Container Apps | 7 | Tec 4: **Azure SignalR / observabilidade** |
  | 8 | Conceito: "só muda a string da authority" | 8 | Conceito: **Identidade unificada — "modernizar sem destruir"** — gêmeo do slide de identidade das Quartas |
  | 9 | Conceito: "modernizar sem destruir" | — | *(sem equivalente — o conceito-chave "Onde foi o n8n?" foi REMOVIDO do deck: o aluno da Final não usa n8n)* |
  | 10 | Arquitetura "a foto completa da F2" | 9 | Arquitetura "a foto completa — tudo que você construiu" (jornada INTEIRA Oitavas→Quartas→Final, não só a Final) |
  | 11 | Encerramento "você concluiu as Quartas" | 10 | Encerramento "você concluiu a Copa do Mundo Azure" |

- **Deck reveal paralelo:** `slides.md` (reveal.js) é a versão navegável do mesmo conteúdo (também
  já SEM o slide do n8n); as `SPEAKER-NOTES.md` seguem a numeração deste `.pptx` (10 slides) **1:1**
  — uma seção por slide (`## Slide 1` … `## Slide 10`).
- **Rastreabilidade (Art. IV):** fontes — ADE-008 (re-arquitetura sem n8n), ADE-009 (X-Gateway-Key),
  **ADE-010 (MI + Key Vault sobre os recursos existentes + observabilidade nível-produção)**,
  **Story 3.5 + ADE-007 (identidade unificada base v1 ↔ CIAM: bcrypt+entra_oid, JIT `/api/v2/me`,
  fence `CiamOnly`)**, `docs/runbooks/final-portal-guide.md`, código real (`FifaTicketTools.cs`,
  `gemini.ts`, `FlowEventType.cs` / `flowNodes.ts`).
