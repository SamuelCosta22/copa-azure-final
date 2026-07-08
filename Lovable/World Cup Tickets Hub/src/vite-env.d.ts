/// <reference types="vite/client" />

// Story 2.3 / F3 — variáveis de ambiente Vite para o fluxo de identidade v2 (MSAL.js).
// Valores reais vêm do .env (não versionado) ou das App Settings do App Service;
// NUNCA hardcoded no repo (ADE-005 Inv 5).
interface ImportMetaEnv {
  /** Backend v1 (Node/Express) — fluxo de comparação didática (intocado). */
  readonly VITE_API_URL?: string;
  /**
   * Oitavas de Final — Base URL da Azure Function v2 (F1, authLevel Anonymous).
   * Quando DEFINIDA, o Checkout passa a usar a compra ASSÍNCRONA (POST /api/v2/purchase
   * → 202 {correlationId} → polling em /api/v2/purchase/{id}). Ausente → fluxo v1 síncrono.
   * NUNCA hardcoded.
   */
  readonly VITE_FUNCTION_V2_URL?: string;
  /** Application (client) ID da App Registration SPA no tenant Entra workforce (v1/2.3). */
  readonly VITE_ENTRA_CLIENT_ID?: string;
  /** GUID do tenant Entra workforce do aluno (v1/2.3). */
  readonly VITE_ENTRA_TENANT_ID?: string;
  /**
   * Story 2.11 / Quartas (F3) — login ADMIN workforce (Microsoft Entra ID, App Role
   * "Admin"). Application (client) ID da App Registration SPA do ADMIN no tenant
   * workforce (= audience do token admin que o gateway valida). Se ausente, cai no
   * fallback VITE_ENTRA_CLIENT_ID. NUNCA hardcoded.
   */
  readonly VITE_ADMIN_CLIENT_ID?: string;
  /**
   * Story 2.11 / Quartas (F3) — GUID do tenant workforce do ADMIN. Deriva a authority
   * https://login.microsoftonline.com/<tenantId>/v2.0. Fallback: VITE_ENTRA_TENANT_ID.
   */
  readonly VITE_ADMIN_TENANT_ID?: string;
  /**
   * Story 2.11 / Quartas (F3) — override OPCIONAL da authority workforce completa
   * (ex.: https://login.microsoftonline.com/<tenantId>/v2.0). Default derivado de
   * VITE_ADMIN_TENANT_ID quando ausente.
   */
  readonly VITE_ADMIN_AUTHORITY?: string;
  /**
   * Story 2.11 / Quartas (F3) — scope do access token admin. Default
   * api://<AdminClientId>/.default (emite a App Role na claim `roles`). Configurável
   * se o tenant expuser um scope nomeado (ex.: api://<id>/access_as_admin).
   */
  readonly VITE_ADMIN_SCOPE?: string;
  /**
   * Story 2.11 / Quartas — Authority COMPLETA do tenant CIAM (Microsoft Entra External
   * ID), ex.: https://contoso.ciamlogin.com/ . É a authority do CLIENTE final (ADE-007
   * Inv 1/2) — NÃO login.microsoftonline.com. O MSAL deriva o host desta URL para
   * knownAuthorities (obrigatório p/ CIAM/B2C). Valor completo plugado pelo instrutor
   * (tenant pré-provisionado, handoff §3). NUNCA hardcoded.
   */
  readonly VITE_CIAM_AUTHORITY?: string;
  /** Application (client) ID da App Registration SPA criada pelo aluno no tenant CIAM. */
  readonly VITE_CIAM_CLIENT_ID?: string;
  /** Scope exposto pela App Registration (ex.: api://<client-id>/purchase.write). */
  readonly VITE_ENTRA_SCOPE?: string;
  /** Base URL do gateway YARP v2 (Container App). Ex.: https://gateway-xy.azurecontainerapps.io */
  readonly VITE_GATEWAY_V2_URL?: string;
  /** Redirect URI registrada na App Registration (dev: http://localhost:5173). */
  readonly VITE_ENTRA_REDIRECT_URI?: string;
  /**
   * Story 2.6 / F6 — base das rotas do serviço FlowEvents EXPOSTAS PELO GATEWAY YARP
   * (ex.: https://gateway-xy.azurecontainerapps.io/flow-events). O gateway é o nó zero:
   * injeta X-Correlation-ID também nas chamadas ao FlowEvents. Inclui /api/flow/** (REST)
   * e /hubs/flow (SignalR). NUNCA hardcoded.
   */
  readonly VITE_FLOW_EVENTS_BASE_URL?: string;
  /**
   * Story 4.6 (Diploma vivo) — timestamp ISO do deploy, INJETADO NO BUILD pelo workflow
   * (ex.: `date -u` no runner do GitHub Actions). É a "hora do deploy" infalsificável do
   * Diploma (AC-3): nasce no CI, nunca no navegador do aluno. Ausente em builds locais →
   * o Diploma degrada graciosamente (não fabrica um `Date.now()` client-side). NUNCA hardcoded.
   */
  readonly VITE_BUILD_TIME?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
