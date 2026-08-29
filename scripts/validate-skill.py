#!/usr/bin/env python3
"""Validate the quick-app-maker skill package and its reliability invariants."""

from __future__ import annotations

import ast
import json
import re
import subprocess
import sys
import zipfile
from pathlib import Path
from urllib.parse import unquote
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[1]
SKILL_ROOT = ROOT / "skills" / "vainreef-fast-publish"
SKILL_MD = SKILL_ROOT / "SKILL.md"
COMMANDS_MD = SKILL_ROOT / "references" / "toolchain" / "v1" / "commands.md"
LAUNCHER = ROOT / "toolchain" / "edge-store-cli" / "Invoke-EdgeStore.ps1"
PROGRAM = ROOT / "toolchain" / "edge-store-cli" / "Program.cs"

REQUIRED_PATHS = (
    "AGENTS.md",
    "README.md",
    "bootstrap/toolchain.json",
    "skills/vainreef-fast-publish/SKILL.md",
    "skills/vainreef-fast-publish/agents/openai.yaml",
    "skills/vainreef-fast-publish/assets/project-readme-template.md",
    "skills/vainreef-fast-publish/references/discovery-interview.md",
    "skills/vainreef-fast-publish/references/toolchain/v1/commands.md",
    "docs/windows-smoke-test.md",
    "docs/partner-center/Agent-运行契约.md",
    "docs/partner-center/Edge-Store-可靠性重构.md",
    "toolchain/edge-store-cli/Invoke-EdgeStore.ps1",
    "toolchain/edge-store-cli/EdgeStore.Cli.csproj",
    "toolchain/winapp-cli/0.6.1/winappcli_x64.msix",
)

TEXT_SUFFIXES = {
    ".cs",
    ".csproj",
    ".gitignore",
    ".html",
    ".json",
    ".md",
    ".ps1",
    ".py",
    ".txt",
    ".yaml",
    ".yml",
}

MARKDOWN_ROOTS = (
    ROOT / "README.md",
    ROOT / "AGENTS.md",
    ROOT / "skills",
    ROOT / "docs",
    ROOT / "toolchain" / "edge-store-cli" / "README.md",
    ROOT / "bootstrap" / "README.md",
)


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def tracked_files() -> list[Path]:
    try:
        raw = subprocess.check_output(
            ["git", "ls-files", "-z"], cwd=ROOT, stderr=subprocess.DEVNULL
        )
        return [ROOT / item.decode() for item in raw.split(b"\0") if item]
    except (OSError, subprocess.CalledProcessError):
        return [path for path in ROOT.rglob("*") if path.is_file()]


def markdown_files() -> list[Path]:
    found: set[Path] = set()
    for entry in MARKDOWN_ROOTS:
        if entry.is_file() and entry.suffix.lower() == ".md":
            found.add(entry)
        elif entry.is_dir():
            found.update(entry.rglob("*.md"))
    return sorted(found)


def parse_frontmatter(text: str) -> dict[str, object]:
    match = re.match(r"\A---\r?\n(.*?)\r?\n---(?:\r?\n|\Z)", text, re.DOTALL)
    if not match:
        raise ValueError("missing or malformed YAML frontmatter")

    result: dict[str, object] = {}
    for raw_line in match.group(1).splitlines():
        if not raw_line.strip() or raw_line[:1].isspace() or ":" not in raw_line:
            continue
        key, raw_value = raw_line.split(":", 1)
        value = raw_value.strip()
        if value[:1] in {"'", '"'}:
            try:
                value = ast.literal_eval(value)
            except (SyntaxError, ValueError) as error:
                raise ValueError(f"invalid quoted value for {key.strip()}: {error}") from error
        result[key.strip()] = value
    return result


def markdown_fence_error(text: str) -> str | None:
    marker: str | None = None
    minimum = 0
    for line_number, line in enumerate(text.splitlines(), 1):
        match = re.match(
            r"^[ \t]*(?:(?:[-+*]|\d+[.)])[ \t]+)?(`{3,}|~{3,})(.*)$", line
        )
        if not match:
            continue
        token, tail = match.groups()
        if marker is None:
            marker = token[0]
            minimum = len(token)
        elif token[0] == marker and len(token) >= minimum and not tail.strip():
            marker = None
            minimum = 0
    if marker is not None:
        return "unclosed fenced code block"
    return None


def relative_link_errors(path: Path, text: str) -> list[str]:
    errors: list[str] = []
    for target in re.findall(r"\]\(([^)]+)\)", text):
        clean = unquote(target.split("#", 1)[0].strip())
        if not clean or "://" in clean or clean.startswith("mailto:"):
            continue
        resolved = (path.parent / clean).resolve()
        try:
            resolved.relative_to(ROOT)
        except ValueError:
            errors.append(f"link escapes repository: {target}")
            continue
        if not resolved.exists():
            errors.append(f"missing link target: {target}")
    return errors


