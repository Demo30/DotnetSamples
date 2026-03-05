using System.Diagnostics;

namespace EtwEventSource;

// Wraps a logman ETW trace session. Requires the process to run as Administrator.
sealed class EtwRecordingSession : IDisposable
{
    readonly string _sessionName;
    string? _manifestPath;       // path of the .man file on disk
    bool _manifestRegistered;    // true once wevtutil im succeeded
    bool _running;

    public string EtlPath { get; }
    public string XmlPath => Path.ChangeExtension(EtlPath, ".xml");

    public EtwRecordingSession(string sessionName, string etlPath)
    {
        _sessionName = sessionName;
        EtlPath = etlPath;
    }

    // Starts the logman session capturing all events from the given provider GUID.
    // The manifest XML is saved to disk here but NOT registered with wevtutil yet —
    // registration happens lazily in DecodeToXml so it never races against event writes.
    public void Start(string providerGuid, string? manifest = null)
    {
        Logman($"stop {_sessionName} -ets");
        if (File.Exists(EtlPath)) File.Delete(EtlPath);
        if (File.Exists(XmlPath)) File.Delete(XmlPath);

        if (manifest != null)
            WriteManifestFile(manifest);

        // Keywords 0x0 = no keyword filter; capture ALL events from this provider.
        // Using 0xffffffff here would filter OUT events with keyword=0 (EventKeywords.None),
        // which is the default for events that don't have explicit keywords assigned —
        // because (eventKeyword=0) & (matchAnyKeyword=0xffffffff) = 0 fails the ETW check.
        Console.WriteLine(Logman(
            $"create trace {_sessionName} -p {{{providerGuid}}} 0x0 255 -o \"{EtlPath}\" -ft 1 -ets").Trim());
        _running = true;

        // The ETW enable notification is delivered asynchronously to the provider
        // after EnableTraceEx2 returns in logman's process. Give the callback thread
        // in our process time to receive and process it before any events are written.
        Thread.Sleep(500);
    }

    // Stops the session, flushing ETW buffers to disk.
    public void Stop()
    {
        if (!_running) return;
        // Force-flush all active ETW buffers before stopping the session.
        // Without this, events in partially-filled buffers may be lost.
        Logman($"update trace {_sessionName} -fd -ets");
        Thread.Sleep(500);
        Console.WriteLine(Logman($"stop {_sessionName} -ets").Trim());
        _running = false;
        Thread.Sleep(500); // let the OS finish writing the file
    }

    // Decodes the captured ETL to XML via tracerpt. Returns null if nothing was captured.
    //
    // Manifest registration is deferred to here (after Stop) so wevtutil im never runs
    // while the ETW session is live. Calling wevtutil im during capture causes the Windows
    // Event Log service to process the manifest asynchronously and send ETW disable
    // callbacks that race against WriteEvent calls, silently dropping events from the ETL.
    public string? DecodeToXml()
    {
        if (!File.Exists(EtlPath)) return null;

        // Register the manifest system-wide so tracerpt can resolve event templates
        // without needing -import (which rejects some EventSource manifests).
        // Safe to call here because the session is already stopped.
        if (_manifestPath != null && !_manifestRegistered)
            RegisterManifest();

        // -lr : less-restricted — emit events even if some don't match the schema exactly.
        var importArg = (!_manifestRegistered && _manifestPath != null)
            ? $" -import \"{_manifestPath}\""
            : "";
        var args = $"\"{EtlPath}\" -o \"{XmlPath}\" -of XML -lr -y{importArg}";
        var (_, err) = Run("tracerpt", args);
        // tracerpt stdout is just boilerplate progress text — not printed.
        if (!string.IsNullOrWhiteSpace(err)) Console.WriteLine($"  tracerpt: {err.TrimEnd()}");
        return File.Exists(XmlPath) ? File.ReadAllText(XmlPath) : null;
    }

    // Queries the running session and returns logman output (useful for diagnostics).
    public string Query() => Logman($"query {_sessionName} -ets");

    public void Dispose()
    {
        Stop();
        if (_manifestRegistered && _manifestPath != null)
        {
            Run("wevtutil", $"um \"{_manifestPath}\"");
            _manifestRegistered = false;
        }
        if (_manifestPath != null && File.Exists(_manifestPath))
            File.Delete(_manifestPath);
        _manifestPath = null;
    }

    // ── manifest helpers ─────────────────────────────────────────────────────

    void WriteManifestFile(string manifest)
    {
        _manifestPath = Path.Combine(Path.GetTempPath(), "myetwtest.man");
        File.WriteAllText(_manifestPath, manifest);
    }

    void RegisterManifest()
    {
        // Unregister any stale installation first so wevtutil im does a clean install.
        Run("wevtutil", $"um \"{_manifestPath}\"");

        var (_, err) = Run("wevtutil", $"im \"{_manifestPath}\"");
        if (string.IsNullOrWhiteSpace(err))
        {
            _manifestRegistered = true;
            Console.WriteLine("  Manifest registered.");
        }
        else
        {
            // The ntdll.dll "metadata resource" warning is expected — we use ntdll as a dummy
            // resourceFileName solely to satisfy the EventLog service's file-access check.
            // The actual message strings live in the manifest XML stored in the registry.
            bool isNtdllWarning = err.Contains("does not contain the metadata resource",
                StringComparison.OrdinalIgnoreCase);
            if (isNtdllWarning)
            {
                _manifestRegistered = true;
                Console.WriteLine("  Manifest registered (ntdll resourceFileName warning suppressed — expected).");
            }
            else
            {
                Console.WriteLine($"  wevtutil im failed (will fall back to -import): {err.Trim()}");
            }
        }
    }

    // ── process helpers ──────────────────────────────────────────────────────

    static string Logman(string args) => Run("logman", args).Out;

    static (string Out, string Err) Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            })!;
            // Read both streams concurrently to avoid the deadlock where one stream's
            // buffer fills up while we're blocking on the other.
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            return (outTask.Result, errTask.Result);
        }
        catch (Exception ex)
        {
            return ("", $"[error launching {exe}]: {ex.Message}");
        }
    }
}
