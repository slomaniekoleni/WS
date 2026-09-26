# WS

Website + AI receptionist for a tattoo & piercing salon (Wise City demo).

```bash
dotnet run --project src/Ws.Api --launch-profile http   # API on http://localhost:5154, DB in src/Ws.Api/data/ws.db
npm install --prefix web && npm run dev --prefix web     # site on http://localhost:5173 (proxies /api)
dotnet test Ws.slnx
docker compose up --build                                # site + API on http://localhost:8080
```

Public API: `GET /api/salon|services|artists?lang=en|ru`, `GET /api/availability?serviceId=&artistId=&from=YYYY-MM-DD&to=`, `POST /api/bookings`.
