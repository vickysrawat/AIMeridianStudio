import {
  ApplicationConfig,
  importProvidersFrom,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { provideAnimations } from '@angular/platform-browser/animations';
import {
  Activity,
  AlertCircle,
  Clipboard,
  Plus,
  Scale,
  SlidersHorizontal,
  ArrowRight,
  BarChart2,
  BarChart3,
  BookMarked,
  BookOpen,
  Bot,
  Check,
  CheckCheck,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Circle,
  Clock,
  Code2,
  Copy,
  Cpu,
  Database,
  DollarSign,
  Download,
  FileCode2,
  FileText,
  CheckCircle,
  CheckCircle2,
  GitBranch,
  GitCompare,
  Gauge,
  Lightbulb,
  Globe,
  Layers,
  ListChecks,
  Loader2,
  LucideAngularModule,
  Maximize2,
  MessageCircle,
  Mic,
  Minimize2,
  Minus,
  Moon,
  MousePointerClick,
  Package,
  Users,
  Monitor,
  Pause,
  Pencil,
  Play,
  Radar,
  RefreshCw,
  RotateCcw,
  Save,
  Search,
  Send,
  Server,
  Settings2,
  Shield,
  ShieldAlert,
  ShieldCheck,
  Sparkles,
  Square,
  StepForward,
  Sun,
  Tag,
  Target,
  Terminal,
  Trash2,
  TrendingUp,
  User,
  Wand,
  X,
  XCircle,
  Zap,
} from 'lucide-angular';
import { routes } from './app.routes';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { loadingInterceptor } from './core/interceptors/loading.interceptor';
import { API_BASE_URL } from './core/tokens/api-base-url.token';

export const appConfig: ApplicationConfig = {
  providers: [
    provideAnimations(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding(), withViewTransitions({
      // Suppress InvalidStateError when a second transition fires before the first completes.
      // This happens when signals update rapidly (e.g. during blueprint streaming).
      onViewTransitionCreated: ({ transition }) => { transition.ready.catch(() => {}); }
    })),
    provideHttpClient(withInterceptors([loadingInterceptor, errorInterceptor])),
    { provide: API_BASE_URL, useValue: 'http://localhost:5000' },

    // Register every Lucide icon used across all components in one place.
    // importProvidersFrom converts ModuleWithProviders → standalone-safe providers.
    importProvidersFrom(
      LucideAngularModule.pick({
        Activity, AlertCircle, ArrowRight, BarChart2, BarChart3, BookMarked, BookOpen, Bot, Clipboard,
        Check, CheckCheck, ChevronDown, ChevronLeft, ChevronRight, ChevronUp, Circle, Clock, Code2,
        CheckCircle, CheckCircle2, Copy, Cpu, Database, DollarSign, Download, FileCode2, FileText,
        GitBranch, GitCompare, Gauge, Lightbulb, Globe, Layers, ListChecks, Loader2, Maximize2, MessageCircle, Mic, Minimize2,
        Minus, Monitor, Moon, MousePointerClick, Package, Pause,
        Pencil, Play, Radar, RefreshCw, RotateCcw, Save, Search, Send, Server, Settings2,
        Plus, Scale, Shield, ShieldAlert, ShieldCheck, SlidersHorizontal, Sparkles, Square, StepForward, Sun, Tag, Target, Terminal,
        Trash2, TrendingUp, User, Users, Wand, X, XCircle, Zap,
      }),
    ),
  ],
};
