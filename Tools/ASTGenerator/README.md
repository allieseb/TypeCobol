# ASTGenerator

`ASTGenerator` is an inspection/debug tool that parses a COBOL source file with TypeCobol and writes a structured JSON dump of the AST root.

This output is intentionally conservative and is not a stable public schema. It contains node type, optional name/id/location metadata, diagnostics, missing copy names, and recursive child nodes.

## Usage

```sh
dotnet run --project Tools/ASTGenerator/ASTGenerator.csproj -- path/to/source.cbl
```

Write JSON to a file:

```sh
dotnet run --project Tools/ASTGenerator/ASTGenerator.csproj -- path/to/source.cbl /tmp/source-ast.json
```

Add copybook search folders:

```sh
dotnet run --project Tools/ASTGenerator/ASTGenerator.csproj -- --copies path/to/copies path/to/source.cbl /tmp/source-ast.json
```

Plain COBOL mode is enabled by default. Use `--typecobol` to parse TypeCobol input.

COBOL reference format is the default because many COBOL samples use fixed columns. Use `--format free` for free-format source:

```sh
dotnet run --project Tools/ASTGenerator/ASTGenerator.csproj -- --format free path/to/source.cbl /tmp/source-ast.json
```
