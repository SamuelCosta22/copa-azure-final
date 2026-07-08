# Runbook do Instrutor — Pré-provisionamento da Grande Final (browser-harness, offline, uma vez)

> **Para quem:** o **instrutor/facilitador** — não é material de aluno. Você roda isto **UMA vez, sozinho, ANTES da aula**.
> **Por quê o browser-harness:** a skill `browser-harness` (já instalada no ambiente do squad) dirige o browser por CDP e é a ferramenta certa para as telas **só-Portal** do Azure. Mas ela **para em auth walls** (login interativo/MFA do Portal) — por isso **não escala em CI** e é, deliberadamente, **ferramenta de instrutor, executada offline**. Você autentica uma vez à mão; ela automatiza a navegação repetitiva a partir daí. Onde o `az` CLI resolve headless, prefira o `az`; reserve o browser-harness para o que só existe no Portal.
> **O que este runbook NÃO é:** um clique-a-clique de aluno. É um runbook **narrativo** — o **porquê de cada ordem**, o **que quebra se você inverter**, e onde a topologia é imutável. O guia clique-a-clique da aula é o [`final-portal-guide.md`](./final-portal-guide.md); aqui é a **fundação** que ele assume pronta.
>
> **Story:** [4.5 — AC-13 / Task 5](../stories/4.5.story.md) · **Fontes (Art. IV):** [ADE-009 (rede/segredos/identidade) — Invariante 3, ordem de rede](../architecture/ade-009-network-secrets-service-identity.md) · [ADE-010 (Managed Identity + Key Vault)](../architecture/ade-010-managed-identity-keyvault.md) · plano `giggly-mapping-hearth.md` §9.4 ("proteger o clímax") · aprendizado das Quartas sobre o **FQDN** (recriar o CAE muda o FQDN do gateway).
> **`[confirmar no Portal]`** (delegado ao provisionamento — não invento): nomes exatos das Managed Identities; disponibilidade de **VNet-integration** no plano do CAE da turma; custo exato do Private Endpoint por região; literal/rotação do `GATEWAY_SHARED_SECRET`. Onde o ADE marca "a confirmar", este runbook mantém o marcador.

---

## Por que este runbook existe — "proteger o clímax"

O clímax da Grande Final são **dois momentos ao vivo**: a regra de ouro (o chatbot que não consegue escrever) e o smoke dos 5 nós (a compra acendendo o visualizador). Nenhum deles pode competir, no relógio da aula, com **provisionar rede fechada, cofre e identidades gerenciadas** — que envolvem esperas de propagação de RBAC (minutos), imutabilidade de recursos e telas de Portal que travam em auth.

A decisão de arquitetura (plano §9.4) é **tirar a topologia complexa do relógio da aula**: o **instrutor pré-provisiona** a topologia **T2** (rede fechada showcase) + semeia os segredos no cofre **antes**, offline. Na aula, o aluno só faz **deploy de código** sobre infra que já existe (o `lab-grande-final.yml` faz **zero `az create`**; ele valida com `acao=check` e publica com `acao=tudo`). É isto que você constrói aqui.

> **T2 é showcase, custa dinheiro:** o **Private Endpoint** custa da ordem de **~US$7/mês** (`[confirmar no Portal]` por região). Suba para a aula e **derrube no mesmo dia**. O piso zero-trust (T1: Managed Identity + Key Vault + `X-Gateway-Key`) é **US$0** e vale em qualquer turma; a rede fechada (T2) é a "revelação" ao vivo.

---

## O que você vai deixar pronto (T2 + cofre semeado)

