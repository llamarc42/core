using Llamarc42.Core.Discovery;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Llamarc42.Core.Tests.Discovery;

public sealed class ProjectDiscoveryTests
{
    [Fact]
    public void DiscoverMinimumProjectDoesNotRequireAgentsFile()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "# Example\n\nDocumentation contract: llamarc42/v0.1\n\nPurpose.\n");

        var result = ProjectDiscovery.Discover(fixture.Root);

        Assert.Equal(ProjectDiscoveryOutcome.Discovered, result.Outcome);
        Assert.Equal(Path.GetFullPath(fixture.Root), result.ProjectRoot);
        var document = Assert.Single(result.Documents);
        Assert.Equal("project.md", document.SourcePath);
        Assert.Equal(DocumentType.ProjectDescriptor, document.DocumentType);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void DiscoverFromFilePreservesSuppliedPathAndFindsNearestRoot()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "root");
        var startFile = fixture.Write("src/feature/source.txt", "source");
        var supplied = Path.Combine(fixture.Root, "src", "feature", ".", "source.txt");

        var result = ProjectDiscovery.Discover(supplied);

        Assert.Equal(ProjectDiscoveryOutcome.Discovered, result.Outcome);
        Assert.Equal(supplied, result.SuppliedPath);
        Assert.Equal(Path.GetFullPath(startFile), result.NormalizedStartPath);
        Assert.Equal(Path.GetFullPath(fixture.Root), result.ProjectRoot);
    }

    [Fact]
    public void DiscoverNestedProjectWinsAndItsSubtreeIsExcludedFromParent()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "parent");
        fixture.Write("architecture/parent.md", "parent guidance");
        var childMarker = fixture.Write("packages/child/project.md", "child");
        fixture.Write("packages/child/guides/child.md", "child guidance");

        var parent = ProjectDiscovery.Discover(fixture.Root);
        var child = ProjectDiscovery.Discover(childMarker);

        Assert.Equal(
            ["project.md", "architecture/parent.md"],
            parent.Documents.Select(document => document.SourcePath));
        Assert.Equal(Path.GetDirectoryName(childMarker), child.ProjectRoot);
        Assert.Equal(
            ["project.md", "guides/child.md"],
            child.Documents.Select(document => document.SourcePath));
    }

    [Fact]
    public void DiscoverInvalidNearerMarkerReportsItAndSelectsValidAncestor()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "ancestor");
        fixture.CreateDirectory("child/project.md");
        var start = fixture.CreateDirectory("child/deeper");

        var result = ProjectDiscovery.Discover(start);

        Assert.Equal(ProjectDiscoveryOutcome.Discovered, result.Outcome);
        Assert.Equal(Path.GetFullPath(fixture.Root), result.ProjectRoot);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ProjectDiscoveryIssueCode.RootMarkerNotRegular, issue.Code);
    }

    [Fact]
    public void DiscoverMissingRootReturnsStableNotFoundOutcome()
    {
        using var fixture = new DiscoveryFixture();
        var start = fixture.CreateDirectory("without/root");

        var result = ProjectDiscovery.Discover(start);

        Assert.Equal(ProjectDiscoveryOutcome.NotFound, result.Outcome);
        Assert.Null(result.ProjectRoot);
        Assert.Empty(result.Documents);
    }

    [Fact]
    public void DiscoverMissingStartPathReturnsStableProgrammaticError()
    {
        using var fixture = new DiscoveryFixture();
        var missing = Path.Combine(fixture.Root, "missing");

        var result = ProjectDiscovery.Discover(missing);

        Assert.Equal(ProjectDiscoveryOutcome.Error, result.Outcome);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ProjectDiscoveryIssueCode.StartPathNotFound, issue.Code);
        Assert.Contains("retry", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscoverClassifiesAndOrdersCompleteInventoryDeterministically()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "root");
        fixture.Write("decisions/ADR-010-later.md", "later");
        fixture.Write("decisions/ADR-002-earlier.md", "earlier");
        fixture.Write("decisions/notes.md", "supplemental");
        fixture.Write("constraints.md", "constraints");
        fixture.Write("architecture/zeta.md", "architecture");
        fixture.Write("history/iterations/2026/2026-10-15.md", "history");
        fixture.Write("README.md", "navigation");
        fixture.Write("AGENTS.md", "navigation");
        fixture.Write("notes.md", "supplemental");
        fixture.Write("ignored.MD", "not a contract document");

        var first = ProjectDiscovery.Discover(fixture.Root);
        var second = ProjectDiscovery.Discover(fixture.Root);

        var expected = new (string Path, DocumentType Type, string? DecisionId)[]
        {
            ("project.md", DocumentType.ProjectDescriptor, null),
            ("decisions/ADR-002-earlier.md", DocumentType.DecisionRecord, "ADR-002"),
            ("decisions/ADR-010-later.md", DocumentType.DecisionRecord, "ADR-010"),
            ("architecture/zeta.md", DocumentType.CurrentGuidance, null),
            ("constraints.md", DocumentType.CurrentGuidance, null),
            ("history/iterations/2026/2026-10-15.md", DocumentType.HistoricalRecord, null),
            ("AGENTS.md", DocumentType.Navigation, null),
            ("README.md", DocumentType.Navigation, null),
            ("decisions/notes.md", DocumentType.Supplemental, null),
            ("notes.md", DocumentType.Supplemental, null),
        };

        Assert.Equal(expected, first.Documents.Select(document =>
            (document.SourcePath, document.DocumentType, document.DecisionId)));
        Assert.Equal(first.Documents, second.Documents);
        Assert.Equal(first.Issues, second.Issues);
    }

    [Fact]
    public void DiscoverOrdersByUnicodeCodePointRatherThanUtf16CodeUnit()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "root");
        fixture.Write("\uE000.md", "private use BMP");
        fixture.Write("\U00010000.md", "supplementary plane");

        var result = ProjectDiscovery.Discover(fixture.Root);

        Assert.Equal(
            ["project.md", "\uE000.md", "\U00010000.md"],
            result.Documents.Select(document => document.SourcePath));
    }

    [Fact]
    public void DiscoverPreservesExposedSourcePathAndNormalizesStableIdentityToNfc()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "root");
        fixture.Write("cafe\u0301.md", "decomposed filename");
        var exposedName = Directory.EnumerateFiles(fixture.Root)
            .Select(Path.GetFileName)
            .Single(name => !string.Equals(name, "project.md", StringComparison.Ordinal))!;

        var result = ProjectDiscovery.Discover(fixture.Root);

        var document = Assert.Single(
            result.Documents,
            entry => entry.DocumentType == DocumentType.Supplemental);
        Assert.Equal(exposedName, document.SourcePath);
        Assert.Equal(exposedName.Normalize(), document.Path);
    }

    [Fact]
    public void DiscoverDirectoryLinkDoesNotCrossProjectBoundary()
    {
        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "root");
        fixture.Write("inside.md", "inside");
        var outside = fixture.CreateSiblingDirectory();
        File.WriteAllText(Path.Combine(outside, "outside.md"), "outside");
        var link = Path.Combine(fixture.Root, "linked-outside");

        CreateDirectoryLink(link, outside);

        var result = ProjectDiscovery.Discover(fixture.Root);

        Assert.Equal(ProjectDiscoveryOutcome.Discovered, result.Outcome);
        Assert.Equal(["project.md", "inside.md"], result.Documents.Select(document => document.SourcePath));
        Assert.DoesNotContain(result.Documents, document => document.SourcePath.Contains("outside", StringComparison.Ordinal));
    }

    [Fact]
    public void DiscoverLinkedRootMarkerIsSkippedForValidAncestor()
    {
        using var fixture = new DiscoveryFixture();
        var ancestorMarker = fixture.Write("project.md", "ancestor");
        var child = fixture.CreateDirectory("child");
        var markerTarget = fixture.CreateSiblingDirectory();
        File.WriteAllText(Path.Combine(markerTarget, "target.md"), ancestorMarker);
        var linkedMarker = Path.Combine(child, "project.md");
        CreateDirectoryLink(linkedMarker, markerTarget);

        var start = fixture.CreateDirectory("child/deeper");
        var result = ProjectDiscovery.Discover(start);

        Assert.Equal(ProjectDiscoveryOutcome.Discovered, result.Outcome);
        Assert.Equal(Path.GetFullPath(fixture.Root), result.ProjectRoot);
        Assert.Contains(result.Issues, issue => issue.Code == ProjectDiscoveryIssueCode.RootMarkerLink);
    }

    [Fact]
    public void DiscoverDoesNotModifyFixtureFiles()
    {
        using var fixture = new DiscoveryFixture();
        var marker = fixture.Write("project.md", "marker bytes");
        var guide = fixture.Write("guides/setup.md", "guide bytes");
        var before = new Dictionary<string, byte[]>
        {
            [marker] = File.ReadAllBytes(marker),
            [guide] = File.ReadAllBytes(guide),
        };

        var result = ProjectDiscovery.Discover(fixture.Root);

        Assert.Equal(ProjectDiscoveryOutcome.Discovered, result.Outcome);
        foreach (var item in before)
        {
            Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
        }
    }

    [Fact]
    public void DiscoverUnixFifoMarkerDoesNotSelectRootOrExcludeSubtree()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "ancestor");
        var child = fixture.CreateDirectory("child");
        fixture.Write("child/inside.md", "inside");
        CreateFifo(Path.Combine(child, "project.md"));
        var start = fixture.CreateDirectory("child/deeper");

        var result = ProjectDiscovery.Discover(start);

        Assert.Equal(ProjectDiscoveryOutcome.Discovered, result.Outcome);
        Assert.Equal(Path.GetFullPath(fixture.Root), result.ProjectRoot);
        Assert.Contains(result.Issues, issue => issue.Code == ProjectDiscoveryIssueCode.RootMarkerNotRegular);
        Assert.Contains(result.Documents, document => document.SourcePath == "child/inside.md");
        Assert.DoesNotContain(result.Documents, document => document.SourcePath == "child/project.md");
    }

    [Fact]
    public void DiscoverUnixSocketMarkerDoesNotSelectRootOrExcludeSubtree()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new DiscoveryFixture();
        fixture.Write("project.md", "ancestor");
        var child = fixture.CreateDirectory("child");
        fixture.Write("child/inside.md", "inside");
        var socketPath = Path.Combine(child, "project.md");
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(socketPath));
        var start = fixture.CreateDirectory("child/deeper");

        var result = ProjectDiscovery.Discover(start);

        Assert.Equal(ProjectDiscoveryOutcome.Discovered, result.Outcome);
        Assert.Equal(Path.GetFullPath(fixture.Root), result.ProjectRoot);
        Assert.Contains(result.Issues, issue => issue.Code == ProjectDiscoveryIssueCode.RootMarkerNotRegular);
        Assert.Contains(result.Documents, document => document.SourcePath == "child/inside.md");
        Assert.DoesNotContain(result.Documents, document => document.SourcePath == "child/project.md");
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"Unable to create fixture junction. stdout: {process.StandardOutput.ReadToEnd()} stderr: {process.StandardError.ReadToEnd()}");
    }

    private static void CreateFifo(string path)
    {
        const uint OwnerReadWrite = 0x180;
        var status = MkFifo(path, OwnerReadWrite);
        Assert.True(
            status == 0,
            $"Unable to create FIFO fixture: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");
    }

#pragma warning disable CA2101 // Unix filesystem paths are marshalled explicitly as UTF-8.
#pragma warning disable SYSLIB1054 // The generated alternative requires enabling unsafe code for the test project.
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
#pragma warning restore SYSLIB1054
#pragma warning restore CA2101
}
