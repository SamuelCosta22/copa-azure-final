// =============================================================================
// Story 4.6 / Grande Final — Diploma vivo (rota /diploma).
//
// O clímax da aula: renderiza a telemetria REAL da sessão do aluno — os correlation-IDs
// das SUAS compras (que atravessaram os 5 microsserviços do F6), a região onde o sistema
// rodou e a hora do deploy — assinado "Squad AIOX + você". Prova infalsificável e pessoal.
//
// INFALSIFICABILIDADE (AC-3): region + correlationIds vêm do backend (FlowEvents /diploma-
// summary, escopado a customDimensions.UserId — AC-6); deployTime é o VITE_BUILD_TIME baked
// no bundle pelo CI. A UI NUNCA fabrica esses três (sem Math.random/Date.now/hardcode). O
// único dado client-side é o nome do próprio aluno (useAuth().user.name).
//
// PII (AC-6): entra_oid NUNCA é buscado, renderizado, copiado nem posto em OG tag. O
// endpoint só devolve region + correlation-IDs (identificadores técnicos) + count.
//
// POSTURA REAL do endpoint (PROTEGIDA — EPIC-004 §Emenda MEDIUM-4 / ADE-009 v1.1): diferente
// do resto do F6, o `/diploma-summary` passou a exigir DUAS provas que um anônimo da internet
// não tem — (1) IDENTIDADE: `fetchDiplomaSummary` manda `Authorization: Bearer` (via gateway v2)
// e o blanket RequireAuthorization barra o anônimo (401); (2) PROVENIÊNCIA: o gateway injeta o
// X-Diploma-Key route-scoped, validado timing-safe no FlowEvents (barra o FQDN direto do ca-flow).
// CONSEQUÊNCIA (correção do split de identidade): o Bearer de cliente = token CIAM, então o
// Diploma agora exige a sessão v2/CIAM (gate `enabled: gateReady`, via useIsAuthenticated). O
// userId da telemetria é resolvido pelo /api/v2/me (não o id v1), casando com o que a compra usou.
// Residual aceito (fast-follow): um aluno AUTENTICADO ainda pode pedir ?userId=<outro> e ler GUIDs
// opacos+count (zero PII) — igual/menor que o `/recent`. Detalhe em docs/security/final-security-debt.md.
//
// Rota NOVA e aditiva (AC-9): AuthProvider/api.ts/apiV2.ts e as demais rotas intocados.
// Design: spec da @ux-design-expert (Uma) — herda o DNA visual do TicketStub (troféu/selo
// dourado, ID em monospace) adaptado ao tema dark. Zero dependência nova.
// =============================================================================

import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Helmet } from 'react-helmet-async';
import { format, isValid, parseISO } from 'date-fns';
import { ptBR } from 'date-fns/locale';
import {
  Trophy,
  ShieldCheck,
  Globe,
  Clock,
  Ticket,
  Linkedin,
  Copy,
  Check,
  Sparkles,
  Lock,
  AlertTriangle,
  RefreshCw,
} from 'lucide-react';
import { toast } from 'sonner';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Separator } from '@/components/ui/separator';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { useAuth } from '@/contexts/AuthContext';
import { usePrefersReducedMotion } from '@/hooks/useFlowConnection';
import { fetchDiplomaSummary } from '@/lib/flowApi';
import { meV2 } from '@/lib/apiV2';
import { isEntraConfigured, loginRequest } from '@/lib/authV2';

// AC-4 — assinatura fixa, LITERAL e obrigatória. String exata (não uma variação livre).
const SIGNATURE = 'Squad AIOX + você';
// Spec §1.4 — no máximo 6 selos visíveis; o resto entra num "ver mais N".
const MAX_VISIBLE_SEALS = 6;

/** Trunca o correlation-ID como TROFÉU (8 primeiros … 4 finais); o todo fica no tooltip/copiar. */
function truncateId(id: string): string {
  if (id.length <= 13) return id;
  return `${id.slice(0, 8)}…${id.slice(-4)}`;
}

/** Formata a hora do deploy (ISO do CI) em pt-BR; inválida/ausente → null (degrada gracioso). */
function formatDeployTime(iso: string | undefined): string | null {
  if (!iso) return null;
  const parsed = parseISO(iso);
  if (!isValid(parsed)) return null;
  return format(parsed, "dd/MM/yyyy 'às' HH:mm", { locale: ptBR });
}

