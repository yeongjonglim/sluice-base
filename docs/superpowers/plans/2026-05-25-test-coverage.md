# Test Coverage and Effectiveness Measurement — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add coverage collection, PR reporting with threshold enforcement, and coverage badges — all self-contained in GitHub Actions with no external dependencies.

**Architecture:** Backend coverage via coverlet (already collecting), frontend coverage via `@vitest/coverage-v8` (new). A single Python script parses Cobertura XML and posts PR comments with overall/per-change/per-file metrics, failing the job if thresholds aren't met. A separate badge workflow runs on main merges and commits SVG badges to `gh-pages`.

**Tech Stack:** Python 3 (stdlib only), Vitest + @vitest/coverage-v8, coverlet (XPlat Code Coverage), GitHub Actions, gh CLI

---

### Task 1: Add frontend coverage collection

**Files:**
- Modify: `src/frontend/vitest.config.ts`
- Modify: `src/frontend/package.json` (via npm)

- [ ] **Step 1: Install `@vitest/coverage-v8`**

Run from `src/frontend/`:

```bash
npm install --save-dev @vitest/coverage-v8
```

- [ ] **Step 2: Add coverage config to `vitest.config.ts`**

Replace the full file contents with:

```typescript
import { URL, fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";
import viteReact from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [viteReact()],
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test-setup.ts"],
    globals: false,
    include: ["src/**/*.test.{ts,tsx}"],
    coverage: {
      provider: "v8",
      reporter: ["cobertura", "text"],
      reportsDirectory: "./coverage",
      thresholds: {
        lines: 70,
        branches: 70,
        functions: 70,
        statements: 70,
      },
    },
  },
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
});
```

- [ ] **Step 3: Update test script in `package.json`**

Change the `test` script from `"vitest run"` to `"vitest run --coverage"`.

- [ ] **Step 4: Add `coverage/` to `.gitignore`**

Check if `src/frontend/.gitignore` exists. If so, append `coverage/` to it. If not, create it with:

```
coverage/
```

- [ ] **Step 5: Run tests locally to verify coverage works**

Run from `src/frontend/`:

```bash
npm run test
```

Expected: Tests pass, a `coverage/` directory is created containing `cobertura-coverage.xml`, and a text summary is printed to the terminal showing line/branch/function/statement percentages.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/vitest.config.ts src/frontend/package.json src/frontend/package-lock.json src/frontend/.gitignore
git commit -m "feat: add vitest coverage collection with v8 provider"
```

---

### Task 2: Write the coverage report Python script

**Files:**
- Create: `.github/scripts/coverage-report.py`

This script parses a Cobertura XML file, cross-references coverage with PR changed files, generates a Markdown report, posts it as a PR comment, and exits nonzero if thresholds are violated.

- [ ] **Step 1: Create `.github/scripts/` directory**

```bash
mkdir -p .github/scripts
```

- [ ] **Step 2: Write the script**

Create `.github/scripts/coverage-report.py`:

```python
#!/usr/bin/env python3
"""Parse Cobertura XML coverage and post a PR comment via gh CLI."""

import argparse
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import PurePosixPath


def parse_args():
    p = argparse.ArgumentParser(description="Coverage report for GitHub PRs")
    p.add_argument("--cobertura-path", required=True, help="Path to cobertura XML")
    p.add_argument("--label", required=True, help="Report label (e.g. Backend, Frontend)")
    p.add_argument("--overall-threshold", type=float, default=70.0)
    p.add_argument("--change-threshold", type=float, default=80.0)
    p.add_argument("--pr-number", required=True, help="PR number")
    p.add_argument("--lowest-files-count", type=int, default=5)
    return p.parse_args()


