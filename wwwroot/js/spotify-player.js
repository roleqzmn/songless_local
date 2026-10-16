let player;
let deviceId;
let sdkPromise;

function delay(milliseconds) {
    return new Promise(resolve => window.setTimeout(resolve, milliseconds));
}

async function getAccessToken() {
    const response = await fetch("/api/spotify/token", { cache: "no-store" });
    if (!response.ok) {
        throw new Error("Sesja Spotify wygasła. Połącz konto ponownie.");
    }

    const result = await response.json();
    return result.accessToken;
}

function loadSdk() {
    if (window.Spotify?.Player) {
        return Promise.resolve();
    }

    if (!sdkPromise) {
        sdkPromise = new Promise((resolve, reject) => {
            window.onSpotifyWebPlaybackSDKReady = resolve;

            const script = document.createElement("script");
            script.src = "https://sdk.scdn.co/spotify-player.js";
            script.async = true;
            script.onerror = () => reject(new Error("Nie udało się załadować odtwarzacza Spotify."));
            document.head.appendChild(script);
        });
    }

    return sdkPromise;
}

async function waitForDevice() {
    const deadline = Date.now() + 15_000;
    while (!deviceId && Date.now() < deadline) {
        await delay(100);
    }

    if (!deviceId) {
        throw new Error("Spotify nie uruchomiło odtwarzacza. Spróbuj ponownie.");
    }

    return deviceId;
}

async function connectPlayer() {
    await loadSdk();

    if (!player) {
        player = new window.Spotify.Player({
            name: "Na ucho",
            getOAuthToken: async callback => {
                try {
                    callback(await getAccessToken());
                } catch (error) {
                    console.error("Spotify token request failed.", error);
                }
            },
            volume: 0.7,
            enableMediaSession: false
        });

        player.addListener("ready", ({ device_id }) => {
            deviceId = device_id;
        });
        player.addListener("not_ready", ({ device_id }) => {
            if (deviceId === device_id) {
                deviceId = undefined;
            }
        });
        player.addListener("initialization_error", ({ message }) => {
            console.error("Spotify player initialization failed.", message);
        });
        player.addListener("authentication_error", ({ message }) => {
            console.error("Spotify player authentication failed.", message);
        });
        player.addListener("account_error", ({ message }) => {
            console.error("Spotify account cannot use the player.", message);
        });
        player.addListener("playback_error", ({ message }) => {
            console.error("Spotify playback failed.", message);
        });
    }

    const connected = await player.connect();
    if (!connected && !deviceId) {
        throw new Error("Nie udało się połączyć odtwarzacza Spotify.");
    }

    return waitForDevice();
}

export async function playSnippet(uri, durationMilliseconds) {
    const activeDeviceId = await connectPlayer();
    const accessToken = await getAccessToken();
    const response = await fetch(
        `https://api.spotify.com/v1/me/player/play?device_id=${encodeURIComponent(activeDeviceId)}`,
        {
            method: "PUT",
            headers: {
                Authorization: `Bearer ${accessToken}`,
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                uris: [uri],
                position_ms: 0
            })
        });

    if (!response.ok) {
        if (response.status === 403) {
            throw new Error("Spotify nie pozwoliło rozpocząć odtwarzania. Sprawdź konto Premium i uprawnienia aplikacji.");
        }

        throw new Error(`Spotify nie rozpoczęło utworu (HTTP ${response.status}).`);
    }

    const deadline = Date.now() + 15_000;

    while (Date.now() < deadline) {
        const state = await player.getCurrentState();
        const currentTrack = state?.track_window?.current_track;

        if (currentTrack?.uri === uri && !state.paused) {
            await delay(durationMilliseconds);
            await player.pause();
            return;
        }

        await delay(50);
    }

    await player.pause();
    throw new Error("Spotify nie rozpoczęło odtwarzania na czas. Spróbuj ponownie.");
}

export async function pause() {
    if (player) {
        await player.pause();
    }
}

export async function disconnect() {
    if (player) {
        await player.pause();
        player.disconnect();
        player = undefined;
        deviceId = undefined;
    }
}
