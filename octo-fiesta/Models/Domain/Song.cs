namespace octo_fiesta.Models.Domain;

/// <summary>
/// Represents a song (local or external) according to the openSubsonicAPI
/// </summary>
public record Song(
    string Id,
    IReadOnlyList<string>? Isrc,
    string? MusicBrainzId,

    string Title,
    string? SortName,
    string? Artist,
    string? ArtistId,
    IReadOnlyList<ArtistRef>? Artists,
    string? DisplayArtist,
    IReadOnlyList<Contributor>? Contributors,
    string? DisplayComposer,

    string? AlbumTitle,
    string? AlbumId,
    string? AlbumArtist,
    IReadOnlyList<ArtistRef>? AlbumArtists,
    string? DisplayAlbumArtist,
    int? AlbumDiscNr,
    int? AlbumTrackNr,

    string? CoverArtId,

    string? Genre,
    IReadOnlyList<string>? Genres,
    IReadOnlyList<string>? Moods,
    int? ReleaseYear,
    string? ExplicitStatus,

    long? Size,
    int? Duration,
    int? BitRate,
    int? BitDepth,
    int? SamplingRate,
    int? ChannelCount,
    int? Bpm,
    ReplayGain? ReplayGain,

    string? ContentType,
    string? Suffix,
    string? TranscodedContentType,
    string? TranscodedSuffix,

    IReadOnlyList<Work>? Works,
    IReadOnlyList<Movement>? Movements,

    string Type,
    string MediaType,
    bool IsDir,
    bool IsVideo,

    // octo-fiesta internal:
    bool IsLocal,
    string? ExternalProvider,
    string? ExternalId,
    string? LocalPath,
    string? CoverArtUrl,
    string? CoverArtUrlLarge
)
{
    internal static Song? TryBuild(UnvalidatedSong draft)
    {
        //################ Parameter Validation ################

        // Id
        if (string.IsNullOrWhiteSpace(draft.Id)) return null;   // required parameter

        // Title
        if (string.IsNullOrWhiteSpace(draft.Title)) return null;    // required parameter

        // Type, MediaType, IsDir, IsVideo
        if (draft.Type is not ("music" or "podcast" or "audiobook" or "video")) return null;    // required parameter
        if (draft.MediaType is not ("song" or "album" or "artist" or "podcast" or "audiobook")) return null;    // required parameter
        if (draft.IsDir is not bool isDirValue) return null;    // required parameter
        if (draft.IsVideo is not bool isVideoValue) return null;    // required parameter

        // IsLocal, ExternalProvider, ExternalId, LocalPath
        if (draft.IsLocal is not bool isLocalValue) return null;    // required parameter
        var externalProvider = ValidateString(draft.ExternalProvider);
        var externalId = ValidateString(draft.ExternalId);
        var localPath = string.IsNullOrWhiteSpace(draft.LocalPath) ? null : draft.LocalPath;


        //################ Cross Parameter Validation ################
        if (isLocalValue && localPath is null) return null;
        if (!isLocalValue && (externalProvider is null || externalId is null)) return null;

        if (draft.Type == "video" && !isVideoValue) return null;
        if (draft.Type == "music" && isVideoValue) return null;

        var validCombos = (draft.Type, draft.MediaType) switch
        {
            ("music", "song") => true,
            ("music", "album") => true,
            ("music", "artist") => true,
            ("podcast", "podcast") => true,
            ("audiobook", "audiobook") => true,
            ("video", "song") => true,
            _ => false
        };
        if (!validCombos) return null;


        return new Song(
            Id: draft.Id,
            Isrc: ValidateStringList(draft.Isrc),
            MusicBrainzId: ValidateString(draft.MusicBrainzId),

            Title: draft.Id,
            SortName: ValidateString(draft.SortName),
            Artist: ValidateString(draft.Artist),
            ArtistId: ValidateString(draft.ArtistId),
            Artists: ValidateAndConvertArtistsList(draft.Artists),
            DisplayArtist: ValidateString(draft.DisplayArtist),
            Contributors: ValidateAndConvertContributors(draft.Contributors),
            DisplayComposer: ValidateString(draft.DisplayComposer),

            AlbumTitle: ValidateString(draft.AlbumTitle),
            AlbumId: ValidateString(draft.AlbumId),
            AlbumArtist: ValidateString(draft.AlbumArtist),
            AlbumArtists: ValidateAndConvertArtistsList(draft.AlbumArtists),
            DisplayAlbumArtist: ValidateString(draft.DisplayAlbumArtist),
            AlbumDiscNr: (draft.AlbumDiscNr < 1) ? null : draft.AlbumDiscNr,
            AlbumTrackNr: (draft.AlbumTrackNr < 1) ? null : draft.AlbumTrackNr,

            CoverArtId: ValidateString(draft.CoverArtId),

            Genre: ValidateString(draft.Genre),
            Genres: ValidateStringList(draft.Genres),
            Moods: ValidateStringList(draft.Moods),
            ReleaseYear: (draft.ReleaseYear < 1000) ? null : draft.ReleaseYear,
            ExplicitStatus: (draft.ExplicitStatus is not (null or "" or "clean" or "explicit")) ? null : draft.ExplicitStatus,

            Size: (draft.Size < 1) ? null : draft.Size,
            Duration: (draft.Duration < 1) ? null : draft.Duration,
            BitRate: (draft.BitRate < 1) ? null : draft.BitRate,
            BitDepth: (draft.BitDepth < 1) ? null : draft.BitDepth,
            SamplingRate: (draft.SamplingRate < 1) ? null : draft.SamplingRate,
            ChannelCount: (draft.ChannelCount < 1) ? null : draft.ChannelCount,
            Bpm: (draft.Bpm < 1) ? null : draft.Bpm,
            ReplayGain: ValidateAndConvertReplayGain(draft.ReplayGain),

            ContentType: ValidateString(draft.ContentType),
            Suffix: ValidateString(draft.Suffix),
            TranscodedContentType: ValidateString(draft.TranscodedContentType),
            TranscodedSuffix: ValidateString(draft.TranscodedSuffix),

            Works: ValidateAndConvertWorks(draft.Works),
            Movements: ValidateAndConvertMovements(draft.Movements),

            Type: draft.Type,
            MediaType: draft.MediaType,
            IsDir: isDirValue,
            IsVideo: isVideoValue,

            IsLocal: isLocalValue,
            ExternalProvider: externalProvider,
            ExternalId: externalId,
            LocalPath: localPath,
            CoverArtUrl: ValidateString(draft.CoverArtUrl),
            CoverArtUrlLarge: ValidateString(draft.CoverArtUrlLarge)
        );
    }

    private static string? ValidateString(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? null
            : s;

    private static IReadOnlyList<string>? ValidateStringList(IReadOnlyList<string>? list)
    {
        if (list is null) return null;
        if (list.Count == 0) return null;
        if (list.Any(s => string.IsNullOrWhiteSpace(s))) return null;
        return list;
    }

    private static IReadOnlyList<ArtistRef>? ValidateAndConvertArtistsList(
        IReadOnlyList<(
            string? Id,
            string? Name)>? artistsRaw)
    {
        if (artistsRaw is null) return null;
        if (artistsRaw.Count == 0) return null;

        var artistsConverted = new List<ArtistRef>();
        foreach (var (id, name) in artistsRaw)
        {
            var artistsRef = ArtistRef.TryBuild(id, name);
            if (artistsRef is null) return null;
            artistsConverted.Add(artistsRef);
        }
        return artistsConverted;
    }

    private static IReadOnlyList<Contributor>? ValidateAndConvertContributors(
        IReadOnlyList<(
        string? Role,
        string? SubRole,
        string? ArtistId,
        string? ArtistName)>? contributorsRaw)
    {
        if (contributorsRaw is null) return null;
        if (contributorsRaw.Count == 0) return null;

        var contributorsConverted = new List<Contributor>();
        foreach (var (role, subRole, artistId, artistName) in contributorsRaw)
        {
            var artistRef = ArtistRef.TryBuild(artistId, artistName);
            if (artistRef is null) return null;

            var contributor = Contributor.TryBuild(role, subRole, artistRef);
            if (contributor is null) return null;

            contributorsConverted.Add(contributor);
        }
        return contributorsConverted;
    }

    private static ReplayGain? ValidateAndConvertReplayGain(
        (double? TrackGain,
        double? AlbumGain,
        double? TrackPeak,
        double? AlbumPeak,
        double? BaseGain)? replayGainRaw)
    {
        if (replayGainRaw is null) return null;
        var (tg, ag, tp, ap, bg) = replayGainRaw.Value;
        return ReplayGain.TryBuild(tg, ag, tp, ap, bg);
    }

    private static IReadOnlyList<Work>? ValidateAndConvertWorks(
        IReadOnlyList<(
        string? Name,
        string? MusicBrainzId)>? worksRaw)
    {
        if (worksRaw is null) return null;
        if (worksRaw.Count == 0) return null;

        var worksConverted = new List<Work>();
        foreach (var (name, mbId) in worksRaw)
        {
            var work = Work.TryBuild(name, mbId);
            if (work is null) return null;
            worksConverted.Add(work);
        }
        return worksConverted;
    }

    private static IReadOnlyList<Movement>? ValidateAndConvertMovements(
        IReadOnlyList<(
        string? Name,
        int? Number,
        int? Count)>? movementsRaw)
    {
        if (movementsRaw is null) return null;
        if (movementsRaw.Count == 0) return null;
        
        var movementsConverted = new List<Movement>();
        foreach (var (name, number, count) in movementsRaw)
        {
            var movement = Movement.TryBuild(name, number, count);
            if (movement is null) return null;
            movementsConverted.Add(movement);
        }
        return movementsConverted;
    }
}


internal record UnvalidatedSong(
    string? Id,
    IReadOnlyList<string>? Isrc,
    string? MusicBrainzId,

    string? Title,
    string? SortName,
    string? Artist,
    string? ArtistId,
    IReadOnlyList<(string? Id, string? Name)>? Artists,
    string? DisplayArtist,
    IReadOnlyList<(string? Role, string? SubRole, string? ArtistId, string? ArtistName)>? Contributors,
    string? DisplayComposer,

    string? AlbumTitle,
    string? AlbumId,
    string? AlbumArtist,
    IReadOnlyList<(string? Id, string? Name)>? AlbumArtists,
    string? DisplayAlbumArtist,
    int? AlbumDiscNr,
    int? AlbumTrackNr,

    string? CoverArtId,

    string? Genre,
    IReadOnlyList<string>? Genres,
    IReadOnlyList<string>? Moods,
    int? ReleaseYear,
    string? ExplicitStatus,

    long? Size,
    int? Duration,
    int? BitRate,
    int? BitDepth,
    int? SamplingRate,
    int? ChannelCount,
    int? Bpm,
    (double? TrackGain, double? AlbumGain, double? TrackPeak, double? AlbumPeak, double? BaseGain)? ReplayGain,

    string? ContentType,
    string? Suffix,
    string? TranscodedContentType,
    string? TranscodedSuffix,

    IReadOnlyList<(string? Name, string? MusicBrainzId)>? Works,
    IReadOnlyList<(string? Name, int? Number, int? Count)>? Movements,

    string? Type,
    string? MediaType,
    bool? IsDir,
    bool? IsVideo,

    bool? IsLocal,
    string? ExternalProvider,
    string? ExternalId,
    string? LocalPath,
    string? CoverArtUrl,
    string? CoverArtUrlLarge
)
{
    internal UnvalidatedSong EnrichFromAlbum(Album sourceAlbum)
    {
        return this with
        {
            AlbumTitle = AlbumTitle ?? sourceAlbum.Title,
            AlbumId = AlbumId ?? sourceAlbum.Id,
            AlbumArtist = AlbumArtist ?? sourceAlbum.Artist,
            CoverArtId = CoverArtId ?? sourceAlbum.Id,
            ReleaseYear = ReleaseYear ?? sourceAlbum.Year,
            Genre = Genre ?? sourceAlbum.Genre,
            CoverArtUrl = CoverArtUrl ?? sourceAlbum.CoverArtUrl,
            CoverArtUrlLarge = CoverArtUrlLarge ?? sourceAlbum.CoverArtUrlLarge
        };
    }
}