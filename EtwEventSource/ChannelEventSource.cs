using System.Diagnostics.Tracing;

namespace EtwEventSource;

/// <summary>
/// An EventSource that routes events to the Windows Event Log via a "channel".
///
/// HOW IT WORKS (in simple terms):
/// ───────────────────────────────
/// Windows has a built-in service called the "Event Log Service" (wevtsvc).
/// This service is ALWAYS running and acts as a permanent ETW consumer.
///
/// Normally, ETW events are ephemeral — they flow through in-memory kernel buffers
/// and vanish unless someone (like logman or PerfView) is actively recording them.
///
/// But when an EventSource defines a "channel", something different happens:
///   1. You register a manifest with Windows (wevtutil im manifest.xml)
///   2. The Event Log Service sees the channel definition and starts listening
///   3. Every event tagged with that channel is automatically saved to an .evtx file
///   4. You can browse these events in Event Viewer (eventvwr.msc) — at any time
///
/// Think of it like this:
///   Without channel → You shout into an empty room. Unless someone is recording, it's lost.
///   With channel    → You shout into a room with a stenographer. Everything is written down.
///
/// The "stenographer" is the Windows Event Log Service, and the "channel" tells it
/// to pay attention to your events.
///
/// CHANNEL TYPES:
///   Admin       — For end-user-facing errors/warnings   (always enabled)
///   Operational — For day-to-day diagnostics             (always enabled)
///   Analytic    — High-volume debug events               (disabled by default)
///   Debug       — Developer-only tracing                 (disabled by default)
///
/// WHERE TO FIND EVENTS IN EVENT VIEWER:
///   Event Viewer → Applications and Services Logs → DotnetSamples-EventLogDemo → Operational
///
/// VERSUS MyEventSource (the other source in this project):
///   MyEventSource      — No channel. Events only flow through ETW kernel buffers.
///                        You MUST use logman/PerfView to capture them, or they're lost.
///   ChannelEventSource — Has an Operational channel. The Event Log Service automatically
///                        captures events for you. No logman needed.
/// </summary>
[EventSource(Name = EventSourceName, Guid = ProviderId)]
sealed class ChannelEventSource : EventSource
{
    public const string ProviderId      = "87654321-4321-4321-4321-CBA987654321";
    public const string EventSourceName = "DotnetSamples-EventLogDemo";

    public static ChannelEventSource Log { get; } = new();

    private ChannelEventSource() : base(EventSourceSettings.EtwManifestEventFormat) { }

    // Tasks are required for channel-based events. Each (Task, Opcode) pair must be unique.
    public static class Tasks
    {
        public const EventTask Request   = (EventTask)1;
        public const EventTask Operation = (EventTask)2;
    }

    [Event(1, Channel = EventChannel.Operational, Level = EventLevel.Informational,
           Task = Tasks.Request, Opcode = EventOpcode.Start,
           Message = "Request started: {0}")]
    public void RequestStarted(string url) => WriteEvent(1, url);

    [Event(2, Channel = EventChannel.Operational, Level = EventLevel.Informational,
           Task = Tasks.Request, Opcode = EventOpcode.Stop,
           Message = "Request completed: {0}")]
    public void RequestCompleted(string message) => WriteEvent(2, message);

    [Event(3, Channel = EventChannel.Operational, Level = EventLevel.Warning,
           Task = Tasks.Operation, Opcode = EventOpcode.Info,
           Message = "Warning: {0}")]
    public void OperationWarning(string warning) => WriteEvent(3, warning);
}
