# Contributing

Contributions are welcome.

By contributing, you agree that your contribution may be distributed under GPL-3.0-or-later.

1. **Open an issue first** if you're proposing a behaviour change. For small fixes (typos, refactors,
   missing edge cases), just send a PR.
2. **Describe what you tested.** Capture and audio bugs are usually environment-specific, so say
   which Windows build, GPU, and virtual cable you used, and whether `--smoke` passed.
3. **Nothing that captures silently.** The point of this app is that you know what is being shared
   and your viewers know what they are hearing. Changes that start capture without a visible,
   deliberate user action, or that hide which windows are live, will be rejected.
4. **Keep the scope small.** One behaviour per PR. Capture, compositing, and audio each have their
   own failure modes and are much easier to review apart.

## Before you open a PR

    ./build.ps1
    ./lint.ps1 -Check
    ./test.ps1

`build.ps1` also activates `.githooks/`, which stamps commit subjects with the build version and
runs lint and build on push.

Commit subjects follow conventional commits: `type(scope): description`. The types that reach the
changelog are `feat`, `fix`, `perf`, `refactor`, `revert`, and `chore(deps)`.

## Where things live

`src/MultiWindowShare.Core` has no OS dependencies and is where new logic should go whenever it can:
everything there is unit-testable without a window, a GPU, or an audio device. `src/MultiWindowShare`
is the part that talks to Windows. If you find yourself wanting to test something in the app project,
that is usually a sign the logic belongs in Core.
