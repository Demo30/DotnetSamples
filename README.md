# DotnetSamples

A collection of focused .NET samples. Each sample lives on its own branch so it can be cloned independently.

## Samples

| Branch | Description |
|--------|-------------|
| [etw-eventsource](https://github.com/Demo30/DotnetSamples/tree/etw-eventsource) | ETW tracing with `EventSource` — define a custom provider, capture events with `logman`, and decode the `.etl` output with `tracerpt`. Demonstrates the full emit → capture → decode pipeline including manifest registration. |
| [static-ctor-type-poisoning](https://github.com/Demo30/DotnetSamples/tree/static-ctor-type-poisoning) | Minimal standalone repro showing how an exception escaping a static constructor poisons the type: subsequent accesses fail even after the configuration is corrected. |
