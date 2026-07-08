# Staging — conteúdo do Template Repository `fifa2026-final-lab`

> ⚠️ **Isto é uma área de STAGING**, não o repositório real. O conteúdo desta pasta
> (`docs/entrega/fifa2026-final-lab/`) é o que deve ser **semeado** no **GitHub Template
> Repository** `fifa2026-final-lab` da org **TFTEC** — a criação/publicação do repo real
> é uma ação na org TFTEC que **depende de autorização do owner** (Story 4.5, Task 1,
> pendente). Nada aqui roda: o `.github/workflows/lab-grande-final.yml` está sob
> `docs/entrega/.../.github/workflows/`, **não** no root deste repo, então o GitHub Actions
> **não** o dispara daqui (proteção intencional).

Story: [`docs/stories/4.5.story.md`](../../stories/4.5.story.md) — Entrega/Capstone (EPIC-004 Frente C).

## O que este pacote entrega (a promessa central: 15 vars + 5 secrets → 1 campo + 1 secret)

| Arquivo (no repo real, na RAIZ) | Papel |
|---|---|
| `lab.config.json` | **1 campo editável** pelo aluno: `{"sufixo": "<iniciais>"}` (AC-4). Só valor não-secreto, versionado. |
| `.github/workflows/lab-grande-final.yml` | Workflow único, `workflow_dispatch`, input `acao` (`check`/`tudo`/`mcp-server`/`flow-events`/`gateway`/`frontend`). |

**Único GitHub Secret exigido do aluno:** `AZURE_CREDENTIALS` (JSON do Service Principal da turma) — AC-7.

## Fluxo do aluno (dry-run — AC-14)

1. **"Use this template"** → cria um repositório **próprio** (não fork — mata a classe de bugs de merge). AC-1.
2. Edita **1 linha** em `lab.config.json`: o `sufixo`. AC-4.
3. Configura **1 secret**: `AZURE_CREDENTIALS`. AC-7. **Diferente das Quartas:** você **não** cria um Service Principal próprio — **seu instrutor te entrega esse JSON** (o SP da turma, com acesso de leitura/deploy aos recursos já provisionados); **cole-o como está** em Settings → Secrets and variables → Actions → New repository secret.
4. Roda **`acao=check`** (Doctor pré-flight, read-only) → confirma ambiente PASS. AC-12.
5. Roda **`acao=tudo`** → build + deploy de código sobre a infra pré-provisionada. AC-8/9.

Nenhuma GitHub Variable criada. Nenhum segredo de aplicação digitado. Nenhum publish profile baixado.

## Convenção de nomes (CONTRATO com o pré-provisionamento do instrutor)

O `sufixo` deriva **todos** os nomes de recurso por convenção fixa `<prefixo>-<sufixo>`,
estendendo a já validada nas Quartas (`cr<sufixo>` / `cae-<sufixo>` / `ca-gateway-<sufixo>` —
`quartas-f2-portal-guide.md` L66-68). **O runbook `browser-harness` do instrutor (Story 4.5
Task 5, @analyst) DEVE provisionar os recursos com estes nomes:**

| Recurso | Nome derivado | Como o workflow o usa |
|---|---|---|
| Resource Group | `rg-<sufixo>` | `-g` de todos os `az` |
| ACR | `cr<sufixo>` / `cr<sufixo>.azurecr.io` | `az acr login`, tag das imagens |
| Container Apps Environment | `cae-<sufixo>` | (referência do pré-provisionamento) |
| Gateway (Container App) | `ca-gateway-<sufixo>` | update de imagem + smoke + URL |
| McpServer (Container App, interno) | `ca-mcp-<sufixo>` | update de imagem + smoke revisão + KV/MI check |
| FlowEvents (Container App) | `ca-flow-<sufixo>` | update de imagem + smoke |
| Frontend (Web App) | `app-frontend-<sufixo>` | fonte da identidade + deploy do bundle |
| Backend v1 (Web App) | `app-backend-<sufixo>` | URL + presença do `GATEWAY_SHARED_SECRET` |
| Function F1 (Function App) | `func-f1-<sufixo>` | URL da compra v2 |
| SQL Server | `sql-<sufixo>` | (conexões via MI-AAD, Story 4.1) |
| Key Vault | `kv-<sufixo>` | secrets de app via KV reference + MI |

