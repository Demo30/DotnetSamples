# ETW EventSource Sample

Demonstrates three approaches to event tracing and logging on Windows:

| Part | Approach | How it works |
|------|----------|-------------|
| **1. ETW with logman** | Manual capture | `EventSource` emits events → `logman` captures to `.etl` → `tracerpt` decodes to XML |
| **2. Traditional EventLog API** | Direct write | `System.Diagnostics.EventLog.WriteEntry()` → events appear in Event Viewer immediately |
| **3. ETW Channels** | Automatic capture | `EventSource` with `Channel` attribute → Event Log Service captures automatically |

## Requirements

- Windows
- .NET 9 SDK
- **Run as Administrator** (required for `logman`, `wevtutil`, and Event Log source registration)

## Run

```
cd EtwEventSource
.\run.bat
```

Or directly:

```
cd EtwEventSource
dotnet run
```

## Key files

| File | Purpose |
|------|---------|
| `MyEventSource.cs` | ETW provider for Part 1 — defines events with no channel (ephemeral unless captured) |
| `ChannelEventSource.cs` | ETW provider for Part 3 — defines events with an Operational channel (auto-saved by Event Log Service) |
| `EtwRecordingSession.cs` | Wraps `logman` / `tracerpt` / `wevtutil` into a clean Start/Stop/Decode API |
| `MyEventSourceListener.cs` | In-process `EventListener` for verifying events fire independently of ETW |
| `Program.cs` | Orchestrates all three parts and prints color-coded output |
| `run.bat` | Launcher script that checks for admin privileges |

## Part 1: ETW with logman — manual capture

Events are **ephemeral** — they only exist in ETW kernel buffers and vanish unless
someone is actively recording them. This part uses `logman` to create a trace session.

| Step | Tool | What happens |
|------|------|--------------|
| Emit | `EventSource` | `MyTestSource` writes "Hello world" events in-process |
| Capture | `logman` | Kernel-level ETW session intercepts events → `.etl` binary file |
| Decode | `tracerpt` + `wevtutil im` | `.etl` converted to XML with decoded payloads |

## Part 2: Traditional EventLog API — direct write

Uses `System.Diagnostics.EventLog` to write directly to the Windows Event Log.
Events appear in Event Viewer immediately under **Windows Logs → Application**.
Does **not** use ETW at all — simplest approach for application logging.

## Part 3: ETW Channels — automatic capture

Uses an `EventSource` with `Channel = EventChannel.Operational`. The Event Log Service
acts as a permanent ETW consumer, automatically saving events to `.evtx` files.
Events appear in Event Viewer under **Applications and Services Logs**.

This approach requires:
1. A native Win32 resource DLL (compiled from the manifest with `mc.exe` + `rc.exe`)
2. Manifest registration with `wevtutil im`
3. A reboot for the Event Log Service to pick up the new channel

## Notable gotchas documented in the code

- **Keyword filter**: `logman` keyword `0xffffffff` silently drops events with `EventKeywords.None` (the default). Use `0x0` to disable the filter entirely.
- **`wevtutil im` timing**: registering the manifest while the ETW session is live causes the Event Log service to send async disable callbacks to your provider, silently dropping events. Registration is deferred to after `Stop()`.
- **`resourceFileName` accessibility**: the path embedded in the manifest must be readable by `NT SERVICE\EventLog`. `ntdll.dll` is used as a harmless dummy to satisfy this check.
- **Channel activation**: new ETW channels require a reboot for the Event Log Service to start consuming them. In dev scenarios, the traditional EventLog API (Part 2) is simpler.
