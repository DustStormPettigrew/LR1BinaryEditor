# AGENTS.md — LR1 Binary Editor

## Project Overview

LR1 Binary Editor is a structured binary inspection and editing tool for Lego Racers data.

It is part of a multi-project toolchain and must remain compatible with:

- LibLR1 (canonical format definitions)
- LR1 Track Editor
- LR1 Online Mod
- future Blender/export tools

LibLR1 is the source of truth for all file format definitions.

The Binary Editor should consume LibLR1 structures whenever possible and must not duplicate format logic unnecessarily.

---

## Primary Responsibilities

The Binary Editor is responsible for:

- Opening supported Lego Racers binary files
- Translating binary structures into a text representation
- Allowing safe structured edits
- Writing edited content back into valid binary form
- Helping reverse-engineer undocumented or partially documented formats

This tool is **not** the canonical owner of file format definitions.

---

## LibLR1 Integration

LibLR1 should be used for:

- parsing known structures
- defining shared types
- maintaining format consistency

The Binary Editor may provide:

- visualization
- editing
- experimental structure exploration

But should not become a competing format implementation.

---

## UI Technology

This project uses **ScintillaNET** as the main structured text editing surface.

ScintillaNET is used for:

- Syntax highlighting
- Code folding
- Line number margin
- Smart indentation
- Undo/redo buffer management

---

### Features actively used

- Syntax highlighting via C++ lexer rules and custom `styles.xml`
  - keywords include items like `float`, `f16`, `byte`, etc.
- Code folding for structured sections
- Auto-resizing line number gutter
- C++-style smart indentation
- Undo/redo buffer cleared after file load so the loaded state becomes the baseline

These editor behaviors are part of the product, not incidental implementation details, and should be preserved during refactors.

---

## Critical Architecture Rules

### 1. No Duplicate Format Definitions

- Do NOT redefine file format structures that exist in LibLR1
- Do NOT create independent parsers for shared formats unless explicitly required

If a format is incomplete or unsupported, improve LibLR1 first when feasible, then integrate the change here.

---

### 2. Separation of Concerns

The editor must support reliable:

binary → structured text → edited text → binary

Rules:

- Do not lose or discard unknown data
- Preserve byte alignment and ordering
- Avoid destructive transformations
- Prefer explicit unknown blocks over guessing

Round-trip correctness is more important than formatting or readability improvements.

### 3. Reverse Engineering Guidelines

This tool is used to explore partially understood formats.

When handling unknown data:

- Preserve raw data where possible
- Label unknown fields clearly
- Avoid silently interpreting data
- Keep experimental logic isolated

Do not treat syntax highlighting or text structure as authoritative format definitions.

---

## Project Responsibilities

The Binary Editor should handle:

- loading a supported binary file
- producing a readable structured document
- applying syntax styles and fold markers
- validating user edits where possible
- writing valid binary output
- surfacing parse or compile errors clearly

The Binary Editor should **not** own:

- rendering logic
- track visualization
- game runtime hooks
- engine-specific graphics logic

---

## Constraints

- Do not reimplement format structures already available in LibLR1
- Do not tightly couple ScintillaNET UI logic to binary parsing logic
- Do not mix rendering or engine code into this project
- Do not treat syntax styling rules as parsing rules
- Do not rely on editor text formatting alone as proof of binary validity

---

## ScintillaNET Rules

When modifying editor behavior:

1. Preserve syntax highlighting behavior based on lexer + `styles.xml`
2. Preserve code folding semantics and fold marker layout
3. Preserve auto-sizing line numbers
4. Preserve smart indentation behavior
5. Preserve undo/redo reset after file load

ScintillaNET configuration should remain centralized and understandable.

Do not scatter lexer/style/folding setup across unrelated files.

---

## File Format Handling Rules

When adding support for a new binary format:

1. Determine whether LibLR1 should define the format first
2. Prefer consuming LibLR1 structures rather than recreating them
3. Define a clear textual representation for the format
4. Ensure the format can be parsed and reserialized reliably
5. Mark unsupported or unknown fields clearly rather than guessing silently

For partially understood formats:
- allow inspection
- avoid destructive writes unless explicitly supported
- isolate experimental logic from stable format handlers

---

## Text Representation Rules

The textual document format should be:

- readable
- deterministic
- diff-friendly
- structurally foldable
- suitable for reverse engineering

