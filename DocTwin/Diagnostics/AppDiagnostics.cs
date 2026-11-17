using System.IO;
using Serilog;
using Serilog.Debugging;
using Serilog.Formatting.Json;

namespace DocTwin.Diagnostics;

internal static class AppDiagnostics
{
    private static int _writeFailed;
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocTwin", "logs");

    public static void Initialize()
    {
        Directory.CreateDirectory(LogDirectory);
        string probe = Path.Combine(LogDirectory, Guid.NewGuid().ToString("N") + ".probe");
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        SelfLog.Enable(_ => Interlocked.Exchange(ref _writeFailed, 1));
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext()
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.File(new JsonFormatter(), Path.Combine(LogDirectory, "doctwin-.jsonl"),
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true, retainedFileCountLimit: 7, shared: true)
            .CreateLogger();
        Log.Information("ApplicationStarted {Version}", typeof(App).Assembly.GetName().Version?.ToString());
        if (ConsumeWriteFailure()) throw new IOException("Local diagnostics could not be started.");
    }

    public static bool ConsumeWriteFailure() => Interlocked.Exchange(ref _writeFailed, 0) != 0;

    public static void Unexpected(Exception error, string boundary)
    {
        // Exception messages and object graphs can contain page text, URLs or credentials.
        Log.Fatal("UnexpectedFailure {Boundary} {ErrorType}", boundary, error.GetType().Name);
        Log.CloseAndFlush();
    }
}