| Camada | Recurso | Fonte |
|---|---|---|
| **Rede** | VNet + **CAE VNet-integrado** (imutável) + **Private Endpoint do SQL** (`publicNetworkAccess: Disabled`) + DNS privado | ADE-009 Inv 3 (T2) |
| **Cofre + identidade** | Key Vault `kv-dev-tk-cin-001` **[já existe]** _(HML de validação — na turma: `kv-<sufixo>`; ver §Nomenclatura)_ · **UA-MI compartilhada só-leitura** (`id-fifa2026-kv-reader` `[nome sugerido]`) · **system-assigned por-app** (SQL) | ADE-010 D1/D2 |
| **Segredos semeados** | `gateway-admin-shared-secret` · `gemini-api-key` · `sql-connection-string` · `azure-signalr-connection-string` · `servicebus-connection-string` · `backend-sql-password` (execução única, offline) | ADE-010 D3 (v1.1) |
| **Container Apps (wired ao cofre)** | `ca-mcp-<sufixo>` · `ca-gateway-<sufixo>` · `ca-flow-<sufixo>` **criados** lendo os app-secrets do Key Vault via KV reference + MI (revisão `Running`) — pré-req do Doctor **item 7**; o workflow só troca a imagem (AC-9) | Bloco 3 passo 5 / ADE-010 |
| **Identidade do frontend** | 5 `VITE_*` de identidade (`VITE_CIAM_AUTHORITY`/`CLIENT_ID`, `VITE_ADMIN_TENANT_ID`/`CLIENT_ID`, `VITE_ADMIN_SCOPE`) semeadas no **App Settings** do `app-frontend-<sufixo>` — **não-secreto**, lidas pelo workflow (Bloco 4) | AC-6 / [AUTO-DECISION] |

O que fica para a aula (NÃO faça aqui): **subir as imagens de código** (`az containerapp update --image`) + o **CORS** e as `VITE_*` **que carregam o FQDN do gateway** (`VITE_GATEWAY_V2_URL`/`VITE_LLM_PROXY_URL`/`VITE_FLOW_EVENTS_BASE_URL`) — essas o `lab-grande-final.yml` resolve **in-run** via `az ... --query` (não são fixadas à mão). **O que é pré-provisionamento (você faz aqui):** (a) as **5 `VITE_*` de identidade** (`VITE_CIAM_AUTHORITY`/`VITE_CIAM_CLIENT_ID`/`VITE_ADMIN_TENANT_ID`/`VITE_ADMIN_CLIENT_ID`/`VITE_ADMIN_SCOPE`) — **não** dependem do FQDN e o workflow as **lê** dos App Settings do frontend (AC-6), **Bloco 4**; (b) **o wiring das KV-references dos Container Apps** — os apps (McpServer/gateway/FlowEvents) **nascem** lendo os app-secrets do cofre via Managed Identity, **Bloco 3 passo 5**. Ambos são **pré-requisito do Doctor `acao=check`** (itens 7 e 8) — se ficarem para a aula, o pré-flight dá FALSO FAIL.

---

## Nomenclatura — o ambiente HML de validação vs. a convenção `<prefixo>-<sufixo>` da turma

> **Contrato oficial (AC-4):** na turma real, **todo** recurso é nomeado pela convenção **`<prefixo>-<sufixo>`** — a MESMA que o workflow `lab-grande-final.yml` deriva do único campo `sufixo` do aluno (`jq` → `$GITHUB_ENV`) e que o `README.md` do Template Repo documenta (`docs/entrega/fifa2026-final-lab/README.md`). É essa convenção, e só ela, que faz o "1 campo" funcionar: se você provisionar com nomes fora dela, o `acao=check` (Doctor) e o `acao=tudo` **não encontram** os recursos.

Os nomes **concretos** que aparecem neste runbook (`kv-dev-tk-cin-001`, `id-fifa2026-kv-reader`) são do **ambiente HML de validação do instrutor** — onde este runbook foi verificado nas sessões de preparação. São **exemplos reais**, **não** a nomenclatura da turma. Ao provisionar para os alunos, **substitua** pelos nomes da convenção, parametrizados pelo `sufixo` de cada aluno:

