import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import App from './app/App';
import { SessionProvider } from './features/session/SessionProvider';
import '@fontsource/be-vietnam-pro/400.css';
import '@fontsource/be-vietnam-pro/700.css';
import './styles/fonts.css';
import './styles/tokens.css';
import './styles/global.css';
// History and leaderboard pages show a loader while refetching; skip the refetch on every app resume or tab focus.
const queryClient = new QueryClient({ defaultOptions: { queries: { refetchOnWindowFocus: false } } });
createRoot(document.getElementById('root')!).render(<StrictMode><QueryClientProvider client={queryClient}><BrowserRouter><SessionProvider><App /></SessionProvider></BrowserRouter></QueryClientProvider></StrictMode>);
