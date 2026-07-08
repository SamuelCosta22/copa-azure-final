---
title: "A Grande Final — Voz (MCP + RAG) e Visão (observabilidade ao vivo)"
subtitle: "Workshop Living Lab Azure-Native · A Grande Final (F5/F6)"
theme: black
revealOptions:
  transition: slide
---

# A Grande Final

## Voz &amp; Visão

Chatbot **MCP + RAG** (só sentidos) · **Flow Visualizer** (5 nós ao vivo)

Workshop **Living Lab Azure-Native**

`Oitavas (F1)` → `Quartas (F2/F3)` → **`Final (F5/F6)`**

<small>A última aula: a aplicação ganha voz para responder e uma tela onde ela se acende.</small>

---

## A jornada até aqui (recap em 1 slide)

| Lab | O que você construiu |
|---|---|
| **Oitavas (F1)** | a **compra** assíncrona: Function → Service Bus → Consumer → SQL |
| **Quartas (F2/F3)** | o **gateway YARP** guardião + **identidade** (CIAM cliente / admin workforce) |
| **A Final (F5/F6)** | **voz** (chatbot que lê o estado real) + **visão** (observabilidade animada) |

<small>A Final **ADICIONA**. Nada das fases anteriores deixa de funcionar (retro-compat dura).</small>

---

## A frase do dia

# "Voz para perguntar.<br/>Visão para enxergar.<br/>E segurança **por construção** —<br/>não por roteamento."

<small>Um chatbot que só tem sentidos. Uma tela onde a arquitetura se acende.</small>

---

## Só o que é NOVO nesta aula

> As fases anteriores já deram: compra async, gateway, identidade, Container Apps, SQL.
> **Hoje** são **quatro** tecnologias novas:

1. **MCP** — o protocolo que dá ferramentas ao LLM
2. **RAG (por tool-use)** — o chatbot **recupera** fatos reais antes de responder
3. **Managed Identity + Key Vault** — os apps leem segredos do **cofre** e telemetria do Log Analytics **sem chave em claro**
4. **Observabilidade ao vivo** — Flow Visualizer + **Azure SignalR**

<small>Este deck não reexplica o que já foi dado. Só as peças novas.</small>

---

## Bloco F5 — VOZ

### MCP · RAG · a regra de ouro por construção

---

## TECNOLOGIA 1 DE 4 · MCP (Model Context Protocol)

- **O que é?** Um **protocolo padrão** para expor "ferramentas" (tools) que um LLM pode descobrir e chamar — o "USB-C das integrações de IA".
- **Como funciona (diagrama):** `Chatbot` → `tools/list` (descobre) → `tools/call` (executa) → **`McpServer`** → `SELECT` no SQL.
- **Principais recursos (4):**
  - **`tools/list`** — o LLM descobre, em runtime, quais ferramentas existem.
  - **`tools/call`** (JSON-RPC 2.0) — a chamada tipada de uma ferramenta.
  - **Server .NET** — SDK oficial MCP; **ingress interno**, atrás do gateway.
  - **7 tools read-only** — todas `[McpServerTool(ReadOnly = true)]`.
- **▸ Nesta etapa:** subir o McpServer (Container App interno) e ver `tools/list` retornar **7 sentidos**.

---

## As 7 ferramentas (todas de leitura)

`consultar_disponibilidade` · `verificar_ingresso` · `consultar_bracket`

`consultar_partidas` · `consultar_classificacao` · `consultar_time` · `consultar_estadio`

> Cada uma faz **`SELECT` parametrizado** (Dapper) no SQL real.
> **Nenhuma** escreve. Não existe uma tool de escrita.

<small>A auditoria de segurança mais simples que existe: ler o `tools/list`.</small>

---

## TECNOLOGIA 2 DE 4 · RAG (grounding por tool-use)

- **O que é?** **Retrieval-Augmented Generation**: o modelo **recupera** um fato de uma fonte externa **antes** de responder — em vez de inventar.
- **Como funciona (diagrama):** `"Quando o Brasil joga?"` → **Gemini** decide a tool → `consultar_partidas` → **SQL** → resposta **fundamentada**.
- **Principais recursos (4):**
  - **Function calling (`AUTO`)** — o Gemini escolhe **qual** tool chamar.
  - **Grounding** — a resposta vem do **banco**, não do "conhecimento" do modelo.
  - **Chave server-side** — a `GEMINI_API_KEY` fica no **proxy** (`/llm`), nunca no browser.
  - **`gemini-2.5-flash`** — o modelo do lab.
- **▸ Nesta etapa:** fazer 3 perguntas e ver, no painel, **qual** tool o Gemini chamou.

---

## RAG aqui NÃO é vector store