| Recurso | No HML de validação (este runbook) | Na turma (convenção `<prefixo>-<sufixo>`) | Workflow deriva? |
|---|---|---|---|
| Resource Group | `rg-hml-tik-cin-001` | `rg-<sufixo>` | Sim (`RG`) |
| Key Vault | `kv-dev-tk-cin-001` | `kv-<sufixo>` | Sim (`KEYVAULT`) |
| UA-MI de leitura do KV | `id-fifa2026-kv-reader` `[nome sugerido]` | **nome livre** — o workflow **não** deriva a MI; confirme 1 nome por turma (compartilhada) ou por aluno `[confirmar no Portal]` | Não |
| ACR | (instância HML equivalente) | `cr<sufixo>` / `cr<sufixo>.azurecr.io` | Sim (`ACR_NAME`) |
| Container Apps Environment | (instância HML equivalente) | `cae-<sufixo>` | Sim (`CAE_NAME`) |
| Gateway (Container App) | (instância HML equivalente) | `ca-gateway-<sufixo>` | Sim (`GATEWAY_APP`) |
| McpServer (Container App) | (instância HML equivalente) | `ca-mcp-<sufixo>` | Sim (`MCP_APP`) |
| FlowEvents (Container App) | (instância HML equivalente) | `ca-flow-<sufixo>` | Sim (`FLOW_APP`) |
| Frontend (Web App) | (instância HML equivalente) | `app-frontend-<sufixo>` | Sim (`FRONTEND_APP`) |
| Backend v1 (Web App) | (instância HML equivalente) | `app-backend-<sufixo>` | Sim (`BACKEND_APP`) |
| Function F1 | (instância HML equivalente) | `func-f1-<sufixo>` | Sim (`FUNCTION_APP`) |
| SQL Server | (instância HML equivalente) | `sql-<sufixo>` | Sim (`SQL_SERVER`) |

> **Por que a UA-MI é a exceção:** o workflow **nunca** referencia a MI pelo nome — o Doctor (`acao=check`) a valida **indiretamente**, pelo secret com `keyVaultUrl` resolvendo na revisão `Running` do McpServer. Então o nome da MI **não** precisa seguir a convenção; mantém-se `[nome sugerido]`. Todos os **demais** recursos **precisam** seguir `<prefixo>-<sufixo>`, senão o workflow não os acha.

Assim **runbook e workflow contam a MESMA história de nomenclatura**: os nomes HML são o laboratório de validação do instrutor; a convenção `<prefixo>-<sufixo>` é o contrato de entrega da turma.

> ⚠️ **O SQL `sql-<sufixo>` precisa do schema CIAM — a coluna `users.entra_oid` + o índice `UQ_users_entra_oid`** (aplicados pela migration `phase-04-ciam-link.sql` das Quartas), não só as tabelas base. É o que o `GET /api/v2/me` — o JIT CIAM do `MeFunction` ([Story 3.5](../stories/3.5.story.md)) que deixa o cliente **nato-CIAM** fechar a compra v2 — usa para resolver/vincular a linha em `users`. **Cenário greenfield** (aluno que chega à Final sem ter passado pelas Quartas, com um `sql-<sufixo>` novo): sem essa coluna, o `/api/v2/me` falha **em runtime** com **`Invalid column name 'entra_oid'`** (o código sobe pelo `acao=function`, mas a compra do nato-CIAM quebra). Garanta **antes da aula** por uma das vias: **(a)** clonar o DB do HML de validação (que já tem o schema completo), OU **(b)** aplicar `schema.sql` → `phase-01.sql` → `phase-03.sql` → `phase-04-ciam-link.sql` (em `fifa2026-api/database/`) nesse SQL — a mesma cadeia que os testes de integração da Story 4.3 aplicam.

---

## A LEI da ordem — o CAE é IMUTÁVEL (ADE-009 Invariante 3)

