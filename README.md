# Static constructor poisoning

A minimal standalone repro of a configuration exception escaping a static
constructor. No product libraries or NuGet packages are required.

## Run

Requires the .NET 10 SDK. From the sample directory:

```powershell
.\Run.ps1
```

Alternatively:

```powershell
dotnet run --project .\StaticCtorPoisoning.csproj
```

## What it demonstrates

The first access to `MeterConfiguration.IsConfigured` invokes its static
constructor. The simulated configuration deliberately throws an
`InvalidOperationException`, which is surfaced as a `TypeInitializationException`.

Before the second access, the program clears the simulated configuration failure.
Nevertheless, both subsequent accesses still fail: the static constructor is not
retried for that type in this process. The counter is stored outside the failed
type so it remains readable.

Expected output:

```text
Access attempt 1:
  Static constructor entered.
  TypeInitializationException
  Inner: InvalidOperationException: Deliberate initialization failure.

Configuration corrected: initialization would now succeed.
Access attempt 2:
  TypeInitializationException
  Inner: InvalidOperationException: Deliberate initialization failure.
Access attempt 3:
  TypeInitializationException
  Inner: InvalidOperationException: Deliberate initialization failure.

Static constructor calls: 1
PASS: all 3 accesses failed, even after correcting configuration.
```

The program checks each failure and the constructor count, and exits unsuccessfully
if the expected behavior is not reproduced. The same source has also been compiled
and run successfully on .NET Framework.
