# Architecture

## Packaging

Three projects, three packages published.

```
Attest.Core    the language-agnostic contracts: stage interfaces, funnel report, rejection reasons
Attest.NET     the library: the loop bound to FsCheck + Stryker.NET
Attest.Cli        dotnet tool: attest --diff, init, doctor; a reusable GitHub Action (danellalc/Attest.NET, action.yml at repo root) wraps it -- PR comments (posting the report back, not just logging it) are still v1.1, not built
```

`Attest.NET` depends on `Attest.Core` as an ordinary NuGet dependency, so `Attest.Core` is published in lockstep rather than embedded — the alternative (merging its DLL into `Attest.NET`'s package via a custom pack target) trades one extra published package for real build fragility. It stays a thin, stable contracts layer; do not pre-fragment further for a second adapter that may not arrive.

## Pipeline

Everything happens in seven stages, in order. New code belongs to exactly one of them.

```
git diff
  1. DiffScope         changed files, changed lines, owning methods, direct callers
  2. Sanitizer         deterministic secret scan + redaction, BEFORE anything reaches an LLM
  3. Proposer          LLM proposes candidate properties for the sanitized, scoped code
  4. Synthesizer       candidates become compilable FsCheck tests in a scratch project
  5. Validator         run against current code; failures are WRONG properties, rejected
  6. Falsifier         Stryker.NET mutates only the changed lines; survivors re-run
                       per mutant; properties that stay green are TRIVIAL, rejected
  7. Evidence          survivors packaged with the mutant(s) they killed; the report
                       passes through the Sanitizer AGAIN before rendering
```

Stages 1, 2, 4, 5, 6, 7 are deterministic. Stage 3 is the only place an LLM exists.

WRONG and TRIVIAL are the two rejection reasons that say something about a candidate's quality; a handful of narrower, named reasons (synthesis failure, a tooling crash mid-validation-or-falsification, exceeding the mutant ceiling) exist alongside them for outcomes that are not a verdict on the candidate at all — see `RejectionReason` in `Attest.Core`.

**Two boundaries, both absolute:**

- **The trust boundary** sits between stage 3 and everything else. Nothing the LLM says is believed; stages 5 and 6 exist to refute it.
- **The privacy boundary** sits at stage 2. No source code reaches the network without passing the Sanitizer, and no report reaches a PR without passing it again. Diff scoping pulls in caller code the author never looked at in this PR (a composition root with a real connection string is the canonical hazard), which is why sanitization is a stage, not an option.

---

## The hard problems

### The Synthesizer is the biggest risk in the project

"LLM text becomes a compilable FsCheck test" works for records and DTOs and probably breaks on real domain types: private constructors, EF-attached entities, DI-resolved services. The panel that reviewed this design called it the single most uncertain piece, and the plan treats it that way:

- v1 strategy: reflection-based construction with per-property overrides, honest failure (`AttestUnsynthesizableTypeException`, naming the type and why) when construction is impossible.
- **A custom-generator escape hatch is v1 scope, not a nice-to-have.** Every serious PBT tool has one; a user must be able to register `Arb`/`Gen` instances for their domain types and have the Synthesizer pick them up. Without this, the tool dies on first contact with a real codebase. **Implemented**: `customGeneratorsType` in `attest.json` names a static Arbitrary-provider class; the Synthesizer emits `[Properties(Arbitrary = [typeof(...)])]` on the generated test class, FsCheck's own convention, no bespoke registration mechanism invented.
- Generator synthesis for domain types is shared territory with EFCore.AutoSeed (same author, same problem: "construct a valid `Order`"); extraction of the common core is roadmap, not v1.

### Diff scoping that actually holds

Scope too narrow and properties miss the changed behavior; too wide and you re-inherit whole-repo mutation cost.

v1 rule: changed methods plus their direct callers **within the solution**, not just the same project. Layered architecture (Web → Application → Domain across projects) is the .NET default, and a same-project-only rule would miss the most common case. `internal` callers count; `InternalsVisibleTo` is respected. A configurable ceiling on mutant count (`maxMutants` in `attest.json`, default 200); a candidate whose scope exceeds it is rejected with a named `MutantCeilingExceeded` reason in the funnel report, not silently dropped. There is no CLI flag for the ceiling today — only the config file.

### Stryker.NET is a dependency with known sharp edges

Stryker's own `--since` (diff) mode has open issues where it silently falls back to mutating the whole project. Attest does not delegate scoping to Stryker: the Falsifier computes the file/line mutation set itself and passes an explicit mutate filter, then **verifies the mutant count Stryker reports against its own expectation**; a mismatch aborts with a named error rather than silently burning an hour of CI. Embedding Stryker programmatically is the project's biggest technical bet, which is why it is a go/no-go spike in week 1, not an assumption inside a two-week phase.

### The oracle problem is structural

Stage 5 has a blind spot: if the current code has a bug and the LLM proposes a property that *encodes the bug*, the property passes validation, and mutation may not catch it either. No mutation score fixes this: mutation proves sensitivity to change, never correctness of intent.

Attest's position, stated in the README next to the promise (not buried here): delivered properties attest that tests detect change. Intent is judged by the human, which is why every delivered property is rendered in plain language. The v2 spec mode (below) attacks this from the other side: when the human writes the intended rule and Attest formalizes it, intent enters the loop from the start.

### Equivalent mutants, without an LLM judge

Some mutants don't change behavior. Meta's ACH uses an LLM to classify these, at 47% recall by their own measurement, which is exactly the "LLM judges LLM" pattern this project forbids. Attest's rule is simpler and deterministic: a property is rejected only if it kills **zero** mutants, never for missing some. One verified kill is proof enough; equivalent mutants then cost compute, not correctness.

### Flaky properties → quarantine

A property involving time, I/O or async can pass validation and fail falsification for environmental reasons. Stage 5 runs every candidate **twice with different seeds**; inconsistent results quarantine the property with a named reason rather than rejecting or delivering it. Quarantine is a first-class outcome: it frequently indicates real nondeterminism in the user's code, which is itself a finding (and becomes a headline feature in v2).

### Cost control

One proposal call per changed-file batch, no retry loops, no agentic wandering. Token spend printed in the report. A per-run LLM cost ceiling (`--max-llm-cost`, mirroring the mutant ceiling) is v1.x, not built yet. Model deprecation is a named risk: provider/model is config, not code. `attest doctor` checks this today only for Ollama, which can be queried for its installed models without cost; for Anthropic it can only confirm the API key is set, since checking model existence would mean spending an API call.

### The zero-delivery case is a designed outcome

"0 properties delivered" must distinguish *your diff had nothing property-testable* (config-only change, plumbing) from *the tool failed*. The funnel report renders every stage's counts even when the end is zero, and `attest doctor` exists so environmental failure is caught before the run, not inferred from an empty result.

---

## Design decisions

### Why properties instead of example tests

Example generators inherit the code's bugs: they assert what the code does. Properties assert what must hold regardless of implementation: idempotency, bounds, round-trips, conservation. That is also precisely the class that catches the "hard 20%" (races, async, edge states) example tests structurally miss, and one property replaces dozens of examples, keeping the mutation budget small. Among all propose-then-refute systems found (ACH, Mutahunter, Cover-Agent), none generates properties. This is the moat that outlasts the loop itself.

### Why the LLM only proposes

[Vikram, Lemieux & Padhye (ISSTA 2023)](https://arxiv.org/abs/2307.04346) measured what LLMs do when writing property tests unsupervised: GPT-4 produced sound, non-trivial properties for only 21% of extractable API properties tested; the rest split between trivial (asserts nothing meaningful) and unsound (actually false). Both failure modes are machine-detectable: wrong properties fail on working code, trivial ones survive mutants. So the LLM gets the one job machines can't do (hypothesis generation) and machines get the one job LLMs can't be trusted with (verification). ACH's 47%-recall LLM judge is the counterexample that proves the rule.

### Why the Sanitizer is a stage, not a flag

BYOK is a vendor-neutrality decision; it is not a privacy guarantee. Scoped code crosses the network at stage 3, and the Evidence report becomes a public, permanent PR comment. A secret that leaks through this pipeline doesn't leak once: it becomes searchable history. Deterministic scanning (pattern + entropy, no network) fits the "everything deterministic except stage 3" rule, runs twice (before the LLM, before the report), and `--fail-on-secret` defaults on in CI.

### Why diff-scoped mutation

Whole-repo mutation is infeasible at scale: [Petrović & Ivanković (Google, ICSE-SEIP 2018)](https://research.google/pubs/archive/46584.pdf) concluded repo-wide mutation scores are both too expensive to compute and not actionable for developers at their scale. A real-world [8-hour Stryker.NET run on a ~20k-line, ~500-file project](https://github.com/stryker-mutator/stryker-net/discussions/3013) confirms the same wall shows up in .NET specifically. The diff is small, fresh in the author's head, and the only code whose correctness is in question today.

### Why evidence is re-verified, not stored

The report claims "this property killed this mutant". Attest re-runs that exact pair before emitting. A tool whose entire pitch is "carry proof" cannot afford a stale one.

### Why a generic provider instead of one per vendor

Anthropic (hosted) and Ollama (fully local, air-gap capable) cover both trust postures, and were the whole of v1's provider surface for a while: a bespoke provider per vendor is config plumbing that never ends. `OpenAiCompatibleProvider` opens this up properly instead: one provider, config-only (base URL, model, API key), that reaches OpenAI, Groq, DeepSeek, Together, a self-hosted vLLM/llama.cpp server, or anything else speaking the same de facto standard API shape.

Real testing against a real repo is what forced this design's two honest limits, both deliberate rather than oversights:

- **JSON reliability is not guaranteed.** The `format`-constrained-generation fix that made Ollama's small local models reliable is Ollama-specific; not every OpenAI-compatible backend supports strict structured output the same way. `jsonMode` (`schema` / `object` / `none`) is config, not auto-detected — the design's "no retry loops" rule means Attest will not silently downgrade and retry on a rejected parameter; a backend that does not support the configured mode fails the call loudly instead.
- **Cost cannot be assumed.** Anthropic ships a hardcoded, maintained pricing table and refuses to run for a model it does not have a price for, because guessing would be worse than refusing. A generic endpoint has no such table to maintain — pricing is optional config (`inputPricePerMillion`/`outputPricePerMillion`); without it, the report shows cost as explicitly not tracked, never a silent `$0.00` that would read as "this was free" for a backend that might not be.

### Why FsCheck over CsCheck for v1

FsCheck v3 is the canonical .NET property framework with mature xUnit integration. CsCheck (C#-first, strong shrinking, parallel testing support) is the planned second synthesis target; framework-specific emission lives behind one interface in the Synthesizer for exactly that reason, and CsCheck's parallel testing is the intended vehicle for v2 concurrency properties.

---

## Roadmap

**v1: the loop, .NET**
Sanitizer, diff scoping (solution-wide callers), LLM proposal (Anthropic, Ollama, or any OpenAI-compatible endpoint), FsCheck synthesis with custom-generator escape hatch, validation, diff-scoped falsification with mutant-count verification, quarantine, evidence with re-verified kills, funnel report (including the zero case), `attest --diff`, `attest init`, `attest doctor`, GitHub Action with fork-PR and fetch-depth guidance documented.

**v1.x: quick wins**
**`--compare-suite` (built):** runs the Falsifier against the tests the repo already has: "do your existing tests kill mutants?" answered with a number, no LLM involved -- `attest --compare-suite --diff <base-ref> --test-project <path>`. Close to a standalone entry product and the strongest possible demo material, so it shipped first among the v1.x items. SARIF and JSON output formats (`--format`): code scanning and downstream tooling respectively. `--max-llm-cost`. PR comments that edit in place instead of stacking (the #1 complaint against existing PR bots). Signed evidence export (JSON). `--trace-id`: pass-through requirement/ticket tag on every delivered property, no new verification. Webhook notifications (Slack/Teams-compatible, a single POST to a URL the user owns, no account, no hosting).

**v2: depth**
**`attest.lock` (properties as a living contract):** survivors accumulate in a versioned file; every future diff re-runs them; a broken accumulated property is either a regression or a conscious business-rule change. This turns a PR tool into the project's invariant guardian, and retention compounds with use.
Stateful/model-based proposals and concurrency properties via CsCheck parallel testing (the real "hard 20%"). Quarantine analytics: "your code is nondeterministic here" as a first-class finding. Incremental mutation cache across pushes in the same PR. NUnit/MSTest synthesis targets. Shared generator core with EFCore.AutoSeed. "% of diff proven" as the honest metric replacing coverage.

**v3: intent and reach**
**Spec mode:** the developer writes the business rule in natural language ("a cancelled order can never be invoiced"); Attest formalizes, validates and maintains the corresponding property. Intent enters the loop from the human side: the structural answer to the oracle problem.
Diff risk ranking: changed lines whose mutants nothing killed (not even the repo's own suite) flagged as unprotected; a free byproduct of what the pipeline already computes.
`Attest.TypeScript` (fast-check + StrykerJS); the adapter contract goes public as an open standard, not a feature.

**v4: learning**
Opt-in local feedback: rejected candidates (labeled wrong/trivial) tune the Proposer prompt per repo. Better on *your* codebase with use: no cloud, no telemetry, cache-local. `Attest.Python`.

### Explicitly out of scope

Whole-repo mutation. E2E/browser testing. Distributed-system correctness proving. LLM-based verification of anything. IDE extensions (CLI + Action only). Compliance-report generation (AIBOM, SOC2; that is a different product). Hosted service, dashboards, org-wide spend tracking. Style or architecture opinions: Attest speaks only when it has proof, and that restraint is the brand.

### Named risks beyond the code

Provider terms-of-service vs. MIT-licensed output (mitigated: Anthropic's and OpenAI's terms both assign output ownership to the customer, no MIT conflict — see README's Security section; the generic OpenAI-compatible provider reaching other backends is the user's own responsibility to check); real CI cost of Stryker runs (not just LLM tokens); model deprecation breaking the Proposer (mitigated: model is config, doctor checks it); an incumbent adding propose-then-refute in a changelog (realistic window of 6–12 months after any visible traction), which is why the durable assets are the property focus, the fully-local path, and the open adapter contract; solo-maintainer bus factor for a tool running inside other people's CI.
