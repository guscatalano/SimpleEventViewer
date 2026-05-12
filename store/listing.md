# Microsoft Store listing — Simple Event Viewer

Paste these fields into **Partner Center → Apps → Simple Event Viewer → Store listings → English (United States)**.

Store identity (already set in `Package.appxmanifest`):

| Field | Value |
|---|---|
| Package/Identity/Name | `GusCatalano.SimpleEventViewer` |
| Package/Identity/Publisher | `CN=119E0257-3B74-437C-A728-AC7C50256853` |
| PublisherDisplayName | `Gus Catalano` |
| Store ID | `9N63Z84M7ZWK` |
| URL | https://apps.microsoft.com/detail/9N63Z84M7ZWK |

---

## Display name
Simple Event Viewer

## Short description (≤ 200 chars)
A fast, modern viewer for Windows Event Logs. Stream live events or open `.evtx` files, filter and search across millions of entries, and copy results out as text, CSV, or JSON.

## Description

Simple Event Viewer is a clean, keyboard-friendly alternative to the built-in Windows Event Viewer — built with WinUI 3 and designed for everyday troubleshooting.

Whether you're investigating a crash, hunting down a flaky service, or pulling evidence for a postmortem, Simple Event Viewer lets you get to the events that matter without fighting an MMC console.

**Read events from anywhere**
- Live Windows event logs (Application channel) — streamed in batches so the UI stays responsive even for 50,000+ entries
- Saved `.evtx` files — open them by drag-and-drop, file picker, or command line

**Filter the way you actually think**
- Time range (Last hour / 24h / 7d / 30d / All / Custom)
- Event level (Critical / Error / Warning / Information / Verbose)
- Source, user, process ID, computer, channel — each picker shows live counts
- Full-text message search

**Inspect every detail**
- Sortable, resizable, reorderable columns that remember widths across launches
- Full event details pane with copy buttons on every field
- Raw event XML view when you need the wire format

**Copy and export**
- Copy a single cell, a whole row, or any selection
- Export to CSV (escaped) or JSON (pretty-printed)

**Eight themes to suit your setup**
- System, Light, Dark, High Contrast, Nord, Dracula, Solarized, Sepia
- Independent accent picker (Blue, Green, Purple, Orange, Red) when you're not on a curated preset

**Optional local MCP server**
- Toggle on a `127.0.0.1`-bound Model Context Protocol server and let an LLM client (VS Code, Cursor, Claude Desktop, Claude Code) query the currently-loaded events. Built-in tools for listing, searching, and inspecting events.

**Designed for performance**
- Streaming reads, throttled UI updates, and background prefetch of older events so widening a time range is instant
- All filtering happens in-memory once data is loaded

Free, no telemetry, no ads.

## What's new (1.0.0)
First official release.

- Live Windows event log reader with streaming load
- Open `.evtx` files; experimental support for `.xml` and `.etl` behind a toggle
- Eight built-in themes and a separate app-wide accent picker
- Column widths persist across launches
- Optional local MCP server with VS Code / Cursor / Claude Desktop / Claude Code configs
- About page shows the running version

## Features (bulleted, for the short list section)
- Live Windows event logs and `.evtx` files
- Rich filters: time range, level, source, user, process, computer, channel, message search
- Sortable, resizable, reorderable columns with persisted widths
- Copy as text / CSV / JSON
- Light, Dark, Nord, Dracula, Solarized, Sepia, High Contrast themes
- Local MCP server for LLM integrations

## Search terms (private, comma-separated)
event log, event viewer, evtx, windows logs, log viewer, eventlog, system logs, troubleshooting, debugging, .NET Runtime errors, MCP server

## Copyright
© 2026 Gus Catalano

## Contact info
https://github.com/guscatalano

## Privacy policy URL
*(required by Store — point to a hosted privacy page; the app collects no telemetry)*

## Screenshots
- `docs/screenshot.png` — main UI showing filters, events grid, and details pane. 1440×737 PNG.

The Store recommends 1366×768 minimum and at least one screenshot per supported form factor. The captured PNG meets that.

---

## Submission checklist

- [x] `Package.appxmanifest` has Store-registered Identity + PublisherDisplayName
- [x] MSIX built and attached to the v1.0.0 GitHub release
- [ ] Upload `SimpleEventViewer_1.0.0.0_x64.msix` to **Packages**
- [ ] Confirm Windows.Desktop is the only checked device family (uncheck Xbox / Holographic / IoT)
- [ ] Fill **Description**, **What's new**, **Search terms** from this file
- [ ] Upload `docs/screenshot.png` (and optionally additional screenshots showing other themes / the MCP settings card)
- [ ] Set a **Privacy policy URL** (Store requires one even if you collect nothing)
- [ ] Choose **Age rating** (typically 3+ for a log viewer)
- [ ] Pick **Pricing** — Free
