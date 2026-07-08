# ADE-009 — Topologia de rede fechada, secrets em cofre e identidade de serviço (gate do EPIC-004 "Nível Produção")

> **Tipo:** Architecture Decision Entry (security hardening + service-identity — endurecimento da malha interna, zero-trust "da porta pra dentro")
> **Status:** ✅ Accepted · **GATE do EPIC-004 "Nível Produção" (Missão Blindar)** · v1.1
> **Date:** 2026-07-01 (v1.0) · **2026-07-03 (v1.1 — emenda Inv 1: exceção route-scoped `X-Diploma-Key`, aprovada pelo owner)**
> **Author:** Aria (Architect) · **Squad AIOX TFTEC**
> **Scope:** **EPIC-004 "Nível Produção"** (o novo épico de hardening da Grande Final — Missão Blindar). Governa a **malha interna** (leste-oeste + data-plane) dos microsserviços .NET do fluxo v2 e do backend v1: `src/Fifa2026.V2.Functions/` (Anonymous hoje), `src/Fifa2026.V2.McpServer/` (trust de header, sem revalidar JWT), `fifa2026-api/` (v1 Node/Express), `src/Fifa2026.V2.Gateway/` (guardião de perímetro — **preservado**), e a infraestrutura em `infra/modules/sql-database.bicep`, `infra/phase-06/flow-events-containerapp.yaml` e os demais Container Apps. Materializa-se em Managed Identity por serviço, Key Vault (KV references), contained users no Azure SQL, RBAC de data-plane, e — no showcase — VNet + CAE VNet-integrado + Private Endpoint no SQL.
> **Supersedes:** N/A (é aditiva — **endurece** o que ADE-003/004/007/008 estabeleceram; não reverte decisão alguma).
> **Related:** **ADE-000** (Inv 2 schema aditivo/idempotente — governa a DDL dos contained users; Inv 4 idempotência; Inv 5 correlationId — **preservada**), **ADE-003** (baseline PaaS + Azure SQL DB + front em App Service, "URL nunca hardcoded" Inv 3 — **preservada e endurecida**), **ADE-004** (gateway YARP = guardião **único** do JWT; Inv 1/5 recursos per-aluno — **preservada; esta ADE é a contraparte interna do perímetro**), **ADE-007** (identidade CIAM dual-issuer; o encadeamento `oid → X-Entra-OID → entra_oid` é o **plano de identidade**, ortogonal ao plano de **confiança de transporte** desta ADE — **preservada**), **ADE-008** (remoção do n8n — a **ADE-002 Inv 4 "tag n8n" já está MOOT**; reafirmada aqui), **`gatewayTrust.js`** (o padrão `X-Gateway-Key` das Quartas — **fonte de reúso da Invariante 1**), **`flow-events-containerapp.yaml`** (o padrão Managed Identity: ACR-pull + Log Analytics Reader via MI — **fonte de reúso da Invariante 2**), **`migration-v1-ciam-design.md`** (@data-engineer — contexto da delegação da DDL dos contained users).
> **Rastreabilidade (Art. IV):** as 4 invariantes derivam do **plano aprovado pelo owner da Grande Final** (`giggly-mapping-hearth.md` §4 "Arquitetura-alvo segura — Missão Blindar", §7 EPIC-004, §9.2 decisão de escopo de segurança, §10 verificação). **Toda afirmação sobre o código/infra foi verificada na fonte da verdade** (arquivos `.bicep`/`.cs`/`.js`/`.yaml` reais — ver §"Verificação na fonte da verdade"). Itens não confirmáveis na fonte estão marcados **"a confirmar"**. **Não invento** valores (nomes de MI, literais de segredo, GUIDs) — a DIREÇÃO é desta ADE; os literais/DDL são delegados (matriz de autoridade).

---

## Context

O owner pediu que a **Grande Final** seja de **nível produção**. A Missão **Blindar** (uma das 4 missões da Final) é o zero-trust: hoje o **perímetro** do sistema é forte e testado (o gateway YARP das Quartas valida JWT dual-issuer, é fail-closed, injeta `X-Correlation-ID`, propaga `X-Entra-OID` e — nas rotas admin — injeta `X-Gateway-Key`), mas a **malha interna** ("da porta pra dentro") ainda é de laboratório. Esta ADE é o **GATE do EPIC-004**: `@pm`/`@sm` **não** draftam o épico/stories de hardening antes dela existir.

**O que "da porta pra dentro" significa hoje (verificado na fonte, não de memória):**

1. **As Functions do fluxo v2 são `AuthorizationLevel.Anonymous`** e **confiam** no header `X-Entra-OID` sem validar o token — a proteção é apenas **obscuridade de URL** ("URL real não exposta", ADE-004 Inv 1/5), **não** isolamento. Verificado: `PurchaseEntryFunction.cs:58` (`AuthorizationLevel.Anonymous`, rota `v2/purchase`; comentário l.18 *"authLevel Anonymous em F1 (segurança entra na F2 com gateway)"*; l.26-31 *"A Function confia neste header (não valida token)"*) e `PurchaseStatusFunction.cs:27` (idem, `v2/purchase/{correlationId}`). **Consequência:** um `curl` que forje `X-Entra-OID` **direto na URL da Function** recebe **200** — é o **P0 do bypass**.

2. **O McpServer confia no `X-Entra-OID` e nunca revalida o JWT**, apoiado em "estar atrás do gateway" (verificado: `McpServer/Program.cs:19-22` — *"O McpServer LÊ o header só para logging/personalização e NUNCA revalida o JWT (gateway é o guardião único)"*). Mas **não há IaC versionado** afirmando `ingress: internal` para o McpServer (só `n8n` e `flow-events` têm yaml; o glob de `infra/phase-05` não retornou nada) — o isolamento do McpServer repousa em **configuração de deploy**, não em contrato versionado.