| RAG "clássico" | RAG desta aula (tool-use) |
|---|---|
| embeddings + banco vetorial | **ferramentas MCP** |
| busca por similaridade | **`SELECT` parametrizado** |
| recupera "trechos" | recupera **o dado exato e vivo** |

> O padrão é o mesmo — **recuperar antes de gerar**. A implementação é **grounding via MCP**, não vetores.

<small>Honestidade arquitetural: chame pelo nome certo. É RAG por function-calling.</small>

---

## CONCEITO-CHAVE · A regra de ouro (agora trivial)

- **Antes** a segurança dependia de **rotear** ações por caminhos seguros (fila, orquestração).
- **Agora** o McpServer é **só sentidos**: **não existe** vetor de escrita para o LLM chamar.

# "O que não existe<br/>não pode ser chamado."

<small>Segurança **por construção**, não por roteamento. Um LLM alucina argumentos — mas não há tool de escrita para receber a alucinação.</small>

---

## ⭐ A regra de ouro AO VIVO

Peça ao chatbot uma **ação**:

> *"Cria um alerta pra mim quando abrir ingresso VIP."*

- O `tools/list` só tem **7 verbos de leitura** → **não há** tool para isso.
- ⚠️ **Nuance honesta:** o LLM pode *dizer em texto* "criei o alerta". Isso é **alucinação de texto**, **não** uma tool call. **Nada** é gravado.

<small>A defesa não depende de o LLM "se comportar" — depende do vetor de escrita **não existir**.</small>

---

## Bloco F6 — VISÃO

### Managed Identity · observabilidade · SignalR

---

## TECNOLOGIA 3 DE 4 · Managed Identity

- **O que é?** Uma identidade do **Azure AD** gerenciada pela plataforma — o serviço se autentica **sem senha, sem segredo no código**.
- **Como funciona (diagrama):** `FlowEvents` (System-assigned MI) → role **`Log Analytics Reader`** → `LogsQueryClient` lê os traces (Kusto).
- **Principais recursos (4):**
  - **System-assigned** — nasce e morre com o Container App.
  - **RBAC** — recebe o papel `Log Analytics Reader` no workspace.
  - **Sem credencial** — nenhuma connection string de telemetria a guardar.
  - **Fail-visível** — sem o papel, o `LogsQueryClient` toma **403** e os nós **não acendem**.
- **▸ Nesta etapa:** ligar a MID do `ca-flow` e conceder `Log Analytics Reader` no workspace.

> 🔐 **Blindar — a MESMA identidade abre o cofre:** essa Managed Identity (com a role `Key Vault Secrets User`) também **lê os segredos do Key Vault** — as chaves (SQL, Gemini, SignalR, o segredo do gateway) **saem do claro** e vão para o cofre já existente, in-place, sem downtime. Uma identidade sem-senha para **telemetria E segredos**.

---

## CONCEITO-CHAVE · Identidade unificada: modernizar sem destruir

- O usuário que já existia no **v1** (senha **bcrypt**) ganha um **`entra_oid` do CIAM** vinculado **na mesma linha `users`** — **vínculo, não substituição**. O bcrypt **não migra** (a Microsoft gerencia a credencial do CIAM); os dois **coexistem**.
- O **JIT `/api/v2/me`** faz **resolve-or-provision**: **eager** (a migração em lote das Quartas) + **lazy** (link por **email** no 1º login/compra de quem chega **nato-CIAM**).
- O fence **`CiamOnly`** blinda o endpoint: um token **admin/workforce nunca** provisiona um cliente.

> **Insight de negócio:** o usuário **nato-CIAM** antes **não conseguia comprar** (o checkout exigia um `users.id` do v1). A unificação o torna **cidadão de primeira classe** na base.

# "O login novo não apaga o usuário antigo —<br/>ele o **adota**."

<small>É o gêmeo, na Final, do "modernizar sem destruir" das Quartas: bcrypt + `entra_oid` na mesma linha, nada é apagado. Story 3.5 / ADE-007.</small>

---

## TECNOLOGIA 4 DE 4 · Observabilidade ao vivo (SignalR)

- **O que é?** A **mesma** telemetria (App Insights + Log Analytics, **já no ar**) alimenta **duas** coisas: o **Flow Visualizer ao vivo** (FlowEvents + **Azure SignalR** por WebSocket) e a **observabilidade nível-produção** da compra — **nada recriado, ~US$0**.
- **Como funciona (diagrama):** `traces (correlationId)` → `FlowEvents` (Kusto) → `TraceEventMapper` classifica → **SignalR** → rota `/flow` acende os nós.
- **Principais recursos (4):**
  - **Trace-driven** — o motor lê **traces correlacionados**; não depende de quem os emitiu.
  - **Azure SignalR (Free_F1)** — **Service Mode `Default`** (não Serverless).
  - **CORS restrito** — WebSocket com credentials → origin exato, nunca `*`.
  - **5 nós** — a "bolinha" atravessa a jornada em &lt; 30s.
