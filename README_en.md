<p align="right">
  <a href="README.md">简体中文</a> | <b>English</b>
</p>

# Quick App Maker V2

Create runnable, testable Windows Electron desktop applications via conversational AI, generate store-ready MSIX packages with 64KB physical block alignment, and automate Microsoft Store submissions.

---

## Core Philosophy

- **Zero Global Environment Dependencies**: Absolutely **no requirement or pre-check** for Git or Node.js on the user's host system; never require users to install global runtimes.
- **Fully Portable Isolated Sandbox**: Node.js 24 LTS, MinGit, npm cache, Playwright, and Electron mirrors are automatically downloaded and strictly isolated inside the current workspace root directory.
- **Decoupled Architecture**:
  - `Project/`: Workspace root directory (`$workspaceRoot`);
  - `Project/node/` & `Project/git/`: Embedded portable runtimes;
  - `Project/quick-app-maker/`: Core toolchain engine and automated CLI;
  - `Project/.agents/skills/`: Workspace-level Agent skills for AI tools;
  - `Project/<app-slug>/`: Standalone user application generated and developed.

> Edge and PowerShell are treated as built-in Windows capabilities; Git, Node.js, and npm are strictly loaded from workspace portable copies. `qam-toolchain.lock.json` is preserved within the `quick-app-maker/` engine directory and loaded explicitly.

---

## Step 0: One-Click Setup (From a Clean Directory)

In a new workspace directory (e.g. `C:\Workspace\Project`), open PowerShell and run:

```powershell
# Download and execute the one-click bootstrap script (downloads portable Node, portable Git, and clones quick-app-maker)
$entry = Join-Path (Get-Location).Path '.qam-entry.ps1'
Invoke-WebRequest -UseBasicParsing `
  -Uri 'https://gitee.com/freevian/quick-app-maker/raw/main/bootstrap/entry.ps1' `
  -OutFile $entry
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry
```

> The bootstrap script is downloaded before the repository is cloned. In releases, the raw entry script stays in sync with `bootstrap/entry.ps1`.

### Generated Directory Structure

```text
Project/                                 <- Workspace root ($workspaceRoot)
├── node/                                <- [Auto-downloaded] Embedded Node.js 24 LTS portable runtime
│   └── node.exe
├── git/                                 <- [Auto-downloaded] Embedded MinGit portable runtime
│   └── cmd/git.exe
├── .cache/                              <- [Workspace cache] npmrc, Electron cache, zero global pollution
└── quick-app-maker/                     <- [Auto-cloned] Core engine and toolchain
    ├── bin/qam.mjs
    ├── packages/
    ├── skills/vainreef-fast-publish/      <- Skill source files
    ├── .agents/skills/                    <- Agent discovery links
    └── docs/
```

---

## Step 1: Initialize Agent Rules & Skills

After bootstrap completes, install the project-level skill in the workspace root:

```powershell
# 1. Create skill directory
New-Item -ItemType Directory -Force -Path .agents\skills\vainreef-fast-publish | Out-Null

# 2. Copy root rules if not present
if (-not (Test-Path .\AGENTS.md)) { Copy-Item -Force quick-app-maker\AGENTS.md .\AGENTS.md }

# 3. Copy skill assets
Copy-Item -Recurse -Force quick-app-maker\skills\vainreef-fast-publish\* .agents\skills\vainreef-fast-publish\
```

### Configured Workspace Structure

```text
Project/
├── .agents/skills/
│   └── vainreef-fast-publish/           <- Workspace Agent skill
├── AGENTS.md                           <- Workspace root rules & contracts
├── node/                                <- Portable Node.js runtime
├── git/                                 <- Portable Git runtime
├── .cache/                              <- Sandbox cache
├── quick-app-maker/                     <- Automation engine & CLI
└── my-app/                              <- Generated business application
```

---

## Step 2: Complete App Development Pipeline

> [!IMPORTANT]
> **Strict Lifecycle Order**: `qam create` generates a minimal runnable scaffold. You **must immediately implement the actual HTML/JS/CSS business logic**, verify with `qam test` and `qam screenshot`, and finally launch `qam dev` for user trial.

