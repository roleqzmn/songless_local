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

## Zasady gry

Każda runda daje 7 prób. Kolejne odsłuchiwane fragmenty trwają: 0,1 s, 0,5 s, 1 s, 2 s, 4 s, 8 s i 16 s. Błędna odpowiedź lub pominięcie odsłania następny fragment. Podpowiedzi wyszukują polubione utwory po tytule i wykonawcy, a okładka albumu pojawia się po ujawnieniu odpowiedzi.
