namespace Llamarc42.Core.Tests.Discovery;

/// <summary>
/// Temporary filesystem fixture for Phase 2 implementation verification.
/// It is not a Phase 3 normative conformance fixture.
/// </summary>
internal sealed class DiscoveryFixture : IDisposable
{
    private readonly List<string> _cleanupRoots = [];

    public DiscoveryFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "llamarc42-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        _cleanupRoots.Add(Root);
    }

    public string Root { get; }

    public string CreateDirectory(string relativePath)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateSiblingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "llamarc42-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _cleanupRoots.Add(path);
        return path;
    }

    public string Write(string relativePath, string content)
    {
        var path = Resolve(relativePath);
        var parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        foreach (var cleanupRoot in _cleanupRoots.OrderByDescending(path => path.Length))
        {
            try
            {
                if (Directory.Exists(cleanupRoot))
                {
                    Directory.Delete(cleanupRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string Resolve(string relativePath) => Path.Combine(
        Root,
        relativePath.Replace('/', Path.DirectorySeparatorChar));
}
