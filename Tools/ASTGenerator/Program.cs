using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using TypeCobol;
using TypeCobol.Compiler;
using TypeCobol.Compiler.CodeElements;
using TypeCobol.Compiler.Diagnostics;
using TypeCobol.Compiler.Directives;
using TypeCobol.Compiler.Nodes;

namespace ASTGenerator;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static int Main(string[] args)
    {
        var arguments = CommandLineArguments.Parse(args);
        if (arguments.ShowHelp)
        {
            Console.Error.WriteLine(CommandLineArguments.Usage);
            return arguments.ParseError == null ? 0 : 1;
        }

        if (arguments.ParseError != null)
        {
            Console.Error.WriteLine(arguments.ParseError);
            Console.Error.WriteLine(CommandLineArguments.Usage);
            return 1;
        }

        try
        {
            var inputPath = Path.GetFullPath(arguments.InputPath);
            var options = new TypeCobolOptions
            {
                ExecToStep = ExecutionStep.AST,
                IsCobolLanguage = arguments.IsCobol
            };

            var parser = new TypeCobol.Parser();
            parser.Init(inputPath, false, options, arguments.DocumentFormat, arguments.CopyFolders);
            parser.Parse(inputPath);

            var root = parser.Results.ProgramClassDocumentSnapshot?.Root
                ?? parser.Results.TemporaryProgramClassDocumentSnapshot?.Root;
            var dump = new AstDump(
                inputPath,
                root == null ? null : AstNodeDto.FromNode(root),
                parser.Results.AllDiagnostics().Select(DiagnosticDto.FromDiagnostic).ToList(),
                parser.MissingCopys?.ToList() ?? new List<string>());

            var json = JsonSerializer.Serialize(dump, JsonOptions);
            WriteOutput(arguments.OutputPath, json);

            return dump.Diagnostics.Any(d => d.Severity == nameof(Severity.Error)) ? 2 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void WriteOutput(string outputPath, string json)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            Console.Out.WriteLine(json);
            return;
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, json + Environment.NewLine);
    }
}

internal sealed record AstDump(
    string InputPath,
    AstNodeDto Root,
    List<DiagnosticDto> Diagnostics,
    List<string> MissingCopies);

