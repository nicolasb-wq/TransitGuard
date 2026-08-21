import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import './styles.css';
import { registriereServiceWorker } from './sw-registrierung';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);

// Offline-Hülle registrieren. Der Worker cacht ausschließlich Build-Dateien —
// niemals Meldungsdaten (siehe public/sw.js, Allowlist-Prinzip).
registriereServiceWorker();
