# PDF Convertor

Windows 11 desktop tool (C#, .NET 10, WinUI 3) that batch-converts a folder of Word/Excel/PowerPoint/image/text files to PDF, either separately or merged into one PDF. Personal/internal use. Office is installed, so Office COM is the only Office engine (no LibreOffice).

Principles: one simple window, no hidden features, flat memory use (temp PDFs on disk, streamed merge), custom hand-drawn XAML icons, and no silent failures (every failure produces a full technical report; no empty `catch`).

## Skills every agent must follow

### Ponytail (`.claude/skills/ponytail/SKILL.md`)
Active for all coding work, at the default `full` level. Make the smallest complete change: reuse what exists, use the standard library or platform first, and add no abstraction, option or config nobody asked for. Any non-trivial logic gets one small test. End each reply with one or two lines on what was skipped or unchecked, and any risk.
It never cuts validation at trust boundaries, data-loss error handling, security, or the crash/diagnostics requirement, which the user asked for explicitly.

### Graphify (query the graph before reading files)
Use the code graph to save tokens. Do not grep or read many files to understand structure when the graph can answer.

1. If `graphify-out/graph.json` exists, query it first:
   - `graphify query "<question>"` for how things connect (output is capped at 2000 tokens; use `--budget N` to change it)
   - `graphify explain "<Symbol>"` for one node and its neighbours
   - `graphify path "<A>" "<B>"` for how two things relate
   - `graphify affected "<Symbol>"` before changing a symbol, to see what depends on it
   - `graphify god-nodes` for the main architectural hubs
2. Open raw files only for the specific files the graph points to, or when you are about to edit them.
3. If the graph is missing, build it once the first code exists: `graphify extract . --code-only` (local AST only, no API key, no LLM calls).
4. After you change code, refresh it: `graphify update .` (no LLM needed). Do this before finishing a task so other agents query current data.
5. Do not run `graphify install` or change global config. Do not commit `graphify-out/cache/`.
6. If the graph answers wrongly or can't parse C#/XAML, say so in your reply and fall back to normal search. Don't hide it.

Agents running in parallel: only the integrating agent runs `graphify update .`, to avoid concurrent writes to `graphify-out/`.