3. **O SQL está aberto a "todos os serviços Azure".** Verificado: `sql-database.bicep:38` (`publicNetworkAccess: 'Enabled'`) + `:60-67` (regra de firewall `AllowAllAzureServices` com `startIpAddress: '0.0.0.0'` / `endIpAddress: '0.0.0.0'`). Essa regra **não** é "os serviços do lab" — é **qualquer recurso de qualquer tenant Azure**. O próprio bicep já sinaliza o alvo: comentário l.9 *"Para produção real: trocar por Private Endpoint"* e l.58-59 *"Para apertar: trocar por Private Endpoint + VNet integration"*.

4. **Segredos de serviço vivem em connection string.** O SQL é acessado por connection string (SQL auth `adminLogin`/`adminPassword` — `sql-database.bicep:35-36`; e a *"connection string SQL permanece NAS FUNCTIONS"*, `Gateway/Program.cs:49`); o Service Bus por `ServiceBusConnection` (`PurchaseEntryFunction.cs:46`, `ServiceBusOutput(..., Connection = "ServiceBusConnection")`). **Já há um farol do caminho certo:** o `flow-events-containerapp.yaml` faz **ACR-pull via Managed Identity** (`registries[].identity: system`, l.42-44) e **Log Analytics Reader via MI** (nota l.69-71) — só o SignalR ainda é secret cru (l.38-41).

**A oportunidade (o que já está pronto para reusar — não inventar):** as Quartas **já resolveram** o problema de confiança serviço-a-serviço para o **backend v1**: `gatewayTrust.js` valida `X-Gateway-Key` com comparação **timing-safe** (`crypto.timingSafeEqual`, l.28-38) e é **fail-closed/fail-open condicionado ao segredo** (secret vazio = legado v1-only; secret configurado = a trava está armada — l.45-56, comentário l.17-18); e o gateway **já injeta** `X-Gateway-Key` **apenas** no cluster `backend-v1` (`Gateway/Program.cs:118-144`, escopo por `ClusterId`). Esta ADE **generaliza** esse padrão às Functions/McpServer (o P0) e o padrão MI do FlowEvents ao SQL/Service Bus/ACR/Log Analytics.

**Escopo consciente (decisão do owner, §9.2 do plano):** a aula entrega **T1 (US$0) + showcase T2 no SQL**; T3 é **nomeado, não construído**. O scaffolding de rede complexo é **pré-provisionado pelo instrutor** (fora do relógio da aula); o aluno faz os **grants** (RBAC/contained users) + a **demo do bypass** (a "demo-dinheiro"). O clímax da aula (chatbot + visualizer + Diploma) é protegido — a rede é a fundação, não o espetáculo.

---

## Decision

Adotamos o pattern **"Zero-trust da porta pra dentro: confiança serviço-a-serviço provada, identidade gerenciada no data-plane, rede fechada como alvo e app-layer como piso"**, com **4 invariantes**. O eixo é: **o gateway continua sendo o único validador de JWT (ADE-004 preservada); os backends provam que "o gateway me chamou" (Inv 1); nenhum segredo de data-plane vive em config (Inv 2); a rede fechada é o alvo e o `X-Gateway-Key` é o piso onde ela não fechar (Inv 3); e o SQL nunca fica aberto ao cloud inteiro (Inv 4).**

### Invariante 1 — Confiança serviço-a-serviço: `X-Gateway-Key` (timing-safe, fail-closed quando configurado) OU isolamento de rede; o gateway é o único validador de JWT

Todo backend que o gateway proxia — **v1 (Node/Express), Functions F1, McpServer** — passa a **provar que a request veio do gateway**, por **um** de dois mecanismos, sem exceção:

- **(a) Trava de aplicação `X-Gateway-Key`:** o serviço valida o header `X-Gateway-Key` em **comparação de tempo constante** (anti timing-attack), **fail-closed quando o segredo está configurado** (header ausente/divergente → rejeita) e **legado quando o segredo está vazio** (comportamento atual preservado). É a **generalização literal** do `gatewayTrust.js` das Quartas às Functions/McpServer.
- **(b) Isolamento de rede:** o serviço tem `ingress: internal` no CAE + rede fechada (Inv 3), de modo que **nada fora da malha o alcança**.

O **gateway permanece o guardião único do JWT** (ADE-004 Inv 4 **preservada**): os backends **não revalidam** o token — eles apenas provam a origem. O gateway já injeta `X-Gateway-Key` **por escopo de cluster** (`Gateway/Program.cs:118-144`, hoje só `backend-v1`); Inv 1 estende a injeção aos clusters `functions-f1` e `mcp-server` (mesmo transform escopado por `ClusterId`, mesma semântica anti-spoofing: **remover** qualquer `X-Gateway-Key` de origem externa **antes** de injetar o real — `Gateway/Program.cs:130-132`).

**Fecha o P0 (o bypass das Functions):** hoje as Functions são `Anonymous` + trust de `X-Entra-OID` protegidas só por obscuridade de URL. Armado o `X-Gateway-Key`, um `curl` forjando `X-Entra-OID` **direto na Function** → **401**; **via gateway** → **200**. Essa é a **demo-dinheiro** da aula (plano §4/§10).

**Por que o McpServer também, se ele "está atrás do gateway":** porque **não há IaC versionado** garantindo `ingress: internal` para ele (só configuração de deploy) — a proteção não pode repousar em um estado não-versionado. O `X-Gateway-Key` é o **piso enforçável em código**; o isolamento de rede é o **complemento**. Com o n8n removido (ADE-008), some o tráfego leste-oeste `n8n → McpServer` (ADE-006 Inv 7, MOOT), mas o piso de app-layer permanece a defesa que não depende de topologia.

