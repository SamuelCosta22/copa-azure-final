# ADE-010 — Managed Identity + Key Vault sobre os recursos EXISTENTES: migração manual (Portal), sem downtime, das chaves em claro para o cofre

> **Tipo:** Architecture Decision Entry (execução concreta de segurança — data-plane / secrets management, **hands-on Portal**)
> **Status:** ✅ Accepted · **Refina ADE-009 Inv 2 para execução manual** · **v1.1** (v1.0 — criação; **v1.1 — Emenda: fecha o gap do inventário orientado-a-recurso-NOVO com uma varredura por-recurso-REUSADO (Function F1, backend v1); +2 secrets `servicebus-connection-string` e `backend-sql-password`; subfases de migração in-place na Fase 9 do runbook**)
> **Date:** 2026-07-02 (v1.0) · **Amended:** 2026-07-05 (v1.1 — varredura por-recurso-reusado)
> **Author:** Aria (Architect) · **Squad AIOX TFTEC**
> **Scope:** **EPIC-004 "Nível Produção" — Missão Blindar (data-plane), fatia executável à mão.** Converte, **sem recriar recurso algum**, as chaves hoje em **texto puro** nas App Settings/env vars dos recursos no ar (`ca-gateway-dev-tk-cin-001`, backend `app-dev-tk-bend-cin-001`, Functions `func-dev-tk-cin-001`, McpServer/FlowEvents da Final) em **Key Vault references resolvidas por Managed Identity**, usando o Key Vault **já existente** `kv-dev-tk-cin-001` (RBAC habilitado). Cobre também o **SQL via MI** (o código já suporta) como **Fase 2/showcase** e uma seção de **Observabilidade nível-produção** reusando `appi-dev-tk-cin-001` + `log-dev-tk-cin-001`.
> **Supersedes:** N/A — **aditiva**. É a **materialização hands-on-Portal** da ADE-009 Invariante 2. Não reverte decisão alguma.
> **Related:** **ADE-009** (parente direta — Inv 2 "zero segredo de serviço em config: data-plane via MI+Entra; irredutíveis no KV; menor-privilégio por serviço" — esta ADE é o **como fazer à mão**), **ADE-008** (regra de ouro "McpServer nunca escreve" → contained user `db_datareader`-only), **ADE-007** (identidade CIAM dual-issuer — **plano ortogonal**; MI/KV não tocam o `oid → X-Entra-OID → entra_oid`), **ADE-003** (baseline PaaS — já previa `SqlConnectionString = @Microsoft.KeyVault(SecretUri=...)`, l.51 — esta ADE **cumpre** aquela intenção nos recursos reais), **`phase-08-contained-users.sql`** (DDL idempotente dos contained users — @data-engineer, **já no repo**), **`final-security-debt.md`** (HIGH-3 — a dívida que esta ADE fecha).
> **Rastreabilidade (Art. IV):** o inventário de chaves foi **levantado na fonte** (`.github/workflows/lab-a-final.yml`, `deploy-phase-05.yml`, `deploy-phase-06.yml`, `deploy-phase-02.yml`; guia `final-portal-guide.md`) — ver §"Verificação na fonte da verdade". O suporte a SQL-MI foi **confirmado no código** (`PurchaseRepository.cs:26-45`, `FifaQueryRepository.cs:22-43`). **Não invento** nomes de MI, GUIDs de tenant, valores de segredo nem URIs exatos — a **DIREÇÃO** é desta ADE; os **literais** ficam marcados `[confirmar no Portal]` e são executados pelo owner.

---

## Context

A Story 4.1 ("MI + Key Vault") foi **registrada como débito** (`final-security-debt.md`, HIGH-3) em vez de implementada: a busca em `.github/`/`infra/` retorna **zero** `@Microsoft.KeyVault`, `keyvaultref`, `SecretUri`. Hoje **todo segredo de serviço vive em claro** na configuração de runtime dos recursos:

- **Gateway** `ca-gateway-dev-tk-cin-001`: `Gateway__AdminSharedSecret` é **env var em TEXTO PURO** (App Setting no Container App, criada à mão — `[verificado no ambiente]`).
- **McpServer / Functions / FlowEvents**: as chaves entram como **Container App secrets** (`secretref:`) — melhor que env plaintext, mas **não** é Key Vault (o valor mora no próprio Container App, sem cofre/rotação/auditoria central). Verificado: `lab-a-final.yml:177-206` (`sql-conn`, `gemini-key`, `groq-key`, `mistral-key`, `gateway-secret`) e `:390-399` (`azure-signalr-conn`).
- **SQL**: connection string com **senha** (SQL auth) — apesar de o código já suportar MI-AAD (o comentário em `PurchaseRepository.cs:26-45` documenta a troca de string exata).

**O que o owner quer (e a frustração legítima):** (1) **APROVEITAR os recursos que já estão no ar** — não recriar nada; (2) **fechar as chaves em claro** movendo-as para o KV existente com MI + KV references; (3) tudo executável **à mão pelo Portal** (o alvo agora é o **guia manual**, não workflow/IaC). Esta ADE dá a **direção decidida** para o @analyst reescrever o guia.

**Princípio norteador — REUSO, não recriação (reforço do owner):** **nenhum recurso novo** é criado por esta ADE além dos que o lab da Final já provisiona (McpServer, FlowEvents, SignalR). A mudança é **100% de configuração** sobre o que existe: criar *secrets* dentro do KV `kv-dev-tk-cin-001` **já provisionado**, atribuir uma *role*, ligar uma *Managed Identity* e trocar cada App Setting de plaintext → KV reference. O gateway, o backend, as Functions, o SQL, o App Insights e o Log Analytics **permanecem os mesmos recursos** — só passam a ler segredos do cofre em vez de guardá-los em claro.

**Restrição dura (retro-compat):** gateway, backend e Functions **servem as Quartas AGORA**. A migração **não pode derrubá-las** em nenhum passo. O design abaixo é **zero-downtime por construção** (§Ordem de migração).

---

## Decision

Adotamos **"cofre único existente, uma identidade de leitura compartilhada para o KV, identidade por-serviço só onde há menor-privilégio de dados (SQL), migração in-place sem downtime"**, com **5 definições**:

### D1 — Modelo de identidade: **uma User-Assigned MI compartilhada para LER o KV** (`id-fifa2026-kv-reader`), e **system-assigned por-app só para o SQL** (Fase 2)

Decisão **decidida e justificada** (o item que o owner pediu para eu resolver):

**Para ler o Key Vault (Fase 1 — todas as chaves em claro):** **UMA User-Assigned Managed Identity compartilhada**, `id-fifa2026-kv-reader` `[nome sugerido; owner confirma]`, anexada a **todos** os apps que leem o cofre (gateway, McpServer, FlowEvents, backend, Functions).

Por quê a compartilhada vence a system-assigned-por-app **para o plano de leitura de segredo**:

| Critério | User-Assigned compartilhada (escolhida p/ KV) | System-Assigned por-app |
|---|---|---|
| **Grants no KV** | **UM** role assignment (`Key Vault Secrets User`) na vida toda | **N** grants (um por app), refeitos a cada recriação |
| **Reprodutível à mão** | ✅ cria 1x, atribui 1x, **anexa** a cada app | ⚠️ liga identidade + regrant por app, toda vez |
| **Sobrevive à recriação** (o owner **recria** McpServer/FlowEvents à mão) | ✅ a MI é recurso **independente** — persiste; só re-anexar | ❌ a system-assigned **morre com o app** → **novo** objectId → **regrant obrigatório** no KV |
| **Menor-privilégio de leitura** | Igual — ler segredo é uniforme; `Key Vault Secrets User` já é vault-wide em RBAC | Igual |

Como **ler segredo é uma operação uniforme** (não há "este app lê só o segredo X" barato em RBAC — a role é vault-wide de qualquer jeito), a granularidade por-app **não compra segurança** no plano de leitura, e a compartilhada **elimina** o atrito de regrant a cada recriação manual. É a escolha **mais simples de fazer à mão E reproduzível** — exatamente o pedido.

**Para autenticar no SQL (Fase 2 — showcase/opcional):** **NÃO** usar a MI compartilhada. O SQL exige **menor-privilégio por-serviço** (McpServer `db_datareader`-only vs Functions writer+reader — ADE-008 Inv 1 / DDL `phase-08-contained-users.sql`). Uma única MI colapsaria McpServer + Functions no **mesmo contained user** e **quebraria a regra de ouro**. Logo, no SQL cada app usa a **própria system-assigned MI** → o próprio contained user → o próprio papel. Vantagem operacional: com system-assigned e connection string `Authentication=Active Directory Managed Identity` **sem** `User Id`, a **mesma string** serve todos os apps e cada um resolve para a **sua** identidade → um único segredo `sql-connection-string` no KV preserva o menor-privilégio. `[gotcha a verificar — ver Riscos R-6]`

> **Resumo do modelo:** um app pode ter **duas** identidades — a **UA compartilhada** (lê o KV) **e** a **system-assigned** (fala com o SQL na Fase 2). São planos distintos: "quem lê o cofre" ≠ "quem grava no banco". Fase 1 usa só a UA compartilhada; a system-assigned entra na Fase 2.

### D2 — RBAC: `Key Vault Secrets User` na `id-fifa2026-kv-reader`, escopo = o KV `kv-dev-tk-cin-001` (RBAC, não access policy)

Como o KV está com **RBAC habilitado** (`enableRbacAuthorization = true` — access *policies* estão inativas), o acesso é por **role assignment**, no Portal:

1. **Portal → Key Vault `kv-dev-tk-cin-001` → Access control (IAM) → Add role assignment.**
2. Role: **`Key Vault Secrets User`** (built-in típico `4633458b-17de-408a-b874-0445c86b69e6` — só leitura de valor de segredo; **não** list/set/delete). Members: **Managed identity → `id-fifa2026-kv-reader`**. Scope: **este KV** (o próprio recurso — menor escopo possível, não a subscription/RG).
3. **Para o próprio owner criar/editar os secrets à mão**, ele precisa de **`Key Vault Secrets Officer`** (típico `b86a8fe4-44ce-4948-aee7-eec1d1c7a7b0`) **em si mesmo** no mesmo KV — senão o blade "Secrets" nega com 403 mesmo sendo Owner do recurso (Owner do *management plane* ≠ *data plane* em RBAC-KV). Este é o **gotcha #1** de quem nunca migrou KV-RBAC.
4. Atribuir a role **NÃO é instantâneo** — a propagação leva alguns minutos. Validar antes de trocar qualquer App Setting.

> CLI equivalente (se o principal não aparecer no seletor do Portal): `az role assignment create --role "Key Vault Secrets User" --assignee-object-id <objectId-da-MI> --assignee-principal-type ServicePrincipal --scope <resourceId-do-KV>`.

### D3 — Mapa `secret → KV` (para CADA chave em claro hoje)

**Nomes de secret no KV** (kebab-case, convenção KV) e **quem referencia**. O valor inicial de cada secret é **cópia byte-a-byte** do valor em claro de hoje (Fase 1 troca o *lar* do segredo, não o *valor* — isso é o que garante zero-downtime).

| Secret no KV (`kv-dev-tk-cin-001`) | Valor (origem hoje) | Recurso(s) que referencia | App Setting / env var no destino |
|---|---|---|---|
| **`gateway-admin-shared-secret`** | o X-Gateway-Key (hoje **plaintext** no gateway) | **Gateway** (injeta) **+ Backend + Functions + McpServer** (validam) | Gateway: `Gateway__AdminSharedSecret` · demais: `GATEWAY_SHARED_SECRET` |
| **`gemini-api-key`** | `GEMINI_API_KEY` (secret do CA hoje) | McpServer | `GEMINI_API_KEY` |
| **`groq-api-key`** *(opc)* | `GROQ_API_KEY` | McpServer | `GROQ_API_KEY` |
| **`mistral-api-key`** *(opc)* | `MISTRAL_API_KEY` | McpServer | `MISTRAL_API_KEY` |
| **`sql-connection-string`** | connection string SQL **ADO.NET** (hoje com **senha**) | McpServer **+** Functions (consumidores .NET `SqlClient`) | `SqlConnectionString` |
| **`servicebus-connection-string`** *(v1.1)* | `ServiceBusConnection` — SAS `RootManageSharedAccessKey` (hoje **plaintext** na App Setting da Function, **herdada de Oitavas**) | **Function F1** `func-dev-tk-cin-001` | `ServiceBusConnection` |
| **`backend-sql-password`** *(v1.1)* | **senha discreta** do SQL (hoje `DB_PASSWORD` **plaintext** na App Setting do backend, herdada de Oitavas/Quartas) | **Backend v1** `app-dev-tk-bend-cin-001` (Node, campos `DB_*` discretos — **não** ADO.NET) | `DB_PASSWORD` |
| **`azure-signalr-connection-string`** | `AzureSignalRConnectionString` (secret do CA) | FlowEvents | `AzureSignalRConnectionString` |
| **`appinsights-connection-string`** *(ver §Observabilidade)* | conn string do App Insights (ingestion key) | Gateway, McpServer, FlowEvents, Functions | `APPLICATIONINSIGHTS_CONNECTION_STRING` |

