import React, { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { ShoppingCart, User, Menu, X, Trophy, Ticket, MapPin, Calendar, Users, BarChart3, Brain, History, Award } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useCart } from '@/contexts/CartContext';
import { useAuth } from '@/contexts/AuthContext';
import { LoginV2Button } from '@/components/LoginV2Button';
import { cn } from '@/lib/utils';

const navLinks = [
  { href: '/matches', label: 'Jogos', icon: Calendar },
  { href: '/groups', label: 'Grupos', icon: Users },
  { href: '/standings', label: 'Tabela', icon: BarChart3 },
  { href: '/qualified', label: 'Classificados', icon: Ticket },
  { href: '/stadiums', label: 'Estádios', icon: MapPin },
  { href: '/teams', label: 'Seleções', icon: Trophy },
  { href: '/quiz', label: 'Quiz', icon: Brain },
  { href: '/historia', label: 'História', icon: History },
  // Story 4.6 / Grande Final — Diploma vivo (clímax da aula). Aditivo: não altera nenhuma
  // rota/fluxo existente (AC-9); dá descoberta ao Diploma sem depender só da URL do workflow.
  { href: '/diploma', label: 'Diploma', icon: Award },
];

export const Navbar: React.FC = () => {
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const location = useLocation();
  const { totalItems } = useCart();
  const { isAuthenticated, user } = useAuth();

  return (
    <header className="fixed top-0 left-0 right-0 z-50 bg-background/95 backdrop-blur-md border-b border-border/50">
      <div className="container mx-auto px-4">
        <nav className="flex items-center justify-between h-16 md:h-20">
          {/* Logo */}
          <Link
            to="/"
            className="flex items-center gap-2 group"
          >
            <div className="w-10 h-10 rounded-full bg-gradient-gold flex items-center justify-center shadow-gold">
              <Trophy className="w-5 h-5 text-primary-foreground" />
            </div>
            <div className="hidden sm:block">
              <span className="font-display text-2xl text-gradient">FIFA 2026</span>
              <span className="block text-xs text-muted-foreground -mt-1">World Cup Tickets</span>
            </div>
          </Link>

          {/* Desktop Navigation */}
          <div className="hidden md:flex items-center gap-1">
            {navLinks.map(({ href, label, icon: Icon }) => (
              <Link
                key={href}
                to={href}
                className={cn(
                  "px-4 py-2 rounded-lg text-sm font-medium transition-all duration-200 flex items-center gap-2",
                  location.pathname === href
                    ? "bg-primary/10 text-primary"
                    : "text-muted-foreground hover:text-foreground hover:bg-secondary"
                )}
              >
                <Icon className="w-4 h-4" />
                {label}
              </Link>
            ))}
          </div>

          {/* Actions */}
          <div className="flex items-center gap-2">
            {/* Story 2.3 / F3 — Login v2 (Entra OIDC), paralelo ao login v1 abaixo. */}
            <LoginV2Button />

            {/* Cart */}
            <Link to="/cart">
              <Button
                variant="ghost"
                size="icon"
                className="relative"
              >
                <ShoppingCart className="w-5 h-5" />
                {totalItems > 0 && (
                  <span className="absolute -top-1 -right-1 w-5 h-5 rounded-full bg-primary text-primary-foreground text-xs flex items-center justify-center font-bold animate-scale-in">
                    {totalItems}
                  </span>
                )}
              </Button>
            </Link>

            {/* User */}
            <Link to={isAuthenticated ? '/profile' : '/login'}>
              <Button
                variant={isAuthenticated ? 'default' : 'outline'}
                size="sm"
                className={cn(
                  "hidden sm:flex gap-2",
                  isAuthenticated 
                    ? "bg-gradient-to-r from-[hsl(45,100%,50%)] to-[hsl(35,100%,45%)] hover:opacity-90 text-[hsl(220,20%,4%)]" 
                    : "border-[hsl(45,100%,50%)] text-[hsl(45,100%,50%)] hover:bg-[hsl(45,100%,50%)]/10"
                )}
              >
                <User className="w-4 h-4" />
                <span className="hidden md:inline">
                  {isAuthenticated ? user?.name : 'Entrar'}
                </span>
              </Button>
              <Button
                variant="ghost"
                size="icon"
                className="sm:hidden text-[hsl(45,100%,50%)] hover:text-[hsl(45,100%,50%)] hover:bg-[hsl(45,100%,50%)]/10"
              >
                <User className="w-5 h-5" />
              </Button>
            </Link>

            {/* Mobile Menu Toggle */}
            <Button
              variant="ghost"
              size="icon"
              className="md:hidden"
              onClick={() => setIsMenuOpen(!isMenuOpen)}
            >
              {isMenuOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
            </Button>
          </div>
        </nav>

        {/* Mobile Menu */}
        {isMenuOpen && (
          <div className="md:hidden py-4 border-t border-border animate-fade-in">
            <div className="flex flex-col gap-2">
              {navLinks.map(({ href, label, icon: Icon }) => (
                <Link
                  key={href}
                  to={href}
                  onClick={() => setIsMenuOpen(false)}
                  className={cn(
                    "px-4 py-3 rounded-lg text-sm font-medium transition-all duration-200 flex items-center gap-3",
                    location.pathname === href
                      ? "bg-primary/10 text-primary"
                      : "text-muted-foreground hover:text-foreground hover:bg-secondary"
                  )}
                >
                  <Icon className="w-5 h-5" />
                  {label}
                </Link>
              ))}
            </div>
          </div>
        )}
      </div>
    </header>
  );
};
