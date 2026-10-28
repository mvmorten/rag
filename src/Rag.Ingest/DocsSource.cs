using System.Diagnostics;

namespace Rag.Ingest;

/// <summary>Sparse, shallow clone of a folder from MicrosoftDocs/azure-docs.</summary>
public static class DocsSource
{
    private const string Repo = "https://github.com/MicrosoftDocs/azure-docs.git";

    public static async Task<string> EnsureAsync(string cloneDir, string folder)
    {
        if (!Directory.Exists(Path.Combine(cloneDir, ".git")))
        {
            Directory.CreateDirectory(cloneDir);
            await Git(cloneDir, "clone", "--depth", "1", "--filter=blob:none", "--sparse", Repo, ".");
            await Git(cloneDir, "sparse-checkout", "set", folder);
        }
        else
        {
            await Git(cloneDir, "pull", "--depth", "1", "--ff-only");
        }
        return Path.Combine(cloneDir, folder);
    }

    /// <summary>articles/container-apps/foo.md -> https://learn.microsoft.com/en-us/azure/container-apps/foo</summary>
    public static string ToUrl(string relativePath)
    {
        var p = relativePath.Replace('\\', '/');
        if (p.StartsWith("articles/")) p = p["articles/".Length..];
        if (p.EndsWith(".md")) p = p[..^3];
        return $"https://learn.microsoft.com/en-us/azure/{p}";
    }

    private static async Task Git(string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = workDir, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {err}");
    }
}
