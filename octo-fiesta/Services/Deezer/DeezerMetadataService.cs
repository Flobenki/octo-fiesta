using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Models.Download;
using octo_fiesta.Models.Search;
using octo_fiesta.Models.Subsonic;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace octo_fiesta.Services.Deezer;

/// <summary>
/// Metadata service implementation using the Deezer API (free, no key required)
/// </summary>
public class DeezerMetadataService : IMusicMetadataService
{
    private readonly HttpClient _httpClient;
    private readonly SubsonicSettings _settings;
    private const string BaseUrl = "https://api.deezer.com";

    public DeezerMetadataService(IHttpClientFactory httpClientFactory, IOptions<SubsonicSettings> settings)
    {
        _httpClient = httpClientFactory.CreateClient();
        _settings = settings.Value;
    }

    public async Task<List<Song>> SearchSongsAsync(string query, int limit = 20)
    {
        try
        {
            var url = $"{BaseUrl}/search/track?q={Uri.EscapeDataString(query)}&limit={limit}";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode) return new List<Song>();
            
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(json);
            
            var songs = new List<Song>();
            if (result.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var track in data.EnumerateArray())
                {
                    var unvalidatedSong = ParseDeezerTrack(track);
                    var song = Song.TryBuild(unvalidatedSong);
                    if (song is not null && ShouldIncludeSong(song))
                    {
                        songs.Add(song);
                    }
                }
            }
            
