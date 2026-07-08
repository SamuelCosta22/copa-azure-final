# Quiz — A Grande Final (F5/F6) · conteúdo das perguntas

> **Padrão herdado (Oitavas/Quartas):** o quiz é um **Google Forms criado FORA do repositório** (o owner monta o Forms). Este arquivo é só o **conteúdo-fonte** das perguntas — nível **fácil**, para fixação, não avaliação punitiva. O **link do Forms** é referenciado no guia do aluno (`final-portal-guide.md`, Bloco 3).
> **Rastreabilidade (Art. IV):** cada pergunta cobre um item exigido pela Story 3.6 AC-4 e um conceito verificado no guia/código. A resposta correta está marcada com ✅ e traz a fonte.
> **Tom:** fácil, uma resposta certa por pergunta, distratores plausíveis mas claramente errados para quem seguiu a aula.

---

## Cabeçalho sugerido do Forms

- **Título:** A Grande Final (F5/F6) — Voz &amp; Visão · quiz de encerramento
- **Descrição:** 8 perguntas rápidas sobre o que você construiu hoje: chatbot MCP, RAG por tool-use, Managed Identity, observabilidade ao vivo e a lição de simplificação. Sem pegadinha — se você acompanhou o lab, acerta.
- **Config:** coletar e-mail (opcional), 1 tentativa, mostrar nota ao enviar.

---

## Pergunta 1 — O que é MCP? *(cobre AC-4: "o que é MCP")*

*Qual a função do **MCP (Model Context Protocol)** no chatbot da Final?*

- a) Guardar a senha do banco de dados
- b) ✅ **Expor "ferramentas" (tools) que o LLM descobre (`tools/list`) e chama (`tools/call`) para consultar dados**
- c) Hospedar o modelo Gemini dentro do Azure
- d) Substituir o gateway YARP

> **Fonte:** guia Fase 1 (o McpServer expõe `/mcp`, JSON-RPC; `tools/list`/`tools/call`).

---

## Pergunta 2 — A regra de ouro *(cobre AC-4: "por que o chatbot nunca escreve, agora por construção")*

*Por que, na Final, o chatbot **não consegue** executar uma ação de escrita (ex.: criar um alerta)?*

- a) Porque um IF no código bloqueia comandos perigosos
- b) Porque o gateway recusa requisições de escrita
- c) ✅ **Porque não existe nenhuma ferramenta de escrita para o LLM chamar — o McpServer só tem 7 tools de leitura (segurança por construção)**
- d) Porque o Gemini foi treinado para nunca escrever

> **Fonte:** guia Fase 5 ("A regra de ouro AO VIVO") · ADE-008 Inv 1. "O que não existe não pode ser chamado."

---

## Pergunta 3 — A nuance da alucinação *(reforça AC-4: regra de ouro)*

*O chatbot responde em texto "pronto, criei o alerta!". O que realmente aconteceu no banco?*

- a) Uma linha foi inserida na tabela de alertas
- b) ✅ **Nada — foi uma alucinação de texto; não houve tool call de escrita (ela não existe)**
- c) O gateway gravou o alerta por segurança
- d) O Gemini pediu confirmação antes de gravar

> **Fonte:** guia Fase 5 (nuance honesta: texto ≠ tool call; nenhuma escrita ocorre).

---

## Pergunta 4 — RAG por tool-use *(cobre a tecnologia nova "RAG")*

*Quando você pergunta "Como está o grupo A?", de onde vem o dado da resposta?*

- a) Da "memória" de treinamento do Gemini
- b) De um banco de dados vetorial (embeddings)
- c) ✅ **De um `SELECT` no SQL real, via a tool `consultar_classificacao` (grounding/RAG por tool-use)**
- d) De uma busca no Google feita pelo chatbot

> **Fonte:** guia Fase 2 · slides "RAG (grounding por tool-use)". O modelo **recupera** o fato real antes de responder; aqui é via MCP, não vetores.

---

## Pergunta 5 — Correlation-ID / observabilidade *(cobre AC-4: "correlation-ID/observabilidade (F6)")*

*No Flow Visualizer, o que garante que a "bolinha" mostra **a sua** compra atravessando os 5 nós?*

- a) A ordem de chegada das requisições
- b) ✅ **O mesmo `correlationId` em cada hop — o FlowEvents lê os traces por esse ID (Kusto) e empurra por SignalR**
- c) O endereço IP do navegador
- d) O número do ingresso comprado

> **Fonte:** guia Fase 9 · ADE-008 Inv 5 (trace-driven, `customDimensions.CorrelationId`).

---

## Pergunta 6 — Os 5 nós *(cobre AC-4/AC-5: por que removemos o n8n)*

*A jornada de compra no Flow Visualizer tem **quantos** nós, e por quê?*

- a) 6 nós — incluindo o nó de orquestração externa
- b) ✅ **5 nós — a orquestração externa foi removida; a notificação pós-compra é inline na Function Consumer**
- c) 4 nós — o gateway não conta como nó
- d) 7 nós — um por tool do chatbot

> **Fonte:** guia Fase 9 · ADE-008 Inv 5. Nós: Gateway → Entry → Service Bus → Consumer → SQL.

---

## Pergunta 7 — A lição de simplificação *(cobre AC-4/AC-5: "por que removemos o n8n")*

*Qual foi a decisão de arquitetura por trás de a notificação pós-compra virar **inline** na Function?*

- a) Trocar a orquestração externa por outro orquestrador gerenciado
- b) ✅ **Remover o componente (não substituí-lo): menos peças, menos custo, menos falhas — é a Function que orquestra o pós-compra**
- c) Mover a notificação para o frontend
- d) Deixar de notificar o pós-compra

> **Fonte:** ADE-008 (Rationale: "simplificar &gt; substituir"; Logic Apps rejeitado) · slides "Onde foi o n8n?".

---

## Pergunta 8 — Managed Identity *(cobre a tecnologia nova "Managed Identity")*

*Como o serviço **FlowEvents** consegue ler a telemetria (Log Analytics) sem guardar uma senha?*

- a) A connection string fica hardcoded no código
- b) O usuário digita a senha ao abrir a rota `/flow`
- c) ✅ **Usa uma Managed Identity (System-assigned) com o papel `Log Analytics Reader` no workspace**
- d) O gateway repassa a senha do banco

> **Fonte:** guia Fase 6.3 · slides "Managed Identity". Sem o papel, o `LogsQueryClient` toma 403 e os nós não acendem.
> **Bônus (opcional, conceito):** essa mesma Managed Identity resolve os segredos do **Key Vault** — entregue na missão Blindar (ADE-010): as chaves saem do claro e passam a ser lidas do cofre por MI. A MI que lê o Log Analytics é irmã da que lê o Key Vault — mesmo princípio zero-segredo.

---

## Gabarito rápido

| # | Tema | Resposta |
|---|---|---|
| 1 | O que é MCP | **b** |
| 2 | Regra de ouro (por construção) | **c** |
| 3 | Alucinação de texto ≠ escrita | **b** |
| 4 | RAG por tool-use | **c** |
| 5 | correlationId / observabilidade | **b** |
| 6 | 5 nós (por quê) | **b** |
| 7 | Simplificar &gt; substituir (n8n removido) | **b** |
| 8 | Managed Identity | **c** |
