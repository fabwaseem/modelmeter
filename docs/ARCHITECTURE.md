# Architecture

## Boundaries
- **ExtensionApp** owns WidBar lifecycle and WinUI presentation.
- **Core** owns normalized usage contracts, provider interfaces and aggregation.
- **Infrastructure** will own SQLite, file monitoring and source readers.
- **Provider adapters** will isolate Claude Code, Codex, Gemini CLI and OpenCode formats.

## Data flow
1. Detect supported provider installations and sources.
2. Import historical usage and persist source checkpoints.
3. Monitor file changes as a signal, then reconcile from stable offsets.
4. Normalize and deduplicate usage events.
5. Persist usage-only records locally.
6. Aggregate summaries for taskbar preview and flyout.

File watcher notifications are not authoritative: events can be duplicated or missed. Collectors must reconcile from source identity and offsets and periodically verify.

## Data honesty
- Subscription quota is not API billing.
- API-equivalent cost is an estimate, not an invoice.
- Quotas and reset times appear only when a supported source reports them.
- Missing, stale or unsupported values must be explicit.
- Never collect prompt/response content or source code.

## UI surfaces
Taskbar preview is compact and glanceable. Flyout contains Overview, Providers, Analytics, Models, Sessions and Quotas. Settings control preview visibility and display preferences.
