# Security

## The trust boundary this tool has, on purpose

Attest sends scoped source code to the LLM provider **you** configure, and nowhere else — the
Sanitizer redacts secrets from that outbound code, and again from the report, before either ever
leaves the machine. See [README's Security section](README.md#security-your-code-and-your-secrets)
for that side of it.

The other side of the same boundary: **the LLM's response is untrusted code, and Attest compiles
and runs it.** `attest --diff` takes the model's proposed property, writes it into a real scratch
project, `dotnet build`s it, `dotnet test`s it, and runs `dotnet-stryker` against it — all with the
privileges of whatever process invoked `attest`. On a laptop that's your own account. In CI, that
can be a pipeline holding deploy keys or cloud credentials in scope.

This is inherent to what the tool does, not a bug to be fixed. Two things follow from it:

- **Only point `attest` at a provider and a CI environment you trust.** A compromised or malicious
  LLM endpoint can return code designed to do more than fail a property.
- **Prefer scoped, ephemeral CI credentials** for any job that runs `attest`, the same way you
  would for any step that executes code it did not write itself.

`customGeneratorsType` (a type name from `attest.json`) and every path Attest reads are validated
against injection into the generated test source before compilation; that closes off attest.json
itself as an attack vector, but does not — and cannot — change the fact that the LLM's own output
is executed code, not just text.

## Reporting a vulnerability

Please do **not** open a public GitHub issue for a security finding. Email
danellaclaudioluiz@gmail.com with a description and, if possible, reproduction steps. This is a
solo-maintained project — expect an acknowledgment within a few days, not an SLA.
