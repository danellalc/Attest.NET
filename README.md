# Attest

Property-based tests that carry proof. Attest proposes properties for the code you changed, then tries to prove each one worthless: against your real code and against mutants of it. **You only see the survivors, each one shipped with the mutant it killed.**

> Status: in development. This README describes the design being built; see the [roadmap](ARCHITECTURE.md#roadmap).

> [GIF placeholder: will be recorded from the first real run before launch. No invented numbers here.]

## The problem

AI writes your tests now. And AI treats your buggy code as the source of truth: ask it to test a function and it will faithfully cover whatever the function does, bugs included. The result is a suite that is green, comprehensive-looking, and proves nothing. False green.

Coverage will not save you: 100% line coverage with weak assertions is decorative. The only honest measure of a test is whether it fails when the code is wrong, and nobody has time to check that by hand for every generated test.

Property-based testing would help: one property replaces a hundred examples and catches the race conditions and edge cases that example tests miss. But almost nobody writes properties, because thinking of them is genuinely hard. And when you ask an LLM to write them unsupervised, research shows it produces properties that are frequently trivial or wrong: [Vikram, Lemieux & Padhye (ISSTA 2023)](https://arxiv.org/abs/2307.04346) found GPT-4 produced sound, non-trivial properties for only 21% of extractable API properties tested.

Both failure modes are machine-detectable. That is the whole idea.

## How it works

```bash
dotnet tool install -g Attest.Cli
attest --diff origin/main --project path/to/YourProject.csproj
```

1. **Scope.** Reads your diff: changed files, changed lines, the methods that own them.
2. **Sanitize.** A deterministic secret scanner runs over the scoped code **before anything reaches an LLM**. Keys, connection strings and high-entropy tokens are redacted before the code — or the report — ever leaves the Sanitizer.
3. **Propose.** An LLM proposes candidate properties: invariants, idempotency, ordering, round-trips.
4. **Validate.** Each candidate runs against your current code. Fails on code that works? The property is wrong. Rejected.
5. **Falsify.** Attest mutates the changed lines (via Stryker.NET) and re-runs the survivors. Stays green on broken code? The property proves nothing. Rejected.
6. **Deliver.** What remains is true *and* useful. Each property ships with the mutant it killed, and the kill is re-verified before the report is emitted, never stored on faith.

The LLM proposes. The machine refutes. You receive only what survived.

## What this proves, and what it doesn't

A delivered property attests that **your tests detect change**: it holds on the current code, and it fails when that code is deliberately broken.

It does **not** attest that your code matches intent. If the code has a bug and a property accurately describes that bug, it can survive both filters. That is the oracle problem, it is structural, and no mutation score fixes it. Attest's answer is to keep the human in the loop where only the human works: every delivered property is rendered in plain language, precisely so you can read it and say "wait, that is not what this should do."

Proof of sensitivity, by machine. Judgment of intent, by you. That division of labor is the design.

## Prior art, and what is different here

This idea has serious ancestors, and naming them matters more than pretending they don't exist:

- **Meta's ACH** (FSE 2025) runs *LLM proposes → mutation decides → only survivors ship, with the mutant as evidence in the diff* in production across Facebook, Instagram and WhatsApp since 2024. It is internal, example-test based, and uses an LLM to judge equivalent mutants, at 47% recall, per their own paper.
- **Mutahunter** is open source and pairs LLM-generated tests with mutation filtering, today.
- **Qodo's Cover-Agent** descends from Meta's earlier TestGen-LLM paper: generates example tests, gates on coverage improvement.

What none of them does, and Attest does:

1. **Properties, not examples.** Every ancestor generates example tests that assert current behavior. Attest generates invariants: the class of test that catches races, edge states and the "hard 20%" example tests structurally miss.
2. **.NET.** FsCheck + Stryker.NET. This ecosystem has none of the above.
3. **No LLM as judge, ever.** Where ACH uses an LLM to classify equivalent mutants (47% recall), Attest's rule is absolute: the LLM exists in exactly one stage, proposal. Everything that accepts or rejects is deterministic. Verification you cannot audit is not verification.
4. **Open, installable, bring-your-own-key.** ACH will never be on NuGet.

So the honest pitch: **the first open productization of propose-then-refute for .NET, and the only one in any ecosystem that never lets an LLM judge.**

## Security: your code and your secrets

Attest sends scoped source code to the LLM provider **you** configure, and nowhere else. That has consequences the design takes seriously:

- The **Sanitizer** stage (deterministic, no network) scans scoped code for secrets before any proposal call, and redacts them. There is no opt-out.
- The same scan runs **again** on the report before it is rendered: a report is meant to be shared, and defense in depth is cheaper than an incident.
- **Fully local is a first-class path:** set `"provider": "ollama"` in `attest.json` and code never leaves the machine — no Anthropic call is ever made.
- Diff scoping pulls in direct callers of changed methods, code you may not have looked at in this PR. That is exactly why the Sanitizer is not optional.

## Language-agnostic by design

The loop (sanitize, propose, validate, falsify, deliver) has nothing .NET-specific in it. Each language needs an adapter binding a property framework and a mutator:

| Adapter | Property framework | Mutator | Status |
|---|---|---|---|
| `Attest.NET` | FsCheck | Stryker.NET | **v1, in development** |
| `Attest.TypeScript` | fast-check | StrykerJS | roadmap |
| `Attest.Python` | Hypothesis | mutmut | roadmap |

The adapter contract goes public when the first external adapter has a consumer (roadmap).

## What it does not do

- **It does not replace your test suite.** It adds verified properties on top of whatever you have.
- **It does not prove intent.** See the oracle problem above; that caveat sits next to the promise on purpose.
- **It does not prove your whole system correct.** That is deterministic simulation territory (Antithesis). Attest proves the code you changed today, on your laptop, in minutes.
- **It does not trust the LLM.** Proposal only. No LLM judges, classifies or verifies anything.
- **It does not run mutation on your whole repo.** Diff-scoped, always, with a hard mutant ceiling (`maxMutants` in `attest.json`, default 200); a candidate whose scope exceeds it is rejected with a named `MutantCeilingExceeded` reason in the funnel report, not silently dropped.
- **It is not a service.** CLI and CI step. No cloud, no account.

## Supported

| | Status |
|---|---|
| `net10.0`, `net8.0` | v1 target |
| xUnit + FsCheck v3 | v1 target |
| LLM providers | Anthropic, Ollama (fully local) at v1 |

## Compared to

- **Copilot / Qodo test generation**: example tests, no verification they catch anything. Attest refuses to show you a test without proof.
- **Meta ACH / Mutahunter / Cover-Agent**: the ancestors; see [prior art](#prior-art-and-what-is-different-here).
- **Diffblue Cover**: the most mature generator, JVM-only, example-first. No .NET equivalent exists.
- **Stryker.NET**: the mutator Attest uses internally. Stryker audits tests you wrote; Attest generates, audits and filters in one loop.
- **FsCheck / CsCheck**: the property frameworks. They run properties; they don't propose them. Attest sits on top.
- **Antithesis**: whole-system correctness under fault injection, enterprise scale and price. Attest is the pocket version: your diff, your laptop, free.

## License

MIT

*Note on naming: an unrelated `Attest.*` package family (a fakes framework) exists on NuGet, and an unrelated `attest-framework` exists on GitHub. This project is `Attest.NET` / `Attest.Cli`, at `github.com/danellalc/Attest.NET`. The repo name matches the package on purpose, so the link itself disambiguates.*
