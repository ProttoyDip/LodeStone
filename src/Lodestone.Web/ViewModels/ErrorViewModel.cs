namespace Lodestone.Web.ViewModels;

/// <summary>
/// What an error page is allowed to say. Deliberately carries no exception detail, stack trace or
/// request data: this page is rendered for anonymous visitors and for students in distress.
/// </summary>
public sealed record ErrorViewModel(int StatusCode, string Title, string Message)
{
    public string StatusLabel => StatusCode > 0 ? $"Error {StatusCode}" : "Error";

    /// <summary>A student who hits a dead end should still be one click from help.</summary>
    public bool IsCrisisRelevant => true;

    public static ErrorViewModel ForStatusCode(int? statusCode)
        => statusCode switch
        {
            404 => new ErrorViewModel(404, "We could not find that page",
                "The link may be out of date, or the item may have been removed."),
            403 => new ErrorViewModel(403, "You do not have access to that page",
                "Your account does not have permission to open it."),
            429 => new ErrorViewModel(429, "Too many requests",
                "Please wait a moment and try again."),
            null or 0 or 500 => new ErrorViewModel(500, "Something went wrong",
                "The problem has been logged. Please try again, and tell us if it keeps happening."),
            _ => new ErrorViewModel(statusCode.Value, "Something went wrong",
                "The request could not be completed. Please try again.")
        };
}
