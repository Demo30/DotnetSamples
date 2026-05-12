using System.Diagnostics.Tracing;
using System.Security.Principal;
using System.Xml.Linq;
using EtwEventSource;

static class Program
{
    static void Main()
    {
        PrintExperimentDescription();

        var etlPath = Path.Combine(Path.GetTempPath(), "myetwtest.etl");
        bool isAdmin = IsRunningAsAdmin();

        if (!isAdmin)
            PrintAdminWarning();

        PrintProviderInfo(etlPath);
        if (MyEventSource.Log.ConstructionException is { } ex)
        {
            WriteError($"ConstructionException: {ex}");
            return;
        }

        // In-process listener lets us verify events fire even without ETW.
        Section("In-process listener");
        using var listener = new MyEventSourceListener("InProc");
        Console.WriteLine();

        // Pre-init the EventSource before logman so EtwRegister runs first.
        // EtwSession.Start() then sleeps to let the async enable callback arrive.
        _ = MyEventSource.Log;

        using var recordingSession = new EtwRecordingSession("MyETWTest", etlPath);

        StartRecordingSession(recordingSession);
        EmitEvents();
        PrintSessionDiagnostics(recordingSession);
        StopRecordingSession(recordingSession, etlPath);
        ScanEtlForPayloads(etlPath);
        DecodeAndPrintEvents(recordingSession);

        if (!isAdmin)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Payloads show as <BinaryEventData> because the manifest is not registered.");
            Console.WriteLine($"  To decode: run elevated, or open {etlPath} in PerfView.");
            Console.ResetColor();
        }

