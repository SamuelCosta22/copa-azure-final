# Débito de Segurança — "A Grande Final" (EPIC-004)

> **Status:** REGISTRADO COMO DÉBITO CONHECIDO (decisão do owner, 2026-07-01)
> **Origem:** security review consolidada da branch `phase-08-hardening` (delta EPIC-004, `4150d53..a3e4509`)
> **Decisão do owner:** *Débito agora + armar a trava no Azure ao vivo* na passada de validação com browser-harness (subs HML da copa). Sem alteração de código/pipeline nesta rodada.
> **Escopo da análise:** o que os **artefatos versionados** (código + IaC + workflows) garantem. A config de runtime do ambiente vivo `copa-azure-final` foi feita à mão e **não é determinável pelo repo** — os itens marcados `[verificar no Azure]` serão checados na etapa browser-harness.

---

## Tese central

A **lógica** de hardening está correta e fail-closed **no código** (confirmado pelo code review: JWT dual-issuer, fence `CiamOnly`, `email_verified`, `X-Gateway-Key` timing-safe, cache pós-auth, `ForwardLimit=1`). O problema é que **o perímetro que essa lógica pressupõe NÃO está cabeado em nenhum artefato de deploy versionado** — então a postura *entregue por padrão* é **fail-open**. A segurança real depende de configuração manual, não reproduzível.

---

## Achados priorizados

### 🔴 CRITICAL-1 — `X-Gateway-Key` é fail-open por default e nenhum pipeline versionado o arma

- `GatewayKeyValidator.Evaluate` retorna `LegacyBypass` (libera tudo) quando o segredo está vazio (`Functions/Infrastructure/GatewayKeyValidator.cs:51-54`); o default do repo é vazio (`Gateway/appsettings.json:15`).
- **Nenhum workflow** seta `Gateway__AdminSharedSecret` (lado gateway) nem `GATEWAY_SHARED_SECRET` nas Functions/McpServer. `deploy-phase-02/03/05.yml` não mencionam; `lab-quartas-de-final.yml:205-211` seta só no **backend v1**, e o deploy do gateway ali só faz `containerapp update --image` (`:279-283`, sem `set-env-vars`).
- **Vetor:** Functions rodam em Consumption **sem VNet**, triggers HTTP `Anonymous` (`PurchaseEntryFunction.cs:58`, `PurchaseStatusFunction.cs:27`, `MeFunction.cs:57`) → alcançáveis pela internet no `*.azurewebsites.net`. Com o segredo desarmado, `curl` direto em `/api/v2/me` forjando `X-Entra-OID`+`X-Entra-Email` da vítima dispara o link-by-email (`MeFunction.cs:114-121` → `TryLinkByEmailAsync`) → **account takeover** — exatamente o A-1 que o `email_verified` do gateway (`Program.cs:211-215`) mata, mas que a Function não re-checa. Mesmo caminho força `X-Entra-OID` em `POST /api/v2/purchase`.
- **Nuance:** morto **no código do gateway**, não no **perímetro das Functions**. `[verificar no Azure]` se o segredo já está armado no ambiente vivo.
- **Remediação (owner: armar ao vivo):** setar o segredo nas App Settings de gateway + Functions + McpServer na subs HML. Fix definitivo (futuro): cabear nos workflows + flip do `LegacyBypass` p/ exigir opt-in explícito em Produção (sem quebrar labs sem gateway).

### 🟠 HIGH-1 — Backend/Functions confiam em headers de identidade sem verificação própria

- `MeFunction.cs:63-71` e `PurchaseEntryFunction.cs:130-134` leem `X-Entra-OID/Email/Name` e agem direto. Toda a defesa (email_verified, fence CiamOnly, anti-spoof) mora **só no gateway** (`Program.cs:195-235, 583-598`). As Functions são o ponto único de falha e não impõem nada sozinhas.
- **Defesa em profundidade (futura):** re-checar `email_verified` antes do arm de link na `MeFunction` e/ou `AuthorizationLevel.Function` nos triggers HTTP.

### 🟠 HIGH-2 — McpServer `/llm` como open proxy + `/mcp` consultável direto (sem IaC de ingress interno)