**Correção estrutural que o cofre destrava (ganho, não só higiene):** o `Gateway__AdminSharedSecret` (lado que **injeta** o `X-Gateway-Key`) e o `GATEWAY_SHARED_SECRET` (lado que **valida**, no backend/Functions/McpServer) **têm de ter o MESMO valor** — hoje são dois App Settings independentes que podem **divergir por engano** (e divergir = 401 em toda request → Quartas caem). Centralizando em **UM** secret `gateway-admin-shared-secret` referenciado pelos dois lados, a igualdade vira **garantia estrutural**, não disciplina manual.

> **SQL**: na Fase 1 o `sql-connection-string` **mantém a senha** (só sai do plaintext para o cofre — já fecha o vetor de leitura em claro). Na Fase 2 (D5) o **valor** troca para `Authentication=Active Directory Managed Identity;Encrypt=True` (senha **deixa de existir**). O **nome** do secret e as referências **não mudam** entre as fases — só o valor dentro do cofre.

### D4 — Forma exata da KV reference: Container App vs App Service/Functions (são diferentes)

**(a) Container Apps** (gateway, McpServer, FlowEvents) — a KV reference é uma propriedade do **secret do Container App**, não do env var:

- O env var **continua** `NOME=secretref:<nome-do-secret-do-CA>` (**inalterado** — esta é a beleza: zero churn no env).
- O que muda é o **secret do CA**: de valor **inline** para **KV-backed** (`keyVaultUrl` + `identity`).
- **Portal:** Container App → **Secrets** → o secret (ex. `gemini-key`) → **Edit** → tipo **"Key Vault reference"** → cola o **Secret URI** (`https://kv-dev-tk-cin-001.vault.azure.net/secrets/gemini-api-key`) → **Identity** = `id-fifa2026-kv-reader` (a UA compartilhada).
- **CLI:** `az containerapp secret set -n <app> -g <rg> --secrets "gemini-key=keyvaultref:https://kv-dev-tk-cin-001.vault.azure.net/secrets/gemini-api-key,identityref:<resourceId-da-UA-MI>"`.
- **Pré-requisito de ordem:** a **UA-MI tem de estar anexada ao Container App ANTES** de criar o secret KV-backed (senão o ARM rejeita o `identityref`). Anexar: Container App → **Identity → User assigned → + Add**.

**(b) App Service (backend) e Functions** — a KV reference é o **valor do próprio App Setting**:

- App Setting: `NOME=@Microsoft.KeyVault(SecretUri=https://kv-dev-tk-cin-001.vault.azure.net/secrets/<nome>/)`.
- **Identidade que resolve:** por padrão a **system-assigned**. Para usar a **UA compartilhada**, definir a propriedade **`keyVaultReferenceIdentity`** = resourceId da `id-fifa2026-kv-reader` (**Portal** não expõe isso na tela de Configuration de forma óbvia → via CLI: `az webapp update -n <app> -g <rg> --set keyVaultReferenceIdentity=<resourceId-da-UA-MI>`; Functions idem com `az functionapp update`). **Esquecer isto** = a reference tenta a system-assigned (talvez ausente) → **não resolve** → o App Setting entrega a **string literal** `@Microsoft.KeyVault(...)` ao app → quebra. É o **gotcha #2** (ver R-3).
- **Verificação:** Portal → App Service → Configuration → o setting mostra **"Key Vault Reference"** com status **Resolved** (verde). Se "não resolvido", **não** avance.

> ADE-003 (l.51) já documentava exatamente `SqlConnectionString = @Microsoft.KeyVault(SecretUri=.../sql-connection-string/)` — esta ADE **executa** aquela intenção nos recursos reais.

### D5 — SQL via MI: **Fase 2 (opcional/showcase)**, código já pronto, risco de retro-compat isolado

O código **já suporta** — é troca de **string**, não de mecanismo (verificado):

- `PurchaseRepository.cs:26-45` e `FifaQueryRepository.cs:22-43`: `new SqlConnection(_connectionString)` **intocado**; `Microsoft.Data.SqlClient` 5.2.2 resolve o token AAD **nativamente** a partir da keyword `Authentication=`.
- **O que muda:** o **valor** do secret `sql-connection-string`, de `Server=...;User Id=...;Password=...` para `Server=tcp:sql-dev-tk-cin-001.database.windows.net,1433;Database=FIFA2026Tickets;Authentication=Active Directory Managed Identity;Encrypt=True`. (Se a MI for User-Assigned, acrescentar `;User Id=<client-id-da-MI>`.)
- **Pré-requisitos (sem eles o SQL-MI FALHA):** (1) **Azure AD admin configurado no SQL Server** `sql-dev-tk-cin-001` (Portal → SQL Server → Microsoft Entra ID → Set admin); (2) rodar `phase-08-contained-users.sql` **conectado COMO esse admin via AAD** (não SQL-auth), no banco `FIFA2026Tickets`, com os placeholders `<mi-*>` substituídos pelos **nomes reais** das MIs (@data-engineer/@devops); (3) as MIs system-assigned já habilitadas nos apps.