## Como cada uma das 15 vars + 5 secrets foi colapsada

| Origem (Quartas) | Qtd | Resolução nesta entrega |
|---|---|---|
| Nomes de recurso (`SQL_SERVER`, `RESOURCE_GROUP`, `ACR_LOGIN_SERVER`, `PHASE04_CONTAINERAPP_NAME`, `PHASE04_RESOURCE_GROUP`, `FRONTEND_APP_NAME`, `BACKEND_APP_NAME`) | 7 vars | **Derivados do `sufixo`** (`jq` → `$GITHUB_ENV`) por convenção fixa |
| URLs (`GATEWAY_V2_URL`, `BACKEND_URL`, `FUNCTION_V2_URL`) | 3 vars | **Consultadas in-run** via `az containerapp/webapp/functionapp show --query` (AC-5) — nunca hardcoded |
| Identidade frontend (`VITE_CIAM_AUTHORITY`/`CLIENT_ID`, `VITE_ADMIN_TENANT_ID`/`CLIENT_ID`, `VITE_ADMIN_SCOPE`) | 5 vars | **Lidas dos App Settings** do frontend existente (`az webapp config appsettings list`) — AC-6/[AUTO-DECISION] |
| `SQL_CONNECTION_STRING` | 1 secret | **Eliminado** — MI-AAD (Story 4.1) |
| `AZURE_FRONTEND_PUBLISH_PROFILE` / `AZURE_BACKEND_PUBLISH_PROFILE` | 2 secrets | **Eliminados** — deploy via `az webapp deploy` autenticado por `AZURE_CREDENTIALS` |
| `GATEWAY_SHARED_SECRET` (+ LLM keys) | 1 secret | **Fora do GitHub** — só no Key Vault, via KV reference + Managed Identity |
| `AZURE_CREDENTIALS` | 1 secret | **Permanece** — o único GitHub Secret |

## `acao=check` — Doctor pré-flight (read-only, AC-12)

Consome `docs/runbooks/deploy-preflight-checklist.md` (Story 4.4). Cada item lê o estado do
recurso (`az ... show/list/--query`) e imprime PASS/FAIL no job summary — **nenhum**
`az set/update/create`, **nenhum** valor de secret em log:

| # | Item | Como valida |
|---|---|---|
| 1 | RBAC do SP no RG do gateway | `az role assignment list --assignee <sp> -g rg-<sufixo>` não-vazio |
| 2 | ACR conectado nos Registries | `registries[].server` contém `cr<sufixo>.azurecr.io` |
| 3 | `ingress.targetPort = 8080` | `ingress.targetPort` == 8080 |
| 4 | health probes na 8080 | `probes[].httpGet.port` todas 8080 (ou nenhuma) |
| 5 | segredo X-Gateway-Key | **presença do NOME** `Gateway__AdminSharedSecret` (gateway) + `GATEWAY_SHARED_SECRET` (backend) — nunca o valor |
| 6 | invariante de perímetro (@architect) | `ingress.external` presente (ingress do CAE é a única borda — premissa do `ForwardLimit=1`) |
| 7 | KV/MI smoke (Story 4.1) | McpServer tem secret(s) via `keyVaultUrl` **e** revisão ativa `Running` (a MI resolveu o KV ref) — só metadados, nunca o valor |
| 8 | `VITE_*` de identidade no frontend (AC-6) | `app-frontend-<sufixo>` tem as 5 `VITE_*` (`VITE_CIAM_AUTHORITY`/`CLIENT_ID`, `VITE_ADMIN_TENANT_ID`/`CLIENT_ID`, `VITE_ADMIN_SCOPE`) semeadas pelo instrutor (runbook Bloco 4) — **presença do NOME**, nunca o valor. Sem isso, o `acao=tudo` quebra no build do frontend. |

Falha o job (exit 1) se qualquer item FAIL → o aluno corrige antes de `acao=tudo`.

## Pendências (aguardando autorização do owner / outros agentes)