Antes de qualquer comando, internalize a única regra que, se violada, **custa um rebuild inteiro do frontend na frente da turma**:

> **O Container Apps Environment (CAE) é IMUTÁVEL — a VNet dele NÃO muda depois de criado.** Portanto a VNet e o CAE **nascem primeiro**, antes de qualquer app. E o **FQDN do gateway** — que sai do CAE — tem de estar **estável ANTES** de você fixar `VITE_*` (build do frontend) e `CORS`.

**O que quebra se você inverter (aprendizado registrado das Quartas):** nas Quartas, **recriar a VNet do env em produção mudou o FQDN do gateway**. Consequência em cadeia: foi preciso **atualizar `GATEWAY_V2_URL`** e **rebuildar o frontend** (o bundle carrega a URL do gateway em build-time via `VITE_*`), além de refazer o **CORS**. Uma peça de rede lá embaixo forçou retrabalho lá em cima. (O que **não** quebrou: a **private DNS zone** `privatelink.azurewebsites.net` linkada à VNet manteve o `BackendV1Url` estável — DNS privado é o que segura os nomes internos.)

A moral: **rede fechada é o alvo mais forte, mas tem custo e imutabilidade.** Você planeja a rede **primeiro** exatamente porque ela é a camada que **não se recria de graça** depois. Tudo o que depende de nome (frontend, CORS) vem **por último**.

---

## A sequência correta (execute nesta ordem — cada bloco é um portão)

### Bloco 1 — VNet + CAE VNet-integrado **PRIMEIRO** (a fundação imutável)

**O que:** crie a **VNet** (com a subnet delegada ao Container Apps) e o **CAE VNet-integrado** em torno dela. Se você já tem o CAE das fases anteriores **sem** VNet-integration, saiba que **não dá para "adicionar" VNet a um CAE existente** — a integração de VNet é definida **no nascimento** do ambiente; migrar significa **recriar**. `[confirmar no Portal]` a disponibilidade de VNet-integration no **plano** do CAE da turma.

**Por que primeiro:** porque é imutável. Todo app (gateway, McpServer, FlowEvents) vai **nascer dentro** deste CAE; o FQDN de cada um deriva do ambiente. Criar a rede depois dos apps = recriar os apps.

**Portão (não avance sem):** o CAE existe, VNet-integrado, e você **anotou o domínio/FQDN base** que o ambiente vai dar aos apps. **Não fixe nada de `VITE_*`/CORS ainda** — o FQDN do gateway só é definitivo quando o gateway existir dentro deste CAE.

**browser-harness:** a criação de VNet/CAE é confortável no **`az` CLI** (headless). Use o browser-harness só se precisar inspecionar visualmente a subnet delegada ou a associação VNet no blade do CAE — e lembre: a **primeira** navegação vai bater no **auth wall** do Portal; autentique à mão, então deixe a skill seguir.

### Bloco 2 — Private Endpoint do SQL (a rede fechada de dados)

**O que:** crie um **Private Endpoint** para o Azure SQL, ligado à VNet do Bloco 1, e deixe o SQL com **`publicNetworkAccess: Disabled`**. Crie/liga a **private DNS zone** apropriada para que o nome do SQL resolva para o IP privado **de dentro** da VNet.

**Por que depende do Bloco 1:** o Private Endpoint **vive dentro da VNet** — sem a VNet/CAE, não há onde ancorá-lo. Com ele, o tráfego serviço→SQL **não passa pela internet pública**: o McpServer (que só lê) e as Functions (que gravam) alcançam o banco por dentro da malha. É a "revelação" de rede fechada que a aula mostra ao vivo.

**Portão:** o SQL responde **de dentro** da VNet (resolução privada OK) e **recusa** de fora (`publicNetworkAccess: Disabled`). Se um app não conectar mais tarde, o suspeito nº1 é **DNS privado não linkado** à VNet.

