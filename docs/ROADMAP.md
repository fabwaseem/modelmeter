# Roadmap

## Phase 1 — Foundation
- [x] Initialize repository and WidBar WinUI host
- [x] Premium taskbar preview and flyout navigation
- [x] Overview, Providers, Analytics, Models, Sessions and Quotas dashboard pages
- [x] Basic settings surface
- [x] Provider-neutral usage contracts and aggregation
- [x] Windows widget build and Core test workflow
- [ ] Replace temporary MSIX placeholder icons with final brand assets
- [ ] Verify installation and runtime inside WidBar on a Windows desktop

## Phase 2 — Provider ingestion
- [x] Claude Code local JSONL usage reader and dashboard integration
- [x] Codex local rollout token_count reader and dashboard integration
- [x] Gemini CLI opt-in local telemetry reader
- [x] OpenCode local session/database reader
- [x] Local SQLite persistence, indexes and idempotent upserts
- [ ] Incremental source checkpoints and safe reconciliation
- [ ] Background refresh while the widget is active
- [ ] Import diagnostics and provider-specific recovery guidance

## Phase 3 — Analytics
- [ ] Date-range aggregation and model/project breakdowns
- [ ] Versioned pricing catalogue and manual overrides
- [ ] Session explorer and export
- [ ] Provider-reported quota snapshots

## Phase 4 — Polish and distribution
- [ ] Preview modes and notifications
- [ ] Usage forecasting and alerts
- [ ] High-DPI, accessibility and theme QA
- [ ] Signed MSIX, release workflow and WidBar catalog submission
