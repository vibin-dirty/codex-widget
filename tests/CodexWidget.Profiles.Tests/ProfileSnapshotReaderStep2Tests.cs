using CodexWidget.Core;
using System.Diagnostics;

namespace CodexWidget.Profiles.Tests;

public sealed class ProfileSnapshotReaderStep2Tests
{
    private readonly ProfileSnapshotReader _reader = new();

    [Fact]
    public async Task ReadAsync_ReturnsUnavailableSnapshot_WhenHomeDirectoryIsMissing()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        Directory.Delete(fixture.HomePath, recursive: true);

        var snapshot = await _reader.ReadAsync(new ProfileSnapshotReadOptions
        {
            HomeResolution = fixture.CreateResolutionOptions(),
        });

        Assert.Empty(snapshot.Profiles);
        Assert.Equal(4, snapshot.Sources.Count);
        Assert.All(snapshot.Sources, source => Assert.Equal(SourceStatusState.Unavailable, source.State));
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Missing);
        Assert.All(snapshot.Diagnostics, diagnostic => Assert.Contains(RedactionHelper.RedactedPathMarker, string.Join(" ", diagnostic.Context.Values), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_ReturnsUnavailableSnapshot_WhenProfilesDirectoryIsMissing()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        Directory.Delete(fixture.ProfilesDirectoryPath, recursive: true);

        var snapshot = await _reader.ReadAsync(new ProfileSnapshotReadOptions
        {
            HomeResolution = fixture.CreateResolutionOptions(),
        });

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Missing);
        Assert.All(snapshot.Sources, source => Assert.Equal(SourceStatusState.Unavailable, source.State));
        Assert.Empty(snapshot.RawSources.SavedProfileFiles);
    }

    [Fact]
    public async Task ReadAsync_CreatesProfilesLock_WhenProfilesDirectoryExistsAndLockIsMissing()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        if (File.Exists(fixture.ProfilesLockPath))
        {
            File.Delete(fixture.ProfilesLockPath);
        }

        var snapshot = await _reader.ReadAsync(new ProfileSnapshotReadOptions
        {
            HomeResolution = fixture.CreateResolutionOptions(),
        });

        Assert.True(File.Exists(fixture.ProfilesLockPath));
        Assert.Equal(SourceStatusState.Available, snapshot.Sources.Single(source => source.Source == StatusSourceKind.SavedProfileAuth).State);
    }

    [Fact]
    public async Task ReadAsync_EnumeratesSavedProfileJsonFiles_ExcludingProfilesAndUpdate()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        fixture.WriteSavedProfileJson("zeta", SyntheticCodexHomeFixture.CreateSyntheticApiKeyAuthJson("sk-zeta"));
        fixture.WriteSavedProfileJson("alpha", SyntheticCodexHomeFixture.CreateSyntheticApiKeyAuthJson("sk-alpha"));
        File.WriteAllText(Path.Combine(fixture.ProfilesDirectoryPath, "profiles.json"), """{"profiles":{}}""");
        File.WriteAllText(Path.Combine(fixture.ProfilesDirectoryPath, "update.json"), """{"ignored":true}""");

        var snapshot = await _reader.ReadAsync(new ProfileSnapshotReadOptions
        {
            HomeResolution = fixture.CreateResolutionOptions(),
        });

        Assert.Collection(snapshot.RawSources.SavedProfileFiles,
            file => Assert.Equal("alpha", file.ProfileId),
            file => Assert.Equal("zeta", file.ProfileId));
        Assert.All(snapshot.RawSources.SavedProfileFiles, file =>
        {
            Assert.Equal(ProfileSourceParseState.Available, file.ReadState);
            Assert.NotNull(file.Content);
        });
    }

    [Fact]
    public async Task ReadAsync_ReturnsUnavailableWhenLockIsContended_WithoutHanging()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        using var blockingLock = new FileStream(
            fixture.ProfilesLockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        var stopwatch = Stopwatch.StartNew();
        var snapshot = await _reader.ReadAsync(new ProfileSnapshotReadOptions
        {
            HomeResolution = fixture.CreateResolutionOptions(),
            LockAcquireTimeout = TimeSpan.FromMilliseconds(150),
            LockRetryInterval = TimeSpan.FromMilliseconds(10),
        });
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Lock contention test exceeded bound: {stopwatch.Elapsed}.");
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Unavailable);
        Assert.All(snapshot.Diagnostics, diagnostic => Assert.Contains(RedactionHelper.RedactedPathMarker, string.Join(" ", diagnostic.Context.Values), StringComparison.Ordinal));
        Assert.All(snapshot.Sources, source => Assert.Equal(SourceStatusState.Unavailable, source.State));
    }

    [Fact]
    public async Task ReadAsync_RespectsCancellation_WithoutHanging()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        using var blockingLock = new FileStream(
            fixture.ProfilesLockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var snapshot = await _reader.ReadAsync(new ProfileSnapshotReadOptions
        {
            HomeResolution = fixture.CreateResolutionOptions(),
            LockAcquireTimeout = TimeSpan.FromMilliseconds(300),
            LockRetryInterval = TimeSpan.FromMilliseconds(10),
        }, cancellationSource.Token);

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Summary.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
        Assert.All(snapshot.Sources, source => Assert.Equal(SourceStatusState.Unavailable, source.State));
    }
}