**browser-harness:** o blade de **Private Endpoint** e de **Private DNS zone** é território de Portal; aqui o browser-harness ajuda (após o auth wall). O `publicNetworkAccess` você troca no `az` CLI.

### Bloco 3 — Key Vault + Managed Identities + semeadura dos segredos (execução única, offline)

Esta é a parte que **só o instrutor** faz, e **só uma vez** — porque envolve **colocar valores de segredo** no cofre.

1. **Acesso de dados a você mesmo (gotcha do KV-RBAC):** o KV `kv-dev-tk-cin-001` **[já existe]** _(nome do HML de validação — na turma, `kv-<sufixo>`; ver §Nomenclatura)_ com RBAC. Ser *Owner* do recurso **não** dá acesso ao data-plane — dê a si mesmo **`Key Vault Secrets Officer`** no cofre, senão o blade **Secrets** nega **403** (ADE-010 D2, "gotcha #1").
2. **A identidade de leitura compartilhada:** crie a **User-Assigned MI** `id-fifa2026-kv-reader` `[nome sugerido; confirme]` e conceda-lhe **`Key Vault Secrets User`** (só lê o valor) com **escopo = o cofre**. **Espere a propagação de RBAC** (minutos) e confirme antes de seguir. Por que **compartilhada** e não uma por app: **um** grant na vida toda, e ela **sobrevive** quando você recria McpServer/FlowEvents à mão (uma system-assigned morre com o app → novo objectId → regrant). Ler segredo é uniforme → granularidade por-app não compra segurança aqui (ADE-010 D1).
3. **As identidades do SQL (menor-privilégio):** para o SQL via MI (showcase/Fase 2), cada app usa a **própria system-assigned** — McpServer `db_datareader`-only, Functions writer+reader. **Não** use a UA compartilhada no SQL (colapsaria os dois no mesmo contained user e quebraria a regra de ouro do McpServer). Ligue as system-assigned dos apps `[confirmar nomes no Portal]`.
4. **Semeie os segredos no cofre (o passo único-offline):** crie, com **valor byte-a-byte** do valor atual, os secrets:
   - `gateway-admin-shared-secret` (o `X-Gateway-Key` — o mesmo das Quartas)
   - `gemini-api-key`
   - `sql-connection-string`
   - `azure-signalr-connection-string`
   - `servicebus-connection-string` (valor = o App Setting `ServiceBusConnection` **atual** da Function F1, byte-a-byte — a SAS `RootManageSharedAccessKey` herdada das Oitavas)
   - `backend-sql-password` (valor = o App Setting `DB_PASSWORD` **atual** do backend v1, byte-a-byte — a senha discreta que o `database.js` lê)

   **Por que agora, offline:** valores de segredo **nunca** vão para o GitHub, arquivo ou log — só o instrutor os toca, no Portal/`az`, fora do relógio da aula. No **momento da semeadura** ninguém referencia esses secrets ainda → **risco zero** neste passo; o wiring dos apps para as KV references vem **logo a seguir**, ainda no pré-provisionamento (**passo 5**).

