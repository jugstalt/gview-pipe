# Contributing to gView.Pipe

Thanks for helping. The short version: build, test, keep the documentation and the changelog in step with the code.

## Build and test

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build gView.Pipe.slnx
dotnet test --solution gView.Pipe.slnx
dotnet run --project src/gView.Pipe.Shell
```

Python is needed only for the documentation (`pip install -r docs/requirements.txt`, then `./build.cmd Docs`, or `python -m mkdocs serve` after `dotnet run --project src/gView.Pipe.Docs`).

## Where things are

The structure, conventions and the steps for writing a node are in [CLAUDE.md](CLAUDE.md). Terms are defined in [GLOSSARY.md](GLOSSARY.md), decisions in [`docs/adr`](docs/adr). The sample workflows in `samples/workflows` are run by the plugin tests.

## A change

- Follow the conventions of the surrounding code: file-scoped namespaces, the `Pipe` prefix for public types, `static public`, package versions only in `Directory.Packages.props`. Code, comments and UI text are in English.
- Add tests for behavior; the Razor components are tested with bUnit.
- A change to a node, a parameter, a setting or the behavior updates the matching pages of the documentation in `docs/site/en` and `docs/site/de`. The node reference and the sample downloads are generated, never edited.
- Add a line to *Unreleased* in [CHANGELOG.md](CHANGELOG.md).
- A new package needs an entry in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); a test fails otherwise.
- A decision with trade-offs gets an ADR in `docs/adr`.
- Never change the id of a built-in node (`PipeBuiltIn.cs`): saved workflows refer to it.

Security issues: see [SECURITY.md](SECURITY.md).