- Não há IaC para o McpServer (só `infra/phase-04` n8n e `infra/phase-06` flow); o Dockerfile não fixa ingress. Com `GATEWAY_SHARED_SECRET` desarmado (CRITICAL-1), o middleware fica em `LegacyBypass`. Se o ingress for `external` (padrão dos outros Container Apps, ex. `flow-events-containerapp.yaml:31`), então `POST https://<mcp-fqdn>/llm/gemini/...` proxia prompts do atacante usando `GEMINI_API_KEY`/`GROQ_API_KEY`/`MISTRAL_API_KEY` (`LlmProxyEndpoints.cs:54-92`) → **abuso financeiro da chave LLM**; e as 7 tools ficam consultáveis sem token. Rate limit só existe no gateway (`Program.cs:312-330`).
- **Remediação:** fixar `ingress.external=false` do McpServer em IaC versionado; criar o manifesto de Container App faltante. `[verificar no Azure]` o modo de ingress real.

### 🟠 HIGH-3 — Story 4.1 "MI + Key Vault" não está refletida em nenhum IaC/workflow

- Busca em `.github/` e `infra/` retorna **zero** `@Microsoft.KeyVault`, `keyvaultref`, `SecretUri`, `az keyvault`, `--user-assigned`. Todo segredo é GitHub Actions secret → Container App secret (`secretref:`), que **não** é Key Vault reference:
  - `GEMINI/GROQ/MISTRAL_API_KEY` + SQL conn → secrets em claro (`deploy-phase-05.yml:114-129`).
  - Azure SignalR conn → secret em claro (`deploy-phase-06.yml:122`; `flow-events-containerapp.yaml:38-41`).
  - `EntraTenantId`/`EntraClientId` → `--set-env-vars` em **plaintext** (`deploy-phase-03.yml:149-154`).
  - `GATEWAY_SHARED_SECRET` → App Setting via `az webapp config appsettings set` (`lab-quartas-de-final.yml:205-211`).
- **Vetor:** qualquer principal com `listSecrets`/management plane lê tudo em claro; sem KV audit/rotação. A migração MI+KV é aspiracional relativa ao repo.
- **Remediação (futura):** converter secrets p/ Key Vault references resolvidas por user-assigned MI; SQL conn → `Authentication=Active Directory Managed Identity` (o código já suporta em `PurchaseRepository.cs:27-45` — só falta IaC).

### 🟡 MEDIUM-1 — FlowEvents `external` sem auth própria

- ADE-009 Inv 1 escopou a `X-Gateway-Key` só p/ `backend-v1/functions-f1/mcp-server`; FlowEvents não valida JWT nem gateway key (`FlowEvents/Program.cs`), e o container é `external: true` (`flow-events-containerapp.yaml:31`). `GET /api/flow/recent`, `/api/flow/{correlationId}` e `POST /{id}/replay` (`FlowEndpoints.cs:50-68`) são não-autenticados. **Mitigante:** expõe só `correlationId`/`timestamp`/`status`+trace (não PII direta) — info disclosure de volume/IDs de compra + trigger de push SignalR não-autenticado.

### 🟡 MEDIUM-2 — Sem diagrama de topologia atualizado (pós-n8n + hardening)

- Só existem 3 drawios (`arquitetura-geral`, `oitavas`, `quartas`). O geral é de 2026-06-25 (antes da remoção do n8n e do EPIC-004); grep retorna 0 p/ `X-Gateway-Key`/`Key Vault`/`Managed Identity`/`VNet`. A camada de segurança está **não representada**. (Story 3.6 + 4.6 previstas p/ isso.)

### 🟡 MEDIUM-3 — `deploy-phase-03.yml` desatualizado (single-issuer)

- Ainda seta `EntraTenantId/ClientId` (single-issuer) que o gateway dual-issuer atual ignora → startup guard falharia. Reconciliar com a config dual-issuer real.

### ✅ MEDIUM-4 — FlowEvents `/api/flow/diploma-summary` — **FECHADO pela Opção F** (Story 4.6 §Emenda MEDIUM-4, 2026-07-03)

> **Status: FECHADO** (fix real implementado, não aceito como débito). O owner **rejeitou** aceitar o débito (2026-07-03) e pediu proteção real; o `@architect` desenhou e o owner aprovou a **Opção F (defesa em profundidade)**. Histórico do achado preservado abaixo para rastreabilidade.