> **Semântica fail-closed/fail-open (idêntica ao `gatewayTrust.js`):** `GATEWAY_SHARED_SECRET` vazio/ausente = trava desligada = comportamento legado (as Functions seguem `Anonymous`, o que **preserva os labs sem gateway** — Oitavas/F1 não têm gateway e continuam funcionando). Segredo **configurado** = trava **armada** = produção fail-closed. Esse gating por config é o que dá **compatibilidade retroativa** (F1 intacto) **e** produção blindada, sem um branch de código por ambiente.

#### Emenda v1.1 (2026-07-03) — exceção **route-scoped** `X-Diploma-Key` para `FlowEvents /api/flow/diploma-summary` (aprovada pelo owner)

> **Contexto:** a Story 4.6 (Diploma vivo) adicionou `GET /api/flow/diploma-summary?userId={int}` no FlowEvents. Como o **cluster** `flow-events` está **fora** do escopo do `X-Gateway-Key` (decisão original desta Inv 1), o endpoint herdou a postura anônima do MEDIUM-1: no FQDN externo do `ca-flow` ele é **anônimo na internet, com `userId` sequencial enumerável** (registrado como **MEDIUM-4** em `docs/security/final-security-debt.md`). O owner **REJEITOU aceitar o débito** (2026-07-03) e pediu **proteção real** (não registro). Design escolhido e aprovado = **Opção F: defesa em profundidade** (Bearer no gateway + prova de proveniência no FlowEvents).

**A emenda (estreita e explícita):** o gateway passa a injetar um header **`X-Diploma-Key`** — reusando o **mesmo valor** do `Gateway:AdminSharedSecret` (`GATEWAY_SHARED_SECRET`), sob um header **distinto** — **apenas na rota** `diploma-summary` (escopo por **`RouteId`**, espelhando o padrão route-scoped do `me-get`/Story 3.5, **não** por `ClusterId`). O FlowEvents valida o `X-Diploma-Key` em **comparação timing-safe** (`CryptographicOperations.FixedTimeEquals`) **somente** nesse endpoint, com a **mesma semântica fail-closed/legado** da Inv 1(a): segredo (`DiplomaSharedSecret`) configurado no `ca-flow` → header ausente/divergente → **401**; segredo vazio → **legado/anônimo** (preserva dev local e o estado pré-provisionamento). Combinado com o blanket `RequireAuthorization()` do gateway (o cliente `/diploma` passa a mandar **Bearer**), fechar o buraco exige **as duas provas simultâneas**: *identidade* (Bearer válido no gateway, barra o anônimo-via-gateway) **e** *proveniência* (`X-Diploma-Key` injetado, barra o FQDN direto do `ca-flow`).

**O que a emenda NÃO altera (o invariante de cluster permanece):**
- O **cluster** `flow-events` **continua fora** do `X-Gateway-Key` cluster-scoped — o teste **`FlowEvents_Cluster_Does_NOT_Receive_GatewayKey` segue verde** (o header injetado é `X-Diploma-Key`, mecanismo e nome distintos; a injeção é por rota, não por cluster).
- `/api/flow/recent`, `/{correlationId}`, `/{id}/replay` e o Hub **SignalR** permanecem **intocados** (seguem como MEDIUM-1 aceito) — **ingress do `ca-flow` fica `external`** (não há flip de rede; blast-radius zero no F6 "Done").
- A Inv 2 (irredutíveis no KV) governa o `X-Diploma-Key`: é o **mesmo segredo simétrico** do `GATEWAY_SHARED_SECRET` (não um novo) → já vive no Key Vault e é lido por KV reference via a MI `id-fifa2026-kv-reader` que o `ca-flow` já possui.

**Rationale (por que route-scoped e não estender o cluster ao `X-Gateway-Key`):** estender o cluster `flow-events` ao `X-Gateway-Key` (Opção B) ou fechar o ingress (Opção C) teria estourado toda a superfície F6 (recent/timeline/SignalR passariam a exigir key+auth, reescrevendo cliente "Done" + SignalR `accessTokenFactory`) — retro-compat inaceitável para um endpoint de leitura zero-PII. A exceção **route-scoped** fecha exatamente o `diploma-summary` reusando o segredo e o padrão já ensinados (Story 4.2 + Fase 9 MI+KV), a **US$0** e com atrito didático mínimo (uma App Setting a mais no `ca-flow`).

**Residual aceito (documentado, não escondido):** um aluno **autenticado** ainda pode passar `?userId=<outro>` via gateway (Bearer válido → o gateway injeta o key) e ler GUIDs opacos + count de outro aluno — **zero PII**, igual-ou-menor que o `/recent` (MEDIUM-1). O escopo infalsificável-por-identidade (aluno A nunca lê aluno B) = **Opção D** (gateway injeta `X-Entra-OID`; FlowEvents resolve `oid→userId`), **condicionada a verificar** se a telemetria já carrega o `oid` (hoje `customDimensions.UserId` é o int v1 — **não assumir**, Art. IV) — fica como **fast-follow**, fora desta emenda.

**Execução:** materializada na Story 4.6 §"Emenda MEDIUM-4" (ACs `AC-M4-1..7`), executor @dev. Delegação da Inv 2 e do provisionamento (MI/KV/App Setting no `ca-flow`) segue a matriz de autoridade (@devops); a DIREÇÃO é desta emenda.

### Invariante 2 — Zero segredo de serviço em config: data-plane via Managed Identity + Entra ID; irredutíveis no Key Vault; RBAC menor-privilégio por serviço

