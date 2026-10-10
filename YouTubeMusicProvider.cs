using System.Text.Json;
using System.Web;
using Lavalink4NET;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using PlexBot.Core.Exceptions;
using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Services.Music;
using PlexBot.Utils;

namespace PlexBot.Extensions.YouTube;

/// <summary>YouTube music provider using Lavalink for search and URL playback.
/// Supports YouTube search, single video URLs, and playlist URLs.</summary>
public class YouTubeMusicProvider(IAudioService audioService) : IMusicProvider
{
    public string Id => "youtube";
    public string DisplayName => "YouTube";
    public bool IsAvailable => true;
    public int Priority => 10;
    public MusicProviderCapabilities Capabilities =>
        MusicProviderCapabilities.Search | MusicProviderCapabilities.UrlPlayback;

    private const string LookupFailedMessage =
        "YouTube lookup failed. Check that youtube-plugin is loaded on Lavalink (see the extension README).";

    private const string PluginName = "youtube-plugin";
    private const int PluginCheckAttempts = 5;
    private static readonly TimeSpan PluginCheckRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PluginCheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Search YouTube via Lavalink's YouTube search mode</summary>
    public async Task<SearchResults> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        Logs.Info($"[YouTube] Searching for: '{query}' with mode={TrackSearchMode.YouTube}");

        TrackLoadResult searchResults = await audioService.Tracks.LoadTracksAsync(
            query, TrackSearchMode.YouTube, cancellationToken: cts.Token);

        Logs.Info($"[YouTube] LoadResult: IsSuccess={searchResults.IsSuccess}, " +
                  $"HasMatches={searchResults.HasMatches}, " +
                  $"IsPlaylist={searchResults.IsPlaylist}, " +
                  $"TrackCount={searchResults.Tracks.Length}");

        if (!searchResults.IsSuccess || searchResults.IsFailed)
        {
            Logs.Warning($"[YouTube] Search failed — IsFailed={searchResults.IsFailed}, " +
                         $"Exception={searchResults.Exception}, Query='{query}'");
        }

        if (IsLoadFailure(searchResults))
        {
            throw new PlayerException(
                $"youtube-plugin search failed for '{query}'. Check that youtube-plugin is loaded on Lavalink " +
                $"(see the extension README). Lavalink: {DescribeLoadFailure(searchResults)}",
                "Search", LookupFailedMessage);
        }

        SearchResults results = new() { Query = query, SourceSystem = "youtube" };

        foreach (LavalinkTrack lt in searchResults.Tracks.Take(25))
        {
            results.Tracks.Add(CreateTrackFromLavalink(lt));
        }

