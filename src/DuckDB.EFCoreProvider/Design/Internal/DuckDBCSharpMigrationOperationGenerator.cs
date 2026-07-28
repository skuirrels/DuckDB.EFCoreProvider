using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DuckDB.EFCoreProvider.Design.Internal;

/// <summary>
///     Renders STRUCT migration annotations as typed C# instead of opaque unknown literals.
/// </summary>
internal sealed class DuckDBCSharpMigrationOperationGenerator(
    CSharpMigrationOperationGeneratorDependencies dependencies)
    : CSharpMigrationOperationGenerator(dependencies)
{
    protected override void Annotations(
        IEnumerable<Annotation> annotations,
        IndentedStringBuilder builder)
        => GenerateAnnotations(annotations, builder, old: false);

    protected override void OldAnnotations(
        IEnumerable<Annotation> annotations,
        IndentedStringBuilder builder)
        => GenerateAnnotations(annotations, builder, old: true);

    private void GenerateAnnotations(
        IEnumerable<Annotation> annotations,
        IndentedStringBuilder builder,
        bool old)
    {
        var code = Dependencies.CSharpHelper;
        foreach (var annotation in annotations)
        {
            builder
                .AppendLine()
                .Append(old ? ".OldAnnotation(" : ".Annotation(")
                .Append(code.Literal(annotation.Name))
                .Append(", ")
                .Append(annotation.Value is DuckDBStructFieldInfo field
                    ? GenerateFieldLiteral(field)
                    : code.UnknownLiteral(annotation.Value))
                .Append(")");
        }
    }

    private string GenerateFieldLiteral(DuckDBStructFieldInfo field)
    {
        var code = Dependencies.CSharpHelper;
        var type = code.Reference(typeof(DuckDBStructFieldInfo), fullName: true);
        var nestedFields = code.Literal(field.NestedFieldNames.ToArray());
        var leaf = field.LeafFieldName is null ? null : code.Literal(field.LeafFieldName);

        return leaf is null
            ? $"new {type}({code.Literal(field.StructColumnName)}, {nestedFields})"
            : $"new {type}({code.Literal(field.StructColumnName)}, {nestedFields}, {leaf})";
    }
}