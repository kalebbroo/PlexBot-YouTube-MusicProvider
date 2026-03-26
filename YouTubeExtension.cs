using Microsoft.Extensions.DependencyInjection;
using PlexBot.Core.Extensions;
using PlexBot.Core.Services.Music;
using PlexBot.Utils;

namespace PlexBot.Extensions.YouTube;

/// <summary>Extension that adds YouTube as a music source for PlexBot.
/// Provides search via Lavalink's YouTube search and URL playback for
/// youtube.com/youtu.be links including playlist support.</summary>
public class YouTubeExtension : Extension
{
    public override string Id => "youtube-music-provider";
    public override string Name => "YouTube Music Provider";
    public override string Version => "1.0.0";
    public override string Author => "PlexBot";
    public override string Description => "Adds YouTube search and URL playback via Lavalink";

    public override void RegisterServices(IServiceCollection services)
    {
        // Register as IMusicProvider — PlexBot auto-registers it with MusicProviderRegistry
        services.AddSingleton<IMusicProvider, YouTubeMusicProvider>();
    }

    protected override Task<bool> OnInitializeAsync(IServiceProvider services)
    {
        Logs.Info($"{Name} v{Version} initialized — YouTube search and URL playback available");
        return Task.FromResult(true);
    }
}
