using System.Diagnostics.Tracing;

namespace EtwEventSource;

[EventSource(Name = EventSourceName, Guid = ProviderId)]
sealed class MyEventSource : EventSource
{
    public const string ProviderId      = "12345678-1234-1234-1234-123456789012";
    public const string EventSourceName = "MyTestSource";

    public static MyEventSource Log { get; } = new();

    // NOTE: must use base(EventSourceSettings) here, NOT base(name, settings).
    // The (name, settings) overload generates the GUID from a name-hash and ignores
    // the [EventSource(Guid = ...)] attribute entirely.
    //
    private MyEventSource() : base(EventSourceSettings.EtwManifestEventFormat) { }

    // Message = "..." generates %1/%2 message-string entries in the manifest so that
    // tracerpt and other tools can render the payload parameters as human-readable text
    // once the manifest is registered system-wide (wevtutil im).
    //
    // NOTE on Channel: adding Channel = EventChannel.Debug would put a <channels> element
    // in the manifest (needed for Event Viewer / Message Analyzer), but Debug channels must
    // be explicitly enabled via `wevtutil sl <channel> /e:true` before EventSource will
    // write through them — without that, events are silently suppressed. For plain ETW
    // trace sessions (logman) the channel attribute is not required; omitting it keeps
    // event capture unconditional while the registered manifest still decodes payloads.
    [Event(1, Opcode = EventOpcode.Start, Message = "Started: {0}")]
    public void MyEventStart(string requestName) => WriteEvent(1, requestName);

    [Event(2, Opcode = EventOpcode.Stop, Message = "Stopped: {0}")]
    public void MyEventStop(string message) => WriteEvent(2, message);
}
