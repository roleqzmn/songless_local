using SpotiSognless.Components;
using SpotiSognless.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<SpotifyOptions>(builder.Configuration.GetSection("Spotify"));
builder.Services.AddHttpClient("Spotify");
builder.Services.AddHttpClient<SpotifyApiClient>();
builder.Services.AddSingleton<SpotifyAuthService>();
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddSingleton<GameHistoryStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();
app.MapStaticAssets();

app.MapGet("/auth/login", (SpotifyAuthService spotifyAuth) =>
    Results.Redirect(spotifyAuth.CreateAuthorizationUrl()));

app.MapGet("/auth/callback", async (
    string? code,
    string? state,
    string? error,
    SpotifyAuthService spotifyAuth,
    HttpContext context,
    ILogger<Program> logger) =>
{
    if (!string.IsNullOrWhiteSpace(error))
    {
        var message = $"Spotify odmówiło logowania: {error}";
        return Results.Redirect($"/?spotifyError={Uri.EscapeDataString(message)}");
    }

    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
    {
        return Results.BadRequest("Brakuje kodu lub stanu OAuth.");
    }

    try
    {
        await spotifyAuth.CompleteLoginAsync(code, state, context.RequestAborted);
        return Results.Redirect("/");
    }
    catch (InvalidOperationException exception)
    {
        return Results.Redirect($"/?spotifyError={Uri.EscapeDataString(exception.Message)}");
    }
    catch (SpotifyApiException exception)
    {
        logger.LogWarning(exception, "Spotify login could not be completed.");
        return Results.Redirect($"/?spotifyError={Uri.EscapeDataString(exception.Message)}");
    }
    catch (HttpRequestException exception)
    {
        logger.LogError(exception, "Request to Spotify's token endpoint failed.");
        return Results.Redirect(
            "/?spotifyError=Nie%20uda%C5%82o%20si%C4%99%20po%C5%82%C4%85czy%C4%87%20ze%20Spotify.");
    }
});

app.MapGet("/auth/logout", (SpotifyAuthService spotifyAuth) =>
{
    spotifyAuth.Logout();
    return Results.Redirect("/");
});

app.MapGet("/api/spotify/token", async (
    SpotifyAuthService spotifyAuth,
    CancellationToken cancellationToken) =>
{
    try
    {
        var accessToken = await spotifyAuth.GetAccessTokenAsync(cancellationToken);
        return Results.Ok(new { accessToken });
    }
    catch (InvalidOperationException)
    {
        return Results.Unauthorized();
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