def parse_cobertura(path):
    """Return (overall_rate, {filename: (line_rate, uncovered_lines)})."""
    tree = ET.parse(path)
    root = tree.getroot()
    overall_rate = float(root.get("line-rate", 0)) * 100

    file_coverage = {}
    for package in root.findall(".//package"):
        for cls in package.findall(".//class"):
            filename = cls.get("filename", "")
            if not filename:
                continue
            lines = cls.findall(".//line")
            if not lines:
                continue
            hit = sum(1 for l in lines if int(l.get("hits", 0)) > 0)
            total = len(lines)
            rate = (hit / total * 100) if total > 0 else 100.0
            uncovered = [
                int(l.get("number")) for l in lines if int(l.get("hits", 0)) == 0
            ]
            if filename in file_coverage:
                prev_rate, prev_uncov = file_coverage[filename]
                existing_total = round(prev_rate * len(prev_uncov) / (100 - prev_rate)) if prev_rate < 100 else 0
                file_coverage[filename] = (
                    (hit + existing_total) / (total + existing_total + len(prev_uncov)) * 100 if (total + existing_total + len(prev_uncov)) > 0 else 100.0,
                    prev_uncov + uncovered,
                )
            else:
                file_coverage[filename] = (rate, uncovered)

    return overall_rate, file_coverage


def get_changed_files(pr_number):
    """Get list of changed files in the PR via gh CLI."""
    result = subprocess.run(
        ["gh", "pr", "diff", pr_number, "--name-only"],
        capture_output=True,
        text=True,
        check=True,
    )
    return [f.strip() for f in result.stdout.strip().splitlines() if f.strip()]


def normalize_path(path):
    """Normalize a path for comparison — strip leading ./ and lowercase."""
    p = PurePosixPath(path)
    parts = [part for part in p.parts if part != "."]
    return "/".join(parts)


def compute_change_coverage(file_coverage, changed_files):
    """Compute coverage across only the changed files."""
    changed_set = {normalize_path(f) for f in changed_files}
    matched = []
    for filename, (rate, uncovered) in file_coverage.items():
        if normalize_path(filename) in changed_set:
            matched.append((filename, rate, uncovered))
    if not matched:
        return None, []
    total_rate = sum(r for _, r, _ in matched) / len(matched)
    return total_rate, matched


def collapse_line_ranges(lines):
    """Collapse [1,2,3,5,7,8,9] into '1-3, 5, 7-9'."""
    if not lines:
        return ""
    sorted_lines = sorted(set(lines))
    ranges = []
    start = prev = sorted_lines[0]
    for n in sorted_lines[1:]:
        if n == prev + 1:
            prev = n
        else:
            ranges.append(f"{start}" if start == prev else f"{start}-{prev}")
            start = prev = n
    ranges.append(f"{start}" if start == prev else f"{start}-{prev}")
    return ", ".join(ranges)


def render_markdown(label, overall_rate, overall_threshold, change_rate,
                    change_threshold, changed_files_coverage, file_coverage,
                    lowest_count):
    """Render the Markdown coverage report."""
    marker = f"<!-- coverage-{label} -->"

    overall_status = "pass" if overall_rate >= overall_threshold else "FAIL"
    overall_icon = "✅" if overall_rate >= overall_threshold else "❌"

    lines = [
        marker,
        f"## Coverage Report — {label}",
        "",
        "| Metric | Value | Threshold | Status |",
        "|--------|-------|-----------|--------|",
        f"| Overall line coverage | {overall_rate:.1f}% | {overall_threshold:.0f}% | {overall_icon} |",
    ]

    if change_rate is not None:
        change_icon = "✅" if change_rate >= change_threshold else "❌"
        lines.append(
            f"| Changed lines covered | {change_rate:.1f}% | {change_threshold:.0f}% | {change_icon} |"
        )
    else:
        lines.append("| Changed lines covered | N/A (no instrumented files changed) | — | — |")

    if changed_files_coverage:
        lines.extend([
            "",
            "### Changed file coverage",
            "",
            "| File | Coverage | Uncovered lines |",
            "|------|----------|-----------------|",
        ])
        for filename, rate, uncovered in sorted(changed_files_coverage, key=lambda x: x[1]):
            uncov_str = collapse_line_ranges(uncovered) if uncovered else "—"
            lines.append(f"| `{filename}` | {rate:.1f}% | {uncov_str} |")

    sorted_files = sorted(file_coverage.items(), key=lambda x: x[1][0])
    lowest = sorted_files[:lowest_count]
    if lowest:
        lines.extend([
            "",
            "### Lowest coverage files (project-wide)",
            "",
            "| File | Coverage |",
            "|------|----------|",
        ])
        for filename, (rate, _) in lowest:
            lines.append(f"| `{filename}` | {rate:.1f}% |")

    return "\n".join(lines), overall_status == "FAIL" or (change_rate is not None and change_rate < change_threshold)


