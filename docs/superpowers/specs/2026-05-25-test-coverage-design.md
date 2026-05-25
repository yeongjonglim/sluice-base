# Test Coverage and Effectiveness Measurement

## Context

SluiceBase has 21 backend integration tests (xUnit + Aspire + Testcontainers) and 15 frontend unit tests (Vitest) + 4 e2e tests (Playwright). CI currently collects backend coverage via `--collect:"XPlat Code Coverage"` but doesn't report, gate, or surface it. Frontend has no coverage collection at all.

The testing strategy is integration-first: a single Aspire stack is stood up and endpoints are tested through HTTP. Unit tests exist only where endpoint-level testing is too tedious. This is a deliberate architectural choice.

## Goals

1. Surface coverage metrics in every PR as a comment — overall, per-change, and per-file
2. Enforce thresholds that block merge: 70% overall line coverage, 80% on changed lines
3. Provide test-to-code correlation by showing which files/lines are exercised and which are blind spots
4. Zero external dependencies — everything runs in GitHub Actions with tools already available on the runner

## Non-Goals

- Mutation testing (poor cost-benefit with integration-test-heavy architecture)
- External SaaS (Codecov, SonarCloud, Coveralls)
- Historical trend dashboard (can be added later via GitHub Pages if needed)
- E2E (Playwright) coverage collection (complex to instrument, low ROI for this iteration)
- External badge services (shields.io endpoint, etc.) — badges are self-generated

## Architecture

The system has three components:

```
┌──────────────────────────────────────────────────────────┐
│ pr-checks.yml (on pull_request)                          │
│                                                          │
│  backend job:                                            │
│    dotnet test --collect:"XPlat Code Coverage"           │
│    → TestResults/**/coverage.cobertura.xml               │
│    → coverage-report.py → PR comment + exit code         │
│                                                          │
│  frontend job:                                           │
│    vitest run --coverage                                 │
│    → coverage/cobertura-coverage.xml                     │
│    → coverage-report.py → PR comment + exit code         │
│                                                          │
├──────────────────────────────────────────────────────────┤
│ coverage-badges.yml (on push to main)                    │
│                                                          │
│  Runs tests with coverage → generates badge SVGs         │
│  → commits to badges/ directory on gh-pages branch       │
│  → README references badges via raw GitHub URL           │
└──────────────────────────────────────────────────────────┘
```

### Component 1: Coverage Collection

#### Backend

Already in place via `--collect:"XPlat Code Coverage"` in the `dotnet test` command. Coverlet (bundled with the .NET SDK) produces Cobertura XML at `TestResults/{guid}/coverage.cobertura.xml`.

No new packages needed. Threshold enforcement is handled by the reporting script (Component 2), not coverlet's built-in `--threshold` flag — this ensures the PR always gets a coverage comment even when the threshold is violated.

#### Frontend

Add `@vitest/coverage-v8` as a dev dependency.

Update `vitest.config.ts`:

```typescript
export default defineConfig({
  // ...existing config...
  test: {
    // ...existing test config...
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
});
```

Update `package.json` test script to include coverage:
```json
"test": "vitest run --coverage"
```

Vitest fails the run if thresholds aren't met.

### Component 2: PR Reporting Script

A single Python script at `.github/scripts/coverage-report.py` that handles both backend and frontend reports.

**Inputs** (command-line arguments):
- `--cobertura-path` — path to the Cobertura XML file
- `--label` — "Backend" or "Frontend" (for the comment header)
- `--overall-threshold` — minimum overall line coverage (default: 70)
- `--change-threshold` — minimum coverage on changed lines (default: 80)
- `--pr-number` — PR number for posting the comment
- `--lowest-files-count` — how many lowest-coverage files to show (default: 5)

**Behavior**:
1. Parse the Cobertura XML using `xml.etree.ElementTree`
2. Compute overall line coverage from the root `<coverage>` element's `line-rate` attribute
3. Get the list of changed files via `gh pr diff --name-only`
4. For each changed file, look up its coverage in the Cobertura `<class>` entries
5. Compute per-change coverage (lines hit / lines instrumented across changed files only)
6. Identify the N lowest-coverage files project-wide
7. Render a Markdown report
8. Post/update the PR comment via `gh pr comment`
9. Exit nonzero if either threshold is not met

**PR comment format**:

```markdown
## 📊 Coverage Report — {label}

| Metric | Value | Threshold | Status |
|--------|-------|-----------|--------|
| Overall line coverage | 74.2% | 70% | ✅ |
| Changed lines covered | 82.1% | 80% | ✅ |

### Changed file coverage

| File | Coverage | Uncovered lines |
|------|----------|-----------------|
| src/SluiceBase.Api/Endpoints/QueryEndpoint.cs | 88.2% | 45, 48-52 |
| src/SluiceBase.Core/Services/ApprovalService.cs | 91.0% | 12 |

### Lowest coverage files (project-wide)

| File | Coverage |
|------|----------|
| src/SluiceBase.Core/Services/ApprovalService.cs | 32.1% |
| src/SluiceBase.Api/Endpoints/UpdateEndpoint.cs | 41.5% |
```

