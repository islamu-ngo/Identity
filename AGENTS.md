# AGENTS.md

## Build, Lint, and Test Commands
- **Build solution:**
  ```sh
  dotnet build
  ```
- **Run application:**
  ```sh
  dotnet run --project <ProjectPath>
  ```
- **Lint/Format code:**
  ```sh
  dotnet format
  ```
- **Run tests:**
  > No test projects or files detected. If tests are added, use:
  ```sh
  dotnet test
  ```
  To run a single test: `dotnet test --filter FullyQualifiedName~TestName`

## Code Style Guidelines
- **Imports:** Use `using` statements at the top, remove unused imports.
- **Formatting:** 4 spaces per indent, no tabs. Use `dotnet format` for consistency.
- **Types:** Prefer explicit types over `var` unless type is obvious.
- **Naming:**
  - Classes/Methods/Properties: PascalCase
  - Private Variables/Fields: underscore+camelCase
  - Variables/Fields: camelCase
  - Constants: ALL_CAPS
- **Error Handling:** Use try/catch for expected exceptions, log errors, avoid empty catch blocks.
- **Comments:** Use XML doc comments for public APIs, concise inline comments elsewhere.
- **No custom Cursor or Copilot rules present.**

> Follow standard .NET/C# conventions unless otherwise specified.