def duplicate_heading_errors(path: Path, text: str) -> list[str]:
    if path.name not in {"partner-center-guide.md", "SKILL.md"}:
        return []
    seen: dict[str, int] = {}
    errors: list[str] = []
    for line_number, line in enumerate(text.splitlines(), 1):
        match = re.match(r"^#{2,3}\s+(.+?)\s*$", line)
        if not match:
            continue
        heading = re.sub(r"\s+", " ", match.group(1)).strip().casefold()
        if heading in seen:
            errors.append(
                f"duplicate heading at line {line_number}; first seen at line {seen[heading]}: {match.group(1)}"
            )
        else:
            seen[heading] = line_number
    return errors


def extract_launcher_actions(text: str) -> set[str]:
    match = re.search(
        r"\[ValidateSet\((.*?)\)\]\s*\r?\n\s*\[string\]\$Action",
        text,
        re.DOTALL,
    )
    if not match:
        return set()
    return set(re.findall(r"'([A-Za-z][A-Za-z0-9_-]*)'", match.group(1)))


def extract_launcher_phases(text: str) -> set[str]:
    match = re.search(
        r"\[ValidateSet\((.*?)\)\]\s*\r?\n\s*\[string\]\$Phase",
        text,
        re.DOTALL,
    )
    if not match:
        return set()
    return set(re.findall(r"'([A-Za-z][A-Za-z0-9_-]*)'", match.group(1)))


def documented_actions(text: str) -> set[str]:
    return {
        value.casefold()
        for value in re.findall(r"-Action\s+([A-Za-z][A-Za-z0-9_-]*)", text)
    }


def documented_phases(text: str) -> set[str]:
    return {
        value.casefold()
        for value in re.findall(r"-Phase\s+([A-Za-z][A-Za-z0-9_-]*)", text)
    }


def stale_command_errors(path: Path, text: str) -> list[str]:
    errors: list[str] = []
    for line_number, line in enumerate(text.splitlines(), 1):
        stripped = line.strip()
        if re.match(
            r"^(?:&\s+)?(?:\$winapp|winapp(?:\.exe)?)\s+(?:package|pack)\b.*--(?:generate-cert|install-cert)\b",
            stripped,
            re.I,
        ):
            errors.append(
                f"line {line_number}: Store/default package command contains development-certificate flags"
            )
        if re.match(r"^Import-PfxCertificate\b.*CurrentUser\\(?:TrustedPeople|Root)", stripped, re.I):
            errors.append(
                f"line {line_number}: stale CurrentUser certificate-import command"
            )
        if re.match(r"^Add-AppxPackage\b", stripped, re.I) and "bootstrap" not in path.parts:
            errors.append(
                f"line {line_number}: direct package install appears in the default skill workflow"
            )
        if re.search(r"Start-Process\b.*\bdotnet\b", stripped, re.I):
            errors.append(
                f"line {line_number}: background/wrapped dotnet invocation reintroduces handle-hang risk"
            )
        if re.search(r"\bwinapp(?:\.exe)?\s+run\b.*--detach\b.*--debug-output\b", stripped, re.I):
            errors.append(
                f"line {line_number}: winapp run combines mutually exclusive --detach and --debug-output"
            )
    return errors


