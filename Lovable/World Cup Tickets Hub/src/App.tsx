import { lazy, Suspense } from "react";
import { Loader2 } from "lucide-react";
import { Toaster } from "@/components/ui/toaster";
import { Toaster as Sonner } from "@/components/ui/sonner";
import { TooltipProvider } from "@/components/ui/tooltip";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter, Routes, Route } from "react-router-dom";
import { CartProvider } from "@/contexts/CartProvider";
import { AuthProvider } from "@/contexts/AuthProvider";
// Story 2.11 / Quartas (F3) — provider do login ADMIN workforce (Entra ID + App Role),
// escopado à área /admin. Instância MSAL separada da CIAM (ver src/lib/authAdmin.ts).
import { AdminAuthProvider } from "@/contexts/AdminAuthProvider";
import { MsalProvider } from "@azure/msal-react";
import { msalInstance } from "@/lib/authV2";
import Layout from "@/components/layout/Layout";
import Index from "./pages/Index";

// Páginas públicas mais leves: lazy para reduzir bundle inicial.
const Matches = lazy(() => import("./pages/Matches"));
const MatchDetail = lazy(() => import("./pages/MatchDetail"));
const Stadiums = lazy(() => import("./pages/Stadiums"));
const StadiumDetail = lazy(() => import("./pages/StadiumDetail"));
const Teams = lazy(() => import("./pages/Teams"));
const TeamDetail = lazy(() => import("./pages/TeamDetail"));
const Groups = lazy(() => import("./pages/Groups"));
const Standings = lazy(() => import("./pages/Standings"));
const Quiz = lazy(() => import("./pages/Quiz"));
const Qualified = lazy(() => import("./pages/Qualified"));
const Cart = lazy(() => import("./pages/Cart"));
const Login = lazy(() => import("./pages/Login"));
const Register = lazy(() => import("./pages/Register"));
const Checkout = lazy(() => import("./pages/Checkout"));
const PaymentConfirmation = lazy(() => import("./pages/PaymentConfirmation"));
const Profile = lazy(() => import("./pages/Profile"));
const NotFound = lazy(() => import("./pages/NotFound"));
const TicketVerify = lazy(() => import("./pages/TicketVerify"));
const WorldCupHistory = lazy(() => import("./pages/WorldCupHistory"));
const WorldCupDetail = lazy(() => import("./pages/WorldCupDetail"));
// Story 2.6 / F6 — Flow Visualizer (rota /flow). Lazy: bundle separado (framer-motion + signalr).
const Flow = lazy(() => import("./pages/Flow"));
// Story 4.6 / Grande Final — Diploma vivo (rota /diploma). Telemetria real do aluno
// (mesma fonte do F6). Rota NOVA e aditiva: nenhum fluxo existente é alterado (AC-9).
const Diploma = lazy(() => import("./pages/Diploma"));

// Admin pages: bundle separado, só carrega para admins.
const AdminLayout = lazy(() => import("./pages/admin/AdminLayout"));
const Dashboard = lazy(() => import("./pages/admin/Dashboard"));
const AdminMatches = lazy(() => import("./pages/admin/AdminMatches"));
const AdminStadiums = lazy(() => import("./pages/admin/AdminStadiums"));
const AdminUsers = lazy(() => import("./pages/admin/AdminUsers"));
const AdminSales = lazy(() => import("./pages/admin/AdminSales"));

// Defaults com cache mais agressivo: stadiums/teams quase não mudam,
// matches mudam ocasionalmente. Evita refetch ao trocar de página
// e ao voltar para uma aba já visitada.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5 * 60 * 1000,       // 5 min: dados considerados frescos
      gcTime: 30 * 60 * 1000,         // 30 min: mantém no cache mesmo sem assinatura
      refetchOnWindowFocus: false,    // não refetcha ao alternar de aba
      retry: 1,                       // só 1 tentativa em vez de 3 padrão
    },
  },
});

const PageLoader = () => (
  <div className="min-h-[60vh] flex items-center justify-center">
    <Loader2 className="w-8 h-8 animate-spin text-primary" />
  </div>
);

// Story 2.3 / F3 — MsalProvider envolve a app para o fluxo de identidade v2 (Entra).
// Coexiste com AuthProvider (v1 bcrypt+JWT, intocado) — comparação didática v1 vs v2.
const App = () => (
  <QueryClientProvider client={queryClient}>
    <TooltipProvider>
      <MsalProvider instance={msalInstance}>
      <AuthProvider>
        <CartProvider>
          <Toaster />
          <Sonner />
          <BrowserRouter>
            <Suspense fallback={<PageLoader />}>
              <Routes>
                {/* Admin Routes — Quartas (F3): gate via Entra workforce + App Role "Admin". */}
                <Route path="/admin" element={<AdminAuthProvider><AdminLayout /></AdminAuthProvider>}>
                  <Route index element={<Dashboard />} />
                  <Route path="matches" element={<AdminMatches />} />
                  <Route path="stadiums" element={<AdminStadiums />} />
                  <Route path="users" element={<AdminUsers />} />
                  <Route path="sales" element={<AdminSales />} />
                </Route>

                {/* Public Routes */}
                <Route path="/" element={<Layout><Index /></Layout>} />
                <Route path="/matches" element={<Layout><Matches /></Layout>} />
                <Route path="/matches/:id" element={<Layout><MatchDetail /></Layout>} />
                <Route path="/stadiums" element={<Layout><Stadiums /></Layout>} />
                <Route path="/stadiums/:id" element={<Layout><StadiumDetail /></Layout>} />
                <Route path="/teams" element={<Layout><Teams /></Layout>} />
                <Route path="/teams/:id" element={<Layout><TeamDetail /></Layout>} />
                <Route path="/groups" element={<Layout><Groups /></Layout>} />
                <Route path="/standings" element={<Layout><Standings /></Layout>} />
                <Route path="/quiz" element={<Layout><Quiz /></Layout>} />
                <Route path="/historia" element={<Layout><WorldCupHistory /></Layout>} />
                <Route path="/historia/:year" element={<Layout><WorldCupDetail /></Layout>} />
                <Route path="/qualified" element={<Layout><Qualified /></Layout>} />
                <Route path="/cart" element={<Layout><Cart /></Layout>} />
                <Route path="/login" element={<Layout><Login /></Layout>} />
                <Route path="/register" element={<Layout><Register /></Layout>} />
                <Route path="/checkout" element={<Layout><Checkout /></Layout>} />
                <Route path="/payment-confirmation" element={<Layout><PaymentConfirmation /></Layout>} />
                <Route path="/ticket/verify/:id" element={<Layout><TicketVerify /></Layout>} />
                {/* Story 2.6 / F6 — Flow Visualizer em tempo real (Gateway YARP → SQL). */}
                <Route path="/flow" element={<Layout><Flow /></Layout>} />
                {/* Story 4.6 / Grande Final — Diploma vivo (telemetria real do aluno). */}
                <Route path="/diploma" element={<Layout><Diploma /></Layout>} />
                <Route path="/profile" element={<Layout><Profile /></Layout>} />
                <Route path="*" element={<Layout><NotFound /></Layout>} />
              </Routes>
            </Suspense>
          </BrowserRouter>
        </CartProvider>
      </AuthProvider>
      </MsalProvider>
    </TooltipProvider>
  </QueryClientProvider>
);

export default App;
