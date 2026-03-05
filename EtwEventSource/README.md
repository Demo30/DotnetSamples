# ETW EventSource Sample

Demonstrates the full **ETW (Event Tracing for Windows)** pipeline in .NET:
emit events with `EventSource`, capture them out-of-process with `logman`, and
decode the resulting `.etl` file with `tracerpt`.

## Requirements

- Windows
- .NET 9 SDK
- **Run as Administrator** (required for `logman` session creation and `wevtutil im`)

## Run

```
dotnet run
```

## What it does

| Step | Tool | What happens |
|------|------|--------------|
| 1. Emit | `EventSource` | `MyTestSource` provider writes structured events in-process |
| 2. Capture | `logman` | Kernel-level ETW session intercepts events → `.etl` binary file |
| 3. Decode | `tracerpt` + `wevtutil im` | `.etl` converted to XML with decoded payloads |

## Key files

| File | Purpose |
|------|---------|
| `MyEventSource.cs` | The ETW provider — defines event IDs, opcodes, and message templates |
| `EtwRecordingSession.cs` | Wraps `logman` / `tracerpt` / `wevtutil` into a clean Start/Stop/Decode API |
| `MyEventSourceListener.cs` | In-process `EventListener` for verifying events fire independently of ETW |
| `Program.cs` | Orchestrates the pipeline and prints color-coded output |

## Sample output

```
── What this sample demonstrates ────────────────────────────
  This program demonstrates the full ETW (Event Tracing for Windows) pipeline:

  1. EMIT   — A custom EventSource (MyTestSource) writes structured events in-process
              using the .NET EventSource API.

  2. CAPTURE — logman creates a kernel-level ETW trace session that intercepts those
               events and writes them to a binary .etl file, with no code changes
               to the emitting side.

  3. DECODE — tracerpt reads the .etl and converts it to human-readable XML.
              For payloads to be decoded (rather than shown as raw BinaryEventData),
              the event schema must be known. This is what the manifest is for.
  ...

── Provider info ────────────────────────────────────────────
  Provider : MyTestSource ({12345678-1234-1234-1234-123456789012})
  ETL file : C:\Users\...\AppData\Local\Temp\myetwtest.etl
  Settings : EtwManifestEventFormat
  Guid     : 12345678-1234-1234-1234-123456789012

── In-process listener ──────────────────────────────────────
[InProc] Listening to: MyTestSource {12345678-1234-1234-1234-123456789012}
  TID   ActivityId                             Event           Payload

── [1] Starting ETW session ─────────────────────────────────
The command completed successfully.
  IsEnabled = True

── [2] Emitting events ──────────────────────────────────────
  41580 00000011-0000-0000-0000-000032e99d59   MyEventStart    Hello world #0
  41580 00000011-0000-0000-0000-000032e99d59   MyEventStop     Done #0
  41580 00000012-0000-0000-0000-000033e99d59   MyEventStart    Hello world #1
  41580 00000012-0000-0000-0000-000033e99d59   MyEventStop     Done #1
  ...

── [3] Stopping session ─────────────────────────────────────
The command completed successfully.
  Size : 24,576 bytes
  Path : C:\Users\...\AppData\Local\Temp\myetwtest.etl

  Raw ETL scan: "Hello world" found 5x — payload bytes ARE in the binary.

── [4] Decoding with tracerpt ───────────────────────────────
  Manifest registered.

── [4] 11 event(s) from MyTestSource ────────────────────────
  ID    Opcode    Payload
  ──    ──────    ──────────────────────────────────────
  65534  254       {Infrastructure event — manifest blob, not decoded}
  1     Start     Hello world #0
  2     Stop      Done #0
  1     Start     Hello world #1
  2     Stop      Done #1
  1     Start     Hello world #2
  2     Stop      Done #2
  1     Start     Hello world #3
  2     Stop      Done #3
  1     Start     Hello world #4
  2     Stop      Done #4
```

## Notable gotchas documented in the code

- **Keyword filter**: `logman` keyword `0xffffffff` silently drops events with `EventKeywords.None` (the default). Use `0x0` to disable the filter entirely.
- **`wevtutil im` timing**: registering the manifest while the ETW session is live causes the Event Log service to send async disable callbacks to your provider, silently dropping events. Registration is deferred to after `Stop()`.
- **`resourceFileName` accessibility**: the path embedded in the manifest must be readable by `NT SERVICE\EventLog`. `ntdll.dll` is used as a harmless dummy to satisfy this check.
