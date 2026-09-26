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

## Open questions (ask the owner)

- Salon details: name, address, hours, artists, services and prices, policies (deposit, cancellation, minimum age)
- Deposits/prepayment online? (would need a payment provider)
- Sync with an existing calendar (Google Calendar) or our own calendar only?
- Hosting and domain

## Communication and working style

Follow the owner's global CLAUDE.md (casual, short, honest feedback, ask when vague, commit and push freely, ask before deleting files).