- **▸ TAMBÉM (nível-produção, ~US$0):** tracing ponta-a-ponta por `correlationId` (Transaction Search / Application Map) · **Workbook** da jornada da compra · alertas (**5xx** no gateway · **dead-letter** no Service Bus).
- **▸ Nesta etapa:** fazer uma compra real e ver `/flow` acender **5 nós** pelo mesmo `correlationId`; ligar o App Insights para ver o trace ponta-a-ponta.

<small>A MI que lê o **Log Analytics** é irmã da MI que lê o **Key Vault** — mesmo princípio zero-segredo, contado duas vezes.</small>

---

## Os 5 nós (o grande final visual)

```
0 Gateway YARP → 1 Function Entry → 2 Service Bus
              → 3 Function Consumer → 4 SQL
```

| # | Nó | O que acontece |
|---|---|---|
| 0 | **Gateway YARP** | injeta `X-Correlation-ID` (nó zero) |
| 3 | **Function Consumer** | grava no SQL **e** emite a notificação **INLINE** |
| 4 | **SQL** | linha em `purchases.correlation_id` — fim |

<small>Uma compra, um `correlationId`, cinco nós.</small>

---

## ARQUITETURA · A foto completa — tudo que você construiu

```
IDENTIDADE (F2/F3)          BORDA — NÓ 0                SERVIÇOS
┌───────────────────┐   ┌───────────────────┐
│ Cliente → CIAM     │   │   GATEWAY YARP     │──/api/v2/me (JIT)─→ [Azure SQL · users]
│ (Browser SPA)      │──▶│  guardião único    │                     bcrypt v1 + entra_oid
│ Admin → workforce  │   │  JWT dual-issuer   │──COMPRA (5 nós)──→ [Entry]→[Service Bus]
└───────────────────┘   │  X-Entra-OID +     │                    →[Consumer *inline*]→[SQL]
                         │  X-Gateway-Key     │──VOZ (F5)────────→ [McpServer interno·7 tools]→SQL
                         │  cache pós-auth ·  │                    proxy /llm →[Gemini] key server-side
                         │  rate-limit · CORS │──VISÃO (F6)──────→ [FlowEvents]→[SignalR]→ /flow
                         └───────────────────┘
─────────────────────────────────────────────────────────────────────────────────
TRANSVERSAL · Segurança (Blindar): Managed Identity + Key Vault (segredos no cofre, sem chave em claro)
TRANSVERSAL · Observabilidade: App Insights + Log Analytics (traces por correlationId → a Visão lê o Kusto)
```

- **Identidade (F2/F3):** dois emissores — cliente **CIAM**, admin **workforce** — validados pelo mesmo gateway.
- **Borda (nó 0):** o **Gateway YARP** é o guardião único — tudo passa por ele.
- **Serviços:** compra async **5 nós** (F1) · voz **McpServer** (F5) · visão **FlowEvents/SignalR** (F6) · unificação **`/api/v2/me`** (3.5).

<small>Um sistema **Azure-native completo**, do zero, retro-compatível, com segredos **no cofre**. (O draw.io de topologia completa é a Story 4.6, ainda não feita.)</small>

---

## As 4 missões da Final (retrospectiva)

| Missão | O que você provou |
|---|---|
| **Voz** (F5) | uma IA consulta dados reais **com segurança** — 7 sentidos, zero escrita |
| **Visão** (F6) | observabilidade distribuída: uma compra animada em **5 nós** por `correlationId` |
| **Blindar** (hardening) | gateway guardião único (`X-Gateway-Key` fecha o bypass); **segredos no Key Vault** (via Managed Identity), não em claro; chave Gemini nunca no bundle |
| **Unificar** (identidade) | base v1 (bcrypt) ↔ CIAM na mesma linha `users`; JIT `/api/v2/me` torna o cliente nato-CIAM **cidadão de primeira classe** |

---

## ENCERRAMENTO · O Living Lab completo

Você começou com **uma compra de ingresso**.

Terminou com um sistema **Azure-native** completo:

- assíncrono (Service Bus), com **gateway** e **identidade federada**;
- um **chatbot** que conversa com os dados **sem nunca poder alterá-los**;
- uma tela onde a própria arquitetura **se acende** diante de você.

# Isso é uma **Grande Final**. 🏆

<small>Guardião único · segurança por construção · observabilidade correlacionada · identidade unificada. É o que se leva para qualquer sistema em produção.</small>

---

# Obrigado!

## Você construiu tudo — do zero, com as próprias mãos.

`Oitavas` → `Quartas` → **`Final`** · Living Lab Azure-Native

<small>Quiz de encerramento: link no guia do aluno (`final-portal-guide.md`).</small>
