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
- Minsk (`Europe/Minsk`, UTC+3, no DST), prices in BYN, room has 2 workstations.

## Design (Wise City)

- **Current pick (2026-09-27, design paused):** `moss` theme with layout "moss, paper, cedar": moss only on header/hero/footer (`[data-theme='moss'] .header/.hero/.footer` re-scope the tokens), cream paper body, walnut `.band-walnut` behind the home artists. Default in `theme.ts` (localStorage key `ws-theme`).
- Four switchable themes (`web/src/theme.ts`, `?theme=bark|terracotta|shoji|moss`; `:root` holds the shoji tokens, `:root[data-theme=...]` the others). The non-moss ones are kept only for comparison; remove once the owner is sure. "Bark and earth" (default): light wood `#f3e9dc`, wood-ring pattern `#d9b99b` behind the logo, bark rust `#8b2e00` buttons/stamps, earth `#3f1c11` text, forest `#3d3b2c` intro cards. "Terracotta Japandi": warm beige `#e5d9c5`, natural panel `#cbbca4` with a clay band, terracotta `#98472a` buttons/stamps, walnut `#4a3326` kanji stamp, Playfair Display headings. "Shoji house": shoji white `#f4f0e8`, tatami panel with shoji grid, cedar brown `#8b5e3c` accents, moss labels, soft-black buttons, red `#a3201c` only as hanko stamps and brush stroke, Cormorant Garamond headings. Owner is still comparing.
- "Moss forest" theme (picked by the owner from palette mockups based on their Japanese colour references): moss green base `#2f3a2c`, oxblood buttons `--accent-fill #8e1b24`, warm red text accent `--accent`, tatami `#c9b98f` eyebrows, fog-paper text `#ece4d4`. Seigaiha waves + flowing water lines in the hero, oxblood brush stroke, small red hanko marks on section headings, vertical 達磨.
- Headings in Playfair Display (serif, has Cyrillic), body in Manrope.
- Logo: the salon's Daruma in an ensō circle (`web/public/brand/logo-original.png`; `logo-black.png` / `logo-white.png` are transparent versions, plus favicon and apple-touch-icon). Shown as black ink on a paper disc (`<Seal>`), never inverted.
- **The Daruma's painted eye is the one on the viewer's right, exactly as on the logo.** Any Daruma drawn or edited must keep that.
- Noto Serif JP only for the kanji (subset).
- Windows note: never edit non-ASCII files with PowerShell 5.1 `Get-Content`/`Set-Content` (it garbles UTF-8 like 達磨 and adds a BOM); use the Edit tool or .NET with explicit UTF-8.

## Still open

- Real salon details: address, artist names/styles/hours, prices (current BYN numbers are guesses)

## Codebase

