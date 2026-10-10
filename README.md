# PlexBot-YouTube-MusicProvider

## What it does

This extension adds YouTube to PlexBot as a music source. It plays YouTube through Lavalink's youtube-plugin.

- **Search.** `/search mode:youtube` searches YouTube and lists the results.
- **Links.** `/play` accepts a YouTube link to a single video or a playlist, and PlexBot queues the tracks.

The extension does not play audio itself. It asks Lavalink, the audio server PlexBot already uses, to look up YouTube.

## Why a plugin

The youtube-source README says that Lavaplayer's built-in YouTube source is deprecated, and names youtube-source as its replacement. On our test node (Lavalink 4.2.2), the built-in source failed on both search and direct URLs with "Something went wrong while looking up the track." We tested this on Lavalink 4.2.2 only. This extension uses youtube-plugin instead, and turns the built-in source off.

## Requirements

- PlexBot with Lavalink 4.x. PlexBot's Docker stack uses the `lavalink:4` image.
- Internet access for the Lavalink container. Lavalink downloads the youtube-plugin jar when it starts.
- Git, to clone this repository.

## Install

1. Clone this repository into PlexBot's `Extensions/` folder. The folder name must not end in `.disabled`, because that marks an extension as switched off.

   ```bash
   cd /path/to/PlexBot/Extensions
   git clone https://github.com/kalebbroo/PlexBot-YouTube-MusicProvider.git
   ```

2. From the PlexBot folder, rebuild and start the stack.

   ```bash
   cd /path/to/PlexBot
   Install/start.sh --build
   ```

`start.sh` regenerates `Install/Docker/lavalink.application.yml` from the base Lavalink config and the `lavalink.plugin.yml` fragment of each enabled extension. This fragment adds the plugin to Lavalink, adds its settings, and turns off the built-in YouTube source. With `--build`, `start.sh` then runs `docker compose up -d --build --force-recreate`, which recreates the Lavalink container so that it loads the plugin.

Only `--build` recreates Lavalink. Running `Install/start.sh` without it regenerates the config but keeps the existing Lavalink container, which keeps the config it started with. Use `--build` every time you install or change this extension.

## Verify

1. Check that Lavalink lists the plugin. Replace the placeholder with the `LAVALINK_SERVER_PASSWORD` value from your `.env` file. If you changed `LAVALINK_SERVER_PORT`, use that port.

   ```bash
   curl -s -H "Authorization: YOUR_LAVALINK_PASSWORD" http://localhost:2333/v4/info
   ```

   The `plugins` list in the reply should include an entry named `youtube-plugin`.

2. Check search. In Discord, run `/search mode:youtube query:<anything>`. You should get a list of results, not "No Results" or "Search Error".

3. Check the startup log. `Install/start.sh --logs` should not show a warning that `youtube-plugin` is not loaded. The bot checks Lavalink at startup, trying up to five times, 30 seconds apart, so give it a couple of minutes.

## Configuration

The settings live in `lavalink.plugin.yml`, under `pluginConfig:`. The generator moves that block to the top-level `plugins:` key of `Install/Docker/lavalink.application.yml`, which is the file Lavalink reads. The values as Lavalink receives them are:

```yaml
plugins:
  youtube:
    enabled: true
    allowSearch: true
    allowDirectVideoIds: true
    allowDirectPlaylistIds: true
    clients:
      - TVHTML5_SIMPLY
      - MUSIC
      - ANDROID_VR
      - WEB
      - WEBEMBEDDED
    oauth:
      enabled: false
      refreshToken: ""
```

The option descriptions follow the 1.18.2 README of youtube-source:

| Option | What it does |
|---|---|
| `enabled` | Whether this source can be used. |
| `allowSearch` | Whether "ytsearch:" and "ytmsearch:" can be used. `/search mode:youtube` needs this set to `true`. |
| `allowDirectVideoIds` | Whether just video IDs can match. If false, only complete URLs will be loaded. |
| `allowDirectPlaylistIds` | Whether just playlist IDs can match. If false, only complete URLs will be loaded. |
| `clients` | The clients to use for track loading. Clients are queried in the order they are given, so the first client is queried first and so on. The client table is in the [youtube-source README](https://github.com/lavalink-devs/youtube-source/blob/1.18.2/README.md#available-clients). |
| `oauth` | Optional sign-in, off by default. See Optional OAuth below. |

Two more points:

- The `sources: youtube: false` block in the same fragment turns off Lavalink's built-in YouTube source. It overrides the `youtube: true` in `Install/Docker/lavalink.base.yml`. Keep it, because the plugin needs the built-in source off.
- Existing Lavalink options, such as `youtubePlaylistLoadLimit`, are picked up by the plugin automatically. This stack sets that value in `Install/Docker/lavalink.base.yml`.

## Optional OAuth

OAuth is off by default. Turn it on only if searches or links fail with an error that YouTube wants you to sign in to confirm you're not a bot.

> **Warning:** OAuth is not a silver bullet. The worst case is that YouTube terminates the account you sign in with. Use a burner account, not your main account. It can also cause rate-limit errors under heavy use.

1. In `lavalink.plugin.yml`, change `oauth: enabled: false` to `enabled: true`. Leave `refreshToken: ""` as it is.
2. Rebuild: `Install/start.sh --build`.
3. Watch the Lavalink log with `docker logs -f LavaLink`. The plugin prints a Google sign-in URL and a code. Open the URL, sign in with the burner account, and enter the code.
4. When the sign-in completes, the plugin logs your refresh token. This stack's Lavalink log level is INFO (`Install/Docker/lavalink.base.yml`), so the message should appear. If it does not, the youtube-source README says to set the log level of `dev.lavalink.youtube.http.YoutubeOauth2Handler` to INFO.
5. Optional: to skip the sign-in on later starts, paste the token into `refreshToken:` in `lavalink.plugin.yml` and run `Install/start.sh --build`. A valid refresh token skips the flow entirely.

With `enabled: true` and no refresh token, Lavalink starts the sign-in flow on every boot, so steps 3 and 4 repeat each time. Step 5 avoids that.

> **Keep the refresh token private.** It works like a password for the account. `lavalink.plugin.yml` is tracked in git, so never commit a file that contains a token, and never push one.

## Updating

The plugin version is pinned in `lavalink.plugin.yml`:

```yaml
plugin:
  dependency: "dev.lavalink.youtube:youtube-plugin:1.18.2"
```

To update:

1. Read the release notes at https://github.com/lavalink-devs/youtube-source/releases.
2. Change the version in `lavalink.plugin.yml`.
3. Check the `clients` list against the client table for the new version. The youtube-source README advises checking the client list periodically, because clients can be deprecated or removed.
4. Run `Install/start.sh --build`, then follow the Verify steps.

## Troubleshooting

| Symptom | What to check |
|---|---|
| `/search mode:youtube` shows "YouTube lookup failed" | Lavalink could not load from YouTube. Run the Verify steps. If `youtube-plugin` is not listed, install or fix the plugin (see the rows below). If it is listed, check `docker logs LavaLink` for errors from the YouTube source, and check `Install/start.sh --logs` for `[YouTube]` lines. |
| `/search mode:youtube` finds nothing | YouTube returned no matches for that query. Try another search, and run the Verify steps if every search returns nothing. |
| Error says YouTube wants you to sign in to confirm you're not a bot | YouTube flagged the requests. Turn on OAuth with a burner account (see Optional OAuth). |
| Search and links work, but tracks fail to play. The logs show `Suspicious: ... All clients failed to load the item` or `Loading formats ... missing fields` | YouTube is refusing the stream requests from this server. This happens to some videos and can start after many requests in a short time. Wait before retrying. If it continues, turn on OAuth with a burner account (see Optional OAuth). The bot skips a track that fails to play and does not post a message in Discord. |
| `/v4/info` does not list `youtube-plugin` | Lavalink did not load the plugin. Check that the extension folder is in `Extensions/` and does not end in `.disabled`. Run `Install/start.sh --build`. Check that `Install/Docker/lavalink.application.yml` lists the `youtube-plugin` dependency. Check `docker logs LavaLink` for download errors, because the container needs internet access at startup. |
| The built-in YouTube source is still enabled | In `Install/Docker/lavalink.application.yml`, `lavalink.server.sources.youtube` must be `false`. If it is `true`, the config was not regenerated. Run `Install/start.sh --build`. |
| The bot warns at startup that `youtube-plugin` is not loaded | Lavalink answered, but without the plugin. Follow the fix in the warning: run `Install/start.sh --build`, then work through the rows above. |
| The bot logs "Could not reach Lavalink /v4/info" | The startup check was skipped. Lavalink may not be running, or `LAVALINK_HOST`, `LAVALINK_SERVER_PORT` or `LAVALINK_SERVER_PASSWORD` in `.env` may be wrong. The log line gives the last reason. For example, `HTTP 401` means the password is wrong. The bot keeps running. |

## License and links

- License: MIT. See [LICENSE](LICENSE).
- youtube-plugin: https://github.com/lavalink-devs/youtube-source (README and releases)
- Lavalink: https://github.com/lavalink-devs/Lavalink
- PlexBot: https://github.com/kalebbroo/PlexBot