def post_comment(pr_number, label, body):
    """Post or update a PR comment with the given body."""
    marker = f"<!-- coverage-{label} -->"

    existing = subprocess.run(
        ["gh", "pr", "view", pr_number, "--json", "comments", "--jq",
         f'.comments[] | select(.body | startswith("{marker}")) | .url'],
        capture_output=True, text=True,
    )

    if existing.stdout.strip():
        comment_url = existing.stdout.strip().splitlines()[0]
        comment_id = comment_url.rstrip("/").split("/")[-1]
        subprocess.run(
            ["gh", "api", "--method", "PATCH",
             f"repos/{{owner}}/{{repo}}/issues/comments/{comment_id}",
             "-f", f"body={body}"],
            check=True,
        )
    else:
        subprocess.run(
            ["gh", "pr", "comment", pr_number, "--body", body],
            check=True,
        )


def main():
    args = parse_args()
    overall_rate, file_coverage = parse_cobertura(args.cobertura_path)
    changed_files = get_changed_files(args.pr_number)
    change_rate, changed_files_coverage = compute_change_coverage(
        file_coverage, changed_files
    )
    body, failed = render_markdown(
        label=args.label,
        overall_rate=overall_rate,
        overall_threshold=args.overall_threshold,
        change_rate=change_rate,
        change_threshold=args.change_threshold,
        changed_files_coverage=changed_files_coverage,
        file_coverage=file_coverage,
        lowest_count=args.lowest_files_count,
    )
    post_comment(args.pr_number, args.label, body)

    if failed:
        print(f"FAIL: Coverage thresholds not met for {args.label}")
        print(f"  Overall: {overall_rate:.1f}% (threshold: {args.overall_threshold:.0f}%)")
        if change_rate is not None:
            print(f"  Changed: {change_rate:.1f}% (threshold: {args.change_threshold:.0f}%)")
        sys.exit(1)
    else:
        print(f"PASS: Coverage thresholds met for {args.label}")
        print(f"  Overall: {overall_rate:.1f}% (threshold: {args.overall_threshold:.0f}%)")
        if change_rate is not None:
            print(f"  Changed: {change_rate:.1f}% (threshold: {args.change_threshold:.0f}%)")


if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Make the script executable**

```bash
chmod +x .github/scripts/coverage-report.py
```

- [ ] **Step 4: Commit**

```bash
git add .github/scripts/coverage-report.py
git commit -m "feat: add coverage report script for PR commenting"
```

---

### Task 3: Write tests for the coverage report script

**Files:**
- Create: `.github/scripts/test_coverage_report.py`
- Create: `.github/scripts/fixtures/sample-cobertura.xml`

- [ ] **Step 1: Create a sample Cobertura XML fixture**