O **data-plane** autentica por **Managed Identity + Entra ID**, **não** por connection string/chave:

| Recurso | Hoje (verificado) | Alvo (Inv 2) | Papel RBAC (menor-privilégio) |
|---|---|---|---|
| **Azure SQL** | connection string SQL auth (`adminLogin`/`adminPassword`; conn string nas Functions) | **Contained users** (`CREATE USER [<mi>] FROM EXTERNAL PROVIDER`) + token AAD (`Authentication=Active Directory Managed Identity`) | Consumer (grava `purchases`) = `db_datawriter`+`db_datareader`; **McpServer read-only = SÓ `db_datareader`** (ADE-008 Inv 1 regra de ouro) |
| **Service Bus** | connection string `ServiceBusConnection` | **MI + roles de data-plane** | Entry (produtor) = **Azure Service Bus Data Sender**; Consumer = **Data Receiver** |
| **ACR** | (já MI no FlowEvents) | **MI pull** (`identity: system`) | `AcrPull` |
| **Log Analytics** | (já MI no FlowEvents) | **MI** (visualizer consulta Kusto) | **Log Analytics Reader** / **Monitoring Reader** |

Os **irredutíveis** — segredos sem data-plane gerenciado por Entra: as **chaves de LLM** (Gemini/Groq/Mistral não federam com Entra) e o **`GATEWAY_SHARED_SECRET`** (segredo simétrico por natureza, Inv 1) — vivem no **Key Vault** e são lidos por **KV reference resolvida pela MI do serviço**: **nunca** em App Setting texto puro, **nunca** no repo, **nunca** em log.

**Menor-privilégio por serviço é invariante, não sugestão:** cada MI recebe **só** os papéis de que precisa. O McpServer é read-only por decisão de arquitetura (ADE-008 Inv 1) → seu contained user é **`db_datareader` e nada mais** (a regra de ouro "o chatbot nunca escreve" vira uma **garantia de RBAC**, não só de código). O FlowEvents já é o farol (ACR-pull + Log Analytics Reader via MI hoje; o SignalR conn string vira candidato a KV reference).

> **Delegação (matriz de autoridade):** a **DDL dos contained users** (o `CREATE USER ... FROM EXTERNAL PROVIDER`, os `ALTER ROLE ... ADD MEMBER`, o `db_datareader`-only do McpServer) é **delegada a @data-engineer** — aditiva/idempotente (ADE-000 Inv 2), padrão dos scripts de migração (`migration-v1-ciam-design.md`). A **DIREÇÃO** (MI no data-plane; KV só para irredutíveis; menor-privilégio; McpServer só-leitura) é desta ADE; os **nomes exatos** das MIs e o mapa fino de grants **não são inventados aqui**.

### Invariante 3 — Rede fechada é o alvo, app-layer é o piso; o CAE é imutável → VNet+CAE nascem primeiro, o FQDN do gateway é planejado ANTES de `VITE_*`/CORS

O **alvo de produção** é uma rede fechada: **VNet + CAE VNet-integrado + Private Endpoints** (SQL com `publicNetworkAccess: Disabled`) + **DNS privado**. Onde a rede é fechada, o tráfego serviço-a-serviço **não** trafega pela internet pública.

**Restrição operacional (aprendida e verificada nas Quartas — é um sequenciamento duro do épico):** o **CAE (Container Apps Environment) é IMUTÁVEL** — a VNet dele **não muda** após criado. Portanto:

- **VNet + CAE nascem PRIMEIRO**, antes de qualquer app.
- O **FQDN do gateway é planejado ANTES** de setar `VITE_*` (build do frontend) e `CORS` — porque **recriar o CAE muda o FQDN do gateway** → força **rebuild do frontend** + atualização de CORS. (Nas Quartas, recriar a VNet do env em PRD mudou o FQDN e exigiu atualizar `GATEWAY_V2_URL` + rebuild; a private DNS zone `privatelink.azurewebsites.net` linkada à VNet manteve o `BackendV1Url` estável.)

**Onde a rede NÃO fechar** (labs T1-only, turmas com restrição de custo/tempo), a **trava `X-Gateway-Key` (Inv 1) é o PISO** — a confiança de app-layer **substitui** o isolamento de rede. Rede-fechada é o cinto; `X-Gateway-Key` é o suspensório; **no mínimo se usa o suspensório**.

### Invariante 4 — SQL nunca aberto a "todos os serviços Azure"

A regra `AllowAllAzureServices` (`0.0.0.0`–`0.0.0.0`, `sql-database.bicep:60-67`) é **removida**. Ela **não** é "os serviços do lab" — é **qualquer recurso de qualquer tenant Azure** (exposição inter-tenant, não intra-lab). O acesso ao SQL passa a ser:

- **T2/prod:** via **Private Endpoint** com `publicNetworkAccess: Disabled` (`sql-database.bicep:38` deixa de ser `'Enabled'`).
- **T1:** via **MI + AAD** com `publicNetworkAccess` **restrito** (não wildcard) — o SQL é alcançado por **identidades nomeadas com menor-privilégio** (Inv 2), não por um range de IP que abarca o cloud inteiro.

O bicep **já aponta** esse alvo (comentário l.9 e l.58-59) — esta ADE **promove o comentário a decisão**. Remover o wildcard é **defense-in-depth independente da força da senha**: mesmo com senha forte, `0.0.0.0/0` de todos os tenants é superfície inaceitável.

---

## Priorização T1 / T2 / T3 (o "núcleo em US$0" e a nota de custo)

> Decisão do owner (plano §4/§9.2): **a aula entrega T1 + showcase T2 no SQL; T3 é nomeado, não construído.** As invariantes acima são **agnósticas de tier** — o que muda por tier é **quanto da rede** se fecha; o **piso (Inv 1/2/4) é sempre T1**.

