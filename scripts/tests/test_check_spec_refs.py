"""Tests for scripts/check-spec-refs.py.

Each test builds a small repo tree. A reference that the checker accepts by
mistake lets a rename or a move of a requirement break code-to-spec links
without notice, so every kind of broken reference has a test that must fail.
"""

from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[1] / "check-spec-refs.py"
spec = importlib.util.spec_from_file_location("check_spec_refs", SCRIPT)
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)

# Built by concatenation so that this file holds no reference itself.
REF = "@" + "ref"
SPECS = "docs/" + "specs/"

OWNERSHIP = SPECS + "ownership.md"
PROMISES = SPECS + "promises.md"
OWNERSHIP_TEXT = """# Ownership

### Requirement: Handles Release Exactly Once

Text.

```markdown
### Requirement: Name In A Fence
```
"""
PROMISES_TEXT = """# Promises

### Requirement: Promises Settle Once

Text.
"""


class CheckSpecRefsTests(unittest.TestCase):
    def run_check(self, extra: dict[str, str]) -> list[str]:
        files = {OWNERSHIP: OWNERSHIP_TEXT, PROMISES: PROMISES_TEXT, **extra}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for rel_path, text in files.items():
                path = root / rel_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text, encoding="utf-8")
            return checker.check(root, sorted(files))

    def assert_one_failure(self, failures: list[str], location: str, part: str) -> None:
        self.assertEqual(1, len(failures), failures)
        self.assertTrue(failures[0].startswith(location + ": "), failures)
        self.assertIn(part, failures[0])

    def test_valid_references_pass(self) -> None:
        failures = self.run_check(
            {
                "src/a.cs": f'// {REF} {OWNERSHIP} "Handles Release Exactly Once"\n',
                "src/b.cpp": f"/* {REF} {PROMISES} */\n",
                "docs/guide.md": (
                    f'<!-- {REF} {PROMISES} "Promises Settle Once" -->\n'
                    "See `### Requirement: Handles Release\n"
                    "Exactly Once` in `ownership.md` and\n"
                    f"`### Requirement: Promises Settle Once` (`{PROMISES}`).\n"
                ),
            }
        )
        self.assertEqual([], failures)

    def test_ref_to_missing_requirement_fails(self) -> None:
        failures = self.run_check({"src/a.cs": f'\n// {REF} {OWNERSHIP} "Renamed"\n'})
        self.assert_one_failure(failures, "src/a.cs:2", 'no requirement "Renamed"')

    def test_ref_to_requirement_in_another_spec_names_that_spec(self) -> None:
        failures = self.run_check(
            {"src/a.cs": f'// {REF} {OWNERSHIP} "Promises Settle Once"\n'}
        )
        self.assert_one_failure(failures, "src/a.cs:1", f"it is in {PROMISES}")

    def test_ref_to_missing_spec_fails(self) -> None:
        failures = self.run_check({"src/a.cs": f"// {REF} {SPECS}moved.md\n"})
        self.assert_one_failure(failures, "src/a.cs:1", f"{SPECS}moved.md is not a spec file")

    def test_prose_path_to_missing_spec_fails(self) -> None:
        failures = self.run_check({"docs/guide.md": f"Intro.\n\nRead `{SPECS}moved.md` first.\n"})
        self.assert_one_failure(failures, "docs/guide.md:3", f"{SPECS}moved.md is not a spec file")

    def test_malformed_ref_fails(self) -> None:
        for target in (
            "ownership.md",
            f"{OWNERSHIP} Handles Release Exactly Once",
            f'{OWNERSHIP} "Handles Release',
            f'{OWNERSHIP} "Handles Release Exactly Once" and more',
        ):
            with self.subTest(target=target):
                failures = self.run_check({"src/a.py": f"# {REF} {target}\n"})
                self.assert_one_failure(failures, "src/a.py:1", "must be")

    def test_requirement_in_code_fence_is_not_a_target(self) -> None:
        failures = self.run_check(
            {"src/a.cs": f'// {REF} {OWNERSHIP} "Name In A Fence"\n'}
        )
        self.assert_one_failure(failures, "src/a.cs:1", "no requirement")

    def test_citation_of_missing_requirement_fails(self) -> None:
        failures = self.run_check(
            {"AGENTS.md": "Rule.\n\nSee `### Requirement: Removed\nRule`.\n"}
        )
        self.assert_one_failure(failures, "AGENTS.md:3", 'no spec has requirement "Removed Rule"')

    def test_citation_that_names_the_wrong_spec_fails(self) -> None:
        failures = self.run_check(
            {"AGENTS.md": "`### Requirement: Promises Settle Once` in `ownership.md`\n"}
        )
        self.assert_one_failure(failures, "AGENTS.md:1", f"is in {PROMISES}, not in {OWNERSHIP}")

    def test_duplicate_requirement_name_fails(self) -> None:
        failures = self.run_check(
            {SPECS + "zz.md": "# Z\n\n### Requirement: Promises Settle Once\n"}
        )
        self.assert_one_failure(failures, SPECS + "zz.md:3", f"is also in {PROMISES}")

    def test_spec_naming_a_missing_file_fails(self) -> None:
        failures = self.run_check({SPECS + "README.md": "- `gone.md`: old.\n- `promises.md`\n"})
        self.assert_one_failure(failures, SPECS + "README.md:1", "gone.md is not a spec file")


if __name__ == "__main__":
    unittest.main()