**Por que Fase 2 e não Fase 1:** (a) exige AAD admin no SQL + contained users + smoke de permissão negada — **mais cerimônia e mais risco** que uma KV reference; (b) o **backend v1** `app-dev-tk-bend-cin-001` **ainda não foi convertido** (o `database.js` usa `SQLAZURECONNSTR_`/`DB_PASSWORD`; o Bloco C da DDL é *forward-looking*) → converter o backend agora **arriscaria as Quartas**. Fase 1 (senha da SQL **para o cofre**) já fecha o vetor de leitura em claro com **risco baixíssimo**; Fase 2 (senha **eliminada**) é o showcase. **Se algo não for seguro fazer à mão hoje, o backend-v1-SQL fica como débito residual** (senha no KV, não em claro) — ver R-7.

---

## Emenda v1.1 — varredura por-recurso-REUSADO fecha o gap do inventário (a Fase 1.4 do runbook foi orientada a recurso-NOVO)

> **Adicionada 2026-07-05.** Ao auditar o `docs/runbooks/final-portal-guide.md` contra esta ADE, encontrei um **gap de cobertura do inventário**: a lista de secrets do cofre da **Fase 1.4** foi montada **olhando os recursos NOVOS da Final** (McpServer, FlowEvents, SignalR, Gemini) **+ 1 segredo herdado** (`Gateway__AdminSharedSecret`), e **não varreu as App Settings sensíveis que Oitavas e Quartas deixaram nos recursos REUSADOS** (Function F1, backend v1). **Causa-raiz:** o inventário foi **orientado a recurso-NOVO** (o que a Final acrescenta), não a uma **varredura por recurso-REUSADO** (o que cada recurso já no ar carrega em claro). É emenda **aditiva** — não reverte D1–D5; **reforça** a mesma disciplina "zero chave em claro na config" (Consequência positiva #1) sobre a superfície que o inventário original não enxergou.

**Gap por severidade (achados da varredura por-recurso):**

| # | Sev | Chave em claro | Recurso reusado | Situação no inventário original | Resolução (v1.1) |
|---|---|---|---|---|---|
| **H-1** | ALTA | `ServiceBusConnection` — SAS `RootManageSharedAccessKey` | Function F1 `func-dev-tk-cin-001` | **Nunca citada** no guia | **Novo secret** `servicebus-connection-string` + subfase de migração in-place na Fase 9 |
| **H-2** | ALTA | `DB_PASSWORD` do backend v1 | Backend `app-dev-tk-bend-cin-001` | Fase 9 **afirmava** migração **sem passo**; o secret ADO.NET `sql-connection-string` **não serve** ao backend Node | **Novo secret** `backend-sql-password` (senha isolada) + subfase na Fase 9 |
| **H-3** | ALTA | `SqlConnectionString` da Function F1 | Function F1 | **Nomeado** na Fase 1.4 ("McpServer + Functions"), mas **sem passo** de migração na Fase 9 | Secret **já em D3** (`sql-connection-string`) — o furo era **de passo no runbook**, resolvido por subfase na Fase 9 |
| **M-1** | MÉDIA | `AzureWebJobsStorage` — account key | Function F1 | Em claro | **Débito residual nomeado** — KV-ref tem ressalva de **bootstrap do host Functions** (migração cega é risco); **não** migrado às cegas nesta emenda |
| **L-1** | BAIXA | `APPLICATIONINSIGHTS_CONNECTION_STRING` criada em claro pelo Portal na Function | Function F1 | Coexiste com a KV-ref já prevista (Fase 13.1) | **Substituir, não duplicar** — a KV-ref `appinsights-connection-string` (já em D3) prevalece sobre a conn string em claro criada pelo Portal |

**Por que o backend v1 exige um secret PRÓPRIO — racional da separação de formas (H-2).** O secret `sql-connection-string` de D3 é uma **connection string ADO.NET completa**, consumida pelos clientes **.NET `SqlClient`** (McpServer + Function F1: `new SqlConnection(_connectionString)`). O **backend v1 é Node** e lê **campos `DB_*` discretos** (`fifa2026-api/src/config/database.js` — `DB_SERVER`/`DB_USER`/`DB_PASSWORD`/…), **não** uma connection string ADO.NET; injetar a string ADO.NET no backend **não funciona**. Logo o backend precisa da **senha isolada** num secret próprio `backend-sql-password`, referenciado só pelo App Setting `DB_PASSWORD`. **Formatos distintos ⇒ secrets distintos** — a mesma lógica que mantém a string ADO.NET separada dos `DB_*` discretos. (H-3 **não** gera secret novo: a `SqlConnectionString` da Function F1 é ADO.NET e **compartilha** o `sql-connection-string` já mapeado — o furo dela era **passo de runbook**, não secret faltante.)

**Decisão (o que o runbook ganha na Fase 9 — subfases novas de migração in-place):**
- Migrar **in-place** para `@Microsoft.KeyVault(SecretUri=…)`: `SqlConnectionString` **+** `ServiceBusConnection` da **Function F1**, e `DB_PASSWORD` do **backend v1** — todas resolvidas pela **UA compartilhada `id-fifa2026-kv-reader` já anexada** (mesmo `keyVaultReferenceIdentity` de D4-(b); **sem novo grant** — a role `Key Vault Secrets User` da MI já é vault-wide, D2).
- **Dois secrets NOVOS** no cofre: `servicebus-connection-string` e `backend-sql-password` (D3 atualizada). O `sql-connection-string` (H-3) **já existia** — resolveu-se com passo de runbook, não secret novo.
- **Invariantes preservadas (idênticas ao núcleo da ADE):** retro-compat Oitavas/Quartas **DURA**; migração **in-place um recurso por vez**; valor **byte-idêntico** (plaintext → cofre, sem troca de valor); **gate "Key Vault Reference · Resolved"** (D4) antes de avançar; **reversão** = plaintext byte-idêntico; **blast radius = um serviço**. Cada nova chave entra pela mesma **Ordem de migração** (passos 5–8) e pelos mesmos **pontos que derrubam as Quartas** — em especial **P-2** (typo na `sql-connection-string`/`servicebus-connection-string`) e **P-3** (`keyVaultReferenceIdentity` na Function/backend, que são App Service/Functions → forma D4-(b)).

**Débitos residuais nomeados por esta emenda (não construídos):**
- **`AzureWebJobsStorage` (M-1):** a account key do storage do host Functions **fica em claro** — a KV-ref tem ressalva de **bootstrap** (o runtime Functions lê `AzureWebJobsStorage` **antes** de resolver KV-refs; migração cega pode **derrubar o host**). Débito nomeado, **não** migrado às cegas aqui.
- **Eliminação TOTAL da senha SQL do backend:** a Fase 9 (v1.1) tira o `DB_PASSWORD` do **plaintext** (vai para o cofre), mas a **senha ainda existe** até o backend Node falar com o SQL por **MI** (SQL-MI) — **showcase do Apêndice E** do runbook, alinhado a R-7/D5 (backend-v1 SQL como débito residual da Fase 2). Sair do plaintext já é o ganho maior; a eliminação é o showcase.

---

## Ordem de migração dos recursos EXISTENTES — zero-downtime (o núcleo p/ o owner)

> **Regra de ouro:** a Fase 1 **nunca muda o VALOR** de um segredo — só o **lar** (plaintext → cofre). Como o valor resolvido é idêntico, o app não percebe a troca. Downtime só aparece se (a) o valor for digitado errado no KV, ou (b) a reference não resolver e o app receber lixo. Toda etapa tem um **gate de validação** antes da próxima.

**Preparação global (1x — nada quebra aqui, ninguém referencia ainda):**
1. Owner ganha **`Key Vault Secrets Officer`** em si no `kv-dev-tk-cin-001` (senão não cria secret).
2. Criar **`id-fifa2026-kv-reader`** (User-Assigned MI) — recurso novo **de identidade** (não é "recriar" serviço).
3. Atribuir **`Key Vault Secrets User`** à MI, escopo = o KV. **Esperar a propagação** (minutos) e conferir.
4. **Criar todos os secrets** no KV com os **valores atuais copiados byte-a-byte** (`gateway-admin-shared-secret`, `gemini-api-key`, `sql-connection-string`, `azure-signalr-connection-string`, …). Ninguém referencia ainda → **risco zero**.

**Por recurso (repetir; cada um é independente e reversível):**
5. **Anexar** a `id-fifa2026-kv-reader` ao recurso (Container App: Identity → User assigned; Web App/Functions: Identity → User assigned **e** setar `keyVaultReferenceIdentity`). Anexar identidade é **não-disruptivo**.
6. **Trocar in-place** plaintext → reference:
   - Container App: editar o **secret do CA** de inline → KV-backed (env var `secretref:` **inalterado**).
   - Web App/Functions: editar o **valor do App Setting** para `@Microsoft.KeyVault(...)` (dispara **restart** do app — segundos).
7. **GATE de validação** (não avançar sem ✅): status **Resolved** da reference + `/health` 200 + **smoke funcional retro-compat**:
   - Gateway/Backend: **login CIAM + compra v2 das Quartas** funcionam; POST sem token → 401.
   - McpServer: `tools/list` via gateway com Bearer = 7 tools.
   - FlowEvents: `/health` healthy + rota `/flow` recebe evento.
8. Repetir 5–7 por recurso. **Sem big-bang** — um recurso por vez, cada um validado.

**Pontos onde um erro DERRUBA as Quartas (vigiar):**
- **P-1 (o pior):** `gateway-admin-shared-secret` com valor **diferente** do que o backend valida → `X-Gateway-Key` não bate → **401 em toda request**. Mitigação: **um** secret referenciado pelos dois lados (D3); na migração, o valor no KV = o valor plaintext atual, **byte-idêntico**.
- **P-2:** typo no `sql-connection-string`, no `servicebus-connection-string` ou no `backend-sql-password` (server/db/keyword/senha) → apps não conectam → 500 nas rotas de compra/consulta (ou trigger do Service Bus que não liga). Mitigação: copiar exato; validar McpServer/Functions/backend **antes** de seguir.
- **P-3:** `keyVaultReferenceIdentity` **não setado** no Web App/Functions → reference não resolve → App Setting entrega a string literal → app quebra. Mitigação: setar a identidade **antes**; exigir status **Resolved**.
- **P-4:** trocar o secret KV-backed do Container App **antes** de anexar a MI → ARM rejeita → o update pode falhar **depois** de já mexer no app. Mitigação: anexar MI **primeiro** (passo 5 antes do 6).
- **P-5:** **rotação** do `gateway-admin-shared-secret` **não é atômica** entre apps (Container App refaz cache de KV ref a cada ~30 min / restart; Web App cacheia também) → janela em que o gateway já tem o valor novo e o backend ainda o antigo → 401. Mitigação: rotação é **ação de manutenção planejada** (restart coordenado dos dois lados), **nunca** casual. (Isto vale para *rotação futura* — a migração inicial mantém o valor.)
- **P-6:** **rede do KV** — se `kv-dev-tk-cin-001` tiver firewall/`publicNetworkAccess: Disabled`, os apps precisam de linha de visão (trusted Azure services / private endpoint). **`[verificar no Portal]`** o networking do KV antes; provavelmente público+RBAC (default), mas confirmar.

**Reversão (se um gate falhar):** reverter o App Setting/secret ao valor **inline plaintext** anterior (o valor ainda está no seu clipboard/registro da prep) → app volta ao estado pré-migração. Como é **um recurso por vez**, o blast radius de um erro é **um** serviço, não o sistema.

---

## Observabilidade — nível produção (reusando `appi-dev-tk-cin-001` + `log-dev-tk-cin-001`, ~US$0)

> **Amarração didática com a segurança:** a **mesma linha de raciocínio** — "uma Managed Identity com uma role *Reader* lê um recurso gerenciado, sem segredo" — vale para os **dois** planos: a MI que lê o **Key Vault** (`Key Vault Secrets User`) é irmã da MI que lê o **Log Analytics** (`Log Analytics Reader`, que o **FlowEvents já usa** para o visualizador). Segurança e observabilidade são **a mesma disciplina de identidade gerenciada**, contada duas vezes. Bom gancho de aula.

**O que JÁ existe (só usar / ligar):**
- **Recursos no ar:** App Insights **`appi-dev-tk-cin-001`** + Log Analytics **`log-dev-tk-cin-001`** (verificado em `aula1-f1-portal-guide`). **Não recriar.**
- **Wiring no código, já pronto:** Gateway, McpServer, FlowEvents e Functions **já** inicializam telemetria via `APPLICATIONINSIGHTS_CONNECTION_STRING` — **no-op se ausente** (ADE-000 Inv 5; `Gateway/Program.cs:602`, `McpServer/Program.cs:62`, `FlowEvents/Program.cs:57`, Functions `local.settings.json:15-16`). **Ligar** = setar o App Setting (idealmente como **KV reference** `appinsights-connection-string`, D3).
- **Correlação ponta-a-ponta, já emitida:** o gateway injeta **`X-Correlation-ID`** (W3C Trace Context) e ele **propaga** Gateway → Function → Service Bus → Consumer (ADE-000 Inv 5; `phase-02/SPEAKER-NOTES.md:234`). O FlowEvents **já consulta** o Kusto/Log Analytics via MI para montar os **5 nós** do visualizador.
- **Logs estruturados, já emitidos:** `ILogger` em todos os serviços; a **notificação pós-compra inline correlacionada** (ADE-008, F6 5 nós) já loga por `correlationId`.

**O que CONFIGURAR à mão no Portal (do mais barato/simples ao mais elaborado):**

1. **Distributed tracing por `correlationId` (ligar a telemetria):** setar `APPLICATIONINSIGHTS_CONNECTION_STRING` nos 4 serviços (via KV ref). No Portal → App Insights `appi-dev-tk-cin-001`:
   - **Transaction search**: buscar o `X-Correlation-ID` de uma compra → ver o trace **Gateway → Function → Service Bus → Consumer** ponta-a-ponta (é o **"trace end-to-end"** já previsto no AC-11 das Quartas, hoje runtime-only por falta da conn string).
   - **Application Map**: topologia viva dos serviços + dependências (SQL, Service Bus, SignalR) com latência/erro por aresta — o "mapa da cidade" para a aula.
2. **Workbook da jornada da compra (dashboard):** um **Azure Workbook** no App Insights (US$0) com: **latência por hop** (gateway → function → consumer), **taxa de falha** por serviço, **throughput/backlog do Service Bus**, **saúde do McpServer/gateway** (`/health`, 5xx, cold starts). Base: `requests`/`dependencies`/`traces` correlacionados por `operation_Id` (ligado ao `X-Correlation-ID`).
3. **Alertas úteis a ~US$0** (Azure Monitor alert rules — algumas gratuitas, log-based no free tier de ingestão):
   - **5xx no gateway** acima de N/5min (saúde de perímetro).
   - **Dead-letter no Service Bus** > 0 (compra travada — sinal de negócio).
   - **Latência do chatbot** (dependência LLM proxy) acima do p95 alvo.
   - **Falha de deploy** (via GitHub Actions notification, fora do Azure) e **exceptions** novas no App Insights.
4. **Consulta por Kusto no Portal** (Logs do `log-dev-tk-cin-001`): ex. `requests | where customDimensions.CorrelationId == "<id>" | order by timestamp asc` para a jornada; `traces | where message has "pós-compra"` para a notificação inline correlacionada. O FlowEvents **já** faz Kusto via MI (`Log Analytics Reader`) — mesma role, mesmo raciocínio de D2.

**Marcação (o que é o quê):**
- **Já existe, só usar:** recursos `appi`/`log`, wiring no código, `X-Correlation-ID`, logs estruturados, Kusto do FlowEvents via MI.
- **Configurar à mão:** ligar `APPLICATIONINSIGHTS_CONNECTION_STRING` (via KV ref), montar Workbook, criar alertas, salvar queries Kusto.
- **Débito residual:** amostragem/retenção sob controle de custo (free tier tem teto de ingestão); OpenTelemetry pleno e correlação do **frontend** (browser SDK) ficam como **T3 nomeado, não construído** — o alvo é US$0/Portal.

---

## Consequences

### Positivas
- ✅ **Zero chave em claro na config:** todo segredo de serviço lido do KV por MI; `grep` de valor plaintext na config → 0. Fecha `final-security-debt.md` HIGH-3.
- ✅ **Nada recriado:** KV, gateway, backend, Functions, SQL, App Insights, Log Analytics — **os mesmos recursos**, só reconfigurados. (Só a MI de leitura é recurso novo — de identidade, não de serviço.)
- ✅ **Igualdade do X-Gateway-Key vira estrutura:** um secret, dois lados → não dá mais para divergir por engano (P-1 estruturalmente mitigado).
- ✅ **Reprodutível à mão:** UA-MI compartilhada = 1 grant na vida; sobrevive à recriação manual do McpServer/FlowEvents (sem regrant).
- ✅ **Menor-privilégio de dados preservado:** SQL por system-assigned per-app → McpServer `db_datareader`-only intacto (ADE-008 Inv 1).
- ✅ **Observabilidade nível-produção a US$0** reusando `appi`/`log` já no ar; a MI-Reader do KV e a MI-Reader do Log Analytics contam **a mesma história**.
- ✅ **Retro-compat por construção:** migração in-place, valor idêntico, um recurso por vez, gate de validação, reversão trivial.

### Negativas / Trade-offs aceitos
- ⚠️ **UA-MI compartilhada é coarse para leitura:** qualquer app com a MI lê **todos** os secrets do vault. Aceito para o lab (ler segredo é uniforme); segregação por-secret/vault separado = T3.
- ⚠️ **Duas identidades por app na Fase 2** (UA p/ KV + system p/ SQL). Aceito: planos distintos; a Fase 1 usa só a UA.
- ⚠️ **Rotação do shared secret é não-atômica** (P-5) → vira ação de manutenção planejada, não casual.
- ⚠️ **KV-RBAC exige data-plane role no próprio owner** (`Secrets Officer`) — contraintuitivo (Owner ≠ acesso a segredo). Documentado como gotcha #1.
- ⚠️ **Fase 2 (SQL-MI) fica pendente** se não for seguro à mão hoje → senha migra p/ o cofre mas **ainda existe** (débito residual R-7). Mesmo assim: sair do plaintext já é o ganho maior.

---

## Alternatives Considered (rejeitadas)

- **Alt 1 — System-assigned por-app também para ler o KV.** Rejeitada para o **plano de leitura**: N grants refeitos a cada recriação manual (o owner **recria** McpServer/FlowEvents) → atrito e erro humano, sem ganho de segurança (ler segredo é uniforme). Mantida **só** onde há menor-privilégio real: o SQL (D1/D5).
- **Alt 2 — Manter Container App secrets (secretref inline), sem KV.** Rejeitada: o valor mora no próprio Container App — **sem cofre, sem rotação central, sem auditoria**. É "menos pior que env plaintext", não "segredo em cofre". Não fecha HIGH-3.
- **Alt 3 — MI compartilhada única também no SQL.** Rejeitada por **impossibilidade de menor-privilégio**: colapsaria McpServer + Functions no mesmo contained user → mataria a regra de ouro (McpServer `db_datareader`-only). O SQL exige identidade por-serviço.
- **Alt 4 — Recriar os recursos "limpos" já com MI/KV.** Rejeitada pelo owner (**"não recriar o que já existe"**) e por retro-compat: recriar o gateway muda FQDN → rebuild do frontend + CORS (lição das Quartas). Migração **in-place** é a única compatível com as Quartas no ar.
- **Alt 5 — Converter o backend-v1 SQL para MI agora (Fase 1).** Rejeitada: `database.js` não convertido; mexer no caminho de dados do backend que serve as Quartas **agora** é risco desnecessário. Fica Fase 2/débito (R-7).

---

## Validation (checklist manual/observado — @devops/@data-engineer no EPIC-004; **não implemento, aponto**)

- [ ] `id-fifa2026-kv-reader` criada; `Key Vault Secrets User` no KV `kv-dev-tk-cin-001` (escopo do recurso); propagação confirmada.
- [ ] Todos os secrets de D3 criados no KV com valor byte-idêntico ao plaintext atual.
- [ ] Cada recurso: MI anexada + App Setting/secret = **KV reference** com status **Resolved**; **zero** valor em claro remanescente na config.
- [ ] Gate retro-compat por recurso: login CIAM + compra v2 (Quartas) OK; POST sem token → 401; McpServer `tools/list` = 7; FlowEvents `/health` healthy.
- [ ] `gateway-admin-shared-secret` **único**, referenciado por gateway (`Gateway__AdminSharedSecret`) e backend/Functions/McpServer (`GATEWAY_SHARED_SECRET`) — mesmo valor garantido pela referência única.
- [ ] **Fase 2 (se executada):** AAD admin no `sql-dev-tk-cin-001`; `phase-08-contained-users.sql` aplicado (placeholders reais); `sql-connection-string` = `Authentication=Active Directory Managed Identity`; smoke: INSERT via MI do McpServer → **permissão negada**.
- [ ] **Observabilidade:** `APPLICATIONINSIGHTS_CONNECTION_STRING` ligado (via KV ref); trace por `X-Correlation-ID` visível em Transaction Search; Application Map povoado; Workbook da compra; alertas 5xx/dead-letter criados.
- [ ] ADEs preservadas: gateway segue único validador de JWT (ADE-004); `oid → X-Entra-OID → entra_oid` intacto (ADE-007); DDL aditiva/idempotente (ADE-000 Inv 2); menor-privilégio SQL (ADE-008 Inv 1).

---

## Riscos e retro-compat (o que vigiar; o que fica como débito)

| # | Risco | Vigiar / Mitigar | Se não for seguro à mão |
|---|---|---|---|
| **R-1** | Valor divergente do shared secret (P-1) | Secret único referenciado pelos 2 lados; valor byte-idêntico na migração | — (estrutural) |
| **R-2** | Typo em `sql-connection-string` (P-2) | Copiar exato; validar McpServer/Functions antes de seguir | — |
| **R-3** | `keyVaultReferenceIdentity` esquecido no App Service/Functions (P-3) | Setar antes; exigir status **Resolved** | — |
| **R-4** | Ordem MI×secret no Container App (P-4) | Anexar MI antes do secret KV-backed | — |
| **R-5** | Rotação não-atômica do shared secret (P-5) | Ação de manutenção planejada, restart coordenado | Débito: rotação futura precisa de runbook próprio |
| **R-6** | SQL-MI: app com system **e** user-assigned → string sem `User Id` pode resolver a identidade errada | `[verificar]` qual MI o `Active Directory Managed Identity` escolhe quando há 2; se ambíguo, usar `User Id=<client-id>` explícito | Fase 2 — não bloqueia Fase 1 |
| **R-7** | Backend-v1 SQL ainda com senha (`database.js` não convertido) | Não converter agora (retro-compat Quartas) | **Débito residual:** senha vai p/ o KV (Fase 1) mas persiste até a conversão do `database.js` (Fase 2) |
| **R-8** | Networking do KV (P-6) | `[verificar no Portal]` firewall/public access do `kv-dev-tk-cin-001` | Se privado, precisa PE/trusted services — reavaliar |
| **R-9** | Custo/retenção de observabilidade | Free tier tem teto de ingestão; controlar amostragem | Débito T3: OTel pleno + RUM do frontend nomeados, não construídos |

**Débito residual consolidado (se a Fase 2 não rodar hoje):** a **senha do SQL** deixa de estar em claro (vai para o KV — Fase 1), mas **ainda existe** até o SQL-MI (Fase 2). O **X-Gateway-Key** ainda precisa estar **armado** (valor não-vazio) — esta ADE define **onde ele mora** (KV), não **se está armado** (isso é CRITICAL-1 da dívida; pode ser feito junto: criar o secret **já com valor forte** e referenciá-lo arma e cofre num passo).

---

## Verificação na fonte da verdade (Art. IV)

| Afirmação | Verificado em |
|---|---|
| Chaves hoje como Container App secrets `secretref:` (não KV): sql-conn, gemini/groq/mistral, gateway-secret, signalr | `.github/workflows/lab-a-final.yml:177-206, 390-399`; `deploy-phase-05.yml:115-129`; `deploy-phase-06.yml:115-124` |
| `Gateway__AdminSharedSecret` é App Setting do gateway (criado à mão, plaintext) | `lab-a-final.yml:242-243` (comentário); `final-security-debt.md` CRITICAL-1/HIGH-3 `[verificado no ambiente]` |
| SQL-MI suportado por troca de string (código intocado) | `src/Fifa2026.V2.Functions/Data/PurchaseRepository.cs:26-45`; `src/Fifa2026.V2.McpServer/Data/FifaQueryRepository.cs:22-43` |
| DDL de contained users idempotente, McpServer `db_datareader`-only, backend Bloco C forward-looking | `fifa2026-api/database/migrations/phase-08-contained-users.sql:99-172` |
| KV `kv-dev-tk-cin-001` com RBAC habilitado (role assignment, não access policy) | `[verificado no ambiente pelo owner]` (mission brief) |
| App Insights/Log Analytics já existem; wiring no-op sem conn string; `X-Correlation-ID` propaga | `aula1-f1-portal-guide:661-662`; `Gateway/Program.cs:602`, `McpServer/Program.cs:62`, `FlowEvents/Program.cs:57`; `phase-02/SPEAKER-NOTES.md:234` |
| ADE-003 já previa `SqlConnectionString = @Microsoft.KeyVault(SecretUri=...)` | `docs/architecture/ade-003-v2-infrastructure-baseline.md:51` |

**A confirmar (não inventado):** nome final da UA-MI; GUIDs dos built-in roles (Portal mostra por nome — GUIDs citados são os típicos); Secret URIs exatos; nomes reais das MIs para os placeholders da DDL; networking do KV; comportamento de seleção de MI no SQL quando há system+user (R-6). Todos delegados ao provisionamento (@devops) / DDL (@data-engineer) / execução manual (owner).

---

**Authority:** Aria (Architect) · Squad AIOX TFTEC — arquitetura de segurança, seleção de tecnologia, padrões de integração. **DDL dos contained users** delegada a **@data-engineer**; **provisionamento MI/RBAC/KV/observabilidade** a **@devops**; **execução manual no Portal** pelo **owner**. Esta ADE **não implementa** — dá a direção e é a base para o @analyst reescrever o guia manual.
**Review cycle:** Imutável durante o EPIC-004. Mudanças → nova ADE que a supersede.

## Change Log

| Date | Author | Description |
|---|---|---|
| 2026-07-05 | @architect (Aria) | **v1.1 — Emenda: varredura por-recurso-REUSADO fecha o gap do inventário (a Fase 1.4 do runbook era orientada a recurso-NOVO).** Ao auditar o `final-portal-guide.md` contra esta ADE, achei chaves em claro nos **recursos reusados** (Function F1, backend v1) que o inventário original — montado sobre os recursos **novos** da Final + o `Gateway__AdminSharedSecret` herdado — **não varreu**. **5 achados:** **H-1** `ServiceBusConnection` (SAS `RootManageSharedAccessKey`) da Function F1 nunca citada → **novo secret** `servicebus-connection-string`; **H-2** `DB_PASSWORD` do backend v1 (a Fase 9 afirmava migração sem passo; o secret ADO.NET `sql-connection-string` **não serve** ao backend Node de campos `DB_*`) → **novo secret** `backend-sql-password` (senha isolada — **formatos distintos ⇒ secrets distintos**); **H-3** `SqlConnectionString` da Function F1 (nomeado na Fase 1.4, sem passo na Fase 9) → **já em D3**, resolvido por subfase de runbook (sem secret novo); **M-1** `AzureWebJobsStorage` → **débito residual** (KV-ref tem ressalva de bootstrap do host Functions; migração cega é risco); **L-1** `APPLICATIONINSIGHTS_CONNECTION_STRING` em claro criada pelo Portal coexiste com a KV-ref da Fase 13.1 → **substituir, não duplicar**. **Decisão:** subfases novas na **Fase 9** migram in-place `SqlConnectionString` + `ServiceBusConnection` (Function F1) e `DB_PASSWORD` (backend v1) para `@Microsoft.KeyVault(SecretUri=…)` pela **UA compartilhada `id-fifa2026-kv-reader` já anexada** (mesmo `keyVaultReferenceIdentity`, **sem novo grant** — role vault-wide). **D3 atualizada** (+2 secrets). **Aditiva** — não reverte D1–D5; preserva retro-compat Oitavas/Quartas DURA, migração in-place um recurso/vez, valor byte-idêntico, gate "Resolved", reversão trivial, blast radius = um serviço. **Débitos residuais nomeados:** `AzureWebJobsStorage` (bootstrap do host) e eliminação total da senha SQL do backend (SQL-MI, showcase Apêndice E). |
| 2026-07-02 | @architect (Aria) · Squad AIOX TFTEC | **ADE-010 criada — MI + Key Vault hands-on Portal, sobre os recursos EXISTENTES, sem downtime.** Refina ADE-009 Inv 2 para execução manual. **D1** modelo de identidade: **UA-MI compartilhada** (`id-fifa2026-kv-reader`) p/ ler o KV (1 grant, sobrevive à recriação manual) + **system-assigned por-app** só p/ SQL (menor-privilégio). **D2** RBAC `Key Vault Secrets User` na MI, escopo do KV `kv-dev-tk-cin-001` (RBAC, não access policy) + `Secrets Officer` no owner. **D3** mapa secret→KV das 6+1 chaves em claro (shared secret unificado nos 2 lados). **D4** forma exata: Container App (secret KV-backed, `secretref:` inalterado) vs App Service/Functions (`@Microsoft.KeyVault` + `keyVaultReferenceIdentity`). **D5** SQL-MI = Fase 2/showcase (código pronto; backend não convertido = débito). **Ordem zero-downtime** in-place (valor idêntico, 1 recurso/vez, gate de validação, reversão trivial) + 6 pontos que derrubam as Quartas. **Observabilidade nível-produção** reusando `appi-dev-tk-cin-001` + `log-dev-tk-cin-001` (~US$0): tracing por `X-Correlation-ID`, Application Map, Workbook da compra, alertas, Kusto — amarrada à segurança (MI-Reader do KV ≙ MI-Reader do Log Analytics). **Reuso total, nada recriado.** Aditiva; preserva ADE-004/007/003/000/008. Verificação na fonte; itens não confirmáveis marcados. |