| Tier | Escopo | Mecanismo | Custo | Mapeia para |
|---|---|---|---|---|
| **T1 — MUST (o núcleo)** | Managed Identity por serviço + Key Vault (KV refs) + **SQL/Service Bus via MI-AAD** (zero connection string) + **`X-Gateway-Key` nas Functions/McpServer** (fim do bypass) + **remover `AllowAllAzureServices`** | Inv 1 (a) + Inv 2 + Inv 4 (arm T1) | **US$0** | O piso zero-trust — vale em qualquer turma |
| **T2 — SHOWCASE** | **SQL Private Endpoint** + **VNet** + **CAE VNet-integrado** (só em torno do SQL) | Inv 3 (parcial) + Inv 4 (arm PE) | **~US$7/mês** (derrubar no mesmo dia) | A "revelação" de rede fechada ao vivo |
| **T3 — ASPIRACIONAL** | Private Endpoint p/ **Key Vault + Service Bus**, **NAT Gateway** (egress IP estável), **Function Private Endpoint** | Inv 3 (pleno) | **nomear, não construir** | Roteiro de "como iria a produção real" |

**Nota de custo (Art. IV — números aproximados, "a confirmar" no provisionamento):** Managed Identity, RBAC, Key Vault (operações em escala de lab) e a remoção do wildcard são **US$0**. O **Private Endpoint** custa da ordem de **~US$7/mês** (cobrança por hora do endpoint + processamento de dados) — por isso o T2 é **showcase**: sobe para a aula e **derruba no mesmo dia**. O T3 multiplica PEs/NAT — daí ser **nomeado, não construído** no lab.

---

## O que NÃO muda (Art. IV — o hardening é aditivo, não um reset)

- **ADE-004 (gateway YARP guardião único do JWT):** **preservada.** Esta ADE é a **contraparte interna** do perímetro — o gateway segue validando o JWT dual-issuer; os backends **não** passam a validar token (só provam origem). Inv 1/5 (recursos per-aluno) intactas.
- **ADE-007 (identidade CIAM dual-issuer):** **preservada e ortogonal.** O encadeamento `oid → X-Entra-OID → entra_oid` é o **plano de identidade**; o `X-Gateway-Key` é o **plano de confiança de transporte**. São eixos distintos: `X-Entra-OID` diz *"quem é o usuário"*; `X-Gateway-Key` diz *"quem me chamou foi o gateway"*. Nenhum toca o outro.
- **ADE-000 (invariantes foundational):** **preservada.** A DDL dos contained users (Inv 2) respeita **Inv 2** (só aditivo/idempotente); a idempotência do consumer (**Inv 4**) e o `correlationId` (**Inv 5**) são intocados — trocar connection string por MI **não** altera a lógica de INSERT/idempotência.
- **ADE-003 (baseline PaaS):** **preservada e endurecida** — o "URL nunca hardcoded" (Inv 3) casa com o planejamento do FQDN do gateway (Inv 3 desta ADE).
- **ADE-008 (remoção do n8n) e ADE-002:** a **ADE-002 Inv 4 ("tag n8n")** já está **MOOT** (ADE-008) — **reafirmado** aqui (sem n8n, não há tag/segredo/tráfego leste-oeste a proteger). Os pins .NET (ADE-002 Inv 1/5) seguem.

---

## Rationale

### Por que `X-Gateway-Key` (e não Private Endpoint na Function) para fechar o bypass?
- **Reúso comprovado + US$0 + inspecionável.** As Quartas já provaram o `gatewayTrust.js` (timing-safe, fail-closed condicionado ao segredo). Estender o mesmo transform escopado por cluster é **configuração**, não reescrita. Function Private Endpoint + lockdown de VNet é **T3** (custo + cerimônia + tempo de provisionamento que não cabe na aula) — e, pedagogicamente, uma trava **em código** ("por que o `curl` direto dá 401") ensina mais que uma caixa-preta de rede.
- **Compatibilidade retroativa.** O gating por segredo vazio preserva os labs sem gateway (Oitavas/F1 seguem `Anonymous`). Um Private Endpoint quebraria o F1 gateway-less.

### Por que MI+AAD (e não connection string no Key Vault)?
- **KV reference esconde o segredo; MI+AAD elimina o segredo.** Guardar a connection string no KV é **melhor que texto puro**, mas o segredo **ainda existe** (rotação, blast radius, "há uma string que vaza se vazar"). MI+AAD **remove** o segredo do data-plane — não há string. Por isso o KV fica **reservado aos irredutíveis** (chaves de LLM, `GATEWAY_SHARED_SECRET`), onde não há data-plane gerenciado por Entra.

### Por que menor-privilégio por serviço (McpServer só `db_datareader`)?
- A regra de ouro do chatbot (ADE-008 Inv 1, "nunca escreve") deixa de ser só uma propriedade do **código** e vira uma **garantia do banco**: mesmo que o código regredisse, o contained user read-only **não consegue** escrever. Defense-in-depth: duas travas independentes (código + RBAC).

### Por que rede fechada é "alvo" e app-layer é "piso" (e não o inverso)?
- Rede fechada é a defesa **mais forte** (nada alcança o serviço), mas tem **custo e imutabilidade** (o CAE não muda de VNet). O `X-Gateway-Key` é **US$0, versionado e portátil** entre turmas. A postura honesta é: **almeje** a rede fechada (T2/T3), mas **garanta** o piso de app-layer (T1) que vale em toda turma.

---

## Consequences