// -----------------------------------------------------------------------------
// StatCard — um dos 3 cartões do TelemetryGrid (Região · Deploy · Jornadas).
// -----------------------------------------------------------------------------
function StatCard({
  icon: Icon,
  label,
  value,
  highlight = false,
}: {
  icon: typeof Globe;
  label: string;
  value: string;
  highlight?: boolean;
}) {
  return (
    <div className="rounded-lg border border-border/60 bg-background/40 p-4 text-center">
      <div className="mb-1 flex items-center justify-center gap-1.5 text-xs uppercase tracking-widest text-muted-foreground">
        <Icon className="h-3.5 w-3.5" aria-hidden="true" />
        {label}
      </div>
      <div
        className={
          highlight
            ? 'gold-text font-display text-2xl leading-tight'
            : 'text-lg font-semibold text-foreground'
        }
      >
        {value}
      </div>
    </div>
  );
}

// -----------------------------------------------------------------------------
// AuthenticitySeal — 1 correlation-ID como "selo de autenticidade" (não dump de log).
// -----------------------------------------------------------------------------
function AuthenticitySeal({ correlationId }: { correlationId: string }) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(correlationId);
      setCopied(true);
      toast.success('Correlation ID copiado.');
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      toast.error('Não consegui copiar — copie manualmente do tooltip.');
    }
  };

  return (
    <li
      className="flex items-center justify-between gap-2 rounded-md border border-primary/40 bg-background/40 px-3 py-2"
      aria-label={`Selo de autenticidade, compra ${correlationId}`}
    >
      <span className="flex items-center gap-2 overflow-hidden">
        <ShieldCheck className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
        <Tooltip>
          <TooltipTrigger asChild>
            <span className="truncate font-mono text-sm text-foreground">
              {truncateId(correlationId)}
            </span>
          </TooltipTrigger>
          <TooltipContent>
            <span className="font-mono text-xs">{correlationId}</span>
          </TooltipContent>
        </Tooltip>
      </span>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="h-6 w-6 shrink-0"
        onClick={copy}
        aria-label={`Copiar correlation ID ${correlationId}`}
      >
        {copied ? (
          <Check className="h-3.5 w-3.5 text-success" aria-hidden="true" />
        ) : (
          <Copy className="h-3.5 w-3.5" aria-hidden="true" />
        )}
      </Button>
    </li>
  );
}

// -----------------------------------------------------------------------------
// Estados de gate/erro/carregando — cartões centrados, não a página inteira.
// -----------------------------------------------------------------------------
function GateCard({
  icon: Icon,
  title,
  children,
  action,
}: {
  icon: typeof Lock;
  title: string;
  children: React.ReactNode;
  action?: React.ReactNode;
}) {
  return (
    <div className="mx-auto max-w-md">
      <Card className="glass-card">
        <CardContent className="flex flex-col items-center gap-3 p-8 text-center">
          <Icon className="h-10 w-10 text-primary" aria-hidden="true" />
          <h2 className="font-display text-2xl">{title}</h2>
          <p className="text-sm text-muted-foreground">{children}</p>
          {action}
        </CardContent>
      </Card>
    </div>
  );
}