```text
┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ 1. Scaffold  │ ──► │ 2. Code App  │ ──► │ 3. Automated │ ──► │ 4. Real-User │
│ (qam create) │     │ (HTML/JS/CSS)│     │ Verification │     │  Experience  │
│              │     │              │     │ (test/screen)│     │  (qam dev)   │
└──────────────┘     └──────────────┘     └──────────────┘     └──────────────┘
```

### 2.1 Verify Environment and Create App Scaffold

```powershell
# 1. Verify toolchain and sandbox health
.\quick-app-maker\bootstrap\qam.cmd doctor
.\quick-app-maker\bootstrap\qam.cmd bootstrap
.\quick-app-maker\bootstrap\qam.cmd self-test

# 2. Create new app scaffold (e.g. countdown-app)
.\quick-app-maker\bootstrap\qam.cmd create --name "倒计时时钟" --slug countdown-app

# 3. Verify scaffold contract
.\quick-app-maker\bootstrap\qam.cmd test .\countdown-app
```

### 2.2 Implement Business Logic

Implement user features inside the app directory (e.g. `countdown-app/`). **Zero compilation step; edits take effect immediately**:

1. **DOM & Structure** (`src/renderer/index.html`): Semantic layout, components, and reactive containers;
2. **State & Logic** (`src/renderer/app.js`): Native Vue 3 reactivity, algorithms, and data flow;
3. **Styling & Aesthetics** (`src/renderer/styles.css`): Theme, typography, contrast hierarchy, and animations;
4. **Data Contract** (`src/main/main.cjs`): Extend main process IPC validation for secure local file persistence.

### 2.3 Automated Testing & Visual Inspection

```powershell
# 1. Run headless contract & mount test (sub-second exit with deterministic code)
.\quick-app-maker\bootstrap\qam.cmd test .\countdown-app

# 2. Generate headless screenshots, including representative populated/image states; the user confirms appearance
.\quick-app-maker\bootstrap\qam.cmd screenshot .\countdown-app --width 1366 --height 768
```

---

## Step 3: User Trial & Dev Watcher (`qam dev`)

Once code is implemented and verified, launch dev mode for user trial:

```powershell
# Launch development mode (watches HTML/JS/CSS with hot reload; restarts on main/preload edits)
.\quick-app-maker\bootstrap\qam.cmd dev .\countdown-app
```

> [!WARNING]
> **Process Model & Agent Guidelines**:
> - `qam dev` is an **interactive long-running watcher process**. Press `Ctrl+C` to stop;
> - **AI Agents must never synchronously block and wait for `dev` to exit** (will trigger timeouts and false crash assessments);
> - In automated pipelines, use `qam test` for quality evidence, then launch `dev` in the background and present the window to the user;
> - When switching to the publishing workflow, stop the background `dev` task to release the workspace lock (`WorkspaceLock`).

---

## Step 4: Microsoft Store Automated Publishing (Triggered on User Request)

When the user explicitly requests to publish to Microsoft Store, initiate the automated pipeline.

> [!NOTE]
> - **Free Developer Registration**: Microsoft individual developer registration is free and requires no expensive enterprise audits;
> - **Zero Code-Signing Cost**: MSIX packages are signed automatically by Microsoft Store cloud upon certification—**no third-party code signing certificate needed**;
> - **5-Step Human-AI Pipeline**: `store launch` (instant Edge/Chrome popup) $\rightarrow$ User logs in & reserves name $\rightarrow$ User confirms copy & assets $\rightarrow$ Automated phase-by-phase application $\rightarrow$ User does final manual submission.

```powershell
# 1. Launch isolated browser for user login & product name reservation
.\quick-app-maker\bootstrap\qam.cmd store launch --app .\countdown-app
# -> User logs in, clicks "Reserve product name", and replies "Ready" in chat

# 2. Automatically retrieve reserved identity and update appxmanifest
.\quick-app-maker\bootstrap\qam.cmd store reserve --app .\countdown-app --name "倒计时时钟"

# 3. Package production-ready 64KB block-aligned MSIX
.\quick-app-maker\bootstrap\qam.cmd package .\countdown-app --profile store

# 4. Offline preflight check
.\quick-app-maker\bootstrap\qam.cmd store preflight --app .\countdown-app

# 5. Discover or generate Submission 1 draft session
.\quick-app-maker\bootstrap\qam.cmd store discover --app .\countdown-app

# 6. Apply individual phases precisely (auto-sniffs 00_*.txt for verified Chinese copy)
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\countdown-app --phase availability
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\countdown-app --phase properties
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\countdown-app --phase age-ratings --confirm-age-ratings
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\countdown-app --phase packages
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\countdown-app --phase listing
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\countdown-app --phase options

# 7. Comprehensive verification across all 6 phases (all Complete)
.\quick-app-maker\bootstrap\qam.cmd store verify --app .\countdown-app
```