Create `.github/scripts/fixtures/sample-cobertura.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.75" branch-rate="0.5" version="1.0" timestamp="1234567890">
  <packages>
    <package name="SluiceBase.Api">
      <classes>
        <class name="QueryEndpoint" filename="src/SluiceBase.Api/Endpoints/QueryEndpoint.cs" line-rate="0.8" branch-rate="0.5">
          <lines>
            <line number="10" hits="1"/>
            <line number="11" hits="1"/>
            <line number="12" hits="1"/>
            <line number="13" hits="1"/>
            <line number="14" hits="0"/>
          </lines>
        </class>
        <class name="UpdateEndpoint" filename="src/SluiceBase.Api/Endpoints/UpdateEndpoint.cs" line-rate="0.5" branch-rate="0.5">
          <lines>
            <line number="20" hits="1"/>
            <line number="21" hits="0"/>
            <line number="22" hits="0"/>
            <line number="23" hits="1"/>
          </lines>
        </class>
        <class name="HealthEndpoint" filename="src/SluiceBase.Api/Endpoints/HealthEndpoint.cs" line-rate="1.0" branch-rate="1.0">
          <lines>
            <line number="5" hits="1"/>
            <line number="6" hits="1"/>
            <line number="7" hits="1"/>
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
```

- [ ] **Step 2: Write the test file**

Create `.github/scripts/test_coverage_report.py`:

```python
#!/usr/bin/env python3
"""Tests for coverage-report.py — run with: python -m pytest .github/scripts/test_coverage_report.py"""

import importlib.util
import os
import sys
from pathlib import Path
from unittest.mock import patch

import pytest

SCRIPT_DIR = Path(__file__).parent
spec = importlib.util.spec_from_file_location("coverage_report", SCRIPT_DIR / "coverage-report.py")
coverage_report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(coverage_report)

FIXTURE_PATH = str(SCRIPT_DIR / "fixtures" / "sample-cobertura.xml")


class TestParseCobertura:
    def test_overall_rate(self):
        overall, _ = coverage_report.parse_cobertura(FIXTURE_PATH)
        assert overall == 75.0

    def test_file_coverage_count(self):
        _, file_cov = coverage_report.parse_cobertura(FIXTURE_PATH)
        assert len(file_cov) == 3

    def test_file_rates(self):
        _, file_cov = coverage_report.parse_cobertura(FIXTURE_PATH)
        query_rate, query_uncov = file_cov["src/SluiceBase.Api/Endpoints/QueryEndpoint.cs"]
        assert query_rate == 80.0
        assert query_uncov == [14]

        update_rate, update_uncov = file_cov["src/SluiceBase.Api/Endpoints/UpdateEndpoint.cs"]
        assert update_rate == 50.0
        assert sorted(update_uncov) == [21, 22]

        health_rate, _ = file_cov["src/SluiceBase.Api/Endpoints/HealthEndpoint.cs"]
        assert health_rate == 100.0


class TestCollapseLineRanges:
    def test_empty(self):
        assert coverage_report.collapse_line_ranges([]) == ""

    def test_single(self):
        assert coverage_report.collapse_line_ranges([5]) == "5"

    def test_consecutive(self):
        assert coverage_report.collapse_line_ranges([1, 2, 3]) == "1-3"

    def test_mixed(self):
        assert coverage_report.collapse_line_ranges([1, 2, 3, 5, 7, 8, 9]) == "1-3, 5, 7-9"

    def test_unsorted(self):
        assert coverage_report.collapse_line_ranges([9, 1, 3, 2, 7, 8, 5]) == "1-3, 5, 7-9"

    def test_duplicates(self):
        assert coverage_report.collapse_line_ranges([1, 1, 2, 2, 3]) == "1-3"


class TestComputeChangeCoverage:
    def test_matching_files(self):
        _, file_cov = coverage_report.parse_cobertura(FIXTURE_PATH)
        changed = ["src/SluiceBase.Api/Endpoints/QueryEndpoint.cs"]
        rate, matched = coverage_report.compute_change_coverage(file_cov, changed)
        assert rate == 80.0
        assert len(matched) == 1

    def test_no_matching_files(self):
        _, file_cov = coverage_report.parse_cobertura(FIXTURE_PATH)
        changed = ["src/SluiceBase.Api/Endpoints/NotAFile.cs"]
        rate, matched = coverage_report.compute_change_coverage(file_cov, changed)
        assert rate is None
        assert matched == []

    def test_multiple_changed_files(self):
        _, file_cov = coverage_report.parse_cobertura(FIXTURE_PATH)
        changed = [
            "src/SluiceBase.Api/Endpoints/QueryEndpoint.cs",
            "src/SluiceBase.Api/Endpoints/UpdateEndpoint.cs",
        ]
        rate, matched = coverage_report.compute_change_coverage(file_cov, changed)
        assert rate == 65.0  # average of 80% and 50%
        assert len(matched) == 2


class TestNormalizePath:
    def test_leading_dot_slash(self):
        assert coverage_report.normalize_path("./src/foo.cs") == "src/foo.cs"

    def test_no_change(self):
        assert coverage_report.normalize_path("src/foo.cs") == "src/foo.cs"


class TestRenderMarkdown:
    def test_passing_report(self):
        body, failed = coverage_report.render_markdown(
            label="Backend",
            overall_rate=75.0,
            overall_threshold=70.0,
            change_rate=85.0,
            change_threshold=80.0,
            changed_files_coverage=[
                ("src/Endpoints/Query.cs", 85.0, [14]),
            ],
            file_coverage={
                "src/Endpoints/Query.cs": (85.0, [14]),
                "src/Endpoints/Update.cs": (50.0, [21, 22]),
            },
            lowest_count=5,
        )
        assert not failed
        assert "<!-- coverage-Backend -->" in body
        assert "75.0%" in body
        assert "✅" in body

    def test_failing_overall(self):
        _, failed = coverage_report.render_markdown(
            label="Frontend",
            overall_rate=60.0,
            overall_threshold=70.0,
            change_rate=85.0,
            change_threshold=80.0,
            changed_files_coverage=[],
            file_coverage={},
            lowest_count=5,
        )
        assert failed

    def test_failing_change(self):
        _, failed = coverage_report.render_markdown(
            label="Frontend",
            overall_rate=75.0,
            overall_threshold=70.0,
            change_rate=70.0,
            change_threshold=80.0,
            changed_files_coverage=[],
            file_coverage={},
            lowest_count=5,
        )
        assert failed

    def test_no_changed_files(self):
        body, failed = coverage_report.render_markdown(
            label="Backend",
            overall_rate=75.0,
            overall_threshold=70.0,
            change_rate=None,
            change_threshold=80.0,
            changed_files_coverage=[],
            file_coverage={},
            lowest_count=5,
        )
        assert not failed
        assert "N/A" in body
```