- **Task 1 (repo real):** criar `fifa2026-final-lab` na org TFTEC + habilitar "Template repository" + branch `lab-grande-final`. **Ação na org TFTEC — pendente de autorização do owner.**
- **Task 5 (runbook browser-harness):** pré-provisionamento T2 + semeadura do Key Vault — **@analyst** (em paralelo), fora do escopo deste pacote.
- **Task 6.1 (dry-run @po):** validação da UX "1 campo + 1 secret".
- **Semeadura do conteúdo:** quando o repo real existir, copiar para a **raiz** dele (junto com o código-fonte da Final): (1) `lab.config.json`; (2) `.github/workflows/lab-grande-final.yml`; (3) um **`README.md` de raiz voltado ao aluno** — usar o texto da seção **"Conteúdo do README-raiz (aluno)"** abaixo (esta nota de staging NÃO vai para a raiz; é interna).

## Conteúdo do README-raiz (aluno) — copiar para a raiz do repo real na Task 1

> Este bloco é o **README que o aluno vê** ao abrir o repositório criado via "Use this template". Copie-o (sem esta linha-guia) para `README.md` na raiz do `fifa2026-final-lab`.

---

# Grande Final — FIFA 2026 Tickets (lab do aluno)

Bem-vindo à **Grande Final**. Este é o **seu** repositório (criado via "Use this template" — não é um fork). Aqui você faz o deploy da Copa inteira sobre a sua infraestrutura Azure das Oitavas/Quartas, agora em **nível produção** (Managed Identity + Key Vault + rede fechada).

## O que mudou desde as Quartas (a promessa: **15 variáveis + 5 secrets → 1 campo + 1 secret**)

Nas Quartas você preenchia **15 GitHub Variables + 5 GitHub Secrets** à mão. Aqui você edita **1 linha** e configura **1 secret**:

| Nas Quartas (o que você fazia à mão) | Aqui (Grande Final) |
|---|---|
| 7 nomes de recurso (`SQL_SERVER`, `RESOURCE_GROUP`, `ACR_LOGIN_SERVER`, `FRONTEND_APP_NAME`…) | **Derivados** do seu `sufixo` por convenção `<prefixo>-<sufixo>` |
| 3 URLs (`GATEWAY_V2_URL`, `BACKEND_URL`, `FUNCTION_V2_URL`) | **Consultadas** ao Azure durante o deploy (`az … --query`) |
| 5 vars de identidade (`VITE_CIAM_*`, `VITE_ADMIN_*`) | **Já configuradas** no seu ambiente pelo instrutor — lidas automaticamente |
| `SQL_CONNECTION_STRING` (secret) | **Eliminado** — conexão via Managed Identity (sem senha) |
| 2 publish profiles (secrets) | **Eliminados** — deploy autenticado pelo `AZURE_CREDENTIALS` |
| `GATEWAY_SHARED_SECRET` + chaves de LLM (secrets) | **No Key Vault** — nunca no GitHub |
| **= 15 vars + 5 secrets** | **= 1 campo (`sufixo`) + 1 secret (`AZURE_CREDENTIALS`)** |

## Passo a passo (só web)

1. **"Use this template"** já criou este repo pra você (é seu — pode editar à vontade).
2. Edite **1 linha** em [`lab.config.json`](./lab.config.json): troque `"suasiniciais"` pelo **seu sufixo** (letras minúsculas + números, o mesmo das Quartas).
3. Configure **1 secret** em *Settings → Secrets and variables → Actions*: `AZURE_CREDENTIALS`. **Seu instrutor te entrega esse JSON — cole como está, NÃO crie um Service Principal próprio.**
4. Rode o workflow **`Lab Grande Final`** com **`acao=check`** (Doctor pré-flight, read-only). Só avance com **todos os itens PASS**.
5. Rode de novo com **`acao=tudo`** — build + deploy da Final inteira. As URLs finais saem no resumo do job.

> **Não crie nenhuma GitHub Variable, não baixe publish profile, não digite segredo de aplicação.** Se o `acao=check` falhar, a mensagem aponta exatamente o que ajustar (ou o que pedir ao instrutor).

---
