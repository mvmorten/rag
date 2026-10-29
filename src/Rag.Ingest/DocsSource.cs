using System.Diagnostics;

namespace Rag.Ingest;

/// <summary>Where the corpus comes from. Pinned to a commit so the index, eval labels and CI all see the same docs.</summary>
public sealed record CorpusPin(string Repository, string Folder, string Commit);

/// <summary>Sparse, shallow checkout of one folder of a docs repo at a pinned commit.</summary>
public static class DocsSource
{
    public static async Task<string> EnsureAsync(string cloneDir, CorpusPin pin)
    {
        if (!Directory.Exists(Path.Combine(cloneDir, ".git")))
        {
            Directory.CreateDirectory(cloneDir);
            await Git(cloneDir, "clone", "--depth", "1", "--filter=blob:none", "--sparse", pin.Repository, ".");
            await Git(cloneDir, "sparse-checkout", "set", pin.Folder);
        }

        if ((await Git(cloneDir, "rev-parse", "HEAD")).Trim() != pin.Commit)
        {
            // Fetch the exact commit rather than pulling: upstream history gets force-updated,
            // which breaks fast-forward pulls on a shallow clone.
            await Git(cloneDir, "fetch", "--depth", "1", "origin", pin.Commit);
            await Git(cloneDir, "checkout", "--force", "--detach", pin.Commit);
        }
        return Path.Combine(cloneDir, pin.Folder);
    }

    /// <summary>articles/container-apps/foo.md -> https://learn.microsoft.com/en-us/azure/container-apps/foo</summary>
    public static string ToUrl(string relativePath)
    {
        var p = relativePath.Replace('\\', '/');
        if (p.StartsWith("articles/")) p = p["articles/".Length..];
        if (p.EndsWith(".md")) p = p[..^3];
        return $"https://learn.microsoft.com/en-us/azure/{p}";
    }

    private static async Task<string> Git(string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {await stderr}");
        return await stdout;
    }
}
