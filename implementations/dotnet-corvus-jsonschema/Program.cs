using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Corvus.Json;
using Corvus.Json.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyModel;

ICommandSource cmdSource = args.Length == 0 ? new ConsoleCommandSource() : new FileCommandSource(args[0]);

bool started = false;

const string GlobalUsingStatements = @"
// <auto-generated/>

global using global::System;
global using global::System.Collections.Generic;
global using global::System.IO;
global using global::System.Linq;
global using global::System.Net.Http;
global using global::System.Threading;
global using global::System.Threading.Tasks;";

var unsupportedTests = new Dictionary<(string, string), string> {
    [("schema that uses custom metaschema with with no validation vocabulary", "no validation: valid number")] =
        "We do not support optional vocabularies",
    [("schema that uses custom metaschema with with no validation vocabulary",
      "no validation: invalid number, but it still validates")] = "We do not support optional vocabularies",
    [("ignore unrecognized optional vocabulary", "string value")] = "We do not support optional vocabularies",
    [("ignore unrecognized optional vocabulary", "number value")] = "We do not support optional vocabularies",
};

var builders = new Dictionary<string, (Func<IJsonSchemaBuilder>, bool)> {
    ["https://json-schema.org/draft/2020-12/schema"] =
        (() => new Corvus.Json.CodeGeneration.Draft202012.JsonSchemaBuilder(CreateTypeBuilder()), false),
    ["https://json-schema.org/draft/2019-09/schema"] =
        (() => new Corvus.Json.CodeGeneration.Draft201909.JsonSchemaBuilder(CreateTypeBuilder()), true),
    ["http://json-schema.org/draft-07/schema#"] =
        (() => new Corvus.Json.CodeGeneration.Draft7.JsonSchemaBuilder(CreateTypeBuilder()), true),
    ["http://json-schema.org/draft-06/schema#"] =
        (() => new Corvus.Json.CodeGeneration.Draft6.JsonSchemaBuilder(CreateTypeBuilder()), true),
    ["http://json-schema.org/draft-04/schema#"] =
        (() => new Corvus.Json.CodeGeneration.Draft4.JsonSchemaBuilder(CreateTypeBuilder()), true),
};

IJsonSchemaBuilder? currentBuilder = null;
bool validateFormat = false;

AssemblyLoadContext assemblyLoadContext = new TestAssemblyLoadContext();