        // ── Part 2: Event Log Channel demo ──────────────────────────────────
        // This demonstrates the ALTERNATIVE approach: instead of capturing ETW events
        // yourself with logman, let Windows do it automatically via a "channel".
        DemoEventLogChannel(isAdmin);
    }

    // ── setup ────────────────────────────────────────────────────────────────

    static bool IsRunningAsAdmin() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    static void PrintAdminWarning()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine(
            "WARNING: Not running as Administrator — wevtutil manifest registration will fail.\n" +
            "  Events will be captured but tracerpt cannot decode their payloads.\n" +
            "  To read the decoded payloads, either:\n" +
            "    a) Re-run elevated (Admin), or\n" +
            "    b) Open the .etl file in PerfView (https://aka.ms/perfview)\n");
        Console.ResetColor();
    }

    static void PrintExperimentDescription()
    {
        Section("What this sample demonstrates");
        Console.WriteLine("""
          This program demonstrates the full ETW (Event Tracing for Windows) pipeline:

          1. EMIT   — A custom EventSource (MyTestSource) writes structured events in-process
                      using the .NET EventSource API.

          2. CAPTURE — logman creates a kernel-level ETW trace session that intercepts those
                       events and writes them to a binary .etl file, with no code changes
                       to the emitting side.

          3. DECODE — tracerpt reads the .etl and converts it to human-readable XML.
                      For payloads to be decoded (rather than shown as raw BinaryEventData),
                      the event schema must be known. This is what the manifest is for.

          The manifest is an XML file generated from the EventSource class that describes
          every event: its ID, name, parameters, and message template. It is registered
          system-wide via "wevtutil im" so that tracerpt and Event Viewer can resolve
          event templates without needing the original binary at decode time.

          Registering the manifest requires a resourceFileName — a path to a DLL that the
          Windows Event Log service will open to validate the registration. Our bin\Debug
          folder is not readable by that service account (NT SERVICE\EventLog), so we use
          ntdll.dll as a dummy path. ntdll is always accessible to every account on the
          system. Our actual message strings live in the manifest XML stored in the registry,
          not in any DLL, so this substitution is harmless.
        """);
        Console.WriteLine();
    }

    static void PrintProviderInfo(string etlPath)
    {
        Section("Provider info");
        Console.WriteLine($"  Provider : {MyEventSource.EventSourceName} ({{{MyEventSource.ProviderId}}})");
        Console.WriteLine($"  ETL file : {etlPath}");
        Console.WriteLine($"  Settings : {MyEventSource.Log.Settings}");
        Console.WriteLine($"  Guid     : {MyEventSource.Log.Guid}");
        Console.WriteLine();
    }

    // GenerateManifest requires an assembly path that gets embedded as "resourceFileName" in the
    // manifest XML. When wevtutil im registers the manifest, the Windows Event Log service opens
    // that file to validate it. If it can't (e.g. our bin\Debug folder is inside a user profile
    // the service account can't read), it sends an ETW *disable* callback to our provider —
    // silently killing all subsequent event writes.
    //
    // We use ntdll.dll — the lowest-level Windows system DLL, always at System32\ntdll.dll —
    // purely because every account on the system can read it. It is just a dummy path to pass
    // the accessibility check. Our actual message strings are not in ntdll.dll; they live in
    // the manifest XML itself, which wevtutil im stores in the registry. The service will warn
    // that message resources weren't found in ntdll.dll, but that warning is harmless.
    static string GenerateManifest()
    {
        var ntdll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ntdll.dll");
        return EventSource.GenerateManifest(typeof(MyEventSource), ntdll)!;
    }

    // ── steps ────────────────────────────────────────────────────────────────

    static void StartRecordingSession(EtwRecordingSession recordingSession)
    {
        Section("[1] Starting ETW session");
        recordingSession.Start(MyEventSource.ProviderId, GenerateManifest());
        var enabled = MyEventSource.Log.IsEnabled();
        Console.ForegroundColor = enabled ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  IsEnabled = {enabled}");
        Console.ResetColor();
        Console.WriteLine();
    }

    static void EmitEvents()
    {
        Section("[2] Emitting events");
        for (int i = 0; i < 5; i++)
        {
            MyEventSource.Log.MyEventStart($"Hello world #{i}");
            MyEventSource.Log.MyEventStop($"Done #{i}");
        }
        Thread.Sleep(1000); // let ETW buffers settle
        Console.WriteLine();
    }

    static void PrintSessionDiagnostics(EtwRecordingSession recordingSession)
    {
        Section("[2b] Session diagnostics");
        Console.WriteLine(recordingSession.Query());
    }

    static void StopRecordingSession(EtwRecordingSession recordingSession, string etlPath)
    {
        Section("[3] Stopping session");
        recordingSession.Stop();
        var size = File.Exists(etlPath) ? $"{new FileInfo(etlPath).Length:N0} bytes" : "NOT FOUND";
        Console.WriteLine($"  Size : {size}");
        Console.WriteLine($"  Path : {etlPath}");
        Console.WriteLine();
    }

    // Verify the event payloads are physically present in the ETL binary.
    // (tracerpt skips them without a registered manifest, but the raw bytes are there.)
    static void ScanEtlForPayloads(string etlPath)
    {
        if (!File.Exists(etlPath)) return;

        byte[] etlBytes;
        // FileShare.ReadWrite allows reading even if logman still holds the file open.
        using (var fs = new FileStream(etlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            etlBytes = new byte[fs.Length];
            fs.ReadExactly(etlBytes);
        }

        var needle = System.Text.Encoding.Unicode.GetBytes("Hello world");
        int hits = 0;
        for (int i = 0; i <= etlBytes.Length - needle.Length; i++)
            if (etlBytes.AsSpan(i, needle.Length).SequenceEqual(needle))
                hits++;

        bool found = hits > 0;
        Console.ForegroundColor = found ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(found
            ? $"  Raw ETL scan: \"Hello world\" found {hits}x — payload bytes ARE in the binary."
            : "  Raw ETL scan: \"Hello world\" NOT found — events may not have been captured.");
        Console.ResetColor();
        Console.WriteLine();
    }

    static void DecodeAndPrintEvents(EtwRecordingSession recordingSession)
    {
        Section("[4] Decoding with tracerpt");
        var xml = recordingSession.DecodeToXml();
        Console.WriteLine();

        if (xml is null)
        {
            WriteError("tracerpt produced no output.");
            return;
        }

        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var ourEvents = XDocument.Parse(xml)
            .Descendants(ns + "Event")
            .Where(e => string.Equals(
                e.Element(ns + "System")?.Element(ns + "Provider")?.Attribute("Guid")?.Value,
                $"{{{MyEventSource.ProviderId}}}",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        Section($"[4] {ourEvents.Count} event(s) from {MyEventSource.EventSourceName}");
        PrintEventTable(ourEvents, ns);
        Console.WriteLine();

        Section("[4] Full XML");
        foreach (var evt in ourEvents)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(evt.ToString());
            Console.ResetColor();

            var id  = evt.Element(ns + "System")?.Element(ns + "EventID")?.Value;
            var hex = evt.Descendants(ns + "BinaryEventData").FirstOrDefault()?.Value;
            if (hex != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                var decoded = id == "65534"
                    ? "{Infrastructure event — manifest blob, not decoded}"
                    : System.Text.Encoding.Unicode.GetString(Convert.FromHexString(hex)).TrimEnd('\0');
                Console.WriteLine($"  Decoded: {decoded}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }
    }

    static void PrintEventTable(List<XElement> events, XNamespace ns)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {"ID",-4}  {"Opcode",-8}  Payload");
        Console.WriteLine($"  {"──",-4}  {"──────",-8}  ──────────────────────────────────────");
        Console.ResetColor();

        foreach (var evt in events)
        {
            var id     = evt.Element(ns + "System")?.Element(ns + "EventID")?.Value ?? "?";
            var opcode = evt.Descendants(ns + "Opcode").FirstOrDefault()?.Value ?? $"#{id}";
            var hex    = evt.Descendants(ns + "BinaryEventData").FirstOrDefault()?.Value;
            var payload = id == "65534"
                ? "{Infrastructure event — manifest blob, not decoded}"
                : hex != null
                    ? System.Text.Encoding.Unicode.GetString(Convert.FromHexString(hex)).TrimEnd('\0')
                    : "(no payload)";

            Console.Write($"  {id,-4}  {opcode,-8}  ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(payload);
            Console.ResetColor();
        }
    }

    // ── Event Log Channel demo ──────────────────────────────────────────────

    static void DemoEventLogChannel(bool isAdmin)
    {
        Console.WriteLine();
        Section("Event Log Channel — how it works");
        Console.WriteLine("""
          Everything above used logman to manually capture ETW events into an .etl file.
          But Windows can capture events FOR you automatically — using "channels".

          Windows has a built-in service called the "Event Log Service" (wevtsvc).
          It's always running and acts as a permanent ETW consumer.

          The flow WITHOUT a channel (what we did above):
            Your code → ETW kernel buffers → LOST (unless logman/PerfView is recording)

          The flow WITH a channel:
            Your code → ETW kernel buffers → Event Log Service → .evtx file → Event Viewer
                                             ^^^^^^^^^^^^^^^^^^
                                             This is the built-in "stenographer" that Windows
                                             provides. It listens to your ETW events 24/7 and
                                             saves them to disk automatically.

          All you need to do:
            1. Define a "channel" on your EventSource events (Channel = EventChannel.Operational)
            2. Register the manifest with Windows (wevtutil im <manifest.xml>)
            3. Restart the Event Log Service (or reboot) so it picks up the new channel
          After that, the Event Log Service handles everything.
        """);

        if (!isAdmin)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Skipping live demo — manifest registration requires Administrator.");
            Console.WriteLine("  Re-run elevated to see the channel demo.");
            Console.ResetColor();
            return;
        }

        ShowChannelManifest();
        RegisterAndDemoChannel();
    }

    static void ShowChannelManifest()
    {
        Section("[Channel 1] How a channel is defined");
        Console.WriteLine("""
            In ChannelEventSource.cs, events are tagged with a channel:

              [Event(1, Channel = EventChannel.Operational, ...)]
              public void RequestStarted(string url) => WriteEvent(1, url);

            When we generate the manifest, this creates a <channel> entry:
        """);

        // Generate and show the manifest's channel entry
        var manifest = GenerateChannelManifest();
        var channelLine = manifest.Split('\n')
            .FirstOrDefault(l => l.Contains("<channel ", StringComparison.OrdinalIgnoreCase));
        if (channelLine != null)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    {channelLine.Trim()}");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.WriteLine("  This tells the Event Log Service: \"Create an Operational log for this provider.\"");
        Console.WriteLine();
    }

    static void RegisterAndDemoChannel()
    {
        var channelName = $"{ChannelEventSource.EventSourceName}/Operational";
        var manifestPath = Path.Combine(Path.GetTempPath(), "channel-demo.man");

        try
        {
            // Step 2: Register the manifest
            Section("[Channel 2] Registering the manifest");
            var manifest = GenerateChannelManifest();
            File.WriteAllText(manifestPath, manifest);

            Run("wevtutil", $"um \"{manifestPath}\""); // clean up stale
            var (_, err) = Run("wevtutil", $"im \"{manifestPath}\"");
            Run("wevtutil", $"sl \"{channelName}\" /e:true");

            bool registered = string.IsNullOrWhiteSpace(err) ||
                err.Contains("does not contain the metadata resource", StringComparison.OrdinalIgnoreCase);

            if (registered)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✓ Manifest registered. Channel created: {channelName}");
                Console.ResetColor();
            }
            else
            {
                WriteError($"wevtutil im failed: {err.Trim()}");
                return;
            }

            // Step 3: Emit events
            Section("[Channel 3] Emitting events via ChannelEventSource");
            if (ChannelEventSource.Log.ConstructionException is { } ex)
            {
                WriteError($"ChannelEventSource construction failed: {ex}");
                return;
            }
            Thread.Sleep(1000); // let the ETW enable callback arrive

            ChannelEventSource.Log.RequestStarted("https://example.com/api/users");
            Console.WriteLine("  → RequestStarted (Informational)");
            ChannelEventSource.Log.RequestCompleted("200 OK — 3 users returned");
            Console.WriteLine("  → RequestCompleted (Informational)");
            ChannelEventSource.Log.OperationWarning("Response time exceeded 500ms threshold");
            Console.WriteLine("  → OperationWarning (Warning)");
            Console.WriteLine();

            // Step 4: Check if events appeared
            Thread.Sleep(2000);
            Section("[Channel 4] Checking Event Viewer");
            var (xml, _) = Run("wevtutil", $"qe \"{channelName}\" /f:text /c:10");

            if (!string.IsNullOrWhiteSpace(xml) && !xml.Contains("No events were found"))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  ✓ Events found in Event Viewer:");
                Console.ResetColor();
                foreach (var line in xml.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    Console.WriteLine($"    {line.TrimEnd()}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("""
                    Events were emitted but aren't visible in Event Viewer yet.

                    This is expected! The Event Log Service picks up new channels when:
                      • The manifest is registered at install time (MSI/setup), AND
                      • The Event Log Service is restarted (or the machine reboots)

                    In a development scenario like this, the channel is created but the
                    Event Log Service hasn't started its ETW consumer session for it yet.

                    To see the channel working:
                      1. Keep the manifest registered (don't clean up)
                      2. Restart the Event Log Service:
                         Restart-Service EventLog
                      3. Run this program again
                      4. Check Event Viewer → Applications and Services Logs
                """);
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"""

                    In production, this all happens automatically because:
                      • The manifest is registered during app installation
                      • The Event Log Service starts its consumer on next boot
                      • From then on, every WriteEvent with a channel is saved forever
                      • You browse them in Event Viewer at: {channelName}

                    That's the "stenographer" — always listening, always writing it down.
                """);
                Console.ResetColor();
            }

            Console.WriteLine();
        }
        finally
        {
            // Clean up the manifest registration
            if (File.Exists(manifestPath))
            {
                Run("wevtutil", $"um \"{manifestPath}\"");
                File.Delete(manifestPath);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  Manifest unregistered and cleaned up.");
                Console.ResetColor();
            }
        }
    }

    static string GenerateChannelManifest()
    {
        var assemblyPath = typeof(ChannelEventSource).Assembly.Location;
        if (string.IsNullOrEmpty(assemblyPath))
            assemblyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "ntdll.dll");
        return EventSource.GenerateManifest(typeof(ChannelEventSource), assemblyPath)!;
    }

    // Wraps Process.Start for the channel demo.
    static (string Out, string Err) Run(string exe, string args)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            })!;
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

    // ── helpers ──────────────────────────────────────────────────────────────

    static void Section(string title)
    {
        var line = new string('─', Math.Max(0, 60 - title.Length - 3));
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"── {title} {line}");
        Console.ResetColor();
    }

    static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"  ERROR: {message}");
        Console.ResetColor();
    }
}
