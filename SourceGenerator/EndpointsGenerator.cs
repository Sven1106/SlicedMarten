using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text;

namespace SourceGenerator;

[Generator]
public class EndpointsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider.CreateSyntaxProvider(
            (node, _) => node is ClassDeclarationSyntax t && t.BaseList?.Types.Any(x => x.Type.ToString() == "IEndpoint") == true,
            (syntaxContext, _) => (ClassDeclarationSyntax)syntaxContext.Node
        ).Where(x => x is not null);

        var compilation = context.CompilationProvider
            .Combine(provider.Collect());

        context.RegisterSourceOutput(compilation, Execute);
    }

    private void Execute(SourceProductionContext context, (Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes) tuple)
    {
        var (compilation, classes) = tuple;

        var prefixCode = """
                         // <auto-generated />
                         using Skeleton.Endpoints;

                         namespace Skeleton;

                         public static class EndpointsExtension
                         {
                              public static IEndpointRouteBuilder MapAllEndpoints(this IEndpointRouteBuilder endpoints)
                              {
                         """;

        var suffixCode = """
                                  return endpoints;
                              }
                         }
                         """;

        StringBuilder codeBuilder = new StringBuilder();
        codeBuilder.AppendLine(prefixCode);

        foreach (var syntax in classes)
        {
            var symbol = compilation.GetSemanticModel(syntax.SyntaxTree)
                .GetDeclaredSymbol(syntax) as INamedTypeSymbol;

            codeBuilder.AppendLine($"         {symbol!.Name}.MapEndpoints(endpoints.MapGroup(\"\").WithTags(\"{symbol!.Name.Replace("Endpoints", "")}\"));");
        }

        codeBuilder.AppendLine(suffixCode);

        context.AddSource("EndpointsExtension.g.cs", codeBuilder.ToString());
    }
}