static JsonSchemaTypeBuilder CreateTypeBuilder()
{
    var builder = new Corvus.Json.CodeGeneration.JsonSchemaTypeBuilder(new TestDocumentResolver());

    builder.AddDocument("http://json-schema.org/draft-04/schema",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft4/schema.json")));

    builder.AddDocument("http://json-schema.org/draft-06/schema",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft6/schema.json")));

    builder.AddDocument("http://json-schema.org/draft-07/schema",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft7/schema.json")));

    builder.AddDocument("https://json-schema.org/draft/2019-09/schema",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2019-09/schema.json")));
    builder.AddDocument("https://json-schema.org/draft/2019-09/meta/applicator",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2019-09/meta/applicator.json")));
    builder.AddDocument("https://json-schema.org/draft/2019-09/meta/content",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2019-09/meta/content.json")));
    builder.AddDocument("https://json-schema.org/draft/2019-09/meta/core",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2019-09/meta/core.json")));
    builder.AddDocument("https://json-schema.org/draft/2019-09/meta/format",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2019-09/meta/format.json")));
    builder.AddDocument("https://json-schema.org/draft/2019-09/meta/hyper-schema",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2019-09/meta/hyper-schema.json")));
    builder.AddDocument("https://json-schema.org/draft/2019-09/meta/meta-data",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2019-09/meta/meta-data.json")));
    builder.AddDocument("https://json-schema.org/draft/2019-09/meta/validation",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2019-09/meta/validation.json")));

    builder.AddDocument("https://json-schema.org/draft/2020-12/schema",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2020-12/schema.json")));
    builder.AddDocument("https://json-schema.org/draft/2020-12/meta/applicator",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2020-12/meta/applicator.json")));
    builder.AddDocument("https://json-schema.org/draft/2020-12/meta/content",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2020-12/meta/content.json")));
    builder.AddDocument("https://json-schema.org/draft/2020-12/meta/core",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2020-12/meta/core.json")));
    builder.AddDocument("https://json-schema.org/draft/2020-12/meta/format-annotation",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2020-12/meta/format-annotation.json")));
    builder.AddDocument("https://json-schema.org/draft/2020-12/meta/format-assertion",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2020-12/meta/format-assertion.json")));
    builder.AddDocument("https://json-schema.org/draft/2020-12/meta/hyper-schema",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2020-12/meta/hyper-schema.json")));
    builder.AddDocument("https://json-schema.org/draft/2020-12/meta/meta-data",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2020-12/meta/meta-data.json")));
    builder.AddDocument("https://json-schema.org/draft/2020-12/meta/unevaluated",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2020-12/meta/unevaluated.json")));
    builder.AddDocument("https://json-schema.org/draft/2020-12/meta/validation",
                        JsonDocument.Parse(File.ReadAllText("./metaschema/draft2020-12/meta/validation.json")));

    return builder;
}

while (cmdSource.GetNextCommand() is {} line && line != string.Empty)
{
    var root = JsonNode.Parse(line);

    if (root is null)
    {
        continue;
    }

    string? cmd = root["cmd"]?.GetValue<string>() ?? throw new MissingCommand(root);
    switch (cmd)
    {
        case "start":
            JsonNode? version = root["version"] ?? throw new MissingVersion(cmd);
            if (version.GetValue<int>() != 1)
            {
                throw new UnknownVersion(version);
            }

            started = true;
            var startResult = new System.Text.Json.Nodes.JsonObject {
                ["version"] = 1,
                ["implementation"] =
                    new System.Text.Json.Nodes.JsonObject {
                        ["language"] = "dotnet",
                        ["name"] = "Corvus.JsonSchema",
                        ["version"] = GetLibVersion(),
                        ["homepage"] = "https://github.com/corvus-dotnet/corvus.jsonschema",
                        ["documentation"] = "https://github.com/corvus-dotnet/Corvus.JsonSchema/blob/main/README.md",
                        ["issues"] = "https://github.com/corvus-dotnet/corvus.jsonschema/issues",
                        ["source"] = "https://github.com/corvus-dotnet/corvus.jsonschema",

                        ["dialects"] =
                            new System.Text.Json.Nodes.JsonArray {
                                "https://json-schema.org/draft/2020-12/schema",
                                "https://json-schema.org/draft/2019-09/schema",
                                "http://json-schema.org/draft-07/schema#",
                                "http://json-schema.org/draft-06/schema#",
                                "http://json-schema.org/draft-04/schema#",
                            },
                    },
            };
            Console.WriteLine(startResult.ToJsonString());
            break;

        case "dialect":
            if (!started)
            {
                throw new NotStarted();
            }

            string? dialect = root["dialect"]?.GetValue<string>() ?? throw new MissingDialect(root);
            (Func<IJsonSchemaBuilder>? builderFactory, validateFormat) = builders[dialect];
            currentBuilder = builderFactory();
            var dialectResult = new System.Text.Json.Nodes.JsonObject {
                ["ok"] = true,
            };

            Console.WriteLine(dialectResult.ToJsonString());
            break;

        case "run":
            if (!started)
            {
                throw new NotStarted();
            }

            JsonNode? testCase = root["case"] ?? throw new MissingCase(root);
            string? nullableTestCaseDescription = testCase["description"]?.GetValue<string>();

            if (nullableTestCaseDescription is not string testCaseDescription)
            {
                throw new MissingTestCaseDescription(testCase);
            }

            string? schemaText = testCase["schema"]?.ToJsonString() ?? throw new MissingSchema(testCase);
            JsonNode? registry = testCase["registry"];

            if (currentBuilder is null)
            {
                throw new CannotRunBeforeDialectIsChosen();
            }

            if (registry is not null)
            {
                foreach ((string key, JsonNode? value) in registry.AsObject())
                {
                    if (value is JsonNode v)
                    {
                        currentBuilder.AddDocument(key, JsonDocument.Parse(value.ToJsonString()));
                    }
                }
            }

            string fakeURI = $"https://example.com/bowtie-sent-schema-{root["seq"]?.ToJsonString()}.json";
            Type schemaType = SynchronouslyGenerateTypeForVirtualFile(assemblyLoadContext, currentBuilder, schemaText,
                                                                      fakeURI, validateFormat);

            System.Text.Json.Nodes.JsonArray? tests = testCase["tests"]?.AsArray() ?? throw new MissingTests(testCase);
            string testDescription = string.Empty;

            try
            {
                var results = new System.Text.Json.Nodes.JsonArray();

                foreach (JsonNode? test in tests)
                {
                    if (test is null)
                    {
                        throw new MissingTest(tests);
                    }

                    string? nullableTestDescription =
                        test["description"]?.GetValue<string>() ?? throw new MissingTestDescription(test);
                    testDescription = nullableTestDescription;

                    string? testInstance = test["instance"]?.ToJsonString() ?? "null";
                    bool validationResult = ValidateType(schemaType, testInstance);
                    results.Add(new System.Text.Json.Nodes.JsonObject { ["valid"] = validationResult });
                }

                var runResult = new System.Text.Json.Nodes.JsonObject {
                    ["seq"] = root["seq"]?.DeepClone(),
                    ["results"] = results,
                };

                Console.WriteLine(runResult.ToJsonString());
            }
            catch (Exception)
                when (unsupportedTests.TryGetValue((testCaseDescription, testDescription), out string? message))
            {
                var skipResult = new System.Text.Json.Nodes.JsonObject { ["seq"] = root["seq"]?.DeepClone(),
                                                                         ["skipped"] = true, ["message"] = message };
                Console.WriteLine(skipResult.ToJsonString());
            }
            catch (Exception e)
            {
                var errorResult = new System.Text.Json.Nodes.JsonObject {
                    ["seq"] = root["seq"]?.DeepClone(),
                    ["errored"] = true,
                    ["context"] =
                        new System.Text.Json.Nodes.JsonObject {
                            ["message"] = e.ToString(),
                            ["traceback"] = Environment.StackTrace,
                        },
                };
                Console.WriteLine(errorResult.ToJsonString());
            }

            break;

        case "stop":
            if (!started)
            {
                throw new NotStarted();
            }

            Environment.Exit(0);
            break;

        case null:
            throw new UnknownCommand("Missing command!");

        default:
            throw new UnknownCommand(cmd);
    }
}

static Type SynchronouslyGenerateTypeForVirtualFile(AssemblyLoadContext assemblyLoadContext, IJsonSchemaBuilder builder,
                                                    string schema, string virtualFileName, bool validateFormat)
{
    builder.AddDocument($"{virtualFileName}", JsonDocument.Parse(schema));

    (string rootType, ImmutableDictionary<JsonReference, TypeAndCode> generatedTypes) = builder.SafeBuildTypesFor(
        new JsonReference(virtualFileName), "BowtieTest.Model", rebase: true, validateFormat: validateFormat);
    return CompileGeneratedType(assemblyLoadContext, rootType, generatedTypes);
}

static Type CompileGeneratedType(AssemblyLoadContext assemblyLoadContext, string rootType,
                                 ImmutableDictionary<JsonReference, TypeAndCode> generatedTypes)
{
    bool isCorvusType = rootType.StartsWith("Corvus.");

    (IEnumerable<MetadataReference> references, IEnumerable<string?> defines) = BuildMetadataReferencesAndDefines();

    IEnumerable<SyntaxTree> syntaxTrees = ParseSyntaxTrees(generatedTypes, defines);

    // We are happy with the defaults (debug etc.)
    var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
    var compilation =
        CSharpCompilation.Create($"Bowtie.GeneratedTypes_{Guid.NewGuid()}", syntaxTrees, references, options);
    using MemoryStream outputStream = new();
    EmitResult result = compilation.Emit(outputStream);

    if (!result.Success)
    {
        throw new Exception("Unable to compile generated code\r\n" + BuildCompilationErrors(result));
    }

    outputStream.Flush();
    outputStream.Position = 0;

    Assembly generatedAssembly = assemblyLoadContext.LoadFromStream(outputStream);

    if (isCorvusType)
    {
        return AssemblyLoadContext.Default.Assemblies.Single(a => a.GetName().Name == "Corvus.Json.ExtendedTypes")
            .ExportedTypes.Single(t => t.FullName == rootType);
    }

    return generatedAssembly.ExportedTypes.Single(t => t.FullName == rootType);
}

static string BuildCompilationErrors(EmitResult result)
{
    var builder = new StringBuilder();
    foreach (Diagnostic diagnostic in result.Diagnostics)
    {
        builder.AppendLine(diagnostic.ToString());
    }

    return builder.ToString();
}

static (IEnumerable<MetadataReference> MetadataReferences, IEnumerable<string?> Defines)
    BuildMetadataReferencesAndDefines()
{
    DependencyContext? ctx = DependencyContext.Default ?? DependencyContext.Load(Assembly.GetExecutingAssembly());
    return ctx is null
        ? throw new InvalidOperationException("Unable to find compilation context.")
        : ((IEnumerable<MetadataReference> MetadataReferences, IEnumerable<string?> Defines))(from l in ctx.CompileLibraries
                                                                                              from r in l.ResolveReferencePaths()
                                                                                              select MetadataReference.CreateFromFile(r),
           ctx.CompilationOptions.Defines.AsEnumerable());
}

static IEnumerable<SyntaxTree> ParseSyntaxTrees(ImmutableDictionary<JsonReference, TypeAndCode> generatedTypes,
                                                IEnumerable<string?> defines)
{
    CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)
                                          .WithPreprocessorSymbols(defines.Where(s => s is not null).Cast<string>());
    yield return CSharpSyntaxTree.ParseText(GlobalUsingStatements, options: parseOptions,
                                            path: "GlobalUsingStatements.cs");

    foreach (KeyValuePair<JsonReference, TypeAndCode> type in generatedTypes)
    {
        foreach (CodeAndFilename codeAndFilename in type.Value.Code)
        {
            yield return CSharpSyntaxTree.ParseText(codeAndFilename.Code, options: parseOptions,
                                                    path: codeAndFilename.Filename);
        }
    }
}

static bool ValidateType(Type schemaType, string testInstance)
{
    using var document = JsonDocument.Parse(testInstance);
    IJsonValue instance = CreateInstance(schemaType, document.RootElement);
    return instance.Validate(ValidationContext.ValidContext, ValidationLevel.Flag).IsValid;
}

static IJsonValue CreateInstance(Type type, JsonElement data)
{
    ConstructorInfo? constructor =
        type.GetConstructors().SingleOrDefault(
            c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType.Name.StartsWith("JsonElement")) ??
        throw new InvalidOperationException(
            $"Unable to find the public JsonElement constructor on type '{type.FullName}'");

    return (IJsonValue)constructor.Invoke([data]);
}

static string GetLibVersion()
{
    AssemblyInformationalVersionAttribute? attribute =
        typeof(Corvus.Json.CodeGeneration.JsonSchemaTypeBuilder)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
#pragma warning disable SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
    return Regex.Match(attribute!.InformationalVersion, @"\d+\.\d+\.\d+").Value;
#pragma warning restore SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
}

internal interface ICommandSource
{
    string? GetNextCommand();
}

internal class TestDocumentResolver : IDocumentResolver
{
    private readonly Dictionary<string, JsonDocument> documents = [];

    public bool AddDocument(string uri, JsonDocument document)
    {
        return this.documents.TryAdd(uri, document);
    }

    public void Dispose()
    {
        List<Exception> exceptions = [];

        foreach (JsonDocument document in this.documents.Values)
        {
            try
            {
                document.Dispose();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        this.documents.Clear();

        if (exceptions.Count > 0)
        {
            throw new AggregateException(exceptions);
        }
    }

    public void Reset()
    {
        this.Dispose();
    }

    public ValueTask<JsonElement?> TryResolve(JsonReference reference)
    {
        string uri = reference.Uri.ToString();

        if (this.documents.TryGetValue(uri, out JsonDocument? result))
        {
            if (JsonPointerUtilities.TryResolvePointer(result, reference.Fragment, out JsonElement? element))
            {
                return ValueTask.FromResult(element);
            }

            return ValueTask.FromResult < JsonElement ? > (default);
        }

        return ValueTask.FromResult < JsonElement ? > (default);
    }
}

internal class MissingCommand
(JsonNode root) : Exception
{
    public JsonNode Root { get; } = root;
}

internal class MissingTest
(JsonNode tests) : Exception
{
    public JsonNode Tests { get; } = tests;
}

internal class MissingCase
(JsonNode root) : Exception
{
    public JsonNode Root { get; } = root;
}

internal class MissingSchema
(JsonNode testCase) : Exception
{
    public JsonNode TestCase { get; } = testCase;
}

internal class MissingTestDescription
(JsonNode testInstance) : Exception
{
    public JsonNode TestInstance { get; } = testInstance;
}

internal class MissingDialect
(JsonNode root) : Exception
{
    public JsonNode Root { get; } = root;
}

internal class MissingTestCaseDescription
(JsonNode testCase) : Exception
{
    public JsonNode TestCase { get; } = testCase;
}

internal class MissingTests
(JsonNode testCase) : Exception
{
    public JsonNode TestCase { get; } = testCase;
}

internal class UnknownCommand
(string? message) : Exception(message) { }

internal class MissingVersion
(JsonNode command) : Exception
{
    public JsonNode Command { get; } = command;
}

internal class UnknownVersion
(JsonNode version) : Exception
{
    public JsonNode Version { get; } = version;
}

internal class NotStarted : Exception;

internal class CannotRunBeforeDialectIsChosen : Exception;

internal class ConsoleCommandSource : ICommandSource
{
    public string? GetNextCommand()
    {
        return Console.ReadLine();
    }
}

internal class FileCommandSource
(string fileName) : ICommandSource
{
    private readonly string[] fileContents = File.ReadAllLines(fileName);
    private int line;

    public string ? GetNextCommand()
    {
        if (this.line < this.fileContents.Length)
        {
            return this.fileContents[this.line++];
        }

        return null;
    }
}

internal class TestAssemblyLoadContext : AssemblyLoadContext
{
    public TestAssemblyLoadContext() : base($"TestAssemblyLoadContext_{Guid.NewGuid():N}", isCollectible: true) { }
}
