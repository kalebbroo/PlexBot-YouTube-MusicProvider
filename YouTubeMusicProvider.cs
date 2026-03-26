using System.Web;
using Lavalink4NET;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
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
}
