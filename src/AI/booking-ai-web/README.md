# Booking AI Web

A minimal Vue 3 + Vite client for the AI Orchestration service. Two panes:

- **Conversation** — chat with the assistant; when it proposes a write (booking / cancellation) a **Confirm / Decline** card appears (the confirmation gate).
- **Results** — the catalog list (`GET /api/catalog/catalogs`) and bookings (`GET /api/bookings`), refreshed after every confirmed action.

## Run

1. Start the backend so the **AI Orchestration** and **API Gateway** are up.
   - Easiest: run the Aspire AppHost (`dotnet run` in `src/Orchestration/BookingSystem.AppHost`). Note the two ports from the dashboard.
   - The orchestration must have an Anthropic key (user-secret in Development) — see the service's README.

2. Point the dev proxy at those ports (defaults: AI `http://localhost:5000`, gateway `http://localhost:64963`). Override if different:

   ```bash
   # optional — create .env.local
   VITE_AI_TARGET=http://localhost:5000
   VITE_GW_TARGET=http://localhost:64963
   ```

3. Install and run:

   ```bash
   npm install
   npm run dev
   ```

   Open http://localhost:5173.

The browser only ever calls `/ai/*` and `/gw/*` on the Vite dev server, which proxies to the backend — so no CORS configuration is needed on the services.