### Positivas
- ✅ **O P0 do bypass fecha:** `curl` forjando identidade direto na Function → 401; via gateway → 200 (a demo-dinheiro).
- ✅ **Zero connection string no data-plane:** SQL/Service Bus/ACR/Log Analytics por MI-AAD; irredutíveis no KV. `grep` de senha/conn-string na config → 0.
- ✅ **Regra de ouro vira garantia de RBAC:** McpServer só `db_datareader` — a leitura-apenas é imposta pelo banco, não só pelo código.
- ✅ **US$0 no núcleo (T1):** MI/KV/RBAC/remoção do wildcard não custam; PE (T2) é showcase de ~US$7/mês derrubado no dia.
- ✅ **Perímetro preservado:** ADE-004/007 intactas; o gateway segue guardião único do JWT.
- ✅ **Compatibilidade retroativa:** o gating por segredo vazio mantém os labs sem gateway (Oitavas/F1) funcionando.
- ✅ **Farol já existe:** o padrão MI do FlowEvents e o `gatewayTrust.js` das Quartas são reúso, não invenção.

### Negativas / Trade-offs aceitos
- ⚠️ **CAE imutável impõe ordem dura:** VNet+CAE primeiro, FQDN antes de `VITE_*`/CORS. Errar a ordem custa rebuild do frontend. Mitigado: sequenciamento explícito no runbook + pré-provisionamento pelo instrutor.
- ⚠️ **MI+AAD no SQL tem cerimônia:** contained users, `Authentication=Active Directory Managed Identity` na connection, propagação de RBAC (que pode levar minutos). Mitigado: DDL delegada a @data-engineer (idempotente); o instrutor pré-provisiona.
- ⚠️ **`X-Gateway-Key` é segredo simétrico compartilhado** (não é MI). Aceito: é **irredutível** (prova de origem gateway↔backend) → vai para o KV (Inv 2). Blast radius contido: se vazar, rota-se; não dá acesso a dados por si só (ainda exige alcançar a rede).
- ⚠️ **PE custa e é efêmero no lab (T2):** ~US$7/mês → derrubar no dia. Aceito como showcase.
- ⚠️ **Código legado a ajustar** (Functions saem de `Anonymous`-sempre para `Anonymous`-ou-trava; wiring de `DefaultAzureCredential`/KV refs nos `Program.cs`). Trabalho de @dev, apontado aqui — não implementado.
- ⚠️ **Consolidação P0/P1 conexa (não são invariantes desta ADE, mas do mesmo épico):** o `XCacheMiddleware` cacheia **antes** do auth (verificado: pipeline `Gateway/Program.cs:419-424` — cache é passo 3, auth é passo 4 → um HIT serve status sem token por 30s), `gatewayTrust.js` sem testes, guards de startup sem teste, rate-limit sem `UseForwardedHeaders`. Ficam no **escopo do EPIC-004** (Consolidação), roteados a @sm/@dev/@qa — **não** viram invariante aqui (esta ADE é rede/secrets/identidade-de-serviço).

---

## Alternatives Considered (rejeitadas)

### Alt 1 — Function Private Endpoint / lockdown total de rede em vez de `X-Gateway-Key` (T1)
- **Rejeitada como mecanismo T1** porque é **T3** (custo + cerimônia + tempo de provisionamento fora do orçamento da aula) e quebraria os labs sem gateway (F1 `Anonymous`). O `X-Gateway-Key` reusa um padrão provado (Quartas), custa US$0, é inspecionável em código e é compatível com retro. **PE fica nomeado no T3**, não construído.

### Alt 2 — Connection strings no Key Vault (KV reference) em vez de MI+AAD
- **Rejeitada** porque **esconde** o segredo sem **eliminá-lo** — rotação, blast radius e "a string ainda existe" permanecem. MI+AAD remove o segredo do data-plane. KV fica **reservado aos irredutíveis** (LLM keys, `GATEWAY_SHARED_SECRET`). *(KV reference > texto puro; MI-AAD > KV reference quando há data-plane gerenciado.)*

### Alt 3 — Managed Identity para TUDO, inclusive LLM keys e `GATEWAY_SHARED_SECRET`
- **Rejeitada por impossibilidade:** provedores de LLM 3rd-party (Gemini/Groq/Mistral) **não federam** com Entra; o `GATEWAY_SHARED_SECRET` é **simétrico por natureza** (prova de origem). São **irredutíveis** → o lar correto é o **Key Vault**, não a MI. Fingir o contrário seria invenção.

### Alt 4 — Manter `AllowAllAzureServices` confiando na força da senha SQL
- **Rejeitada:** `0.0.0.0`–`0.0.0.0` **não** é "os serviços do lab", é **todo tenant Azure**. Defense-in-depth exige remover o wildcard **independente** da força da senha. (E com MI-AAD, nem há senha.)

### Alt 5 — Construir o T3 pleno (VNet em tudo, todos os PEs, NAT) como a aula
- **Rejeitada** pelo owner (§9.2/§9.4): custo + tempo de provisionamento + **roubaria o clímax** (os últimos 60-90 min são chatbot+visualizer+Diploma). O scaffolding de rede é **pré-provisionado pelo instrutor**; o aluno faz os **grants** + a **demo do bypass**. T3 é roteiro, não hands-on.

---

## Validation

Esta decisão é considerada **validada** quando (checklist manual/observado para @dev/@data-engineer/@devops no EPIC-004 — **não implemento; aponto**):