            return songs;
        }
        catch
        {
            return new List<Song>();
        }
    }

    public async Task<List<Album>> SearchAlbumsAsync(string query, int limit = 20)
    {
        try
        {
            var url = $"{BaseUrl}/search/album?q={Uri.EscapeDataString(query)}&limit={limit}";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode) return new List<Album>();
            
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(json);
            
            var albums = new List<Album>();
            if (result.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var album in data.EnumerateArray())
                {
                    albums.Add(ParseDeezerAlbum(album));
                }
            }
            
            return albums;
        }
        catch
        {
            return new List<Album>();
        }
    }

    public async Task<List<Artist>> SearchArtistsAsync(string query, int limit = 20)
    {
        try
        {
            var url = $"{BaseUrl}/search/artist?q={Uri.EscapeDataString(query)}&limit={limit}";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode) return new List<Artist>();
            
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(json);
            
            var artists = new List<Artist>();
            if (result.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var artist in data.EnumerateArray())
                {
                    artists.Add(ParseDeezerArtist(artist));
                }
            }
            
            return artists;
        }
        catch
        {
            return new List<Artist>();
        }
    }

    public async Task<SearchResult> SearchAllAsync(string query, int songLimit = 20, int albumLimit = 20, int artistLimit = 20)
    {
        // Execute searches in parallel
        var songsTask = SearchSongsAsync(query, songLimit);
        var albumsTask = SearchAlbumsAsync(query, albumLimit);
        var artistsTask = SearchArtistsAsync(query, artistLimit);
        
        await Task.WhenAll(songsTask, albumsTask, artistsTask);
        
        return new SearchResult
        {
            Songs = await songsTask,
            Albums = await albumsTask,
            Artists = await artistsTask
        };
    }

    public async Task<Song?> GetSongAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "deezer") return null;
        
        var url = $"{BaseUrl}/track/{externalId}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) return null;
        
        var json = await response.Content.ReadAsStringAsync();
        var track = JsonDocument.Parse(json).RootElement;
        
        if (track.TryGetProperty("error", out _)) return null;
        
        // For an individual track, get full metadata
        var unvalidatedSong = ParseDeezerTrack(track);
        
        // Enrich with album metadata (genre, album title, cover, etc.)
        if (track.TryGetProperty("album", out var albumRef) &&
            albumRef.TryGetProperty("id", out var albumIdEl))
        {
            var albumId = albumIdEl.GetInt64().ToString();
            try
            {
                var albumUrl = $"{BaseUrl}/album/{albumId}";
                var albumResponse = await _httpClient.GetAsync(albumUrl);
                if (albumResponse.IsSuccessStatusCode)
                {
                    var albumJson = await albumResponse.Content.ReadAsStringAsync();
                    var albumData = JsonDocument.Parse(albumJson).RootElement;
                    var enrichmentAlbum = ParseDeezerAlbum(albumData);

                    unvalidatedSong = unvalidatedSong.EnrichFromAlbum(enrichmentAlbum);
                }
            }
            catch
            {
                // If we can't get the album, continue with track info only
            }
        }
        
        return Song.TryBuild(unvalidatedSong);
    }

    public async Task<Album?> GetAlbumAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "deezer") return null;
        
        var url = $"{BaseUrl}/album/{externalId}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) return null;
        
        var json = await response.Content.ReadAsStringAsync();
        var albumElement = JsonDocument.Parse(json).RootElement;
        
        if (albumElement.TryGetProperty("error", out _)) return null;
        
        var album = ParseDeezerAlbum(albumElement);
        
        // Get album songs
        if (albumElement.TryGetProperty("tracks", out var tracks) &&
            tracks.TryGetProperty("data", out var tracksData))
        {
            foreach (var track in tracksData.EnumerateArray())
            {
                var unvalidatedSong = ParseDeezerTrack(track);
                
                // Ensure album metadata is set (tracks in album response may not have full album object)
                unvalidatedSong = unvalidatedSong.EnrichFromAlbum(album);

                // Validate that required parameters are set and values are valid
                var song = Song.TryBuild(unvalidatedSong);

                if (song is not null && ShouldIncludeSong(song))
                {
                    album.Songs.Add(song);
                }
            }
        }
        
        return album;
    }

    public async Task<Artist?> GetArtistAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "deezer") return null;
        
        var url = $"{BaseUrl}/artist/{externalId}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) return null;
        
        var json = await response.Content.ReadAsStringAsync();
        var artist = JsonDocument.Parse(json).RootElement;
        
        if (artist.TryGetProperty("error", out _)) return null;
        
        return ParseDeezerArtist(artist);
    }

    public async Task<List<Album>> GetArtistAlbumsAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "deezer") return new List<Album>();
        
        var url = $"{BaseUrl}/artist/{externalId}/albums";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) return new List<Album>();
        
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(json);
        
        var albums = new List<Album>();
        if (result.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var album in data.EnumerateArray())
            {
                albums.Add(ParseDeezerAlbum(album));
            }
        }
        
        return albums;
    }

    /// <summary>
    /// Parses a Deezer track with all available metadata
    /// Used for GetSongAsync which returns complete data
    /// </summary>
    private UnvalidatedSong ParseDeezerTrack(JsonElement track)
    {
        string? externalId = track.TryGetProperty("id", out var idEl)
            ? idEl.GetInt64().ToString()
            : null;

        string? mainArtistName = null;
        string? mainArtistId = null;
        if (track.TryGetProperty("artist", out var mainArtistEl))
        {
            mainArtistName = mainArtistEl.TryGetProperty("name", out var n)
                ? n.GetString()
                : null;
            mainArtistId = mainArtistEl.TryGetProperty("id", out var i)
                ? $"ext-deezer-artist-{i.GetInt64()}"
                : null;
        }

        var artists = new List<(string? Id, string? Name)>();
        var contributors = new List<(string? Role, string? SubRole, string? ArtistId, string? ArtistName)>();
        if (!string.IsNullOrWhiteSpace(mainArtistName))
            artists.Add((mainArtistId, mainArtistName));
        else
            mainArtistName = null;

        if (track.TryGetProperty("contributors", out var contribs))
        {
            foreach (var contrib in contribs.EnumerateArray())
            {
                string? contribName = contrib.TryGetProperty("name", out var cn)
                    ? cn.GetString()
                    : null;
                string? contribId = contrib.TryGetProperty("id", out var cid)
                    ? $"ext-deezer-artist-{cid.GetInt64()}"
                    : null;
                string? deezerRole = contrib.TryGetProperty("role", out var cr)
                    ? cr.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(contribName)) continue;

                switch (deezerRole)
                {
                    case "Main":
                        break;
                    case "Featured":
                        artists.Add((contribId, contribName));
                        break;
                    case "Composer":
                        contributors.Add(("composer", null, contribId, contribName));
                        break;
                    case "Author":
                        contributors.Add(("lyricist", null, contribId, contribName));
                        break;
                    case "Producer":
                        contributors.Add(("producer", null, contribId, contribName));
                        break;
                    case "Mixer":
                        contributors.Add(("mixer", null, contribId, contribName));
                        break;
                    default:
                        contributors.Add(("performer", deezerRole, contribId, contribName));
                        break;
                }
            }
        }

        // album element
        JsonElement? albumElement = track.TryGetProperty("album", out var album)
            ? album
            : null;

        // albumId
        string? albumId = albumElement?.TryGetProperty("id", out var aid) == true
            ? $"ext-deezer-album-{aid.GetInt64()}"
            : null;

        // Release date from album
        int? year = null;
        if (track.TryGetProperty("release_date", out var relDate))
        {
            var releaseDate = relDate.GetString();
            if (!string.IsNullOrEmpty(releaseDate) && releaseDate.Length >= 4)
            {
                if (int.TryParse(releaseDate.Substring(0, 4), out var y))
                    year = y;
            }
        }
        else if (track.TryGetProperty("album", out var albumForDate) && albumForDate.TryGetProperty("release_date", out var albumRelDate))
        {
            var releaseDate = albumRelDate.GetString();
            if (!string.IsNullOrEmpty(releaseDate) && releaseDate.Length >= 4)
            {
                if (int.TryParse(releaseDate.Substring(0, 4), out var y))
                    year = y;
            }
        }

        // Explicit content status value
        string? explicitStatus = track.TryGetProperty("explicit_content_lyrics", out var es) 
            ? es.GetInt32() switch
            {
                0 => "",
                1 => "explicit",
                3 => "clean",
                _ => null
            }
            : null;

        // gain
        double? deezerGain = track.TryGetProperty("gain", out var gainValue) && gainValue.ValueKind == JsonValueKind.Number
            ? gainValue.GetDouble()
            : null;


        return new UnvalidatedSong(
            Id: externalId is not null
                ? $"ext-deezer-song-{externalId}"
                : null,
            Isrc: track.TryGetProperty("isrc", out var isrcList) && isrcList.GetString() is { } isrcValue
                ? new[] {isrcValue}
                : null,
            MusicBrainzId: null,

            Title: track.TryGetProperty("title", out var titleValue)
                ? titleValue.GetString()
                : null,
            SortName: null,
            Artist: mainArtistName,
            ArtistId: mainArtistId,
            Artists: artists,
            DisplayArtist: null,
            Contributors: contributors,
            DisplayComposer: null,

            AlbumTitle: albumElement?.TryGetProperty("title", out var albumTitle) == true
                ? albumTitle.GetString()
                : null,
            AlbumId: albumId,
            AlbumArtist: albumElement?.TryGetProperty("artist", out var aaEl) == true && aaEl.TryGetProperty("name", out var aaName)
                ? aaName.GetString()
                : null,
            AlbumArtists: null,
            DisplayAlbumArtist: null,
            AlbumDiscNr: track.TryGetProperty("disk_number", out var discNum)
                ? discNum.GetInt32()
                : null,
            AlbumTrackNr: track.TryGetProperty("track_position", out var trackPos)
                ? trackPos.GetInt32()
                : null,

            CoverArtId: albumId,

            Genre: null,
            Genres: null,
            Moods: null,
            ReleaseYear: year,
            ExplicitStatus: explicitStatus,

            Size: null,
            Duration: track.TryGetProperty("duration", out var durationValue)
                ? durationValue.GetInt32()
                : null,
            BitRate: null,
            BitDepth: null,
            SamplingRate: null,
            ChannelCount: null,
            Bpm: track.TryGetProperty("bpm", out var bpmValue) && bpmValue.ValueKind == JsonValueKind.Number
                ? (int)bpmValue.GetDouble() 
                : null,
            ReplayGain: (deezerGain, null, null, null, null),

            ContentType: null,
            Suffix: null,
            TranscodedContentType: null,
            TranscodedSuffix: null,

            Works: null,
            Movements: null,

            Type: "music",
            MediaType: "song",
            IsDir: false,
            IsVideo: false,
            
            IsLocal: false,
            ExternalProvider: "deezer",
            ExternalId: externalId,
            LocalPath: null,
            CoverArtUrl: albumElement?.TryGetProperty("cover_medium", out var cm) == true
                ? cm.GetString()
                : null,
            CoverArtUrlLarge: albumElement?.TryGetProperty("cover_xl", out var cxl) == true
                ? cxl.GetString()
                : (albumElement?.TryGetProperty("cover_big", out var cb) == true
                    ? cb.GetString()
                    : null)
        );
    }

    private Album ParseDeezerAlbum(JsonElement album)
    {
        var externalId = album.GetProperty("id").GetInt64().ToString();
        
        return new Album
        {
            Id = $"ext-deezer-album-{externalId}",
            Title = album.GetProperty("title").GetString() ?? "",
            Artist = album.TryGetProperty("artist", out var artist) 
                ? artist.GetProperty("name").GetString() ?? "" 
                : "",
            ArtistId = album.TryGetProperty("artist", out var artistForId) 
                ? $"ext-deezer-artist-{artistForId.GetProperty("id").GetInt64()}" 
                : null,
            Year = album.TryGetProperty("release_date", out var releaseDate) 
                ? int.TryParse(releaseDate.GetString()?.Split('-')[0], out var year) ? year : null
                : null,
            SongCount = album.TryGetProperty("nb_tracks", out var nbTracks) 
                ? nbTracks.GetInt32() 
                : null,
            CoverArtUrl = album.TryGetProperty("cover_medium", out var cover)
                ? cover.GetString()
                : null,
            CoverArtUrlLarge = album.TryGetProperty("cover_xl", out var coverXl)
                ? coverXl.GetString()
                : (album.TryGetProperty("cover_big", out var coverBig)
                    ? coverBig.GetString()
                    : null),
            Genre = album.TryGetProperty("genres", out var genres) && 
                    genres.TryGetProperty("data", out var genresData) &&
                    genresData.GetArrayLength() > 0
                ? genresData[0].GetProperty("name").GetString()
                : null,
            ReleaseType = album.TryGetProperty("record_type", out var recordType) 
                ? recordType.GetString() 
                : null,
            IsLocal = false,
            ExternalProvider = "deezer",
            ExternalId = externalId
        };
    }

    private Artist ParseDeezerArtist(JsonElement artist)
    {
        var externalId = artist.GetProperty("id").GetInt64().ToString();
        
        return new Artist
        {
            Id = $"ext-deezer-artist-{externalId}",
            Name = artist.GetProperty("name").GetString() ?? "",
            ImageUrl = artist.TryGetProperty("picture_big", out var pictureBig) 
                ? pictureBig.GetString() 
                : (artist.TryGetProperty("picture_medium", out var picture) 
                    ? picture.GetString()
                    : null),
            AlbumCount = artist.TryGetProperty("nb_album", out var nbAlbum) 
                ? nbAlbum.GetInt32() 
                : null,
            IsLocal = false,
            ExternalProvider = "deezer",
            ExternalId = externalId
        };
    }

    public async Task<List<ExternalPlaylist>> SearchPlaylistsAsync(string query, int limit = 20)
    {
        try
        {
            var url = $"{BaseUrl}/search/playlist?q={Uri.EscapeDataString(query)}&limit={limit}";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode) return new List<ExternalPlaylist>();
            
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(json);
            
            var playlists = new List<ExternalPlaylist>();
            if (result.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var playlist in data.EnumerateArray())
                {
                    playlists.Add(ParseDeezerPlaylist(playlist));
                }
            }
            
            return playlists;
        }
        catch
        {
            return new List<ExternalPlaylist>();
        }
    }
    
    public async Task<ExternalPlaylist?> GetPlaylistAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "deezer") return null;
        
        try
        {
            var url = $"{BaseUrl}/playlist/{externalId}";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode) return null;
            
            var json = await response.Content.ReadAsStringAsync();
            var playlistElement = JsonDocument.Parse(json).RootElement;
            
            if (playlistElement.TryGetProperty("error", out _)) return null;
            
            return ParseDeezerPlaylist(playlistElement);
        }
        catch
        {
            return null;
        }
    }
    
    public async Task<List<Song>> GetPlaylistTracksAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "deezer") return new List<Song>();
        
        try
        {
            var url = $"{BaseUrl}/playlist/{externalId}";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode) return new List<Song>();
            
            var json = await response.Content.ReadAsStringAsync();
            var playlistElement = JsonDocument.Parse(json).RootElement;
            
            if (playlistElement.TryGetProperty("error", out _)) return new List<Song>();
            
            var songs = new List<Song>();
            
            // Get playlist name for album field
            var playlistName = playlistElement.TryGetProperty("title", out var titleEl)
                ? titleEl.GetString() ?? "Unknown Playlist"
                : "Unknown Playlist";

            // Deezer playlist/{id} embeds at most 400 tracks in tracks.data.
            // Use the dedicated tracklist endpoint and follow pagination to load all tracks.
            if (playlistElement.TryGetProperty("tracklist", out var tracklistEl))
            {
                var tracklistUrl = tracklistEl.GetString();
                if (!string.IsNullOrWhiteSpace(tracklistUrl))
                {
                    var nextPageUrl = $"{tracklistUrl}?limit=1000";

                    while (!string.IsNullOrWhiteSpace(nextPageUrl))
                    {
                        var tracklistResponse = await _httpClient.GetAsync(nextPageUrl);
                        if (!tracklistResponse.IsSuccessStatusCode)
                        {
                            break;
                        }

                        var tracklistJson = await tracklistResponse.Content.ReadAsStringAsync();
                        var tracklistElement = JsonDocument.Parse(tracklistJson).RootElement;

                        if (!tracklistElement.TryGetProperty("data", out var pageTracks))
                        {
                            break;
                        }
                        
                        foreach (var track in pageTracks.EnumerateArray())
                        {
                            var unvalidatedSong = ParseDeezerTrack(track);

                            // Override album name to be the playlist name and track number for correct sort order
                            unvalidatedSong = unvalidatedSong with
                            {
                                AlbumTitle = playlistName,
                                AlbumTrackNr = songs.Count + 1
                            };
                       
                            var song = Song.TryBuild(unvalidatedSong);

                            if (song is not null && ShouldIncludeSong(song))
                            {
                                songs.Add(song);
                            }
                        }

                        nextPageUrl = tracklistElement.TryGetProperty("next", out var nextEl)
                            ? nextEl.GetString()
                            : null;
                    }

                    return songs;
                }
            }

            // Fallback for unexpected Deezer responses without tracklist URL.
            if (playlistElement.TryGetProperty("tracks", out var tracks) &&
                tracks.TryGetProperty("data", out var tracksData))
            {
                foreach (var track in tracksData.EnumerateArray())
                {
                    var unvalidatedSong = ParseDeezerTrack(track);

                    // Override album name to be the playlist name and track number for correct sort order
                    unvalidatedSong = unvalidatedSong with
                    {
                        AlbumTitle = playlistName,
                        AlbumTrackNr = songs.Count + 1
                    };
                
                    var song = Song.TryBuild(unvalidatedSong);

                    if (song is not null && ShouldIncludeSong(song))
                    {
                        songs.Add(song);
                    }
                }
            }
            
            return songs;
        }
        catch
        {
            return new List<Song>();
        }
    }

    private ExternalPlaylist ParseDeezerPlaylist(JsonElement playlist)
    {
        var externalId = playlist.GetProperty("id").GetInt64().ToString();
        
        // Get curator/creator name
        string? curatorName = null;
        if (playlist.TryGetProperty("user", out var user) &&
            user.TryGetProperty("name", out var userName))
        {
            curatorName = userName.GetString();
        }
        else if (playlist.TryGetProperty("creator", out var creator) &&
                 creator.TryGetProperty("name", out var creatorName))
        {
            curatorName = creatorName.GetString();
        }
        
        // Get creation date
        DateTime? createdDate = null;
        if (playlist.TryGetProperty("creation_date", out var creationDateEl))
        {
            var dateStr = creationDateEl.GetString();
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var date))
            {
                createdDate = date;
            }
        }
        
        return new ExternalPlaylist
        {
            Id = Common.PlaylistIdHelper.CreatePlaylistId("deezer", externalId),
            Name = playlist.GetProperty("title").GetString() ?? "",
            Description = playlist.TryGetProperty("description", out var desc) 
                ? desc.GetString() 
                : null,
            CuratorName = curatorName,
            Provider = "deezer",
            ExternalId = externalId,
            TrackCount = playlist.TryGetProperty("nb_tracks", out var nbTracks) 
                ? nbTracks.GetInt32() 
                : 0,
            Duration = playlist.TryGetProperty("duration", out var duration) 
                ? duration.GetInt32() 
                : 0,
            CoverUrl = playlist.TryGetProperty("picture_big", out var pictureBig) 
                    ? pictureBig.GetString() 
                    : (playlist.TryGetProperty("picture_medium", out var picture) 
                    ? picture.GetString()
                    : null),
            CreatedDate = createdDate
        };
    }

    /// <summary>
    /// Determines whether a song should be included based on the explicit content filter setting
    /// </summary>
    /// <param name="song">The song to check</param>
    /// <returns>True if the song should be included, false otherwise</returns>
    private bool ShouldIncludeSong(Song song)
    {
        return _settings.ExplicitFilter switch
        {
            // All: No filtering, include everything
            ExplicitFilter.All => true,
            
            // ExplicitOnly: Exclude clean/edited versions
            ExplicitFilter.ExplicitOnly => song.ExplicitStatus != "clean",
            
            // CleanOnly: Only show clean content
            ExplicitFilter.CleanOnly => song.ExplicitStatus != "explicit",
            
            _ => true
        };
    }
}
