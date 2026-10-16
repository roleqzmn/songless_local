using System.Net;

namespace SpotiSognless.Services;

public sealed class SpotifyApiException : Exception
{
    public SpotifyApiException(
        string message,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