- [ ] **Bypass fechado:** `curl` forjando `X-Entra-OID` **direto na Function** → **401**; a **mesma** request via gateway → **200** (Inv 1).
- [ ] **`X-Gateway-Key` armado nas Functions e McpServer** (timing-safe, fail-closed com segredo configurado; legado com segredo vazio) — generalização do `gatewayTrust.js` (Inv 1).
- [ ] **Zero connection string no data-plane:** `grep` de senha/conn-string na config dos serviços = **0**; SQL/Service Bus por **MI-AAD** (Inv 2).
- [ ] **Menor-privilégio confirmado:** contained user do **McpServer = só `db_datareader`** (nenhuma escrita possível); Consumer com writer; Entry = **Service Bus Data Sender**, Consumer = **Data Receiver** (Inv 2).
- [ ] **Irredutíveis no KV:** chaves de LLM e `GATEWAY_SHARED_SECRET` lidos por **KV reference resolvida pela MI** — nunca em App Setting texto/repo/log (Inv 2).
- [ ] **SQL fechado:** `AllowAllAzureServices` **removido**; `publicNetworkAccess: Disabled` (T2/PE) ou `publicNetworkAccess` restrito + MI-AAD (T1) (Inv 4).
- [ ] **Ordem de rede respeitada (se T2):** VNet+CAE criados **antes** dos apps; FQDN do gateway estável **antes** de `VITE_*`/CORS; SQL alcançado por **Private Endpoint** + DNS privado (Inv 3).
- [ ] **ADEs preservadas:** gateway segue único validador de JWT (ADE-004); `oid → X-Entra-OID → entra_oid` intacto (ADE-007); DDL dos contained users aditiva/idempotente (ADE-000 Inv 2); ADE-002 Inv 4 anotada MOOT.

---

## Impact on EPIC-004 (Nível Produção — Missão Blindar)

> **NÃO edito código nem stories** (autoridade de @dev/@data-engineer/@sm/@po). Esta ADE **aponta** o impacto e é o **GATE** do épico. A **DDL dos contained users** é **delegada a @data-engineer**.

| Story / Item (a draftar pelo @sm) | Impacto | Executor |
|---|---|---|
| **`X-Gateway-Key` nas Functions/McpServer** | Estende o transform escopado por cluster (`functions-f1`, `mcp-server`) + validação timing-safe no destino (generaliza `gatewayTrust.js`). **P0 — fecha o bypass.** | @dev (código gateway + Functions + McpServer) |
| **MI + KV wiring** | `DefaultAzureCredential` nos `Program.cs`; KV references para LLM keys + `GATEWAY_SHARED_SECRET`; MI para SQL/Service Bus/ACR/Log Analytics. | @dev (código) + @devops (infra/MI/RBAC assignment) |
| **Contained users no SQL** | `CREATE USER ... FROM EXTERNAL PROVIDER` + grants menor-privilégio (**McpServer = `db_datareader`-only**); aditivo/idempotente (ADE-000 Inv 2). | **@data-engineer (DDL) — delegado** |
| **Remover `AllowAllAzureServices` + `publicNetworkAccess`** | `sql-database.bicep` (linhas 38, 60-67); PE no T2. | @devops/@dev (bicep) |
| **VNet + CAE VNet-integrado + PE (T2 showcase)** | Pré-provisionado pelo instrutor; ordem dura (CAE imutável → FQDN antes de `VITE_*`/CORS). | @devops (infra) |
| **Consolidação P0/P1 (mesmo épico, fora das invariantes desta ADE)** | Cache-antes-do-auth (`XCacheMiddleware`), testes de idempotência SQL, `gatewayTrust` sem testes, startup guards sem teste, `UseForwardedHeaders`. | @sm drafta; @dev/@qa |
| **ADE-002 Inv 4 (tag n8n)** | **MOOT** (já por ADE-008) — reafirmado; registro em sessão de doc-fix. | @architect (registro) |

---

## Verificação na fonte da verdade (Art. IV)

Confirmado no código/infra real (não afirmado de memória):

| Afirmação | Verificado em |
|---|---|
| Functions v2 são `AuthorizationLevel.Anonymous` e confiam em `X-Entra-OID` sem validar token (obscuridade de URL, não isolamento) | `src/Fifa2026.V2.Functions/Functions/PurchaseEntryFunction.cs` (l.58, comentário l.18 e l.26-31), `PurchaseStatusFunction.cs` (l.27) |
| McpServer confia em `X-Entra-OID` só p/ logging e **nunca revalida JWT**; sem IaC versionado de `ingress: internal` (só n8n/flow-events têm yaml) | `src/Fifa2026.V2.McpServer/Program.cs` (l.19-22, l.30-39); glob `infra/**/*.yaml` = só `phase-04/n8n` e `phase-06/flow-events` |
| `X-Gateway-Key` timing-safe, fail-closed condicionado ao segredo (vazio = legado v1) | `fifa2026-api/src/middleware/gatewayTrust.js` (l.28-38 `crypto.timingSafeEqual`; l.45-56; comentário l.17-18) |
| Gateway injeta `X-Gateway-Key` **só** no cluster `backend-v1` (transform escopado por `ClusterId`, anti-spoofing remove header externo) | `src/Fifa2026.V2.Gateway/Program.cs` (l.118-144, esp. l.123-132) |
| Transform `X-Entra-OID` é **global** (todos os clusters); pipeline tem cache **antes** do auth | `src/Fifa2026.V2.Gateway/Program.cs` (l.94-116 global; l.419-424 cache=passo 3, auth=passo 4) |
| SQL: `publicNetworkAccess: 'Enabled'` + firewall `AllowAllAzureServices` `0.0.0.0`–`0.0.0.0`; comentários já apontam PE | `infra/modules/sql-database.bicep` (l.38, l.60-67, comentários l.9 e l.58-59) |
| SQL/Service Bus por connection string hoje (conn string SQL "permanece nas Functions"; `ServiceBusConnection`) | `src/Fifa2026.V2.Gateway/Program.cs` (l.49), `PurchaseEntryFunction.cs` (l.46) |
| Padrão MI já existe: ACR-pull via `identity: system` + Log Analytics Reader via MI (SignalR ainda secret cru) | `infra/phase-06/flow-events-containerapp.yaml` (l.24-25, l.42-44, l.69-71; secret l.38-41) |
| McpServer é read-only (`FifaQueryRepository` só `SELECT`) → sustenta `db_datareader`-only | `src/Fifa2026.V2.McpServer/Data/FifaQueryRepository.cs` (verificado na ADE-008 §Verificação) |