function DiplomaSkeleton() {
  return (
    <div className="mx-auto max-w-3xl">
      <Card className="card-gradient border-primary/40">
        <CardContent className="space-y-6 p-8">
          <Skeleton className="mx-auto h-12 w-12 rounded-full" />
          <Skeleton className="mx-auto h-10 w-64" />
          <Skeleton className="mx-auto h-4 w-80" />
          <div className="grid gap-4 sm:grid-cols-3">
            <Skeleton className="h-20" />
            <Skeleton className="h-20" />
            <Skeleton className="h-20" />
          </div>
          <div className="space-y-2">
            <Skeleton className="h-9" />
            <Skeleton className="h-9" />
            <Skeleton className="h-9" />
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

// -----------------------------------------------------------------------------
// Página.
// -----------------------------------------------------------------------------
export default function Diploma() {
  // `user` (v1) é usado APENAS para o nome no cartão (display) — degrada para um fallback
  // quando o aluno é nato-CIAM (sem conta v1). A telemetria NÃO depende dele (ver abaixo).
  const { user } = useAuth();
  // Emenda MEDIUM-4 — o endpoint agora exige Bearer (via gateway). Bearer de cliente = CIAM.
  // Gate na sessão CIAM (mesmo primitivo do Chatbot/LoginV2Button), NÃO no auth v1: um aluno
  // logado só em v1 não tem token CIAM → sem esse gate, a query dispararia sem Bearer → 401.
  const isV2Authenticated = useIsAuthenticated();
  const v2Configured = isEntraConfigured();
  const gateReady = v2Configured && isV2Authenticated;
  const { instance: msalInstance } = useMsal();
  const reducedMotion = usePrefersReducedMotion();
  const [showAllSeals, setShowAllSeals] = useState(false);

  // UX-1 (review rodada 3) — CTA de login do GATE, VISÍVEL em todos os breakpoints (o
  // `LoginV2Button` da navbar é `hidden lg:flex`, some no mobile). Reusa o MESMO fluxo
  // (loginPopup + loginRequest / PKCE) — só muda a visibilidade, iniciado pelo aluno.
  const handleV2Login = async () => {
    try {
      await msalInstance.loginPopup(loginRequest);
    } catch (error) {
      console.error('Falha no Login v2 (Entra):', error);
    }
  };

  // AC-3 — hora do deploy: build-time injetado no bundle pelo CI (VITE_BUILD_TIME).
  // NUNCA um Date.now() do navegador. Ausente (build local) → degrada gracioso.
  const deployTime = formatDeployTime(import.meta.env.VITE_BUILD_TIME);

  const {
    data,
    isLoading,
    isError,
    refetch,
    isRefetching,
  } = useQuery({
    queryKey: ['diploma-summary'],
    // AC-6 — o userId da telemetria (customDimensions.UserId) é o int resolvido pelo /api/v2/me
    // (CIAM), NÃO o id v1: para o aluno nato-CIAM o id v1 é null, e para o aluno vinculado o /me
    // devolve o MESMO id da base — é EXATAMENTE o valor que o Checkout usou na compra (mesma
    // resolução de Story 3.5). Resolver aqui garante que o escopo bate com a telemetria e exige,
    // de quebra, a sessão CIAM (o /me precisa do Bearer) — alinhado com a proteção do endpoint.
    queryFn: async () => {
      const me = await meV2();
      if (!me.userId || !Number.isFinite(me.userId)) {
        throw new Error(me.error ?? 'Sem sessão CIAM para resolver sua identidade (Login v2).');
      }
      return fetchDiplomaSummary(String(me.userId));
    },
    // `enabled` só dispara com sessão CIAM presente → nenhum fetch anônimo (evita o 401) e nenhum
    // popup de token inesperado ao abrir a página (getV2AccessToken só é chamado quando há conta).
    enabled: gateReady,
    staleTime: 60_000,
  });

  const correlationIds = useMemo(() => data?.correlationIds ?? [], [data]);
  const count = data?.count ?? correlationIds.length;
  const region = data?.region ?? null;

  const visibleSeals = useMemo(
    () => (showAllSeals ? correlationIds : correlationIds.slice(0, MAX_VISIBLE_SEALS)),
    [correlationIds, showAllSeals],
  );
  const hiddenCount = correlationIds.length - visibleSeals.length;

  // AC-5 — "Copiar resumo": o texto PESSOAL (telemetria real) para o corpo do post.
  // NÃO inclui nome (o post já é do perfil dele), correlation-IDs nem entra_oid/e-mail.
  const copySummary = async () => {
    const resumo = [
      '🏆 Acabei de concluir a Grande Final da Copa 2026 — nível produção.',
      '',
      'Construí, integrei e BLINDEI um sistema de 5 microsserviços rodando de verdade no Azure:',
      `🌐 Região do deploy: ${region ?? 'não informada'}`,
      `🕐 Deploy em: ${deployTime ?? 'não informado'}`,
      `🎫 ${count} compras reais que atravessaram os 5 serviços, do Gateway ao SQL — rastreadas de ponta a ponta`,
      '',
      'Blindagem de nível produção: VNet fechada, Private Endpoint, Managed Identity, Key Vault e a trava X-Gateway-Key. Telemetria real, correlacionável, infalsificável.',
      '',
      'Não é um certificado genérico — é a prova de que o sistema que eu construí me responde.',
      '',
      'Assinado: Squad AIOX + eu. 💛',
      '',
      '#Azure #Microservices #CloudSecurity #DevOps #Copa2026 #AIOX',
    ].join('\n');
    try {
      await navigator.clipboard.writeText(resumo);
      toast.success('Resumo copiado! Cole no seu post do LinkedIn.');
    } catch {
      toast.error('Não consegui copiar o resumo — tente novamente.');
    }
  };

  // AC-5 — LinkedIn share-offsite: compartilha a URL pública /diploma (o LinkedIn renderiza
  // o card a partir das OG tags GENÉRICAS, sem PII). O resumo pessoal vai pelo "Copiar resumo".
  const shareLinkedIn = () => {
    const publicDiplomaUrl = `${window.location.origin}/diploma`;
    window.open(
      `https://www.linkedin.com/sharing/share-offsite/?url=${encodeURIComponent(publicDiplomaUrl)}`,
      '_blank',
      'noopener',
    );
    toast('Card aberto no LinkedIn. Dica: cole seu resumo (botão ao lado) no texto do post.');
  };

  // OG tags GENÉRICAS (AC-6) — sem nome, correlation-ID, e-mail ou entra_oid.
  const head = (
    <Helmet>
      <title>Diploma de Produção — Grande Final Copa 2026</title>
      <meta property="og:title" content="Construí um sistema de nível produção na Grande Final ⚽" />
      <meta
        property="og:description"
        content="5 microsserviços, VNet fechada, Managed Identity, Key Vault, telemetria ponta a ponta. Feito por mim + Squad AIOX."
      />
      <meta property="og:type" content="website" />
      <meta property="og:image" content="/og-image.png" />
    </Helmet>
  );

  return (
    <main className="stadium-pattern min-h-[80vh]">
      {head}
      <div className="container mx-auto px-4 py-8">
        <header className="mb-6 text-center">
          <p className="text-xs font-semibold uppercase tracking-widest text-primary">
            Grande Final · Nível Produção
          </p>
          <h1 className="font-display text-3xl sm:text-4xl">
            A Copa que você construiu — agora te responde
          </h1>
        </header>

        {/* Gate CIAM: o Diploma consome um endpoint protegido (Bearer via gateway) — precisa da
            sessão v2/CIAM. Sem ela, é um GATE (não um erro): o aluno clica no "Login v2" (popup
            iniciado por ELE, nunca automático). Cobre tanto o aluno só-v1 quanto o não-logado. */}
        {!gateReady ? (
          <GateCard
            icon={Lock}
            title="Entre com o Login v2 (Entra/CIAM) para ver seu Diploma"
            action={
              v2Configured ? (
                <Button onClick={handleV2Login} className="gap-2">
                  <ShieldCheck className="h-4 w-4" />
                  Login v2 (Entra)
                </Button>
              ) : (
                <Button asChild variant="outline">
                  <Link to="/login">Voltar ao login</Link>
                </Button>
              )
            }
          >
            O Diploma renderiza a telemetria REAL da SUA sessão — a identidade que a comprovou é a
            do fluxo v2 (Entra External ID / CIAM), a mesma da sua compra na Grande Final.
          </GateCard>
        ) : isLoading ? (
          <DiplomaSkeleton />
        ) : isError ? (
          // Estado ERRO — nunca inventa dado de fallback (AC-3/AC-10).
          <GateCard
            icon={AlertTriangle}
            title="Não consegui buscar sua telemetria agora"
            action={
              <Button variant="outline" onClick={() => void refetch()}>
                <RefreshCw
                  className={isRefetching ? 'mr-2 h-4 w-4 animate-spin' : 'mr-2 h-4 w-4'}
                  aria-hidden="true"
                />
                Tentar de novo
              </Button>
            }
          >
            A fonte é o mesmo App Insights que alimenta o Flow Visualizer.
          </GateCard>
        ) : (
          // Estado SUCESSO (com o caso vazio embutido: count === 0 vira convite).
          <div className="mx-auto max-w-3xl">
            <article
              aria-labelledby="diploma-title"
              className={
                reducedMotion
                  ? 'card-gradient glow-gold rounded-xl border border-primary/40 p-8'
                  : 'card-gradient glow-gold animate-float rounded-xl border border-primary/40 p-8'
              }
            >
              {/* Selo do troféu */}
              <div className="mb-4 flex justify-center">
                <div
                  className={
                    reducedMotion
                      ? 'flex h-14 w-14 items-center justify-center rounded-full gold-gradient'
                      : 'flex h-14 w-14 items-center justify-center rounded-full gold-gradient animate-pulse-gold'
                  }
                >
                  <Trophy className="h-7 w-7 text-primary-foreground" aria-hidden="true" />
                </div>
              </div>

              <p className="text-center text-xs uppercase tracking-widest text-muted-foreground">
                Diploma de Produção
              </p>

              {/* Destinatário — nome do próprio aluno (dado dele, useAuth) */}
              <div className="mt-4 text-center">
                <p className="text-sm text-muted-foreground">Este diploma certifica que</p>
                <h2 id="diploma-title" className="gold-text font-display text-3xl sm:text-4xl">
                  {user?.name || 'Aluno da Grande Final'}
                </h2>
                <p className="mx-auto mt-2 max-w-xl text-balance text-sm text-muted-foreground">
                  construiu, integrou e BLINDOU um sistema de 5 microsserviços de nível
                  produção na Grande Final da Copa 2026.
                </p>
              </div>

              {/* TelemetryGrid — 3 StatCards (dados do contrato §7) */}
              <section aria-label="Telemetria do deploy" className="mt-6">
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                  <StatCard icon={Globe} label="Região" value={region ?? '—'} />
                  <StatCard icon={Clock} label="Deploy" value={deployTime ?? '—'} />
                  <StatCard
                    icon={Ticket}
                    label="Jornadas"
                    value={`${count} compra${count === 1 ? '' : 's'} real${count === 1 ? '' : 'is'}`}
                    highlight
                  />
                </div>
              </section>

              {/* Selos de autenticidade — ou o convite (estado vazio, count === 0) */}
              <section aria-label="Selos de autenticidade" className="mt-6">
                {count === 0 ? (
                  <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed border-border/60 p-6 text-center">
                    <Trophy className="h-8 w-8 text-muted-foreground" aria-hidden="true" />
                    <p className="font-display text-xl">
                      Seu diploma está esperando a primeira jornada
                    </p>
                    <p className="max-w-md text-sm text-muted-foreground">
                      Nenhuma compra sua atravessou o sistema ainda. Faça uma compra e volte
                      aqui — seu troféu se preenche sozinho com a telemetria real.
                    </p>
                    <div className="flex flex-wrap justify-center gap-2">
                      <Button asChild>
                        <Link to="/matches">Comprar um ingresso</Link>
                      </Button>
                      <Button asChild variant="outline">
                        <Link to="/flow">Ver o fluxo ao vivo</Link>
                      </Button>
                    </div>
                  </div>
                ) : (
                  <>
                    <p className="mb-2 flex items-center gap-1.5 text-xs uppercase tracking-widest text-muted-foreground">
                      <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                      Selos de autenticidade
                    </p>
                    <ul className="grid gap-2 sm:grid-cols-2">
                      {visibleSeals.map((id) => (
                        <AuthenticitySeal key={id} correlationId={id} />
                      ))}
                    </ul>
                    {hiddenCount > 0 && (
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="mt-2"
                        onClick={() => setShowAllSeals(true)}
                      >
                        e mais {hiddenCount} selo{hiddenCount === 1 ? '' : 's'}
                      </Button>
                    )}
                    <p className="mt-3 text-sm text-muted-foreground">
                      Cada selo é uma compra real sua que atravessou os 5 microsserviços — do
                      Gateway ao SQL — rastreada de ponta a ponta. Impossível de falsificar: os
                      IDs nasceram no backend, não no seu navegador.
                    </p>
                  </>
                )}
              </section>

              {/* Assinatura fixa (AC-4) */}
              <Separator className="my-6 bg-border/50" />
              <div className="text-center">
                <p className="text-xs uppercase tracking-widest text-muted-foreground">
                  Assinado por
                </p>
                <p className="mt-1 flex items-center justify-center gap-2 gold-text font-display text-xl">
                  <Sparkles className="h-5 w-5 text-primary" aria-hidden="true" />
                  {SIGNATURE}
                </p>
              </div>
            </article>

            {/* ShareBar — o loop viral honesto (AC-5) */}
            <div className="mt-6 flex flex-wrap justify-center gap-3">
              <Button onClick={shareLinkedIn}>
                <Linkedin className="mr-2 h-4 w-4" aria-hidden="true" />
                Compartilhar no LinkedIn
              </Button>
              <Button variant="outline" onClick={() => void copySummary()}>
                <Copy className="mr-2 h-4 w-4" aria-hidden="true" />
                Copiar resumo
              </Button>
            </div>
          </div>
        )}
      </div>
    </main>
  );
}
