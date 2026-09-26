# WS (salon website + AI receptionist)

Website + AI receptionist for a tattoo & piercing salon: answers client questions, books appointments, sends reminders.

## Goal and scope

- **Phase 1 (now):** demo for one real salon the owner knows. Real artists, services, prices and hours once the salon provides them; placeholder data until then.
- **Phase 2 (later):** a multi-salon product. Design for it now (keep salon-specific data behind a `SalonId` / config, no hard-coded salon details), but don't build multi-tenancy yet.
- **Channels:** website (booking form + AI chat widget) and Telegram bot now. Instagram DMs and WhatsApp later: keep the receptionist logic channel-agnostic so new channels are thin adapters.
- **Languages:** English and Russian (UI and AI replies). The AI answers in the client's language.

## Stack and decisions

- Backend: C# on .NET 10 (LTS), ASP.NET Core Web API, EF Core (SQLite for dev, Postgres later)
- Frontend: React + Vite + TypeScript (public site + staff admin panel)
- AI: Claude API with tool use (the receptionist calls backend tools: list services, check availability, create/cancel booking, salon info/FAQ). Default to the latest Claude models.
- Telegram: Telegram Bot API (same receptionist behind it)
- Secrets (Claude API key, Telegram token) in `dotnet user-secrets` locally, env vars on servers; never in code or git
- Docker-ready, structured logging, same conventions as the owner's STS project (E:\STS; repo: E:\WS -> https://github.com/slomaniekoleni/WS (private, push as slomaniekoleni))

## What the salon domain needs (MVP)

- **Artists:** profiles, styles, portfolio images, working hours, days off
- **Services:** tattoo consultation, tattoo session (duration depends on size), piercing types (fixed durations/prices), touch-ups; which artists do which services
- **Booking:** slot availability per artist + service duration + buffers; consultation-first flow for tattoos (size/placement/style/reference photo), direct booking for piercings; cancel / reschedule
- **AI receptionist:** answers FAQ (prices, aftercare, age rules/ID, deposits, pain, healing), collects tattoo idea details, suggests an artist by style, books via tools, hands off to a human when unsure
- **Reminders:** 24h and 2h before the appointment (Telegram first; email later)
- **Staff admin panel:** calendar of bookings, manage artists/services/hours, see chat transcripts

## Owner decisions (2026-09-26)

- Salon name: **Wise City**. 5 artists: 3 tattoo, 2 piercers. Everything else (names, prices, hours, address) is placeholder, filled by us until the salon sends real data.
- Age: 16-17 only with a parent/guardian, 18+ solo. (Our addition: nipple/intimate piercings 18+ only.)
- No online deposits/payments for now: booking only.
- Own calendar, no Google sync. One shared room: artists normally work side by side, but an intimate procedure takes the whole room (modeled as `Room.Workstations` + `Service.NeedsPrivateRoom`).
- Every client booking starts **Pending** and needs the artist's approval (holds the slot meanwhile). Staff-created bookings are confirmed immediately.
- Staff notifications + approvals: Telegram staff group.
- Hosting: just Docker for now, decide later.

## Still open

- Real salon details: city/time zone (placeholder Europe/Warsaw), currency (placeholder EUR), address, artists, prices, number of workstations in the room (placeholder 3)

## Codebase

- `src/Ws.Core`: domain (`Domain/`), EF Core `WsDbContext` + migrations + placeholder seed (`Data/`), availability + booking lifecycle (`Scheduling/`: `SlotFinder` is pure logic, `BookingService` loads data and writes)
- `src/Ws.Api`: ASP.NET Core minimal APIs (`Endpoints/`), migrates + seeds on startup. Settings in `Ws:*` (`WsOptions`), env override e.g. `Ws__DatabasePath`
- `src/Ws.Tests`: xUnit (slot rules + BookingService on in-memory SQLite)
- Times stored as UTC; working hours are local to `Salon.TimeZoneId`. Translatable text = `LocalizedText { En, Ru }` stored as JSON.
- Run: `dotnet run --project src/Ws.Api` (DB at `src/Ws.Api/data/ws.db`). Tests: `dotnet test Ws.slnx`. Docker: `docker compose up --build` (port 8080, DB in volume)
- New migration: `dotnet ef migrations add <Name> --project src/Ws.Core --startup-project src/Ws.Api --output-dir Data/Migrations`

## Milestones

1. Skeleton: solution, EF model, seed, logging, Docker <- DONE
2. Availability + booking API + tests <- DONE (approve/decline/cancel in BookingService; no endpoints yet)
3. Public site (EN/RU): services, artists/portfolio, booking form <- NEXT
4. AI receptionist + web chat widget
5. Telegram: client bot, staff group (new booking -> approve/decline buttons, handoffs), 24h/2h reminders
6. Admin panel: calendar, manage artists/services/hours, transcripts, staff login
7. Demo polish + deploy

## Communication and working style

Follow the owner's global CLAUDE.md (casual, short, honest feedback, ask when vague, commit and push freely, ask before deleting files).