**Comment update strategy**: Use `gh pr comment --edit-last` with a marker HTML comment (`<!-- coverage-{label} -->`) at the top of the body. This ensures repeated pushes update the existing comment rather than creating new ones.

**Dependencies**: Python stdlib only (`xml.etree.ElementTree`, `subprocess`, `argparse`, `pathlib`). `gh` CLI is pre-installed on GitHub-hosted runners.

### Component 3: Workflow Changes

Modify `.github/workflows/pr-checks.yml`:

**Permissions** (top-level):
```yaml
permissions:
  contents: read
  pull-requests: write
```

**Backend job** — add after the existing "Run integration tests" step:
```yaml
- name: Report coverage
  if: always() && github.event_name == 'pull_request'
  env:
    GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
  run: |
    python .github/scripts/coverage-report.py \
      --cobertura-path "$(find TestResults -name 'coverage.cobertura.xml' | head -1)" \
      --label "Backend" \
      --overall-threshold 70 \
      --change-threshold 80 \
      --pr-number ${{ github.event.pull_request.number }}
```

**Frontend job** — update existing test step and add coverage reporting:
```yaml
- name: Run tests
  working-directory: src/frontend
  run: npm run test

- name: Report coverage
  if: always() && github.event_name == 'pull_request'
  env:
    GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
  run: |
    python .github/scripts/coverage-report.py \
      --cobertura-path src/frontend/coverage/cobertura-coverage.xml \
      --label "Frontend" \
      --overall-threshold 70 \
      --change-threshold 80 \
      --pr-number ${{ github.event.pull_request.number }}
```

## Threshold Strategy

| Metric | Threshold | Scope | Enforcement |
|--------|-----------|-------|-------------|
| Overall line coverage | 70% | Project-wide | Build fails (coverlet/vitest) + script exit code |
| Changed line coverage | 80% | Files modified in PR | Script exit code |

Thresholds are configured in two places:
- **Vitest config** (`thresholds`) — catches violations in local dev (`npm run test` fails locally)
- **Script-level** (coverage-report.py args) — authoritative CI enforcement for both overall and per-change thresholds, ensures PR comment is always posted before failing

Note: coverlet's `--threshold` flag is intentionally not used in CI. If coverlet fails the build before the report step runs, the PR never gets its coverage comment. The script handles both enforcement and reporting as a single step.

## Test-to-Code Correlation

The per-file coverage table in the PR comment provides test-to-code correlation: for every source file, you can see what percentage of its lines are exercised by the test suite. The "lowest coverage files" section surfaces persistent blind spots that the integration tests don't reach.

This is particularly valuable with the integration-first strategy — it answers "which code paths does our Aspire stack actually exercise?" without requiring a separate tool.

### Component 4: Coverage Badges

A separate workflow `.github/workflows/coverage-badges.yml` runs on pushes to `main` (i.e., after a PR merges). It:

1. Checks out the code
2. Runs backend tests with coverage collection
3. Runs frontend tests with coverage collection
4. Parses both Cobertura XML files to extract overall line coverage percentages
5. Generates two SVG badge files (`backend-coverage.svg`, `frontend-coverage.svg`) using a simple Python script (`.github/scripts/generate-badge.py`)
6. Commits the SVGs to a `badges/` directory on the `gh-pages` branch

**Badge SVG generation**: The script templates a minimal SVG with the coverage percentage and a color (green ≥ 70%, yellow ≥ 50%, red < 50%). No external service needed.

**README integration**: Add badge images to the top of `README.md` referencing the `gh-pages` branch:
```markdown
![Backend Coverage](https://raw.githubusercontent.com/{owner}/{repo}/gh-pages/badges/backend-coverage.svg)
![Frontend Coverage](https://raw.githubusercontent.com/{owner}/{repo}/gh-pages/badges/frontend-coverage.svg)
```

**Why a separate workflow**: Running badge generation inside `pr-checks.yml` would require write access to `gh-pages` on every PR, which is a security concern for forks. A separate workflow triggered on `push` to `main` only runs with trusted permissions.

## File Changes Summary

| File | Change |
|------|--------|
| `.github/scripts/coverage-report.py` | New — coverage parsing and PR commenting script |
| `.github/scripts/generate-badge.py` | New — SVG badge generator |
| `.github/workflows/pr-checks.yml` | Modified — add coverage report steps, update permissions |
| `.github/workflows/coverage-badges.yml` | New — badge generation on main merge |
| `src/frontend/vitest.config.ts` | Modified — add coverage configuration |
| `src/frontend/package.json` | Modified — add `@vitest/coverage-v8` dev dependency |
| `README.md` | Modified — add coverage badge images |

## Future Extensions

- Add GitHub Pages deployment of ReportGenerator HTML for historical trend tracking
- Collect Playwright e2e coverage if browser instrumentation becomes easier
- Add backend unit test project (`tests/SluiceBase.Core.Tests/`) and adjust thresholds upward
- Add mutation testing once unit test coverage is sufficient to make it cost-effective
