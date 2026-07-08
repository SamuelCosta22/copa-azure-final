# A Grande Final — Pacote completo da aula (índice único)

> **Este é o ponto de entrada de TODOS os materiais da aula da Final (labs F5 MCP/chatbot + F6 Flow Visualizer).**
> Branch de entrega: [`lab-a-final` no copa-azure-final](https://github.com/TFTEC/copa-azure-final/tree/lab-a-final)

## Ordem sugerida de validação

| # | Documento | Onde está | O que é |
|---|-----------|-----------|---------|
| 1 | **Deck de slides** | [`slides.md`](./slides.md) | Apresentação enxuta (reveal.js) — só as tecnologias novas: MCP · RAG (tool-use) · Managed Identity + Key Vault (entrega Blindar) · observabilidade/SignalR |
| 2 | **Storyboard do PPT** | [`STORYBOARD-APRESENTACAO.md`](./STORYBOARD-APRESENTACAO.md) | Prompt de 11 slides (DNA das Quartas) para gerar o `.pptx` no Claude for PowerPoint (fora do repo) |
| 3 | **Speaker notes** | [`SPEAKER-NOTES.md`](./SPEAKER-NOTES.md) | Roteiro falado da aula, slide a slide |
| 4 | **Guia do aluno (portal)** | [`../../runbooks/final-portal-guide.md`](../../runbooks/final-portal-guide.md) | Passo a passo completo — 14 fases (0–13): Gemini → cofre (Managed Identity + Key Vault) → F5/F6 → migração sem downtime dos recursos das Quartas → fork/PR/`acao`s → smokes → observabilidade; + Apêndices A–E (inclui SQL via MI, showcase) |
| 5 | **Quiz** | [`QUIZ.md`](./QUIZ.md) | 8 perguntas-fonte para montar o Google Forms (link entra no guia via placeholder) |
| 6 | **Diagrama de arquitetura** | [`../../diagrams/final-f5-f6-mcp-flow.drawio`](../../diagrams/final-f5-f6-mcp-flow.drawio) | Arquitetura pós-ADE-008 (5 nós, McpServer interno 7 tools read-only, zero n8n) — abrir no diagrams.net |
| 7 | **Workflow do lab** | [`../../../.github/workflows/lab-a-final.yml`](../../../.github/workflows/lab-a-final.yml) | Workflow único: `acao = function \| mcp-server \| gateway \| flow-events \| frontend \| tudo` |
| 8 | Débito de segurança (referência) | [`../../security/final-security-debt.md`](../../security/final-security-debt.md) | Contexto do X-Gateway-Key/MI+KV citado nos slides e no guia |

## Status de qualidade (2026-07-02)

- **Guia:** auditado doc-vs-código pelo @qa (aprovado) + correções do code review do workflow aplicadas.
- **Workflow:** code review adversarial (2 rodadas) + gate @architect **PASS** de empacotamento — pendente validação ao vivo.
- **Deck/storyboard/quiz/draw.io:** gate @pm **PASS** (Story 3.6 Done) — anti-hallucination verificado na fonte.

## Pendências fora do repo (owner)

1. Gerar o `.pptx` a partir do storyboard (item 2).
2. Montar o Google Forms com o QUIZ.md (item 5) e substituir o placeholder do link no guia (item 4).
