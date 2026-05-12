# Sample fixtures

Test event log files in each supported format. Use them to manually verify the **Load EVTX** / **Load XML\*** / **Load ETL\*** buttons, or load them via CLI:

> **\* XML and ETL support is experimental.** EVTX is the primary, fully-supported format. XML files are parsed event-by-event and only formats produced by `wevtutil qe ... /f:xml` are reliably supported; ETL traces are read via `EventLogReader` and may show limited detail for kernel/provider traces.

```
SimpleEventViewer.exe samples\sample.evtx
SimpleEventViewer.exe samples\sample.xml
SimpleEventViewer.exe samples\sample.etl
```

| File | Format | Source |
|---|---|---|
| `sample.evtx` | Windows Event Log binary | `wevtutil epl Application ... /q:"*[System[(Level=2 or Level=3)]]"` — errors+warnings only |
| `sample.xml` | Event XML | `wevtutil qe Application /f:xml /c:100` wrapped in a root `<Events>` element |
| `sample.etl` | Event Tracing for Windows | ~3 seconds of `Microsoft-Windows-Kernel-Process` events via `logman` |

## Regenerating

To regenerate with fresh data from your machine:

```pwsh
powershell -ExecutionPolicy Bypass -File samples\generate-fixtures.ps1
```

The ETL capture uses `logman`, which usually needs an elevated shell.