def validate() -> list[str]:
    errors: list[str] = []

    for relative in REQUIRED_PATHS:
        if not (ROOT / relative).exists():
            errors.append(f"missing required path: {relative}")

    if errors:
        return errors

    try:
        frontmatter = parse_frontmatter(read_text(SKILL_MD))
        if frontmatter.get("name") != "vainreef-fast-publish":
            errors.append("SKILL.md name must be 'vainreef-fast-publish'")
        description = frontmatter.get("description")
        if not isinstance(description, str) or not (1 <= len(description) <= 1024):
            errors.append("SKILL.md description must contain 1-1024 characters")
    except ValueError as error:
        errors.append(f"SKILL.md: {error}")

    metadata = read_text(SKILL_ROOT / "agents" / "openai.yaml")
    if "$vainreef-fast-publish" not in metadata:
        errors.append("agents/openai.yaml default prompt must invoke $vainreef-fast-publish")

    for path in tracked_files():
        if path.suffix.lower() not in TEXT_SUFFIXES and path.name != ".gitignore":
            continue
        try:
            data = path.read_bytes()
        except OSError as error:
            errors.append(f"{path.relative_to(ROOT)}: read failed: {error}")
            continue
        controls = [byte for byte in data if byte < 32 and byte not in (9, 10, 13)]
        if controls:
            values = ", ".join(sorted({f"0x{byte:02x}" for byte in controls}))
            errors.append(f"{path.relative_to(ROOT)}: disallowed control character(s): {values}")

    launcher_text = read_text(LAUNCHER)
    launcher_actions = extract_launcher_actions(launcher_text)
    launcher_phases = extract_launcher_phases(launcher_text)
    if not launcher_actions:
        errors.append("Invoke-EdgeStore.ps1: failed to parse Action ValidateSet")
    if not launcher_phases:
        errors.append("Invoke-EdgeStore.ps1: failed to parse Phase ValidateSet")

    program_actions = set(re.findall(r'case\s+"([A-Za-z][A-Za-z0-9_-]*)"\s*:', read_text(PROGRAM)))
    missing_program_actions = launcher_actions - program_actions
    for action in sorted(missing_program_actions):
        errors.append(f"Invoke-EdgeStore.ps1 exposes action missing from Program.cs: {action}")

    for path in markdown_files():
        text = read_text(path)
        prefix = str(path.relative_to(ROOT))
        fence_error = markdown_fence_error(text)
        if fence_error:
            errors.append(f"{prefix}: {fence_error}")
        for issue in relative_link_errors(path, text):
            errors.append(f"{prefix}: {issue}")
        for issue in duplicate_heading_errors(path, text):
            errors.append(f"{prefix}: {issue}")
        for issue in stale_command_errors(path, text):
            errors.append(f"{prefix}: {issue}")

        unsupported = documented_actions(text) - {action.casefold() for action in launcher_actions}
        for action in sorted(unsupported):
            errors.append(f"{prefix}: documents unsupported launcher action '{action}'")
        unsupported_phases = documented_phases(text) - {
            phase.casefold() for phase in launcher_phases
        }
        for phase in sorted(unsupported_phases):
            errors.append(f"{prefix}: documents unsupported launcher phase '{phase}'")

    commands = read_text(COMMANDS_MD)
    required_fragments = (
        "## Choose one delivery route",
        "## 命令执行硬规则",
        "winapp run $project --no-build --detach --json",
        "/p:UseSharedCompilation=false",
        "<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>",
        "--self-contained true",
        "& $dotnet build-server shutdown",
        "## Error convergence map",
    )
    for fragment in required_fragments:
        if fragment not in commands:
            errors.append(f"commands.md missing reliability invariant: {fragment}")

    store_package = re.search(
        r"&\s+\$winapp\s+package\s+\$publishDir\s+`(?P<body>.*?)if \(\$LASTEXITCODE",
        commands,
        re.DOTALL | re.IGNORECASE,
    )
    if not store_package:
        errors.append("commands.md missing canonical Store package command")
    else:
        body = store_package.group("body")
        for flag in ("--manifest", "--self-contained", "--executable", "--output"):
            if flag not in body:
                errors.append(f"canonical Store package command missing {flag}")
        if "--generate-cert" in body or "--install-cert" in body:
            errors.append("canonical Store package command reintroduced certificate flags")

    skill = read_text(SKILL_MD)
    for fragment in (
        "winapp run <project> --no-build --detach --json",
        "同一项目同一时刻只有一个",
        "同一根因只做一次证据化重试",
        "WindowsAppSDKSelfContained=true",
    ):
        if fragment not in skill:
            errors.append(f"SKILL.md missing routing invariant: {fragment}")

    toolchain = json.loads(read_text(ROOT / "bootstrap" / "toolchain.json"))
    package_path = ROOT / toolchain["winappcli"]["repository_path"]
    try:
        with zipfile.ZipFile(package_path) as archive:
            manifest = ElementTree.fromstring(
                archive.read("AppxManifest.xml").decode("utf-8-sig")
            )
        namespace = {"f": "http://schemas.microsoft.com/appx/manifest/foundation/windows10"}
        identity = manifest.find("f:Identity", namespace)
        actual_version = identity.attrib.get("Version", "") if identity is not None else ""
        expected_version = toolchain["winappcli"]["package_version"]
        if actual_version != expected_version:
            errors.append(
                f"bundled WinAppCLI version mismatch: manifest={actual_version}, toolchain={expected_version}"
            )
    except (KeyError, OSError, zipfile.BadZipFile, ElementTree.ParseError) as error:
        errors.append(f"bundled WinAppCLI validation failed: {error}")

    return errors


def main() -> int:
    errors = validate()
    if errors:
        print(f"quick-app-maker validation failed with {len(errors)} issue(s):", file=sys.stderr)
        for issue in errors:
            print(f"- {issue}", file=sys.stderr)
        return 1

    print("quick-app-maker skill validation passed")
    print(f"- skill: {SKILL_ROOT.relative_to(ROOT)}")
    print(f"- launcher actions: {', '.join(sorted(extract_launcher_actions(read_text(LAUNCHER))))}")
    print(f"- markdown files checked: {len(markdown_files())}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
