import React, { useState, useCallback, useEffect } from 'react';
import { useMsal, useIsAuthenticated } from '@azure/msal-react';
import api, { type ApiUser } from '@/lib/api';
import { AuthContext, type User, type Order } from '@/contexts/AuthContext';
// Story 3.5 (ADE-007 Inv 8) — unificação v1 ↔ CIAM no FRONT: a sessão CIAM (Login v2)
// vira sessão de auth de primeira classe via /api/v2/me (resolve-or-provision).
import { isEntraConfigured, msalInstance } from '@/lib/authV2';
import { meV2 } from '@/lib/apiV2';

const STORAGE_KEY = 'copa2026_user';
const ORDERS_KEY = 'copa2026_orders';

function mapApiUser(raw: ApiUser): User {
  return {
    id: String(raw?.id ?? ''),
    email: String(raw?.email ?? ''),
    name: String(raw?.name ?? ''),
    role: raw?.role,
    createdAt: raw?.created_at || raw?.createdAt || new Date().toISOString(),
  };
}

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [orders, setOrders] = useState<Order[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  // Story 3.5 — estado da sessão CIAM (Login v2), lido do MsalProvider que envolve a app.
  const { accounts } = useMsal();
  const isCiamAuthenticated = useIsAuthenticated();

  // Bootstrap auth + orders
  useEffect(() => {
    const init = async () => {
      // Load cached orders
      const storedOrders = localStorage.getItem(ORDERS_KEY);
      if (storedOrders) {
        try {
          setOrders(JSON.parse(storedOrders));
        } catch {
          localStorage.removeItem(ORDERS_KEY);
        }
      }

      // Load cached user (quick UI)
      const cachedUser = localStorage.getItem(STORAGE_KEY);
      if (cachedUser) {
        try {
          setUser(JSON.parse(cachedUser));
        } catch {
          localStorage.removeItem(STORAGE_KEY);
        }
      }

      // If we have a token, validate/refresh user from API
      const token = localStorage.getItem('auth_token');
      if (token) {
        const meRes = await api.getMe();
        if (meRes.data?.user) {
          setUser(mapApiUser(meRes.data.user));
        } else {
          // Only force logout on explicit token errors; keep cached user if server is offline
          const msg = meRes.error || '';
          if (/token/i.test(msg) || /n[ãa]o fornecido/i.test(msg) || /inv[áa]lido/i.test(msg)) {
            api.logout();
            setUser(null);
          }
        }
      }

      setIsLoading(false);
    };

    init();
  }, []);

  // ---------------------------------------------------------------------------
  // Story 3.5 (ADE-007 Inv 8) — UNIFICAÇÃO v1 ↔ CIAM no FRONT. Havendo sessão CIAM
  // (Login v2) e NÃO havendo sessão v1, hidrata o AuthContext a partir do /api/v2/me
  // (resolve-or-provision → users.id) + os claims da conta CIAM. Assim o cliente nato-CIAM
  // vira usuário autenticado de primeira classe e passa o gate v1 de /checkout e /admin.
  // ADITIVO: o v1 tem PRECEDÊNCIA (prev ?? ...); labs sem CIAM ou sem /me = no-op (retro-compat
  // Oitavas/Quartas). Falha do /me (409/422/rede) → não hidrata → o gate segue como antes.
  // ---------------------------------------------------------------------------
  useEffect(() => {
    if (!isEntraConfigured()) return;         // lab sem CIAM: no-op
    if (user) return;                          // v1 (ou já hidratado) tem precedência
    const account = accounts[0];
    if (!account) return;                      // sem sessão CIAM: nada a fazer

    let cancelled = false;
    (async () => {
      const me = await meV2();                 // resolve/vincula/provisiona o users.id
      if (cancelled || !me.userId || !Number.isFinite(me.userId)) return;
      setUser((prev) => prev ?? {
        id: String(me.userId),
        email: account.username ?? '',
        name: account.name ?? account.username ?? '',
        role: 'user',
        createdAt: new Date().toISOString(),
      });
    })();
    return () => { cancelled = true; };
  }, [accounts, isCiamAuthenticated, user]);

  // Sessão CIAM encerrada e SEM sessão v1 → limpa o usuário hidratado do CIAM.
  useEffect(() => {
    if (!isEntraConfigured()) return;
    if (!isCiamAuthenticated && user && !localStorage.getItem('auth_token')) {
      setUser(null);
    }
  }, [isCiamAuthenticated, user]);

  // Persist user
  useEffect(() => {
    if (user) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    } else {
      localStorage.removeItem(STORAGE_KEY);
    }
  }, [user]);

  // Persist orders
  useEffect(() => {
    localStorage.setItem(ORDERS_KEY, JSON.stringify(orders));
  }, [orders]);

  const login = useCallback(async (email: string, password: string): Promise<User | null> => {
    const result = await api.login(email, password);
    if (result.data?.user) {
      // Best effort to load full profile (created_at)
      const meRes = await api.getMe();
      const mapped = meRes.data?.user ? mapApiUser(meRes.data.user) : mapApiUser(result.data.user);
      setUser(mapped);
      return mapped;
    }
    return null;
  }, []);

  const register = useCallback(async (name: string, email: string, password: string): Promise<User | null> => {
    const result = await api.register(name, email, password);
    if (result.data?.user) {
      const meRes = await api.getMe();
      const mapped = meRes.data?.user ? mapApiUser(meRes.data.user) : mapApiUser(result.data.user);
      setUser(mapped);
      return mapped;
    }
    return null;
  }, []);

  const logout = useCallback(() => {
    // Se a sessão ativa veio do CIAM (sem token v1), encerra TAMBÉM o MSAL CIAM — senão o
    // effect de hidratação re-loga o usuário logo em seguida.
    const wasCiamOnly =
      isEntraConfigured() &&
      !localStorage.getItem('auth_token') &&
      msalInstance.getAllAccounts().length > 0;
    api.logout();
    setUser(null);
    if (wasCiamOnly) {
      msalInstance.logoutPopup().catch(() => { /* usuário fechou o popup — ok */ });
    }
  }, []);

  const updateProfile = useCallback(async (data: Partial<User>): Promise<boolean> => {
    if (!user) return false;
    if (!data.name) return true;

    const res = await api.updateProfile({ name: data.name });
    if (res.data?.user) {
      setUser((prev) => (prev ? { ...prev, ...mapApiUser(res.data!.user) } : mapApiUser(res.data!.user)));
      return true;
    }

    return false;
  }, [user]);

  const addOrder = useCallback((order: Omit<Order, 'id' | 'userId' | 'createdAt'>) => {
    if (!user) return;

    const newOrder: Order = {
      ...order,
      id: `order-${Date.now()}`,
      userId: String(user.id),
      createdAt: new Date().toISOString(),
    };

    setOrders((prev) => [newOrder, ...prev]);
  }, [user]);

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated: !!user,
        isLoading,
        login,
        register,
        logout,
        updateProfile,
        orders: orders.filter((o) => o.userId === String(user?.id)),
        addOrder,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};
