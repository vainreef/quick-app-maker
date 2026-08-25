# quick-app-maker Agent Contract

## Partner Center publishing

Before touching Microsoft Partner Center automation, read:

1. `docs/partner-center/Agent-运行契约.md`
2. `docs/partner-center/Edge-Store-可靠性重构.md`
3. `skills/vainreef-fast-publish/SKILL.md`

The only supported implementation is `toolchain/edge-store-cli/`. The ignored `apps/Project/edge-store-cli-fast/` tree is a prior diagnostic copy; never select its historical DLL or `_tmp-diag` scripts from `process.md` as the implementation. The local copy is synchronized for compatibility, but source changes belong in `toolchain/edge-store-cli/`.

## Required completion semantics

- Identify `PageKind` before waiting for a control.
- Treat `0 form differences`, a successful click, same-DOM values, and `EXIT=0` as intermediate evidence.
- After Apply, cold-load and recompute the complete phase diff.
- Verify the corresponding submission-overview module is `Complete` before writing checkpoint `Converged` or reporting `PRODUCT_VERIFIED`.
- A read-only run that finds differences returns exit code 4 and does not write completion.
- Never upload a package when a same-name row is Processing, Error, or duplicated; only one `Validated` row is upload success.
- Use `inspect` or `status` for current-page status; `run -Phase all` is an explicit six-phase operation, not a status probe.
- For brand new product submissions, execute `Invoke-EdgeStore.ps1 -Action reserve -AppName "<AppName>" -Manifest <manifest>` to automate name reservation and Identity extraction. Never dump manual Partner Center console steps onto the user or prompt interactive choice dialogs.
