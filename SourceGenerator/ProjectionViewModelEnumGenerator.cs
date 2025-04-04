using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceGenerator;

[Generator]
public class ProjectionViewModelEnumGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var projectionNames = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) =>
                {
                    if (ctx.Node is not ClassDeclarationSyntax classSyntax)
                        return null;

                    if (ctx.SemanticModel.GetDeclaredSymbol(classSyntax) is not INamedTypeSymbol typeSymbol)
                        return null;

                    // Traverse base types to find SingleStreamProjection<T>
                    for (var baseType = typeSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
                    {
                        if (baseType is { ConstructedFrom.Name: "SingleStreamProjection" } singleStreamProjection)
                        {
                            return singleStreamProjection.TypeArguments[0] as INamedTypeSymbol;
                        }
                        if (baseType is { ConstructedFrom.Name: "MultiStreamProjection"} multiStreamProjection)
                        {
                            return multiStreamProjection.TypeArguments[0] as INamedTypeSymbol;
                        }
                    }

                    return null;
                })
            .Where(static t => t is not null)
            .Select(static (t, _) => t!.Name)
            .Collect();
        context.RegisterSourceOutput(projectionNames, (ctx, names) =>
        {
            var code = $$"""
                         // <auto-generated />
                         namespace Skeleton;

                         public enum ProjectionViewModelEnum
                         {
                             {{string.Join("\n    ", names.Distinct().Select(name => $"{name},"))}}
                         }
                         """;

            ctx.AddSource("ProjectionViewModelEnum.g.cs", code);
        });
    }
}