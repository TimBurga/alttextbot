namespace AltTextBot.Domain.Entities;

public class TrackedImage
{
    public int Id { get; private set; }
    public string PostAtUri { get; private set; } = default!;
    public int Index { get; private set; }
    public string BlobCid { get; private set; } = default!;
    public bool HasAlt { get; private set; }

    // Navigation
    public TrackedPost? Post { get; private set; }

    private TrackedImage() { }

    public static TrackedImage Create(string postAtUri, int index, string blobCid, bool hasAlt)
        => new() { PostAtUri = postAtUri, Index = index, BlobCid = blobCid, HasAlt = hasAlt };
}
