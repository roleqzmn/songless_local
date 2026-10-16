# Na ucho

Lokalna gra w rozpoznawanie utworów z polubionej biblioteki Spotify. Aplikacja używa Blazora po stronie serwera; odtwarzanie w przeglądarce obsługuje Spotify Web Playback SDK.

## Uruchomienie

1. W ustawieniach aplikacji Spotify dodaj dokładny adres przekierowania:
   `http://127.0.0.1:5237/auth/callback`
2. Sprawdź `Spotify:ClientId` i `Spotify:RedirectUri` w `appsettings.json`.
3. Uruchom aplikację poleceniem `dotnet run` i otwórz `http://127.0.0.1:5237`.
4. Połącz Spotify i zezwól aplikacji na dostęp do polubionych utworów oraz odtwarzanie.

Logowanie korzysta z OAuth Authorization Code z PKCE. Client ID jest publiczny; aplikacja nie wymaga client secret. Tokeny są przechowywane w pamięci procesu i trzeba zalogować się ponownie po jego zamknięciu. Wyniki gry są zapisywane w `%LocalAppData%\SpotiSognless\game-history.json`.

Odtwarzanie w Web Playback SDK wymaga Spotify Premium. Aplikacja binduje się lokalnie pod `127.0.0.1:5237`.