5. **Crie os Container Apps já WIRED às KV references (pré-requisito do Doctor item 7):** os apps de deploy — McpServer (`ca-mcp-<sufixo>`), gateway (`ca-gateway-<sufixo>`) e FlowEvents (`ca-flow-<sufixo>`) — têm de **pré-existir** (o `lab-grande-final.yml` faz **zero `az create`**, AC-9: só troca a imagem com `az containerapp update --image`). Portanto **você os cria aqui**, cada um com seus **app-secrets lidos do Key Vault via KV reference + a UA-MI** do passo 2 (ex.: `ca-mcp` → `sql-connection-string`/`gemini-api-key`; `ca-gateway`/Functions → `gateway-admin-shared-secret`). O passo-a-passo detalhado de criar cada app lendo do cofre via MI é o [`final-portal-guide.md`](./final-portal-guide.md) (as 14 fases) — **não o repita aqui**, apenas garanta o resultado. **Por que é pré-provisionamento e não aula:** o Doctor `acao=check` (item 7) exige, no pré-flight, que o `ca-mcp-<sufixo>` já tenha um secret com `keyVaultUrl` resolvendo numa revisão `Running` — um app montado exatamente conforme este runbook **passa** o item 7. Se o wiring ficasse para a aula, o pré-flight daria **FALSO FAIL**.

   > **`[confirmar]` — imagem seed:** criar um Container App exige uma **imagem inicial**, e o Doctor item 7 exige uma revisão **`Running` ANTES** de o aluno rodar qualquer `acao=... update --image`. Use a imagem que o `final-portal-guide.md` manda **buildar/apontar** para cada app (ou uma **imagem-placeholder funcional** que suba e responda health) — o `acao=tudo` do aluno depois troca pela imagem do código dele. **Ponto a confirmar com o owner no provisionamento real** (não inventar nome/tag de imagem aqui).

**Ganho estrutural que você já garante aqui:** o `gateway-admin-shared-secret` é **um** secret que os dois lados vão referenciar — quem **injeta** o `X-Gateway-Key` (gateway) e quem **valida** (Functions/McpServer). A igualdade fica **estrutural** desde a semeadura (ADE-010 D3).

**browser-harness:** criar secrets é confortável no **`az keyvault secret set`** (headless, e evita colar valor numa tela que a skill fotografaria). Reserve o browser-harness para conferir visualmente o IAM da MI e o status de propagação de RBAC no Portal — de novo, atrás do auth wall.

### Bloco 4 — Fixar o FQDN → só ENTÃO `VITE_*`/CORS (a última coisa)

**O que:** com a VNet/CAE definitivos (Bloco 1) e os apps de borda em pé, o **FQDN do gateway** está **estável**. **Só agora** é seguro fixar `VITE_GATEWAY_V2_URL` / `VITE_LLM_PROXY_URL` / `VITE_FLOW_EVENTS_BASE_URL` (build do frontend) e o **CORS** (origin exato do front no gateway e no SignalR).

**Por que por último:** porque tudo isto **carrega o nome do gateway**. Fixar o `VITE_*` antes de a rede estar definitiva é assinar um cheque com um endereço que ainda pode mudar — e mudar o CAE depois **invalida o bundle** e força rebuild na frente da turma. Última camada = a que depende de nomes.

**As `VITE_*` de IDENTIDADE são a exceção — SEMEIE-as agora (não dependem do FQDN):** as 5 vars `VITE_CIAM_AUTHORITY`, `VITE_CIAM_CLIENT_ID`, `VITE_ADMIN_TENANT_ID`, `VITE_ADMIN_CLIENT_ID`, `VITE_ADMIN_SCOPE` derivam do **tenant CIAM + App Registrations** do aluno (criados nas Quartas), **não** do FQDN do gateway. O `lab-grande-final.yml` **lê essas 5 dos App Settings do frontend Web App** (`az webapp config appsettings list`, AC-6/[AUTO-DECISION]) e faz **fail-fast** se qualquer uma faltar — logo o App Settings do frontend é a **fonte-da-verdade** que este pré-provisionamento precisa deixar pronta. Semeie-as no frontend Web App `app-frontend-<sufixo>`:

```bash
az webapp config appsettings set -g rg-<sufixo> -n app-frontend-<sufixo> --settings \
  VITE_CIAM_AUTHORITY='<authority CIAM do aluno>' \
  VITE_CIAM_CLIENT_ID='<client id do SPA cliente>' \
  VITE_ADMIN_TENANT_ID='<tenant id workforce>' \
  VITE_ADMIN_CLIENT_ID='<client id admin>' \
  VITE_ADMIN_SCOPE='<scope da API admin>' -o none
```

