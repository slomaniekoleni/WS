# WS

Website + AI receptionist for a tattoo & piercing salon (Wise City demo).

```bash
dotnet run --project src/Ws.Api     # API on the port from launchSettings; DB in src/Ws.Api/data/ws.db
dotnet test Ws.slnx
docker compose up --build           # API on http://localhost:8080
```

Public API: `GET /api/salon|services|artists?lang=en|ru`, `GET /api/availability?serviceId=&artistId=&from=YYYY-MM-DD&to=`, `POST /api/bookings`.
