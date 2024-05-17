namespace FFDaemon;

public class Orchestrator
{
    public Configuration Configuration { get; set; }
    object Mutex = new object();
    HashSet<string> SkipBuffer = new ();

    public List<string> GetFileCandidates()
    {
        var rawCandidates = Directory.GetFiles(Configuration.WorkingDirectoryPath, "*", searchOption: SearchOption.AllDirectories);
        var candidates = rawCandidates
            .Where(file => Configuration.AllowedInputs.Any(file.ToLower().EndsWith))
            .OrderBy(x =>
            {
                FileInfo fi = new(x);
                return fi.LastWriteTime;
            })
            .ToList();

        return candidates;
    }

    public async Task<string?> PickPath(Func<string, Task<bool>> picker)
    {
        var candidates = GetFileCandidates();
        foreach(var candidate in candidates)
        {
            lock(Mutex)
                if (SkipBuffer.Contains(candidate))
                    continue;

            var r = await picker(candidate);
            lock(Mutex)
            {
                SkipBuffer.Remove(candidate);
                if (r)
                    return candidate;
            }
        }

        return null;
    }
}