> **Não são segredo:** tenant IDs, client IDs e o scope são **identificadores públicos do OIDC** (o próprio bundle da SPA os embute em build-time por desenho) — App Settings é o lugar certo; **nada** vai para GitHub/log/Key Vault. As `VITE_*` de **URL** (que carregam o FQDN) continuam por último, resolvidas pelo workflow — nunca fixadas à mão aqui. `[confirmar no Portal]` os valores exatos de cada aluno (vêm da configuração CIAM/admin das Quartas).

**Portão final:** FQDN do gateway anotado e **estável**; nenhuma etapa posterior pode recriar o CAE/VNet; **as 5 `VITE_*` de identidade semeadas** no App Settings do `app-frontend-<sufixo>` (o `acao=... frontend` do workflow vai lê-las). A partir daqui, o `VITE_*` de URL / CORS pode ser fixado com segurança (na aula/guia).

---

## Checkpoint do pré-provisionamento (antes de fechar o laptop)

- [ ] **VNet + CAE VNet-integrado** existem; CAE **não** será recriado; FQDN base anotado.
- [ ] **Private Endpoint do SQL** ativo; SQL `publicNetworkAccess: Disabled`; **resolve por DNS privado de dentro** da VNet, recusa de fora.
- [ ] **`Key Vault Secrets Officer`** em você; **UA-MI `id-fifa2026-kv-reader`** criada com **`Key Vault Secrets User`** no cofre (propagação confirmada); system-assigned dos apps ligadas. _(Nomes: convenção `<prefixo>-<sufixo>` na turma — KV = `kv-<sufixo>`; MI = nome livre; ver §Nomenclatura.)_
- [ ] **Secrets semeados** (`gateway-admin-shared-secret`, `gemini-api-key`, `sql-connection-string`, `azure-signalr-connection-string`, `servicebus-connection-string`, `backend-sql-password`), valor byte-a-byte (referenciados pelos apps no passo 5).
- [ ] **Container Apps criados e WIRED às KV references** (`ca-mcp-<sufixo>`/`ca-gateway-<sufixo>`/`ca-flow-<sufixo>` lendo app-secrets do cofre via MI, revisão `Running`) — pré-requisito do Doctor **item 7** (o workflow só troca a imagem, AC-9).
- [ ] **5 `VITE_*` de identidade semeadas** no App Settings do `app-frontend-<sufixo>` (não-secreto) — pré-requisito do Doctor **item 8**; o `acao=frontend`/`tudo` do workflow as lê (fail-fast se faltar).
- [ ] **FQDN estável** — nada mais recria a rede; as `VITE_*` de **URL** (FQDN) / CORS ficam para a aula.

## Handoff para a aula

Na aula, o aluno valida a fundação com **`acao=check`** (o Doctor pré-flight do `lab-grande-final.yml`, **8 checks read-only**): os 5 gotchas de deploy (RBAC do SP no RG do gateway, ACR nos Registries, `targetPort=8080`, probes na mesma porta, nome do segredo) + a invariante de perímetro (item 6) + um **smoke de leitura de um secret via a MI** (item 7 — prova que o **cofre, a Managed Identity e o wiring das KV-references dos apps** que você deixou prontos no passo 5 estão resolvendo, revisão `Running`) + a **presença das 5 `VITE_*` de identidade** no App Settings do frontend (item 8 — o que você semeou no Bloco 4). Tudo **sem expor valores**. Só com tudo **PASS** o aluno roda `acao=tudo`. É assim que o pré-provisionamento offline **encontra** o relógio da aula sem roubar o clímax — e cada FALSO FAIL fica impossível porque itens 7/8 checam exatamente o que este runbook deixa pronto.

> **Débito honesto:** o **draw.io de topologia completa** (a foto desta rede fechada + cofre + identidades) é a **Story 4.6** (ainda não feita) — por ora, este runbook é a descrição da topologia; o slide de arquitetura da aula é a foto conceitual.
