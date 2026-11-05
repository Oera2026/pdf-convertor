# Graph Report - PDFile  (2026-11-05)

## Corpus Check
- 40 files · ~14,267 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 7 file(s) not represented in the graph (top: (none) 4, .props 1, .ico 1)

## Summary
- 536 nodes · 1165 edges · 20 communities (16 shown, 4 thin omitted)
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 70 edges (avg confidence: 0.82)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `5929bc32`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Pager
- dynamic
- PDFile.Core.Diagnostics
- BatchRunner
- Window
- OfficeApp
- Task
- MainViewModel
- .Error
- .Warn
- PDFile.Core.csproj
- Resolver
- ImageConverter
- Geometry
- .Scan
- PDFile
- FileStatus
- Skills every agent must follow
- Icons.xaml
- Ponytail

## God Nodes (most connected - your core abstractions)
1. `MainViewModel` - 56 edges
2. `OfficeApp` - 39 edges
3. `Window` - 24 edges
4. `MainWindow` - 24 edges
5. `BatchRunner` - 21 edges
6. `Pager` - 20 edges
7. `FileItem` - 17 edges
8. `ConversionException` - 16 edges
9. `ConversionJob` - 15 edges
10. `PDFile.Core.Diagnostics` - 15 edges

## Surprising Connections (you probably didn't know these)
- `FakeConverter` --implements--> `IConverter`  [EXTRACTED]
  tests/PDFile.Tests/CoreTests.cs → src/PDFile.Core/Contracts.cs
- `MainViewModel` --calls--> `PdfMerger`  [INFERRED]
  src/PDFile.App/MainViewModel.cs → src/PDFile.Core/PdfMerger.cs
- `Window` --references--> `MainViewModel`  [INFERRED]
  src/PDFile.App/MainWindow.xaml → src/PDFile.App/MainViewModel.cs
- `FakeMerger` --implements--> `IPdfMerger`  [EXTRACTED]
  tests/PDFile.Tests/CoreTests.cs → src/PDFile.Core/Contracts.cs
- `FakeMerger` --references--> `MergePart`  [EXTRACTED]
  tests/PDFile.Tests/CoreTests.cs → src/PDFile.Core/Contracts.cs

## Import Cycles
- None detected.

## Communities (20 total, 4 thin omitted)

### Community 0 - "Pager"
Cohesion: 0.06
Nodes (15): IConverter, HtmlConverter, Extensions, MarkdownConverter, Extensions, NativeConverter, Extensions, NativeConverters (+7 more)

### Community 1 - "dynamic"
Cohesion: 0.07
Nodes (17): ExcelApp, Extensions, Name, ProcessName, ProgId, PowerPointApp, Extensions, Name (+9 more)

### Community 2 - "PDFile.Core.Diagnostics"
Cohesion: 0.06
Nodes (11): PDFile.Office, PDFile.Core.Diagnostics, PDFile.Core.Native, PDFile.Core, PDFile.Tests, PDFile.App, TestCrash, Mode (+3 more)

### Community 3 - "BatchRunner"
Cohesion: 0.11
Nodes (24): BatchOptions, BatchProgress, BatchRunner, BatchSummary, ConversionException, HResult_, Stage, TimedOut (+16 more)

### Community 4 - "Window"
Cohesion: 0.09
Nodes (17): Safe, MergedRadio, NoticeBar, OfficeBar, RootGrid, SeparateRadio, SessionBar, TitleBarArea (+9 more)

### Community 5 - "OfficeApp"
Cohesion: 0.07
Nodes (11): OfficeApp, Extensions, Name, ProcessName, ProgId, Session, Owned, Worker (+3 more)

### Community 7 - "MainViewModel"
Cohesion: 0.05
Nodes (38): FailureItem, FileItem, IsDone, IsFailed, IsIncluded, Job, RelativePath, Status (+30 more)

### Community 8 - ".Error"
Cohesion: 0.09
Nodes (13): Fatal, OutputMode, MergedPdf, SeparatePdfs, BatchSnapshot, BatchState, Snapshot, CrashReport (+5 more)

### Community 9 - ".Warn"
Cohesion: 0.14
Nodes (6): Application, App, Converters, Program, SessionMarker, MarkerPath

### Community 10 - "PDFile.Core.csproj"
Cohesion: 0.11
Nodes (16): net10.0-windows, net10.0-windows10.0.22621.0, Markdig (0.45.0), Microsoft.NET.Test.Sdk (17.14.1), Microsoft.WindowsAppSDK (2.6.5), PDFsharp (6.2.4), System.Drawing.Common (10.0.14), System.Security.Cryptography.Pkcs (10.0.14) (+8 more)

### Community 13 - "Geometry"
Cohesion: 0.24
Nodes (4): UserControl, Icon, Data, Shell

### Community 14 - ".Scan"
Cohesion: 0.36
Nodes (3): FolderScanner, ScanResult, CoreScannerTests

### Community 15 - "PDFile"
Cohesion: 0.33
Nodes (5): Build and run, Logs and crash reports, PDFile, Requirements, Testing the crash path

### Community 16 - "FileStatus"
Cohesion: 0.33
Nodes (6): FileStatus, Cancelled, Converting, Done, Failed, Ready

### Community 17 - "Skills every agent must follow"
Cohesion: 0.40
Nodes (4): Graphify (query the graph before reading files), PDFile, Ponytail (`.claude/skills/ponytail/SKILL.md`), Skills every agent must follow

### Community 19 - "Ponytail"
Cohesion: 0.40
Nodes (4): Before you write, Levels, Ponytail, The smallest complete change

## Knowledge Gaps
- **103 isolated node(s):** `Converters`, `Data`, `Ready`, `Converting`, `Done` (+98 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 182 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **4 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainViewModel` connect `MainViewModel` to `Pager`, `PDFile.Core.Diagnostics`, `BatchRunner`, `Window`, `Geometry`?**
  _High betweenness centrality (0.232) - this node is a cross-community bridge._
- **Are the 3 inferred relationships involving `MainViewModel` (e.g. with `.OnLaunched()` and `PdfMerger`) actually correct?**
  _`MainViewModel` has 3 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Converters`, `Data`, `Ready` to the rest of the system?**
  _103 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Pager` be split into smaller, more focused modules?**
  _Cohesion score 0.05683563748079877 - nodes in this community are weakly interconnected._
- **Why does `OfficeApp` connect `OfficeApp` to `Pager`, `dynamic`, `PDFile.Core.Diagnostics`, `BatchRunner`, `.Warn`?**
  _High betweenness centrality (0.143) - this node is a cross-community bridge._
- **Should `dynamic` be split into smaller, more focused modules?**
  _Cohesion score 0.06767676767676768 - nodes in this community are weakly interconnected._
- **Why does `IConverter` connect `Pager` to `dynamic`, `BatchRunner`, `OfficeApp`, `MainViewModel`, `.Warn`?**
  _High betweenness centrality (0.091) - this node is a cross-community bridge._