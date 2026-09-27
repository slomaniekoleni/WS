using Microsoft.AspNetCore.Mvc;
using Ws.Core.Scheduling;

namespace Ws.Api.Controllers;

/// <summary>Rate-limit policy names (configured in Program.cs).</summary>
public static class RateLimits
{
    /// <summary>Each chat message costs a Claude call.</summary>
    public const string Chat = "chat";
    /// <summary>Against password guessing.</summary>
    public const string Login = "login";
}

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>400 with a field error, same shape as automatic model validation errors.</summary>
    protected ActionResult Invalid(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));

    /// <summary>Maps a booking failure to an HTTP problem; `type` carries the code the frontend switches on.</summary>
    protected ActionResult BookingProblem(BookingError error) => error switch
    {
        BookingError.NotFound => Problem(statusCode: 404, title: "Not found", type: error.ToString()),
        BookingError.SlotTaken => Problem(statusCode: 409, title: "This time is no longer available", type: error.ToString()),
        BookingError.InvalidState => Problem(statusCode: 409, title: "Booking is not in a state that allows this", type: error.ToString()),
        _ => Problem(statusCode: 400, title: error.ToString(), type: error.ToString()),
    };
}
