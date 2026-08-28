# Git hooks

Versioned hooks; git does not pick them up automatically. Enable once per clone:

    git config core.hooksPath .githooks

- `pre-commit`: `dotnet format whitespace --verify-no-changes` on the staged `.cs` files (per project, so untouched legacy files never block a commit), a build of the three net8.0 projects (GUI, CLI, tests; the library's net462 leg has never built and is not exercised), and `py_compile` (plus `ruff` when installed) on staged `.py` files.
- `pre-push`: the full test suite, which includes the byte-identity algebra tests against the real save fixture.

Bypass a single run with `--no-verify`. Style preferences beyond whitespace live in `.editorconfig` at suggestion level; run `dotnet format style <project>` to apply them by hand.