- **Origem (histórico):** Story 4.6 (Diploma vivo) adicionou `GET /api/flow/diploma-summary?userId={id}` (`FlowEndpoints.cs`) reusando a MESMA fonte App Insights do F6. Herdava **exatamente** a postura do MEDIUM-1: ingress `external: true`, sem revalidar JWT, fora do `X-Gateway-Key` (ADE-009 Inv 1). No FQDN direto do `ca-flow` o endpoint era **anônimo na internet**, com `userId` v1 **sequencial/enumerável** — um chamador anônimo podia iterar `userId=1,2,3…` e obter os correlation-IDs (GUIDs opacos, zero PII) de cada aluno.
- **Fix (Opção F — defesa em profundidade, ADE-009 v1.1):** **duas provas independentes** que um chamador anônimo da internet não satisfaz:
  1. **Identidade** — `fetchDiplomaSummary` passou a mandar `Authorization: Bearer` (via `VITE_GATEWAY_V2_URL`); o blanket `RequireAuthorization()` do gateway barra o anônimo-via-gateway (401 sem token).
  2. **Proveniência** — o gateway injeta um header **distinto** `X-Diploma-Key` (reuso do **mesmo** `gateway-admin-shared-secret`/`Gateway:AdminSharedSecret`, **route-scoped** à rota `flow-events-diploma`); o FlowEvents valida em **tempo constante** (`CryptographicOperations.FixedTimeEquals`) **só** no `diploma-summary` → barra o FQDN direto do `ca-flow` (o atacante não conhece o segredo).
- **Retro-compat preservada (bloqueante, verificado):** ingress do `ca-flow` **fica `external`** (sem flip de rede, blast-radius zero no F6); `/recent`/`/{id}`/`/replay`/**SignalR** **intocados** (seguem como MEDIUM-1 aceito); o cluster `flow-events` **continua fora** do `X-Gateway-Key` — o teste `FlowEvents_Cluster_Does_NOT_Receive_GatewayKey` **segue verde** (header/mecanismo distintos, injeção por rota). Testes: FlowEvents 26/26 (+2), Gateway 41/41 (+1: `DiplomaRoute_Injects_XDiplomaKey_ButNever_XGatewayKey`).
- **Semântica fail-closed/legado:** `DiplomaSharedSecret` configurado no `ca-flow` (KV reference ao mesmo secret — guia Fase 7.4/9) → header ausente/divergente = **401**; vazio → **bypass legado** (preserva dev local e o estado pré-provisionamento).
- **Residual aceito (fast-follow, declarado):** um aluno **autenticado** ainda pode passar `?userId=<outro>` via gateway (Bearer válido → o gateway injeta o key) e ler GUIDs opacos+count de outro aluno — **zero PII**, igual/menor que o `/recent` (MEDIUM-1). Escopo **infalsificável-por-identidade** (aluno A nunca lê aluno B) = **Opção D** (gateway injeta `X-Entra-OID`; FlowEvents resolve `oid→userId`), **condicionada a verificar** se a telemetria já carrega o `oid` (hoje `customDimensions.UserId` é o int v1 — **não assumir**, Art. IV). Fora desta emenda.

---

## Plano priorizado (para quando o débito for pago)

1. **Armar `GATEWAY_SHARED_SECRET`/`Gateway__AdminSharedSecret` em todo pipeline** (gateway inject + Functions + McpServer + backend), ou fail-closed quando ausente em Produção (flip do `LegacyBypass` p/ opt-in explícito). Fecha CRITICAL-1 e HIGH-2. **→ Owner: armar ao vivo na subs HML agora (browser-harness); versionar depois.**
2. **Defesa em profundidade nas Functions** — re-check `email_verified` antes do link na `MeFunction` + `AuthorizationLevel.Function`. Fecha HIGH-1.
3. **Fixar ingress `internal` do McpServer/FlowEvents em IaC** + auth no FlowEvents. Fecha HIGH-2/MEDIUM-1. *(MEDIUM-4 já FECHADO pela Opção F — Story 4.6 §Emenda MEDIUM-4.)*
4. **MI + Key Vault** — secrets → KV references via user-assigned MI; SQL conn → MI. Fecha HIGH-3.
5. **Diagrama de topologia/segurança atualizado** + reconciliar `deploy-phase-03.yml`. Fecha MEDIUM-2/3.

---

## O que está seguro hoje (confirmado — não é achado)

- JWT do gateway é fail-closed no startup (`Program.cs:393-441`, `ClockSkew=Zero`, `RequireHttpsMetadata`); dual-issuer 1:1.
- Anti-spoof global de headers de identidade (`Remove` antes de injetar, `Program.cs:155-157`).
- SQL parametrizado (Dapper) em todo lugar; Kusto parametrizado+sanitizado. Sem superfície de injeção.
- CORS origin-restrito (não wildcard) no gateway e FlowEvents.
- Chaves LLM server-side only; guard no bundle (`deploy-phase-05.yml:180-190`).
- `.env` gitignored; `appsettings.json`/`local.settings.json` com placeholders vazios — nenhum segredo hardcoded no git.

---

*Gerado pela squad AIOX TFTEC em 2026-07-01, consolidando code review + security review (5 temas) da Grande Final.*