- `src/Ws.Core`: domain (`Domain/`), EF Core `WsDbContext` + migrations + placeholder seed (`Data/`), availability + booking lifecycle (`Scheduling/`: `SlotFinder` is pure logic, `BookingService` loads data and writes)
- `src/Ws.Api`: ASP.NET Core **controllers** (owner's preference: one `[ApiController]` per resource, request/response records at the bottom of the controller file; no minimal-API endpoint files). Public in `Controllers/`, staff in `Controllers/Admin/` (`AdminControllerBase` = `[Authorize]` + salon from the cookie). Note: `{action}` is reserved in MVC routes. Migrates + seeds on startup. Settings in `Ws:*` (`WsOptions`), env override e.g. `Ws__DatabasePath`
- Staff auth: cookie `ws_staff` (HttpOnly, SameSite=Strict), PBKDF2 hashes (`Admin/StaffAuth.cs`); first owner created at startup from `Admin:Login` / `Admin:Password` (min 10 chars) when no staff users exist. Login rate-limited 10 per 5 min per IP.
- Tests: `WebApplicationFactory<Program>` HTTP tests use `UseSetting` (Program reads config before Build) and environment "Testing" so user-secrets never load. For a manual throwaway instance use a non-Development environment (on Windows an empty env var is deleted, it does not override user-secrets).
- `src/Ws.Tests`: xUnit (slot rules + BookingService on in-memory SQLite)
- `web/`: React + Vite + TS public site. `i18n.tsx` (EN/RU strings, lang in localStorage, default from browser), `data.tsx` (salon/services/artists per language), `pages/` (Home, Services, Artists, Info, Book). Times shown in the salon's time zone. Dev: `npm run dev --prefix web` (proxies /api to :5154); prod: the Docker image builds it into the API's wwwroot (SPA fallback, /api/* stays 404)
- Claude key: `dotnet user-secrets set Claude:ApiKey <key> --project src/Ws.Api` (env `Claude__ApiKey` on servers). Without it the site works and chat answers 503.
- The repo is **public** (since 2026-09-26): never commit secrets, real client data or the DB.
- Design playground: source in `design/playground/`, published to GitHub Pages from the `gh-pages` branch (https://slomaniekoleni.github.io/WS/). To update: copy the folder's files onto the `gh-pages` branch and push.
- Dev servers for the browser preview: `.claude/launch.json` (`api` on 5154, `web` on 5173)
- Times stored as UTC; working hours are local to `Salon.TimeZoneId`. Translatable text = `LocalizedText { En, Ru }` stored as JSON.
- Run: `dotnet run --project src/Ws.Api` (DB at `src/Ws.Api/data/ws.db`). Tests: `dotnet test Ws.slnx`. Docker: `docker compose up --build` (port 8080, DB in volume)
- New migration: `dotnet ef migrations add <Name> --project src/Ws.Core --startup-project src/Ws.Api --output-dir Data/Migrations`

## Milestones

1. Skeleton: solution, EF model, seed, logging, Docker <- DONE
2. Availability + booking API + tests <- DONE (approve/decline/cancel in BookingService; no endpoints yet)
3. Public site (EN/RU): services, artists/portfolio, booking form <- DONE (photos are Unsplash placeholders hotlinked from SeedData until the salon sends real work; reference images for tattoos not uploadable yet). Phones validated with libphonenumber on both sides (default region = `Salon.Country`), stored as E.164.
4. AI receptionist + web chat widget <- BUILT, not yet tested against the live API (needs Claude:ApiKey). `Ws.Core/Receptionist`: prompt (cached, catalog from DB), tools over BookingService, manual tool loop, history stored as our own JSON blocks (thinking signatures kept). Model/effort in `Receptionist:*` config (default claude-opus-5, effort medium), server-side refusal fallbacks on. Web: `/api/chat` (rate-limited 12/min/IP) + `ChatWidget.tsx`
5. Telegram: client bot, staff group (new booking -> approve/decline buttons, handoffs), 24h/2h reminders <- BUILT; staff flow verified with the real bot 2026-09-27 (website booking -> group message -> approved via button). Client AI chat in Telegram untested until the Claude key is set. Token + StaffChatId are in user-secrets (restart the API after changing secrets). Core: `Notifications/` (`IStaffNotifier` fired by BookingService for pending bookings and by Receptionist on new handoffs, `Reminders` due-query, `ClientMessages` EN/RU). Api: `Telegram/` (minimal `TelegramApi`, long-polling `TelegramBot`: private chats -> Receptionist, staff group callbacks approve/decline + client notified; `TelegramStaffNotifier` queue; `ReminderWorker` every 5 min). Staff texts are Russian. `/chatid` in a group prints its id. Artists' `TelegramUsername` (for @mentions) not in seed yet.
6. Admin panel: calendar, manage artists/services/hours, transcripts, staff login <- BUILT 2026-09-27 (`web/src/admin/`, lazy-loaded at /admin, EN/RU via `adminI18n.ts`): week calendar + pending strip, booking dialog (approve/decline/cancel/complete/no-show, staff notes; clients on Telegram get notified), staff-made bookings incl. tattoo sessions, artists (profile, one shift per weekday, services, days off), services, chat transcripts + resolve handoffs. Not yet: staff replying to web chats, artist-scoped logins, managing staff users in the UI, portfolio/photo uploads.
7. Demo polish + deploy

## Communication and working style

Follow the owner's global CLAUDE.md (casual, short, honest feedback, ask when vague, commit and push freely, ask before deleting files).