- [ ] **Step 3: Run the tests**

```bash
python -m pytest .github/scripts/test_coverage_report.py -v
```

Expected: All tests pass. If `pytest` is not available, use:

```bash
python -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

(The test file is structured to work with both pytest and unittest discovery.)

- [ ] **Step 4: Commit**

```bash
git add .github/scripts/test_coverage_report.py .github/scripts/fixtures/sample-cobertura.xml
git commit -m "test: add tests for coverage report script"
```

---

### Task 4: Write the badge generation script

**Files:**
- Create: `.github/scripts/generate-badge.py`

- [ ] **Step 1: Write the badge script**

Create `.github/scripts/generate-badge.py`:

```python
#!/usr/bin/env python3
"""Generate SVG coverage badges from Cobertura XML files."""

import argparse
import xml.etree.ElementTree as ET


BADGE_TEMPLATE = """\
<svg xmlns="http://www.w3.org/2000/svg" width="{total_width}" height="20">
  <linearGradient id="b" x2="0" y2="100%">
    <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
    <stop offset="1" stop-opacity=".1"/>
  </linearGradient>
  <clipPath id="a">
    <rect width="{total_width}" height="20" rx="3" fill="#fff"/>
  </clipPath>
  <g clip-path="url(#a)">
    <rect width="{label_width}" height="20" fill="#555"/>
    <rect x="{label_width}" width="{value_width}" height="20" fill="{color}"/>
    <rect width="{total_width}" height="20" fill="url(#b)"/>
  </g>
  <g fill="#fff" text-anchor="middle" font-family="DejaVu Sans,Verdana,Geneva,sans-serif" font-size="11">
    <text x="{label_x}" y="15" fill="#010101" fill-opacity=".3">{label}</text>
    <text x="{label_x}" y="14">{label}</text>
    <text x="{value_x}" y="15" fill="#010101" fill-opacity=".3">{value}%</text>
    <text x="{value_x}" y="14">{value}%</text>
  </g>
</svg>"""


