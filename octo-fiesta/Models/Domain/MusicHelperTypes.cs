namespace octo_fiesta.Models.Domain;

public record ArtistRef(string Id, string Name)
{
    public static ArtistRef? TryBuild(string? id, string? name)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (string.IsNullOrWhiteSpace(name)) return null;
        return new ArtistRef(id, name);
    }
}

public record Contributor(string Role, string? SubRole, ArtistRef Artist)
{
    public static Contributor? TryBuild(string? role, string? subRole, ArtistRef? artist)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;
        if (artist == null) return null;
        if (string.IsNullOrWhiteSpace(subRole)) subRole = null;
        return new Contributor(role, subRole, artist);
    }
}

public record Work(string Name, string? MusicBrainzId)
{
    public static Work? TryBuild(string? name, string? musicBrainzId)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (string.IsNullOrWhiteSpace(musicBrainzId)) musicBrainzId = null;
        return new Work(name, musicBrainzId);
    }
}

public record Movement(string Name, int Number, int Count)
{
    public static Movement? TryBuild(string? name, int? numberRaw, int? countRaw)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (numberRaw is not int number || number < 1) return null;
        if (countRaw is not int count || count < 1) return null;
        if (number > count) return null;

        return new Movement(name, number, count);
    }
}

public record ReplayGain(
    double? TrackGain,
    double? AlbumGain,
    double? TrackPeak,
    double? AlbumPeak,
    double? BaseGain)
{
    public static ReplayGain? TryBuild(
        double? trackGain,
        double? albumGain,
        double? trackPeak,
        double? albumPeak,
        double? baseGain)
    {
        if (trackGain is null &&
         albumGain is null &&
         trackPeak is null &&
         albumPeak is null &&
         baseGain is null) return null;

        return new ReplayGain(trackGain, albumGain, trackPeak, albumPeak, baseGain);
    }
}