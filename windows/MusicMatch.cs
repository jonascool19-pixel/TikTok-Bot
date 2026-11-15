internal sealed class MusicMatch
{
    internal readonly string Id;
    public string Title { get; private set; }

    internal MusicMatch(string id, string title)
    {
        Id = id;
        Title = title;
    }
}
