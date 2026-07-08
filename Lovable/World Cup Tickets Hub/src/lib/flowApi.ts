// =============================================================================
// Story 2.6 / F6 — Cliente REST do Flow Visualizer (serviço FlowEvents via gateway).
//
// Toda chamada passa pelo gateway YARP (VITE_FLOW_EVENTS_BASE_URL = .../flow-events),
// que é o NÓ ZERO do fluxo (injeta X-Correlation-ID). NUNCA referencia APIM — APIM não
// existe no EPIC-002 (ADE-004). As rotas REST são fallback de polling (2s) quando o
// WebSocket SignalR não conecta (AC-6).
// =============================================================================

import { getV2AccessToken } from '@/lib/authV2';

const FLOW_BASE = import.meta.env.VITE_FLOW_EVENTS_BASE_URL ?? '';

// Story 4.6 §Emenda MEDIUM-4 — o Diploma NÃO usa a FLOW_BASE genérica (recent/timeline/replay
// seguem anônimos, MEDIUM-1 aceito). Vai pela MESMA base do apiV2.ts (o gateway v2) com Bearer:
// o blanket RequireAuthorization barra o anônimo (401) e o gateway injeta o X-Diploma-Key só
// nesta rota (ADE-009 v1.1). A rota do FlowEvents fica sob /flow-events no gateway.
const GATEWAY_V2_URL = import.meta.env.VITE_GATEWAY_V2_URL ?? '';

/**
 * Os 5 tipos de evento = os 5 nós do diagrama, na ordem REAL do fluxo (ADE-008 Inv 5 —
 * o nó do n8n foi removido; a notificação pós-compra é inline no nó Consumer).
 * NÓ ZERO = GATEWAY_YARP_RECEIVED (nunca APIM). Espelha FlowEventType.cs do backend.
 */
export type FlowEventType =
  | 'GATEWAY_YARP_RECEIVED'
  | 'FUNCTION_ENTRY_PROCESSED'
  | 'SERVICE_BUS_PUBLISHED'
  | 'FUNCTION_CONSUMER_DONE'
  | 'SQL_INSERTED';

export interface FlowEvent {
  correlationId: string;
  eventType: FlowEventType;
  /** Índice ordinal do nó (0..4) — posição na animação da bolinha. */
  nodeIndex: number;
  timestamp: string;
  durationMs?: number | null;
  status: 'ok' | 'error';
  message?: string | null;
}

export interface RecentPurchase {
  correlationId: string;
  timestamp: string;
  status: 'ok' | 'error';
}

/** AC-5 — últimas N compras (default 50) para a lista do front. */
export async function fetchRecentPurchases(top = 50): Promise<RecentPurchase[]> {
  const response = await fetch(`${FLOW_BASE}/api/flow/recent?top=${top}`, {
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new Error(`Falha ao listar compras recentes (${response.status}).`);
  }
  return (await response.json()) as RecentPurchase[];
}

/** AC-6 — timeline completa de um correlationId (fallback de polling). */
export async function fetchTimeline(correlationId: string): Promise<FlowEvent[]> {
  const response = await fetch(`${FLOW_BASE}/api/flow/${encodeURIComponent(correlationId)}`, {
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new Error(`Falha ao obter a timeline (${response.status}).`);
  }
  return (await response.json()) as FlowEvent[];
}

/**
 * AC-6 — pede ao backend que releia a telemetria e empurre os eventos via SignalR para
 * o grupo correlation-<id>, disparando a animação em tempo real nos clientes assinantes.
 */
export async function replayFlow(correlationId: string): Promise<void> {
  await fetch(`${FLOW_BASE}/api/flow/${encodeURIComponent(correlationId)}/replay`, {
    method: 'POST',
  });
}

/**
 * Story 4.6 (Diploma vivo) — resumo da telemetria do PRÓPRIO aluno.
 * `region` e `correlationIds` vêm do backend (infalsificáveis, AC-3): a região é resolvida
 * no ambiente do FlowEvents e os correlation-IDs são ESCOPADOS ao userId v1 do aluno
 * (customDimensions.UserId) — nenhum dado de outro aluno (AC-6). Reúso da MESMA fonte App
 * Insights do F6 (AC-2), só filtrada. O `deployTime` NÃO vem daqui — é o build-time injetado
 * no bundle (VITE_BUILD_TIME). `region` pode ser null se o ambiente não a expuser.
 */
export interface DiplomaSummary {
  region: string | null;
  correlationIds: string[];
  count: number;
}

/**
 * Story 4.6 §Emenda MEDIUM-4 — busca o resumo do Diploma escopado ao aluno logado (userId v1).
 * DIFERENTE do resto do flowApi: vai pelo GATEWAY v2 (VITE_GATEWAY_V2_URL) com `Authorization:
 * Bearer` (mesmo accessor do apiV2.ts). O gateway exige o Bearer (blanket RequireAuthorization →
 * 401 sem token) e injeta o X-Diploma-Key route-scoped que o FlowEvents valida (ADE-009 v1.1) —
 * as duas provas que fecham o buraco anônimo do MEDIUM-4. Sem token (sem sessão CIAM), a chamada
 * segue e o gateway responde 401 (comportamento esperado do endpoint protegido).
 */
export async function fetchDiplomaSummary(userId: string): Promise<DiplomaSummary> {
  const token = await getV2AccessToken();
  const response = await fetch(
    `${GATEWAY_V2_URL}/flow-events/api/flow/diploma-summary?userId=${encodeURIComponent(userId)}`,
    {
      headers: {
        Accept: 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    },
  );
  if (!response.ok) {
    throw new Error(`Falha ao obter o resumo do Diploma (${response.status}).`);
  }
  return (await response.json()) as DiplomaSummary;
}

/** URL absoluta do Hub SignalR (via gateway). Usada pelo useFlowConnection. */
export const FLOW_HUB_URL = `${FLOW_BASE}/hubs/flow`;
