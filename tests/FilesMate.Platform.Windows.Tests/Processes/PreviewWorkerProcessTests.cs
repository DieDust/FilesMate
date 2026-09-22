using FilesMate.Platform.Windows.Processes;

namespace FilesMate.Platform.Windows.Tests.Processes;

public sealed class PreviewWorkerProcessTests
{
    [Theory]
    [InlineData("worker")]
    [InlineData("allocation-probe")]
    public async Task Worker_is_lifetime_bound_and_cannot_commit_beyond_its_quota(string mode)
    {
        var root = Directory.CreateTempSubdirectory("FilesMate-PreviewWorker-");
        var output = Path.Combine(root.FullName, "result.txt");
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "ProcessIsolationFixture", "FilesMate.ProcessIsolation.Fixture.exe");
            var worker = PreviewWorkerProcess.Start(executable, [mode, output]);
            using var process = System.Diagnostics.Process.GetProcessById(worker.Process.Id);
            try
            {
                var until = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(output) && DateTime.UtcNow < until) await Task.Delay(20);
                Assert.True(File.Exists(output));
                if (mode == "allocation-probe") Assert.Equal("limited", await File.ReadAllTextAsync(output));
            }
            finally { worker.Dispose(); }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(process.HasExited);
        }
        finally { root.Delete(true); }
    }
}
