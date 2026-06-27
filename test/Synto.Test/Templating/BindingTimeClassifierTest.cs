using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Test.Templating;

/// <summary>
/// Unit-tests <see cref="BindingTimeClassifier"/> (plan Task 4 / spec §4): the dataflow partition of a
/// <c>[Template]</c> body into quoted / live-value / live-control nodes given its live roots, plus impossible-cut
/// detection. Exercised directly against a parsed body + <see cref="SemanticModel"/> (the classifier is reached
/// via the project's <c>InternalsVisibleTo Synto.Test</c>); this task is analysis-only, so it changes no
/// emission and leaves all snapshot goldens byte-identical.
/// </summary>
public class BindingTimeClassifierTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    /// <summary>Parses one method body and yields its semantic model + the method declaration.</summary>
    private static (SemanticModel Model, MethodDeclarationSyntax Method) Compile(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());

        var compilation = CSharpCompilation.Create("Test",
            [syntaxTree],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(compilation.GetDiagnostics().Where(diag => diag.Severity == DiagnosticSeverity.Error));

        var method = syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        return (compilation.GetSemanticModel(syntaxTree), method);
    }

    private static IParameterSymbol Parameter(SemanticModel model, MethodDeclarationSyntax method, string name)
    {
        var parameter = method.ParameterList.Parameters.Single(p => p.Identifier.Text == name);
        return (IParameterSymbol)model.GetDeclaredSymbol(parameter)!;
    }

    private static ILocalSymbol Local(SemanticModel model, MethodDeclarationSyntax method, string name)
    {
        var declarator = method.Body!.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(d => d.Identifier.Text == name);
        return (ILocalSymbol)model.GetDeclaredSymbol(declarator)!;
    }

    [Fact]
    public void ValueDependingOnRoot_IsUnquote()
    {
        var (model, method) = Compile(
            """
            class C {
                void M(int root) {
                    var x = root + 1;
                    System.Console.WriteLine(x);
                }
            }
            """);

        var roots = new[] { new StagedRoot(Parameter(model, method, "root")) };
        var partition = BindingTimeClassifier.Classify(model, method.Body!, roots);

        var sum = method.Body!.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        Assert.True(partition.IsUnquote(sum));
        Assert.Empty(partition.ImpossibleCuts);
    }

    [Fact]
    public void ForeachOverRoot_IsStagedControl()
    {
        var (model, method) = Compile(
            """
            using System.Collections.Generic;
            class C {
                void M(IReadOnlyList<int> root) {
                    foreach (var c in root)
                        System.Console.WriteLine(c);
                }
            }
            """);

        var roots = new[] { new StagedRoot(Parameter(model, method, "root")) };
        var partition = BindingTimeClassifier.Classify(model, method.Body!, roots);

        var loop = method.Body!.DescendantNodes().OfType<ForEachStatementSyntax>().Single();
        Assert.True(partition.IsStagedControl(loop));
    }

    [Fact]
    public void IndependentNode_IsQuoted()
    {
        var (model, method) = Compile(
            """
            class C {
                void M(int root, int i) {
                    System.Console.WriteLine(i);
                }
            }
            """);

        var roots = new[] { new StagedRoot(Parameter(model, method, "root")) };
        var partition = BindingTimeClassifier.Classify(model, method.Body!, roots);

        var call = method.Body!.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        Assert.Equal(BindingTime.Quoted, partition.Classify(call));
        Assert.False(partition.IsUnquote(call));
        Assert.False(partition.IsStagedControl(call));
    }

    [Fact]
    public void QuoteParam_DrivingControl_StaysQuoted()
    {
        // [Quote] parameters are deliberately NEVER seeded as live roots (unlike [Unquote]), so a control
        // construct whose driver references only a quoted value stays Quoted (a real runtime loop) instead of
        // StagedControl (unrolled) — spec §3. Modeled by classifying with the quoted `count` NOT among the roots;
        // the contrast run (count AS a root, i.e. an [Unquote]) shows the SAME loop WOULD unroll, proving the
        // not-a-root decision is what keeps the loop.
        var (model, method) = Compile(
            """
            class C {
                void M(int count) {
                    int ret = 0;
                    for (int i = 0; i < count; i++) ret++;
                }
            }
            """);

        var loop = method.Body!.DescendantNodes().OfType<ForStatementSyntax>().Single();

        // [Quote]: count is not a root -> the loop stays a runtime construct.
        var quotedPartition = BindingTimeClassifier.Classify(model, method.Body!, System.Array.Empty<StagedRoot>());
        Assert.False(quotedPartition.IsStagedControl(loop));

        // Contrast: were count live (an [Unquote] root), the same loop unrolls (StagedControl).
        var stagedPartition = BindingTimeClassifier.Classify(model, method.Body!, new[] { new StagedRoot(Parameter(model, method, "count")) });
        Assert.True(stagedPartition.IsStagedControl(loop));
    }

    [Fact]
    public void ForeachOverQuotedSource_StaysQuoted()
    {
        var (model, method) = Compile(
            """
            using System.Collections.Generic;
            class C {
                void M(int root, IReadOnlyList<int> source) {
                    foreach (var c in source)
                        System.Console.WriteLine(c);
                }
            }
            """);

        var roots = new[] { new StagedRoot(Parameter(model, method, "root")) };
        var partition = BindingTimeClassifier.Classify(model, method.Body!, roots);

        var loop = method.Body!.DescendantNodes().OfType<ForEachStatementSyntax>().Single();
        Assert.False(partition.IsStagedControl(loop));
        Assert.Equal(BindingTime.Quoted, partition.Classify(loop));
    }

    [Fact]
    public void StagedDependingOnGeneratedWorld_IsImpossibleCut()
    {
        // A bound root `bad` (modelling `var bad = Unquote(generatedWorld + 1);`) whose binding expression depends
        // on the output-world parameter `generatedWorld` (not a live root) cannot be evaluated at factory time.
        var (model, method) = Compile(
            """
            class C {
                void M(int generatedWorld) {
                    var bad = generatedWorld + 1;
                    System.Console.WriteLine(bad);
                }
            }
            """);

        var bindingExpression = method.Body!.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var roots = new[] { new StagedRoot(Local(model, method, "bad"), bindingExpression) };
        var partition = BindingTimeClassifier.Classify(model, method.Body!, roots);

        var cut = Assert.Single(partition.ImpossibleCuts);
        Assert.Same(bindingExpression, cut.Node);
        Assert.Contains("generatedWorld", cut.Reason);
    }
}