        Logs.Info($"[YouTube] Returning {results.Tracks.Count} tracks for query '{query}'");
        return results;
    }

    /// <summary>Resolve a YouTube track by URL through Lavalink</summary>
    public async Task<Track?> GetTrackDetailsAsync(string trackKey, CancellationToken ct = default)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        TrackLoadResult result = await audioService.Tracks.LoadTracksAsync(
            trackKey, TrackSearchMode.None, cancellationToken: cts.Token);

        LavalinkTrack? lt = result.Track;
        return lt == null ? null : CreateTrackFromLavalink(lt, trackKey);
    }

    /// <summary>Claims youtube.com and youtu.be URLs</summary>
    public bool CanHandleUrl(Uri uri)
    {
        string host = uri.Host.ToLowerInvariant();
        return host.Contains("youtube.com") || host.Contains("youtu.be");
    }

    /// <summary>Resolves a YouTube URL into playable tracks.
    /// Handles both single videos and playlists.</summary>
    public async Task<List<Track>> ResolveUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        Uri parsedUri = new(url);
        bool isPlaylist = IsPlaylistUrl(parsedUri);

        TrackLoadResult loadResult = await audioService.Tracks.LoadTracksAsync(
            url, TrackSearchMode.None, cancellationToken: cts.Token);

        if (IsLoadFailure(loadResult))
        {
            throw new PlayerException(
                $"youtube-plugin could not load '{url}'. Check that youtube-plugin is loaded on Lavalink " +
                $"(see the extension README). Lavalink: {DescribeLoadFailure(loadResult)}",
                "Play", LookupFailedMessage);
        }

        // Handle YouTube playlists
        if (loadResult.IsPlaylist && isPlaylist)
        {
            List<LavalinkTrack> playlistTracks = [.. loadResult.Tracks];
            if (playlistTracks.Count == 0)
            {
                Logs.Warning("YouTube playlist URL resolved but contains no playable tracks");
                return [];
            }

            string playlistName = loadResult.Playlist?.Name ?? "YouTube Playlist";
            Logs.Info($"Loaded YouTube playlist '{playlistName}' with {playlistTracks.Count} tracks");

            return playlistTracks.Select(lt => CreateTrackFromLavalink(lt)).ToList();
        }

        // Single track
        if (loadResult.Track is LavalinkTrack singleTrack)
        {
            return [CreateTrackFromLavalink(singleTrack, url)];
        }

        Logs.Warning($"YouTube URL resolved but no playable content found: {url}");
        return [];
    }

    // YouTube doesn't support browsing — return empty for unsupported operations
    public Task<List<Track>> GetTracksAsync(string containerKey, CancellationToken ct = default) =>
        Task.FromResult(new List<Track>());

    public Task<List<Album>> GetAlbumsAsync(string artistKey, CancellationToken ct = default) =>
        Task.FromResult(new List<Album>());

    public Task<List<Track>> GetAllArtistTracksAsync(string artistKey, CancellationToken ct = default) =>
        Task.FromResult(new List<Track>());

    public Task<List<Playlist>> GetPlaylistsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<Playlist>());

    public Task<Playlist?> GetPlaylistDetailsAsync(string playlistKey, CancellationToken ct = default) =>
        Task.FromResult<Playlist?>(null);

    /// <summary>Creates a normalized Track model from a Lavalink track</summary>
    private static Track CreateTrackFromLavalink(LavalinkTrack lt, string? overrideUrl = null)
    {
        Track track = Track.CreateFromUrl(
            lt.Title ?? "Unknown Title",
            lt.Author ?? "Unknown",
            overrideUrl ?? lt.Uri?.ToString() ?? lt.Identifier,
            lt.ArtworkUri?.ToString() ?? "",
            "youtube");
        track.DurationMs = (long)lt.Duration.TotalMilliseconds;
        track.DurationDisplay = FormatHelper.FormatDuration(lt.Duration);
        return track;
    }

    /// <summary>Checks whether a URI points to a YouTube playlist (has a 'list' query parameter)</summary>
    private static bool IsPlaylistUrl(Uri uri)
    {
        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        return !string.IsNullOrEmpty(queryParams["list"]);
    }

    /// <summary>Lavalink reports a failed load as an error, or as no tracks with an exception.
    /// An empty result with no exception is a real "no matches" and is not a failure.</summary>
    private static bool IsLoadFailure(TrackLoadResult result) =>
        result.IsFailed || (!result.HasMatches && result.Exception is not null);

    private static string DescribeLoadFailure(TrackLoadResult result) =>
        result.Exception?.Message ?? "no error message from Lavalink";

    /// <summary>Starts the youtube-plugin check on a background thread so startup never waits on Lavalink.</summary>
    public static void StartPluginCheckInBackground() => _ = Task.Run(() => CheckPluginLoadedAsync());

    /// <summary>Asks Lavalink's /v4/info whether youtube-plugin is loaded. Makes up to five attempts, 30 seconds apart.
    /// Logs one warning if Lavalink answers without the plugin, or one info line if it never answers. Never throws.</summary>
    private static async Task CheckPluginLoadedAsync()
    {
        try
        {
            // Same names and defaults as Main/ServiceRegistration.cs, so the check reaches the node the bot uses.
            string password = EnvConfig.Get("LAVALINK_SERVER_PASSWORD", "youshallnotpass");
            string host = EnvConfig.Get("LAVALINK_HOST", "lavalink");
            string port = EnvConfig.Get("LAVALINK_SERVER_PORT", "2333");
            string scheme = EnvConfig.GetBool("LAVALINK_SECURE", false) ? "https" : "http";
            Uri infoUri = new($"{scheme}://{host}:{port}/v4/info");

            using HttpClient http = new() { Timeout = PluginCheckTimeout };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", password);

            string lastProblem = "no response";
            for (int attempt = 1; attempt <= PluginCheckAttempts; attempt++)
            {
                try
                {
                    using HttpResponseMessage response = await http.GetAsync(infoUri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        if (ListsPlugin(json, PluginName))
                        {
                            Logs.Info($"[YouTube] {PluginName} is loaded on Lavalink");
                        }
                        else
                        {
                            Logs.Warning($"[YouTube] {PluginName} is not loaded on Lavalink, so YouTube search and links will fail. " +
                                         "Fix: from the PlexBot folder run Install/start.sh --build, then see the extension README.");
                        }
                        return;
                    }

                    lastProblem = $"HTTP {(int)response.StatusCode}";
                }
                catch (Exception ex)
                {
                    lastProblem = ex.Message;
                }

                Logs.Debug($"[YouTube] Lavalink /v4/info check {attempt}/{PluginCheckAttempts} failed: {lastProblem}");
                if (attempt < PluginCheckAttempts)
                {
                    await Task.Delay(PluginCheckRetryDelay);
                }
            }

            Logs.Info($"[YouTube] Could not reach Lavalink /v4/info after {PluginCheckAttempts} attempts ({lastProblem}), " +
                      "so the youtube-plugin check was skipped");
        }
        catch (Exception ex)
        {
            Logs.Warning($"[YouTube] youtube-plugin check could not run: {ex.Message}");
        }
    }

    /// <summary>True when Lavalink's /v4/info JSON lists a plugin with the given name.</summary>
    private static bool ListsPlugin(string infoJson, string pluginName)
    {
        using JsonDocument document = JsonDocument.Parse(infoJson);
        if (!document.RootElement.TryGetProperty("plugins", out JsonElement plugins) ||
            plugins.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement plugin in plugins.EnumerateArray())
        {
            if (plugin.TryGetProperty("name", out JsonElement name) &&
                string.Equals(name.GetString(), pluginName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