> [!IMPORTANT]
> **Safety Guard**: `store verify` never automatically clicks the final "Submit for certification" button. The user reviews the filled draft in the browser and clicks Submit personally.

---

## Phase Breakpoint & Troubleshooting Commands

To inspect or debug individual phases during automation:

```powershell
# Inspect and apply individual phases
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase availability
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase properties
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase age-ratings --confirm-age-ratings
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase packages
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase listing
.\quick-app-maker\bootstrap\qam.cmd store apply --app .\my-app --phase options

# View current checkpoint and session status
.\quick-app-maker\bootstrap\qam.cmd store status --app .\my-app

# Stop current browser session
.\quick-app-maker\bootstrap\qam.cmd store stop --app .\my-app
```

---

## Quality Assurance & Anti-Patterns Checklist

| Anti-Pattern | Bad Consequence | Correct Standard |
| :--- | :--- | :--- |
| **Empty scaffold delivery** | App delivers blank memo scaffold without business features | Must write `index.html`, `app.js`, `styles.css` immediately after `qam create` |
| **Blocking on `qam dev`** | Watcher times out (60s) and gets killed, misidentified as crash | Use `qam test` for automated proof; launch `dev` in background |
| **Directly calling `electron.exe`** | Breaks sandbox isolation, causes environment drift | Always execute through `bootstrap\qam.cmd` (Windows) or `bin/qam.mjs` (macOS/Linux) |
| **Using `localStorage`** | Fails desktop persistence contract, breaks sandbox | Always use `window.qam.saveState` and `window.qam.loadState` with IPC schema |
| **Removing `unsafe-eval` from CSP** | Vue 3 compiler fails in browser, `v-cloak` stays locked | Keep `script-src 'self' 'unsafe-eval'` in `index.html` CSP |
| **Exposing code jargon to users** | Poor user experience for non-technical users | Never mention slug, IPC, Vue; directly open the app window for testing |
| **Treating screenshot generation as visual approval** | Unsupported layout claims from text-only models or OCR; populated states overlooked | Generate representative screenshots, distinguish program evidence from visual review, and let the user confirm appearance |
| **Ignoring warning logs** | Silent false-positives (e.g. empty price tier) pass unnoticed | Zero unhandled warnings: any `not found` or `failed` must be investigated |
| **Modifying source for screenshots** | Risk of source code corruption if interrupted | Use `qam screenshot --eval` or `--click` for non-invasive multi-view capture |
| **Holding dev lock during publish** | Workspace locked, leading to `workspace is busy` | Stop background `dev` task before running `store launch` or package commands |

---

## Environment Guarantees & Acceptance Criteria

- **High-Speed Mirrors**: Node portable archives and npm registry use npmmirror, Electron binaries use China mirror, locked via `qam-toolchain.lock.json` with SHA-256;
- **Atomic Downloads & Sandbox Safety**: All downloads write to `.part` files first, verified by SHA-256 before atomic rename, never polluting the host system;
- **Full Engine Self-Testing**:
  ```powershell
  .\quick-app-maker\bootstrap\qam.cmd check
  .\quick-app-maker\bootstrap\qam.cmd self-test
  ```
- **Delivery Standard**: Record the actual scope and pass/fail/skipped/not-run status of template, mounting, and isolated business checks. `qam screenshot` generates representative images, not proof of visual quality or real persistence. Text-only agents review program/text evidence and show the images without visual endorsements; desktop control and multimodal input are not prerequisites. Launch `qam dev` for the user to confirm appearance, interaction, file selection, and save/reopen behavior. See [acceptance boundaries](skills/vainreef-fast-publish/references/acceptance.md). DevTools is opt-in (`$env:QAM_DEVTOOLS='1'`).