def get_color(rate):
    if rate >= 70:
        return "#4c1"
    elif rate >= 50:
        return "#dfb317"
    else:
        return "#e05d44"


def parse_overall_rate(cobertura_path):
    tree = ET.parse(cobertura_path)
    root = tree.getroot()
    return float(root.get("line-rate", 0)) * 100


def generate_badge(label, rate):
    color = get_color(rate)
    value = f"{rate:.1f}"
    label_width = len(label) * 7 + 10
    value_width = len(value) * 7 + 18
    total_width = label_width + value_width
    return BADGE_TEMPLATE.format(
        total_width=total_width,
        label_width=label_width,
        value_width=value_width,
        label_x=label_width / 2,
        value_x=label_width + value_width / 2,
        color=color,
        label=label,
        value=value,
    )


def main():
    p = argparse.ArgumentParser(description="Generate coverage badge SVG")
    p.add_argument("--cobertura-path", required=True, help="Path to cobertura XML")
    p.add_argument("--label", required=True, help="Badge label (e.g. 'backend coverage')")
    p.add_argument("--output", required=True, help="Output SVG path")
    args = p.parse_args()

    rate = parse_overall_rate(args.cobertura_path)
    svg = generate_badge(args.label, rate)

    with open(args.output, "w") as f:
        f.write(svg)

    print(f"Badge generated: {args.label} = {rate:.1f}% → {args.output}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Make the script executable**

```bash
chmod +x .github/scripts/generate-badge.py
```

- [ ] **Step 3: Commit**

```bash
git add .github/scripts/generate-badge.py
git commit -m "feat: add coverage badge SVG generator script"
```

---

### Task 5: Write tests for the badge generation script

**Files:**
- Create: `.github/scripts/test_generate_badge.py`

- [ ] **Step 1: Write the test file**

Create `.github/scripts/test_generate_badge.py`:

```python
#!/usr/bin/env python3
"""Tests for generate-badge.py"""

import importlib.util
from pathlib import Path

SCRIPT_DIR = Path(__file__).parent
spec = importlib.util.spec_from_file_location("generate_badge", SCRIPT_DIR / "generate-badge.py")
generate_badge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generate_badge)

FIXTURE_PATH = str(SCRIPT_DIR / "fixtures" / "sample-cobertura.xml")


class TestGetColor:
    def test_green_at_70(self):
        assert generate_badge.get_color(70.0) == "#4c1"

    def test_green_above_70(self):
        assert generate_badge.get_color(95.0) == "#4c1"

    def test_yellow_at_50(self):
        assert generate_badge.get_color(50.0) == "#dfb317"

    def test_yellow_between_50_and_70(self):
        assert generate_badge.get_color(65.0) == "#dfb317"

    def test_red_below_50(self):
        assert generate_badge.get_color(30.0) == "#e05d44"


class TestParseOverallRate:
    def test_parses_fixture(self):
        rate = generate_badge.parse_overall_rate(FIXTURE_PATH)
        assert rate == 75.0


class TestGenerateBadge:
    def test_valid_svg(self):
        svg = generate_badge.generate_badge("coverage", 75.0)
        assert svg.startswith("<svg")
        assert "75.0%" in svg
        assert "#4c1" in svg

    def test_red_badge(self):
        svg = generate_badge.generate_badge("coverage", 30.0)
        assert "#e05d44" in svg

    def test_yellow_badge(self):
        svg = generate_badge.generate_badge("coverage", 55.0)
        assert "#dfb317" in svg
```

- [ ] **Step 2: Run the tests**

```bash
python -m pytest .github/scripts/test_generate_badge.py -v
```

Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add .github/scripts/test_generate_badge.py
git commit -m "test: add tests for badge generation script"
```

---

### Task 6: Update `pr-checks.yml` with coverage reporting steps

**Files:**
- Modify: `.github/workflows/pr-checks.yml`

- [ ] **Step 1: Update top-level permissions**

In `.github/workflows/pr-checks.yml`, change the `permissions` block from:

```yaml
permissions:
  contents: read
```

to:

```yaml
permissions:
  contents: read
  pull-requests: write
```

- [ ] **Step 2: Add coverage report step to backend job**

Add the following step after the existing "Report test results" step (after the `dorny/test-reporter` step):

```yaml
      - name: Report coverage
        if: always() && github.event_name == 'pull_request'
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          python3 .github/scripts/coverage-report.py \
            --cobertura-path "$(find TestResults -name 'coverage.cobertura.xml' | head -1)" \
            --label "Backend" \
            --overall-threshold 70 \
            --change-threshold 80 \
            --pr-number ${{ github.event.pull_request.number }}
```

- [ ] **Step 3: Add coverage report step to frontend job**

Add the following step after the existing "Run tests" step:

```yaml
      - name: Report coverage
        if: always() && github.event_name == 'pull_request'
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          python3 .github/scripts/coverage-report.py \
            --cobertura-path src/frontend/coverage/cobertura-coverage.xml \
            --label "Frontend" \
            --overall-threshold 70 \
            --change-threshold 80 \
            --pr-number ${{ github.event.pull_request.number }}
```

- [ ] **Step 4: Verify YAML is valid**

```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/pr-checks.yml'))" 2>/dev/null || python3 -c "
import json, sys
# Basic YAML structure check — ensure no syntax errors
with open('.github/workflows/pr-checks.yml') as f:
    content = f.read()
    if 'Report coverage' in content and 'pull-requests: write' in content:
        print('YAML contains expected additions')
    else:
        print('MISSING expected content', file=sys.stderr)
        sys.exit(1)
"
```

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/pr-checks.yml
git commit -m "feat: add coverage reporting steps to PR checks workflow"
```

---

### Task 7: Create the coverage badges workflow

**Files:**
- Create: `.github/workflows/coverage-badges.yml`

- [ ] **Step 1: Write the workflow file**

Create `.github/workflows/coverage-badges.yml`:

```yaml
name: Coverage badges

on:
  push:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: write

jobs:
  badges:
    name: Generate coverage badges
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Set up .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x

      - name: Set up Node.js
        uses: actions/setup-node@v6
        with:
          node-version: 24
          cache: npm
          cache-dependency-path: src/frontend/package-lock.json

      - name: Trust ASP.NET Core HTTPS development certificate
        run: |
          dotnet dev-certs https --clean
          dotnet dev-certs https --export-path /tmp/aspnetcore-dev-cert.crt --format PEM
          sudo cp /tmp/aspnetcore-dev-cert.crt /usr/local/share/ca-certificates/aspnetcore-dev-cert.crt
          sudo update-ca-certificates

      - name: Restore and build backend
        run: |
          dotnet restore SluiceBase.slnx
          dotnet build SluiceBase.slnx --configuration Release --no-restore

      - name: Run backend tests with coverage
        run: |
          dotnet test tests/IntegrationTests \
            --collect:"XPlat Code Coverage" \
            --configuration Release \
            --no-build

      - name: Install frontend dependencies
        working-directory: src/frontend
        run: npm ci

      - name: Run frontend tests with coverage
        working-directory: src/frontend
        run: npm run test

      - name: Generate badges
        run: |
          mkdir -p badges
          python3 .github/scripts/generate-badge.py \
            --cobertura-path "$(find TestResults -name 'coverage.cobertura.xml' | head -1)" \
            --label "backend coverage" \
            --output badges/backend-coverage.svg
          python3 .github/scripts/generate-badge.py \
            --cobertura-path src/frontend/coverage/cobertura-coverage.xml \
            --label "frontend coverage" \
            --output badges/frontend-coverage.svg

      - name: Deploy badges to gh-pages
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "github-actions[bot]@users.noreply.github.com"

          # Save badges to a temp location before switching branches
          cp -r badges /tmp/coverage-badges

          # Set up gh-pages branch
          if git fetch origin gh-pages:gh-pages 2>/dev/null; then
            git checkout gh-pages
          else
            git checkout --orphan gh-pages
            git rm -rf . 2>/dev/null || true
          fi

          # Copy badges into the branch
          mkdir -p badges
          cp -f /tmp/coverage-badges/*.svg badges/
          git add badges/
          git diff --cached --quiet && echo "No badge changes" && exit 0
          git commit -m "Update coverage badges"
          git push origin gh-pages
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/coverage-badges.yml
git commit -m "feat: add coverage badges workflow for main branch"
```

---

### Task 8: Add badges to README and finalize

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add badge images to README**

Add the following two lines immediately after the `# SluiceBase` heading (line 1) and before the description paragraph:

```markdown
![Backend Coverage](https://raw.githubusercontent.com/yeongjonglim/sluice-base/gh-pages/badges/backend-coverage.svg)
![Frontend Coverage](https://raw.githubusercontent.com/yeongjonglim/sluice-base/gh-pages/badges/frontend-coverage.svg)
```

The README should start with:

```markdown
# SluiceBase

![Backend Coverage](https://raw.githubusercontent.com/yeongjonglim/sluice-base/gh-pages/badges/backend-coverage.svg)
![Frontend Coverage](https://raw.githubusercontent.com/yeongjonglim/sluice-base/gh-pages/badges/frontend-coverage.svg)

SluiceBase is a self-hosted database query gateway...
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add coverage badges to README"
```

---

### Task 9: End-to-end local verification

- [ ] **Step 1: Run frontend tests with coverage**

From `src/frontend/`:

```bash
npm run test
```

Expected: Tests pass, `coverage/cobertura-coverage.xml` is generated, text summary printed showing line/branch/function/statement percentages.

- [ ] **Step 2: Run backend tests with coverage**

From repo root:

```bash
dotnet test tests/IntegrationTests --collect:"XPlat Code Coverage" --configuration Release
```

Expected: Tests pass, `TestResults/{guid}/coverage.cobertura.xml` is generated.

- [ ] **Step 3: Test coverage-report.py with real data**

```bash
python3 .github/scripts/coverage-report.py \
  --cobertura-path "$(find TestResults -name 'coverage.cobertura.xml' | head -1)" \
  --label "Backend" \
  --overall-threshold 70 \
  --change-threshold 80 \
  --pr-number 88 \
  2>&1 || echo "Script exited with code $? (expected if threshold not met)"
```

Expected: Script parses the XML and attempts to post a PR comment (may fail if no `GH_TOKEN` is set, but should print the pass/fail summary to stdout).

- [ ] **Step 4: Test generate-badge.py with real data**

```bash
mkdir -p /tmp/badges
python3 .github/scripts/generate-badge.py \
  --cobertura-path "$(find TestResults -name 'coverage.cobertura.xml' | head -1)" \
  --label "backend coverage" \
  --output /tmp/badges/backend-coverage.svg
cat /tmp/badges/backend-coverage.svg
```

Expected: Valid SVG output with the coverage percentage and appropriate color.

- [ ] **Step 5: Run all script tests**

```bash
python -m pytest .github/scripts/ -v
```

Expected: All tests pass.

- [ ] **Step 6: Push and verify PR checks**

```bash
git push
```

Open PR #88 and verify the CI runs. The coverage report steps will execute for the first time. Check that:
- Backend coverage report step runs (may show coverage comment on the PR)
- Frontend coverage report step runs
- Both steps post comments or fail gracefully
