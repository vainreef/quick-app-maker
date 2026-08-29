#!/usr/bin/env python3
"""Regression tests for the skill validator's demonstrated failure classes."""

from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).with_name("validate-skill.py")
SPEC = importlib.util.spec_from_file_location("validate_skill", MODULE_PATH)
assert SPEC and SPEC.loader
VALIDATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VALIDATOR)


class ValidatorRegressionTests(unittest.TestCase):
    def test_current_repository_passes(self) -> None:
        self.assertEqual([], VALIDATOR.validate())

    def test_detects_missing_markdown_fence(self) -> None:
        self.assertEqual(
            "unclosed fenced code block",
            VALIDATOR.markdown_fence_error("# Title\n\n```powershell\nGet-Date\n"),
        )

    def test_extracts_launcher_actions(self) -> None:
        sample = """
        [ValidateSet('run', 'inspect', 'verify')]
        [string]$Action = 'run'
        """
        self.assertEqual(
            {"run", "inspect", "verify"}, VALIDATOR.extract_launcher_actions(sample)
        )

    def test_extracts_launcher_phases(self) -> None:
        sample = """
        [ValidateSet('all', 'availability', 'properties')]
        [string]$Phase = 'all'
        """
        self.assertEqual(
            {"all", "availability", "properties"},
            VALIDATOR.extract_launcher_phases(sample),
        )

    def test_detects_certificate_loop_command(self) -> None:
        issues = VALIDATOR.stale_command_errors(
            Path("commands.md"),
            "winapp package ./publish --generate-cert --install-cert\n",
        )
        self.assertTrue(issues)

    def test_detects_wrapped_dotnet_build(self) -> None:
        issues = VALIDATOR.stale_command_errors(
            Path("commands.md"),
            'Start-Process -FilePath dotnet -ArgumentList "build" -Wait\n',
        )
        self.assertTrue(issues)

    def test_allows_store_certificate_explanation(self) -> None:
        issues = VALIDATOR.stale_command_errors(
            Path("commands.md"),
            "Store uploads omit `--generate-cert` and `--install-cert`.\n",
        )
        self.assertEqual([], issues)


if __name__ == "__main__":
    unittest.main(verbosity=2)
