# ModelMeter

**Your AI usage. One dashboard.**

ModelMeter is a local-first Windows 11 WidBar widget for understanding AI coding-tool usage across providers. It brings token usage, model activity, API-equivalent cost estimates, sessions and supported quota information into a compact taskbar preview and a rich flyout.

> **Current milestone:** The local Claude Code and Codex usage readers are implemented and covered by Windows CI tests. The UI still needs to be connected to these readers; until that integration lands, its figures remain illustrative demo data.

## First milestone
- Native WidBar widget host using the official standalone template
- Compact taskbar preview
- Flyout with Overview, Providers, Analytics, Models, Sessions and Quotas
- Settings surface
- Premium dark visual system with restrained lime accent
- Provider-neutral domain contracts and aggregation

## Provider support
| Provider | Local source | Status |
|---|---|---|
| Claude Code | `~/.claude/projects/**/*.jsonl` assistant usage metadata | Reader implemented; UI integration pending |
| OpenAI Codex | `$CODEX_HOME/sessions` and `archived_sessions` rollout JSONL `token_count` events | Reader implemented; UI integration pending |
| Gemini CLI | Explicitly opted-in local OpenTelemetry output | Planned |
| OpenCode | Local session/database data | Planned |

The readers return token metadata only. They do not retain message bodies, tool calls, tool outputs, or full working-directory paths. Codex usage is available only when the local rollout contains `token_count` events; interactive sessions without those events cannot be reconstructed from transcript text.

ModelMeter will distinguish subscription quota, actual API billing and API-equivalent estimates. It will never present an estimate as an invoice or infer a quota reset time.

## Build
Requirements: Windows 11, .NET 10 SDK, Visual Studio with WinUI 3 / Windows App SDK tooling, and WidBar installed.

Open `ModelMeter.sln`, select `Debug | x64`, and build the packaging project. For command-line builds from Developer PowerShell:

```powershell
msbuild "ModelMeter (Package)\ModelMeter (Package).wapproj" /p:Configuration=Debug /p:Platform=x64 /restore
```

Deploy/register the MSIX package, open WidBar's Layout page, refresh the widget catalog, then add ModelMeter.

## Privacy principles
- No account, cloud sync or mandatory telemetry.
- No prompt, response or source-code collection.
- No credential access in the UI milestone.
- Usage history remains on the device.
- Provider adapters are isolated and opt-in where required.

## Design tokens
Canvas `#101113` · Surface `#191B1F` · Elevated `#25282D` · Accent `#A3E635`

## License
MIT. See [LICENSE](LICENSE).
