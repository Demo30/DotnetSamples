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
  41580 00000013-0000-0000-0000-00004ce99d59   MyEventStart    Hello world #2
  41580 00000013-0000-0000-0000-00004ce99d59   MyEventStop     Done #2
  41580 00000014-0000-0000-0000-00004de99d59   MyEventStart    Hello world #3
  41580 00000014-0000-0000-0000-00004de99d59   MyEventStop     Done #3
  41580 00000015-0000-0000-0000-00004ee99d59   MyEventStart    Hello world #4
  41580 00000015-0000-0000-0000-00004ee99d59   MyEventStop     Done #4

── [2b] Session diagnostics ─────────────────────────────────

Name:                 MyETWTest
Status:               Running
Root Path:            C:\Users\...\AppData\Local\Temp
Segment:              Off
Schedules:            On

Name:                 MyETWTest\MyETWTest
Type:                 Trace
Output Location:      C:\Users\...\AppData\Local\Temp\myetwtest.etl
Append:               Off
Circular:             Off
Overwrite:            Off
Buffer Size:          8
Buffers Lost:         0
Buffers Written:      3
Buffer Flush Timer:   1
Clock Type:           Performance
File Mode:            File

Provider:
Name:                 {12345678-1234-1234-1234-123456789012}
Provider Guid:        {12345678-1234-1234-1234-123456789012}
Level:                255
KeywordsAll:          0x0
KeywordsAny:          0xffffffffffffffff
Properties:           64
Filter Type:          0

The command completed successfully.

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
  1     1         Hello world #0
  2     2         Done #0
  1     1         Hello world #1
  2     2         Done #1
  1     1         Hello world #2
  2     2         Done #2
  1     1         Hello world #3
  2     2         Done #3
  1     1         Hello world #4
  2     2         Done #4

── [4] Full XML ─────────────────────────────────────────────
<Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
  <System>
    <Provider Guid="{12345678-1234-1234-1234-123456789012}" />
    <EventID>65534</EventID>
    <Version>1</Version>
    <Level>0</Level>
    <Task>65534</Task>
    <Opcode>254</Opcode>
    <Keywords>0xFFFFFFFFFFFFFF</Keywords>
    <TimeCreated SystemTime="2026-03-05T13:21:46.877378900+00:59" />
    <Correlation ActivityID="{00000000-0000-0000-0000-000000000000}" />
    <Execution ProcessID="28812" ThreadID="43744" ProcessorID="18" KernelTime="0" UserTime="0" />
    <Channel />
    <Computer />
  </System>
  <BinaryEventData>...</BinaryEventData>
</Event>
  Decoded: {Infrastructure event — manifest blob, not decoded}

<Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
  <System>
    <Provider Guid="{12345678-1234-1234-1234-123456789012}" />
    <EventID>1</EventID>
    <Version>0</Version>
    <Level>4</Level>
    <Task>0</Task>
    <Opcode>1</Opcode>
    <Keywords>0xF00000000000</Keywords>
    <TimeCreated SystemTime="2026-03-05T13:21:47.394976200+00:59" />
    <Correlation ActivityID="{00000011-0000-0000-0000-000032e99d59}" />
    <Execution ProcessID="28812" ThreadID="41580" ProcessorID="19" KernelTime="45" UserTime="45" />
    <Channel />
    <Computer />
  </System>
  <BinaryEventData>480065006C006C006F00200077006F0072006C0064002000230030000000</BinaryEventData>
</Event>
  Decoded: Hello world #0

<Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
  <System>
    <Provider Guid="{12345678-1234-1234-1234-123456789012}" />
    <EventID>2</EventID>
    <Version>0</Version>
    <Level>4</Level>
    <Task>0</Task>
    <Opcode>2</Opcode>
    <Keywords>0xF00000000000</Keywords>
    <TimeCreated SystemTime="2026-03-05T13:21:47.395864900+00:59" />
    <Correlation ActivityID="{00000011-0000-0000-0000-000032e99d59}" />
    <Execution ProcessID="28812" ThreadID="41580" ProcessorID="19" KernelTime="45" UserTime="45" />
    <Channel />
    <Computer />
  </System>
  <BinaryEventData>44006F006E0065002000230030000000</BinaryEventData>
</Event>
  Decoded: Done #0

  ... (events #1–#4 follow the same pattern)
```

## Notable gotchas documented in the code

- **Keyword filter**: `logman` keyword `0xffffffff` silently drops events with `EventKeywords.None` (the default). Use `0x0` to disable the filter entirely.
- **`wevtutil im` timing**: registering the manifest while the ETW session is live causes the Event Log service to send async disable callbacks to your provider, silently dropping events. Registration is deferred to after `Stop()`.
- **`resourceFileName` accessibility**: the path embedded in the manifest must be readable by `NT SERVICE\EventLog`. `ntdll.dll` is used as a harmless dummy to satisfy this check.
