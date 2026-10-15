using System.Text.Json;
using FinPlan;
using Xunit;

namespace FinPlan.Tests;

public sealed class DemoStoreTests(DemoFixture fixture) : IClassFixture<DemoFixture>
{
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Solution1.sln"))) return d.FullName;
        throw new DirectoryNotFoundException();
    }
    private string Workspace()
    {
        var folder = Path.Combine(Path.GetTempPath(), "finplan-store-test-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(fixture.Output, "dataset.json"), Path.Combine(folder, "dataset.json"));
        return folder;
    }
    [Fact]
    public async Task SuccessfulRegenerationKeepsPreviousFilesAndSurvivesRestart()
    {
        var folder = Workspace();
        try
        {
            var store = new DemoStore(Root(), folder);
            var previous = File.ReadAllText(Path.Combine(folder, "dataset.json"));
            var next = await store.RegenerateAsync(12345);
            Assert.Equal(12345, next.Data.Seed); Assert.Equal(100, next.Data.Documents.Count);
            Assert.Equal(previous, File.ReadAllText(Path.Combine(folder, "dataset.json")));
            Assert.NotEqual("initial", next.Id);
            Assert.Equal(next.Id, new DemoStore(Root(), folder).Current.Id);
            Assert.Equal(100, Directory.GetFiles(Path.Combine(next.Directory, "documents")).Length);
            Assert.Throws<FileNotFoundException>(() => store.Load("../../etc"));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task FailedGenerationDoesNotReplaceCurrentDataAndCleansItsTemporaryFolder()
    {
        var folder = Workspace();
        try
        {
            var store = new DemoStore(Root(), folder, (path, _) => { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "partial"), "partial"); throw new IOException("Test failure"); });
            await Assert.ThrowsAsync<IOException>(() => store.RegenerateAsync());
            Assert.Equal("initial", store.Current.Id);
            Assert.False(File.Exists(Path.Combine(folder, "current.json")));
            Assert.Empty(Directory.GetDirectories(Path.Combine(folder, "generations")));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task ConcurrentRegenerationIsRejectedInsteadOfOverwritingAnotherRun()
    {
        var folder = Workspace(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        try
        {
            var generator = new DemoGenerator(Root());
            var store = new DemoStore(Root(), folder, (path, seed) => { entered.SetResult(); release.Task.GetAwaiter().GetResult(); return generator.Generate(path, seed); });
            var first = store.RegenerateAsync(123);
            await entered.Task;
            await Assert.ThrowsAsync<GenerationBusyException>(() => store.RegenerateAsync(456));
            release.SetResult();
            Assert.Equal(123, (await first).Data.Seed);
        }
        finally { release.TrySetResult(); Directory.Delete(folder, true); }
    }
}
