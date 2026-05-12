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
        BigSection("PART 1: ETW with logman — manual capture to .etl file");
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

    // ── Event Log demo ─────────────────────────────────────────────────────

    static void DemoEventLogChannel(bool isAdmin)
    {
        Console.WriteLine();
        Section("Event Log — the built-in \"stenographer\"");
        Console.WriteLine("""
          Everything above used logman to manually capture ETW events into an .etl file.
          But Windows can save events FOR you automatically — using the Event Log.

          Windows has a built-in service called the "Event Log Service" (wevtsvc).
          It's always running and acts as a permanent event store.

          There are TWO ways to write to the Event Log:

          ┌────────────────────────────────────────────────────────────────────┐
          │  1. TRADITIONAL API (System.Diagnostics.EventLog)                │
          │     • Writes directly to the Event Log — simple and immediate    │
          │     • Events appear in Event Viewer right away                   │
          │     • Does NOT use ETW at all                                    │
          │     • Best for: app logging, operational events                  │
          │                                                                  │
          │  2. ETW CHANNELS (EventSource + Channel attribute)              │
          │     • Events flow through ETW kernel buffers first              │
          │     • The Event Log Service acts as an automatic ETW consumer   │
          │     • Requires: manifest + native resource DLL + reboot         │
          │     • Best for: system providers installed via MSI/setup        │
          └────────────────────────────────────────────────────────────────────┘

          Below we use approach #1 (the traditional API) to demonstrate events
          appearing in Event Viewer. See ChannelEventSource.cs for approach #2.
        """);

        if (!isAdmin)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Skipping — Event Log source registration requires Administrator.");
            Console.ResetColor();
            return;
        }

        ShowChannelSourceCode();
        DemoTraditionalEventLog();
        DemoEtwChannel();
    }

    static void ShowChannelSourceCode()
    {
        Section("[EventLog 1] How ETW channels are defined (for reference)");
        Console.WriteLine("""
            In ChannelEventSource.cs, events are tagged with a channel:

              [Event(1, Channel = EventChannel.Operational, ...)]
              public void RequestStarted(string url) => WriteEvent(1, url);

            This generates a <channel> entry in the manifest:
        """);

        var ntdll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ntdll.dll");
        var manifest = EventSource.GenerateManifest(typeof(ChannelEventSource), ntdll)!;
        var channelLine = manifest.Split('\n')
            .FirstOrDefault(l => l.Contains("<channel ", StringComparison.OrdinalIgnoreCase));
        if (channelLine != null)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    {channelLine.Trim()}");
            Console.ResetColor();
        }

        Console.WriteLine("""

            In production (installed via MSI), this channel approach works like this:
              1. Installer registers the manifest + compiles native Win32 resource DLL
              2. Machine reboots → Event Log Service starts consuming the channel
              3. From then on, every WriteEvent with a channel is saved automatically

            But in a dev scenario, there's no installer or reboot. So below we use the
            simpler traditional API to show events appearing in Event Viewer.
        """);
    }

    static void DemoTraditionalEventLog()
    {
        const string sourceName = "DotnetSamples-TraditionalLog";
        const string logName = "Application";

        BigSection("PART 2: Traditional EventLog API — direct write to Event Viewer");
        Section("[EventLog 2] Writing events to Event Viewer");
        Console.WriteLine($"  Source: {sourceName}");
        Console.WriteLine($"  Log:    {logName}");
        Console.WriteLine();

        if (!System.Diagnostics.EventLog.SourceExists(sourceName))
        {
            System.Diagnostics.EventLog.CreateEventSource(sourceName, logName);
            Console.WriteLine($"  Created event source: {sourceName}");
        }

        System.Diagnostics.EventLog.WriteEntry(sourceName,
            "[Traditional] App started successfully — listening on port 8080",
            System.Diagnostics.EventLogEntryType.Information, 100);
        Console.WriteLine("  → App started (Information, ID=100)");

        System.Diagnostics.EventLog.WriteEntry(sourceName,
            "[Traditional] Database connection pool initialized — 10 connections ready",
            System.Diagnostics.EventLogEntryType.Information, 101);
        Console.WriteLine("  → DB pool ready (Information, ID=101)");

        System.Diagnostics.EventLog.WriteEntry(sourceName,
            "[Traditional] Memory usage above 80% — consider scaling up",
            System.Diagnostics.EventLogEntryType.Warning, 102);
        Console.WriteLine("  → Memory warning (Warning, ID=102)");
        Console.WriteLine();

        Section("[EventLog 3] Reading back from Event Viewer");

        using var log = new System.Diagnostics.EventLog(logName);
        var recent = log.Entries.Cast<System.Diagnostics.EventLogEntry>()
            .Where(e => e.Source == sourceName)
            .OrderByDescending(e => e.TimeGenerated)
            .Take(5)
            .ToList();

        if (recent.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ {recent.Count} event(s) found in Event Viewer!");
            Console.ResetColor();
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {"Time",-22}  {"Type",-12}  {"ID",-4}  Message");
            Console.WriteLine($"  {"────",-22}  {"────",-12}  {"──",-4}  ───────────────────────────────────");
            Console.ResetColor();

            foreach (var entry in recent)
            {
                Console.Write($"  {entry.TimeGenerated,-22:yyyy-MM-dd HH:mm:ss}  {entry.EntryType,-12}  {entry.InstanceId,-4}  ");
                Console.ForegroundColor = ConsoleColor.Green;
                var msg = entry.Message.ReplaceLineEndings(" ");
                Console.WriteLine(msg.Length > 55 ? msg[..52] + "..." : msg);
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  No events found (unexpected).");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("""
          ┌──────────────────────────────────────────────────────────────────┐
          │  To browse these events yourself:                               │
          │    1. Open Event Viewer (eventvwr.msc)                          │
          │    2. Go to: Windows Logs → Application                         │
          │    3. Filter by Source: DotnetSamples-TraditionalLog              │
          │                                                                 │
          │  These events were saved permanently by the Event Log Service.  │
          │  No logman, no PerfView, no ETW session needed.                 │
          │                                                                 │
          │  For production apps with ETW channels (see ChannelEventSource),│
          │  this happens automatically through ETW — no API call needed.   │
          │  The Event Log Service listens to ETW and saves channel events. │
          └──────────────────────────────────────────────────────────────────┘
        """);
        Console.ResetColor();
    }

    static void DemoEtwChannel()
    {
        var channelName = $"{ChannelEventSource.EventSourceName}/Operational";
        var manifestPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EtwEventSourceDemo", "channel-demo.man");
        var resourceDllPath = Path.ChangeExtension(manifestPath, ".dll");

        BigSection("PART 3: ETW Channel — automatic capture by Event Log Service");
        Section("[ETW Channel] Registering for post-reboot test");
        Console.WriteLine("""
            This section registers the ChannelEventSource manifest so the Event Log
            Service will start consuming its ETW channel after a reboot.

            Unlike the traditional EventLog API above, this uses the full ETW pipeline:
              Your code → ETW kernel buffers → Event Log Service → .evtx → Event Viewer

            The Event Log Service only picks up new channels at boot time, so a reboot
            is required before events appear. Here's what we do now:
              1. Compile the manifest into a native Win32 resource DLL (mc.exe + rc.exe + csc.exe)
              2. Register it with wevtutil im
              3. Emit some events (they won't appear until after reboot)
              4. Leave everything registered — reboot and run again to see them!
        """);

        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

        if (!CompileAndRegisterManifest(manifestPath, resourceDllPath, channelName))
            return;

        // Emit events — they'll be captured by the Event Log Service after reboot
        if (ChannelEventSource.Log.ConstructionException is { } ex)
        {
            WriteError($"ChannelEventSource construction failed: {ex}");
            return;
        }
        Thread.Sleep(500);

        ChannelEventSource.Log.RequestStarted("https://example.com/api/users");
        ChannelEventSource.Log.RequestCompleted("200 OK — 3 users returned");
        ChannelEventSource.Log.OperationWarning("Response time exceeded 500ms threshold");
        Console.WriteLine("  Emitted 3 events via ChannelEventSource.");

        // Check if channel is already active (e.g. after a reboot)
        var (xml, _) = Run("wevtutil", $"qe \"{channelName}\" /f:text /c:5");
        if (!string.IsNullOrWhiteSpace(xml))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n  ✓ Events found in {channelName}!");
            Console.ResetColor();
            foreach (var line in xml.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Console.WriteLine($"    {line.TrimEnd()}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  Events not visible yet — reboot to activate the channel.");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"""
          After rebooting, check Event Viewer:
            → Applications and Services Logs → {channelName}

          To clean up later:
            wevtutil um "{manifestPath}"
            rmdir /s "{Path.GetDirectoryName(manifestPath)}"
        """);
        Console.ResetColor();
    }

    // Compiles the EventSource manifest into a native Win32 resource DLL and
    // registers it with the Event Log Service.
    //
    // Why? The Event Log Service needs WEVT_TEMPLATE resources (native Win32)
    // to validate a provider and activate its channel. A plain .NET assembly
    // doesn't have these, so we compile them using the Windows SDK:
    //   mc.exe  → manifest → .rc + .h + .bin (message tables)
    //   rc.exe  → .rc → .res (binary resource)
    //   csc.exe → .res → .dll (minimal DLL with embedded resources)
    static bool CompileAndRegisterManifest(string manifestPath, string resourceDllPath, string channelName)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "channel-demo-build");
        Directory.CreateDirectory(workDir);

        var sdkBin = FindSdkBinPath();
        if (sdkBin == null) { WriteError("Windows SDK not found (mc.exe needed)."); return false; }
        var cscPath = FindCscPath();
        if (cscPath == null) { WriteError("csc.exe not found."); return false; }

        var mcExe = Path.Combine(sdkBin, "mc.exe");
        var rcExe = Path.Combine(sdkBin, "rc.exe");

        // Generate manifest pointing to the resource DLL location
        var manifest = EventSource.GenerateManifest(typeof(ChannelEventSource), resourceDllPath)!;
        File.WriteAllText(manifestPath, manifest);

        // mc.exe: manifest → .rc + .bin
        var (_, mcErr) = Run(mcExe, $"-um \"{manifestPath}\" -h \"{workDir}\" -r \"{workDir}\"");
        if (mcErr?.Contains("error", StringComparison.OrdinalIgnoreCase) == true)
        { WriteError($"mc.exe: {mcErr.Trim()}"); return false; }

        // rc.exe: .rc → .res
        var rcFile = Directory.GetFiles(workDir, "*.rc").FirstOrDefault();
        if (rcFile == null) { WriteError("mc.exe produced no .rc file."); return false; }
        Run(rcExe, $"\"{rcFile}\"");

        // csc.exe: .res → minimal DLL
        var resFile = Path.ChangeExtension(rcFile, ".res");
        if (!File.Exists(resFile)) { WriteError("rc.exe produced no .res file."); return false; }
        Run(cscPath, $"-target:library -out:\"{resourceDllPath}\" -win32res:\"{resFile}\" -nologo");
        if (!File.Exists(resourceDllPath)) { WriteError("csc.exe failed to create resource DLL."); return false; }

        // Grant read access so the Event Log Service (SYSTEM) can load it
        Run("icacls", $"\"{resourceDllPath}\" /grant Everyone:(R)");

        // Register the manifest
        Run("wevtutil", $"um \"{manifestPath}\"");
        var (_, regErr) = Run("wevtutil", $"im \"{manifestPath}\"");
        Run("wevtutil", $"sl \"{channelName}\" /e:true");

        bool ok = string.IsNullOrWhiteSpace(regErr) ||
            regErr.Contains("does not contain the metadata resource", StringComparison.OrdinalIgnoreCase);

        if (ok)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ Manifest registered. Channel: {channelName}");
            Console.ResetColor();

            // Verify provider metadata is loadable
            var (gpOut, _) = Run("wevtutil", $"gp \"{ChannelEventSource.EventSourceName}\"");
            if (gpOut?.Contains("channels:") == true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  ✓ Provider metadata loads correctly.");
                Console.ResetColor();
            }
        }
        else
        {
            WriteError($"wevtutil im: {regErr.Trim()}");
        }

        try { Directory.Delete(workDir, true); } catch { }
        return ok;
    }

    static string? FindSdkBinPath()
    {
        var kitsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Windows Kits", "10", "bin");
        if (!Directory.Exists(kitsRoot)) return null;
        return Directory.GetDirectories(kitsRoot, "10.*")
            .OrderByDescending(d => d)
            .Select(d => Path.Combine(d, "x64"))
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "mc.exe")));
    }

    static string? FindCscPath()
    {
        // .NET Framework csc.exe
        var fw = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        if (File.Exists(fw)) return fw;

        // Visual Studio Roslyn csc.exe
        foreach (var vsRoot in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio"),
        }.Where(Directory.Exists))
        {
            var csc = Directory.GetFiles(vsRoot, "csc.exe", SearchOption.AllDirectories)
                .OrderByDescending(f => f).FirstOrDefault();
            if (csc != null) return csc;
        }
        return null;
    }

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

    static void BigSection(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(new string('═', 70));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('═', 70));
        Console.ResetColor();
        Console.WriteLine();
    }

    static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"  ERROR: {message}");
        Console.ResetColor();
    }
}
