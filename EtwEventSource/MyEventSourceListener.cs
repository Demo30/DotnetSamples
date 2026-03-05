using System.Diagnostics.Tracing;

namespace EtwEventSource;

// In-process listener — an alternative to logman for observing EventSource events.
sealed class MyEventSourceListener : EventListener
{
    readonly string _name;

    public MyEventSourceListener(string name) => _name = name;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == MyEventSource.EventSourceName)
        {
            Console.WriteLine($"[{_name}] Listening to: {eventSource.Name} {{{eventSource.Guid}}}");
            Console.WriteLine($"  {"TID",-5} {"ActivityId",-38} {"Event",-15} Payload");
            EnableEvents(eventSource, EventLevel.LogAlways);
        }
        else if (eventSource.Name == "System.Threading.Tasks.TplEventSource")
        {
            // Keyword 0x80 enables Activity ID propagation through async continuations
            EnableEvents(eventSource, EventLevel.LogAlways, (EventKeywords)0x80);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        lock (this)
        {
            var payload = e.Payload?.Count == 1 ? e.Payload[0]?.ToString() : "";
            Console.WriteLine($"  {e.OSThreadId,-5} {e.ActivityId,-38} {e.EventName,-15} {payload}");
        }
    }
}