Formatting decisions should prioritize:

1. structural clarity
2. stable serialization
3. edit safety

Do not optimize solely for pretty output if it harms round-trip reliability.

---

## Error Handling

Errors should be explicit and actionable.

When parse or serialization fails:
- identify location if possible
- identify field or section involved
- avoid silent fallback behavior
- avoid producing ambiguous output

For unsupported constructs:
- preserve data when possible
- mark unknown regions clearly
- do not discard bytes without explicit handling

---

## Interoperability Goals

This tool should fit into a broader ecosystem:

- LibLR1 defines structures and serializers
- Binary Editor exposes editable structured views
- Track Editor consumes parsed geometry and gameplay-relevant structures
- Blender/other tools may eventually consume shared exported schemas

The Binary Editor should help accelerate reverse engineering, not become a separate incompatible parser stack.

---

## Refactor Guidance

When refactoring:

- preserve behavior first
- separate parser, text generation, and editor setup
- keep ScintillaNET-specific code isolated
- extract reusable format handlers where possible
- prefer incremental migration over large rewrites

If modernizing the project:
- keep editor behavior stable
- avoid breaking existing syntax styling and fold structure
- preserve file load/save semantics

---

## Future Goals

- deeper integration with LibLR1
- cleaner schema-driven format support
- safer validation before binary write
- support for more undocumented formats
- improved reverse-engineering workflows
- possible modernization of UI/runtime without losing editor capabilities

---

## Agent Instructions

When working on this project:

1. Treat LibLR1 as the canonical format layer
2. Preserve ScintillaNET-based editing behavior
3. Separate parsing from text rendering from UI/editor configuration
4. Prioritize round-trip correctness over cosmetic cleanup
5. Do not silently invent unsupported format behavior
6. Prefer explicit unknown-field handling over destructive assumptions
7. Keep changes incremental and testable

---

## Repository Guidelines

### Project Structure & Module Organization
`LR1BinaryEditor.sln` contains a single WinForms application in `LR1BinaryEditor/`. Core UI logic lives in `MainFormScintilla.cs`, startup is in `Program.cs`, and parsing/helpers are split across `Util*.cs`. Runtime data files such as `blocks.cfg`, `properties.cfg`, and `styles.xml` are copied to the output directory and should stay in sync with code changes. `Properties/` contains WinForms resources and generated settings files. `scintilla/` is a vendored upstream dependency with its own sources and tests; avoid editing it unless the change is intentionally for the embedded editor stack.

### Build, Test, and Development Commands
Use Windows tools for this repository.

- `dotnet build LR1BinaryEditor.sln -c Debug`
  Builds the solution for quick validation. In this environment, SDK-style MSBuild reports legacy `.resx` issues, so Visual Studio or a Developer Command Prompt may be more reliable.
- `dotnet build LR1BinaryEditor.sln -c Release`
  Produces the release build in `LR1BinaryEditor/bin/Release/`.
- `start LR1BinaryEditor\\bin\\Debug\\LR1BinaryEditor.exe`
  Launches the app after a successful debug build.

The project targets .NET Framework 4.8, `x86`, and references `..\..\liblr1\LibLR1\bin\Release\LibLR1.dll`; make sure that sibling dependency is built first.

### Coding Style & Naming Conventions
Match the existing C# style: tabs for indentation, Allman braces, and concise inline comments only where needed. Keep naming consistent with the current prefixes: `k_` for constants, `m_` for instance fields, `ms_` for static fields, `g_` for UI controls, and `p_` for method parameters. Prefer small helper methods in `Util*.cs` over adding long methods to the form class. Do not hand-edit `*.Designer.cs` unless the WinForms designer cannot express the change.

### Testing Guidelines
There are no first-party automated tests in this solution today. Validate changes by building the app and manually exercising open, edit, and save flows with representative binary files. If you touch `scintilla/`, use its upstream tests under `scintilla/test/`, but keep those changes isolated from editor feature work.

### Commit & Pull Request Guidelines
Recent commits use short, imperative summaries such as `Add block and property annotations...` and `Automatically resize line-number margin...`. Follow that pattern: one clear sentence, no prefix clutter, and mention the affected feature or format. PRs should explain user-visible behavior, list manual validation steps, call out any dependency on `LibLR1`, and include screenshots when the WinForms UI changes.
