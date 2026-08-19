# Release workflow

## Stable release

1. Make sure `main` is green.
2. Create and push a tag shaped like `vYYYY.M.D.N`, where `N` is the next same-day revision. `Assert-ReleaseVersionSequence.ps1` rejects anything else.
3. `.github/workflows/release.yml` builds the compressed single-file win-x64 zip and the integrity TSV.
4. The workflow publishes the GitHub release and promotes `CHANGELOG.md` from `Unreleased` to the tag section on `main`.

## Prerelease

Use a suffix tag such as `vYYYY.M.D.N-beta`. The same release workflow runs, but the GitHub release is marked as a prerelease and `CHANGELOG.md` promotion is skipped.

## Changelog

`.github/workflows/changelog-append.yml` reads conventional commit subjects on every push to `main` and appends them under `## Unreleased`. `feat` becomes Added, `fix` becomes Fixed, `perf`/`refactor`/`revert`/`chore(deps)` become Changed, and a `!` marks Breaking. `docs`, `ci`, `test`, and `build` are dropped. Commits carrying `[skip changelog]` are ignored, which is how the bot avoids recursing on itself.

Release notes are generated from the promoted `CHANGELOG.md` section by `Generate-ReleaseNotes.ps1`; there is no separate notes file to maintain.