internal sealed class AstNodeDto
{
    public string Kind { get; init; }
    public string Name { get; init; }
    public string Id { get; init; }
    public string Uri { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public string CodeElementKind { get; init; }
    public string StatementKind { get; init; }
    public List<AstNodeDto> Children { get; init; } = new();

    public static AstNodeDto FromNode(Node node)
    {
        var visited = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        return FromNode(node, visited);
    }

    private static AstNodeDto FromNode(Node node, HashSet<Node> visited)
    {
        if (!visited.Add(node))
        {
            return new AstNodeDto
            {
                Kind = node.GetType().Name,
                Name = SafeString(() => node.Name),
                Id = SafeString(() => node.ID),
                Uri = SafeString(() => node.URI)
            };
        }

        var codeElement = node.CodeElement;
        var dto = new AstNodeDto
        {
            Kind = node.GetType().Name,
            Name = SafeString(() => node.Name),
            Id = SafeString(() => node.ID),
            Uri = SafeString(() => node.URI),
            Line = GetIntProperty(codeElement, "Line") ?? GetIntProperty(codeElement, "LineStart"),
            Column = GetIntProperty(codeElement, "Column") ?? GetIntProperty(codeElement, "ColumnStart"),
            CodeElementKind = codeElement?.GetType().Name,
            StatementKind = GetStatementKind(codeElement)
        };

        foreach (var child in node.Children)
        {
            dto.Children.Add(FromNode(child, visited));
        }

        return dto;
    }

    private static string SafeString(Func<string> valueFactory)
    {
        try
        {
            var value = valueFactory();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static int? GetIntProperty(object instance, string propertyName)
    {
        if (instance == null)
        {
            return null;
        }

        try
        {
            var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(instance) switch
            {
                int value when value > 0 => value,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetStatementKind(CodeElement codeElement)
    {
        if (codeElement == null)
        {
            return null;
        }

        foreach (var propertyName in new[] { "StatementType", "CodeElementType" })
        {
            var value = GetPublicPropertyValue(codeElement, propertyName);
            if (value != null)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static object GetPublicPropertyValue(object instance, string propertyName)
    {
        try
        {
            return instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record DiagnosticDto(
    int Code,
    string Severity,
    string Category,
    int LineStart,
    int ColumnStart,
    int LineEnd,
    int ColumnEnd,
    string Message)
{
    public static DiagnosticDto FromDiagnostic(Diagnostic diagnostic)
    {
        return new DiagnosticDto(
            diagnostic.Info.Code,
            diagnostic.Info.Severity.ToString(),
            diagnostic.Info.Category.ToString(),
            diagnostic.LineStart,
            diagnostic.ColumnStart,
            diagnostic.LineEnd,
            diagnostic.ColumnEnd,
            diagnostic.Message);
    }
}

internal sealed class CommandLineArguments
{
    public const string Usage =
        "Usage: dotnet run --project Tools/ASTGenerator/ASTGenerator.csproj -- [--cobol|--typecobol] [--format reference|free] [--copies <dir>] <input.cbl> [output.json]";

    public string InputPath { get; private init; }
    public string OutputPath { get; private init; }
    public bool IsCobol { get; private init; } = true;
    public DocumentFormat DocumentFormat { get; private init; } = DocumentFormat.RDZReferenceFormat;
    public List<string> CopyFolders { get; } = new();
    public bool ShowHelp { get; private init; }
    public string ParseError { get; private init; }

    public static CommandLineArguments Parse(string[] args)
    {
        var result = new CommandLineArguments();
        var positional = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    return result.WithValues(showHelp: true);
                case "--cobol":
                    result = result.WithValues(isCobol: true);
                    break;
                case "--typecobol":
                    result = result.WithValues(isCobol: false);
                    break;
                case "--format":
                    if (++i >= args.Length)
                    {
                        return result.WithValues(parseError: "--format requires 'reference' or 'free'.");
                    }

                    result = args[i].ToLowerInvariant() switch
                    {
                        "reference" => result.WithValues(documentFormat: DocumentFormat.RDZReferenceFormat),
                        "free" => result.WithValues(documentFormat: DocumentFormat.FreeUTF8Format),
                        _ => result.WithValues(parseError: "--format requires 'reference' or 'free'.")
                    };
                    break;
                case "--copies":
                    if (++i >= args.Length)
                    {
                        return result.WithValues(parseError: "--copies requires a directory path.");
                    }

                    result.CopyFolders.Add(Path.GetFullPath(args[i]));
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        return result.WithValues(parseError: $"Unknown option: {arg}");
                    }

                    positional.Add(arg);
                    break;
            }
        }

        if (positional.Count is < 1 or > 2)
        {
            return result.WithValues(parseError: "Expected an input path and optional output path.");
        }

        return result.WithValues(inputPath: positional[0], outputPath: positional.Count == 2 ? positional[1] : null);
    }

    private CommandLineArguments WithValues(
        string inputPath = null,
        string outputPath = null,
        bool? isCobol = null,
        DocumentFormat documentFormat = null,
        bool? showHelp = null,
        string parseError = null)
    {
        var clone = new CommandLineArguments
        {
            InputPath = inputPath ?? InputPath,
            OutputPath = outputPath ?? OutputPath,
            IsCobol = isCobol ?? IsCobol,
            DocumentFormat = documentFormat ?? DocumentFormat,
            ShowHelp = showHelp ?? ShowHelp,
            ParseError = parseError ?? ParseError
        };

        clone.CopyFolders.AddRange(CopyFolders);
        return clone;
    }
}

internal sealed class ReferenceEqualityComparer : IEqualityComparer<Node>
{
    public static readonly ReferenceEqualityComparer Instance = new();

    public bool Equals(Node x, Node y) => ReferenceEquals(x, y);

    public int GetHashCode(Node obj) => RuntimeHelpers.GetHashCode(obj);
}
