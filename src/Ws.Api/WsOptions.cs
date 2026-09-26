namespace Ws.Api;

/// <summary>App settings; every value can be overridden with env vars, e.g. Ws__DatabasePath=/data/ws.db</summary>
public sealed class WsOptions
{
    public const string Section = "Ws";

    public string DatabasePath { get; set; } = "data/ws.db";
    /// <summary>Phase 1 serves a single salon. Phase 2 resolves it per request (domain/header) instead.</summary>
    public int SalonId { get; set; } = 1;
    /// <summary>Structured JSON logs (servers) vs readable console lines (local).</summary>
    public bool LogJson { get; set; }
    /// <summary>Origins allowed to call the API from a browser (the Vite dev server locally).</summary>
    public string[] CorsOrigins { get; set; } = [];
}