**A confirmar (não inventado):** nomes exatos das Managed Identities; mapa fino de RBAC por serviço; literal/rotação do `GATEWAY_SHARED_SECRET`; custo exato do PE por região; disponibilidade de VNet-integration no plano do CAE da turma. Todos delegados ao provisionamento (@devops) / DDL (@data-engineer).

---

**Authority:** Aria (Architect) · Squad AIOX TFTEC — designada por @aiox-master para arquitetura de segurança, seleção de tecnologia e padrões de integração. **Detalhe de dados (DDL dos contained users, grants menor-privilégio, `db_datareader`-only do McpServer) delegado a @data-engineer** (matriz de autoridade). Provisionamento de MI/RBAC/rede/KV = @devops.
**Review cycle:** Imutável durante o EPIC-004. Mudanças → nova ADE que a supersede.

## Change Log

| Date | Author | Description |
|---|---|---|
| 2026-07-03 | @architect (Aria) · Squad AIOX TFTEC | **v1.1 — Emenda à Invariante 1: exceção route-scoped `X-Diploma-Key` (aprovada pelo owner).** O owner **rejeitou aceitar o débito MEDIUM-4** (`FlowEvents /api/flow/diploma-summary` anônimo/enumerável no ingress externo — Story 4.6) e pediu proteção real. Design aprovado = **Opção F (defesa em profundidade)**: o gateway injeta `X-Diploma-Key` (reúso do valor `Gateway:AdminSharedSecret`, header distinto) **apenas na rota** `diploma-summary` (escopo por `RouteId`, padrão do `me-get` — **não** por `ClusterId`); o FlowEvents valida timing-safe (`FixedTimeEquals`) **só** nesse endpoint, mesma semântica fail-closed/legado da Inv 1(a); o cliente `/diploma` passa a mandar **Bearer** (blanket auth). Fechar exige **as duas provas**: identidade (Bearer, barra anônimo-via-gateway) + proveniência (`X-Diploma-Key`, barra o FQDN direto). **Invariante de cluster preservada:** o cluster `flow-events` continua **fora** do `X-Gateway-Key` — teste `FlowEvents_Cluster_Does_NOT_Receive_GatewayKey` **segue verde** (header/mecanismo distintos). `/recent`/timeline/SignalR e o **ingress `external`** do `ca-flow` **intocados** (MEDIUM-1 segue aceito; blast-radius zero no F6). Residual aceito: enumeração **autenticada** intra-lab (zero-PII); upgrade infalsificável-por-identidade = Opção D (`X-Entra-OID` + `oid→userId`), condicionada a verificar se a telemetria carrega o `oid` (fast-follow). Materializada na Story 4.6 §"Emenda MEDIUM-4" (`AC-M4-1..7`). |
| 2026-07-01 | @architect (Aria) · Squad AIOX TFTEC | **ADE-009 criada — GATE do EPIC-004 "Nível Produção" (Missão Blindar).** 4 invariantes de zero-trust "da porta pra dentro": (1) **confiança serviço-a-serviço** — v1/Functions/McpServer provam origem via `X-Gateway-Key` timing-safe (fail-closed quando configurado, legado quando vazio) **OU** isolamento de rede; gateway segue **único validador de JWT** (ADE-004 preservada); **generaliza o `gatewayTrust.js` das Quartas às Functions/McpServer — fecha o P0 do bypass** (`curl` forjando `X-Entra-OID` na Function → 401). (2) **Zero segredo de serviço em config** — data-plane por **MI+Entra** (SQL contained users; Service Bus Sender/Receiver; ACR pull; Log Analytics Reader); irredutíveis (LLM keys, `GATEWAY_SHARED_SECRET`) no **Key Vault** via KV reference lida pela MI; **menor-privilégio por serviço** (**McpServer = `db_datareader`-only** — a regra de ouro vira garantia de RBAC). (3) **Rede fechada é o alvo, app-layer é o piso** — VNet + CAE VNet-integrado + PE (SQL `Disabled`) + DNS privado; **CAE imutável → VNet+CAE primeiro, FQDN antes de `VITE_*`/CORS**; onde a rede não fechar, `X-Gateway-Key` é o piso. (4) **SQL nunca aberto a "todos os serviços Azure"** — remove `AllowAllAzureServices` (`0.0.0.0/0` inter-tenant); acesso por PE (prod) ou MI-AAD com `publicNetworkAccess` restrito. Inclui **priorização T1 (US$0) / T2 (SQL PE ~US$7/mês, derrubar no dia) / T3 (nomear, não construir)** + nota de custo. **Aditiva:** preserva ADE-004/007/000/003; reafirma **ADE-002 Inv 4 (tag n8n) MOOT** (já por ADE-008). **Delegado:** DDL dos contained users → @data-engineer; provisionamento MI/RBAC/rede/KV → @devops. Consolidação P0/P1 (cache-antes-do-auth, testes de idempotência/gatewayTrust/startup guards, `UseForwardedHeaders`) roteada ao EPIC-004 — **não** vira invariante aqui. Todas as afirmações de código/infra verificadas na fonte da verdade (§Verificação); itens não confirmáveis marcados "a confirmar". |
