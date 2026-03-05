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

## Notable gotchas documented in the code

- **Keyword filter**: `logman` keyword `0xffffffff` silently drops events with `EventKeywords.None` (the default). Use `0x0` to disable the filter entirely.
- **`wevtutil im` timing**: registering the manifest while the ETW session is live causes the Event Log service to send async disable callbacks to your provider, silently dropping events. Registration is deferred to after `Stop()`.
- **`resourceFileName` accessibility**: the path embedded in the manifest must be readable by `NT SERVICE\EventLog`. `ntdll.dll` is used as a harmless dummy to satisfy this check.
