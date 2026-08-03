using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>Configuration extensions for opt-in DuckDB STRUCT mappings.</summary>
public static class DuckDBStructPropertyBuilderExtensions
{
    public static ComplexPropertyBuilder UseStructMapping(this ComplexPropertyBuilder propertyBuilder)
        => UseStructMapping(propertyBuilder, null);

    public static ComplexPropertyBuilder UseStructMapping(
        this ComplexPropertyBuilder propertyBuilder,
        string? structColumnName)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.Metadata.SetAnnotation(DuckDBAnnotationNames.UseStructMapping, true);
        if (structColumnName is not null)
        {
            propertyBuilder.Metadata.SetAnnotation(
                DuckDBAnnotationNames.StructColumnName,
                ValidateName(structColumnName, nameof(structColumnName)));
        }

        return propertyBuilder;
    }

    public static ComplexPropertyBuilder<TComplex> UseStructMapping<TComplex>(
        this ComplexPropertyBuilder<TComplex> propertyBuilder)
        where TComplex : class
        => UseStructMapping(propertyBuilder, null);

    public static ComplexPropertyBuilder<TComplex> UseStructMapping<TComplex>(
        this ComplexPropertyBuilder<TComplex> propertyBuilder,
        string? structColumnName)
        where TComplex : class
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.Metadata.SetAnnotation(DuckDBAnnotationNames.UseStructMapping, true);
        if (structColumnName is not null)
        {
            propertyBuilder.Metadata.SetAnnotation(
                DuckDBAnnotationNames.StructColumnName,
                ValidateName(structColumnName, nameof(structColumnName)));
        }

        return propertyBuilder;
    }

    public static ComplexPropertyBuilder<TComplex> HasStructFieldName<TComplex>(
        this ComplexPropertyBuilder<TComplex> propertyBuilder,
        string fieldName)
        where TComplex : class
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.Metadata.SetAnnotation(
            DuckDBAnnotationNames.StructFieldName,
            ValidateName(fieldName, nameof(fieldName)));
        return propertyBuilder;
    }

    public static ComplexPropertyBuilder HasStructFieldName(
        this ComplexPropertyBuilder propertyBuilder,
        string fieldName)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.Metadata.SetAnnotation(
            DuckDBAnnotationNames.StructFieldName,
            ValidateName(fieldName, nameof(fieldName)));
        return propertyBuilder;
    }

    public static ComplexTypePropertyBuilder<TProperty> HasStructFieldName<TProperty>(
        this ComplexTypePropertyBuilder<TProperty> propertyBuilder,
        string fieldName)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        fieldName = ValidateName(fieldName, nameof(fieldName));
        propertyBuilder.HasAnnotation(
            DuckDBAnnotationNames.StructFieldName,
            fieldName);
        if (propertyBuilder.Metadata.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
            is DuckDBStructFieldInfo field)
        {
            propertyBuilder.HasAnnotation(
                DuckDBAnnotationNames.StructField,
                new DuckDBStructFieldInfo(
                    field.StructColumnName,
                    field.NestedFieldNames.ToArray(),
                    fieldName));
        }

        return propertyBuilder;
    }

    /// <summary>
    ///     Configures the physical DuckDB STRUCT leaf name for an entity scalar property.
    /// </summary>
    /// <remarks>
    ///     Use this after <see cref="HasStructField{TProperty}(PropertyBuilder{TProperty}, string, string[])" />
    ///     when the physical leaf name differs from the EF property name.
    /// </remarks>
    public static PropertyBuilder<TProperty> HasStructFieldName<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string fieldName)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        fieldName = ValidateName(fieldName, nameof(fieldName));
        propertyBuilder.HasAnnotation(
            DuckDBAnnotationNames.StructFieldName,
            fieldName);
        if (propertyBuilder.Metadata.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
            is DuckDBStructFieldInfo field)
        {
            propertyBuilder.HasAnnotation(
                DuckDBAnnotationNames.StructField,
                new DuckDBStructFieldInfo(
                    field.StructColumnName,
                    field.NestedFieldNames.ToArray(),
                    fieldName));
        }

        return propertyBuilder;
    }

    /// <summary>
    ///     Maps an entity scalar property to a field inside a physical DuckDB STRUCT column.
    /// </summary>
    /// <param name="propertyBuilder">The scalar property builder.</param>
    /// <param name="structColumnName">The physical STRUCT root column.</param>
    /// <param name="nestedFieldNames">The physical path from the root to the leaf's parent.</param>
    /// <remarks>
    ///     Configure the physical leaf with <see cref="HasStructFieldName{TProperty}(PropertyBuilder{TProperty}, string)" />.
    ///     A mapped scalar can be an EF foreign-key property when both relationship ends are query-only
    ///     DuckDB file sources. The normal EF relationship APIs continue to define the relationship.
    /// </remarks>
    public static PropertyBuilder<TProperty> HasStructField<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string structColumnName,
        params string[] nestedFieldNames)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.HasAnnotation(
            DuckDBAnnotationNames.StructField,
            new DuckDBStructFieldInfo(
                ValidateName(structColumnName, nameof(structColumnName)),
                nestedFieldNames,
                propertyBuilder.Metadata.FindAnnotation(DuckDBAnnotationNames.StructFieldName)?.Value as string));
        return propertyBuilder;
    }

    public static ComplexTypePropertyBuilder<TProperty> HasStructField<TProperty>(
        this ComplexTypePropertyBuilder<TProperty> propertyBuilder,
        string structColumnName,
        params string[] nestedFieldNames)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.HasAnnotation(
            DuckDBAnnotationNames.StructField,
            new DuckDBStructFieldInfo(
                ValidateName(structColumnName, nameof(structColumnName)),
                nestedFieldNames,
                propertyBuilder.Metadata.FindAnnotation(DuckDBAnnotationNames.StructFieldName)?.Value as string));
        return propertyBuilder;
    }

    internal static IReadOnlyList<string> GetMemberPath(LambdaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var members = new List<string>();
        Expression current = UnwrapConvert(expression.Body);
        while (current is MemberExpression member && member.Expression is not null)
        {
            members.Add(member.Member.Name);
            current = UnwrapConvert(member.Expression);
        }

        if (!ReferenceEquals(current, expression.Parameters[0]) || members.Count < 2)
        {
            throw new ArgumentException(
                "The STRUCT field selector must be a nested member path rooted at the entity, "
                + "for example 'e => e.Relationship.ParentId'.",
                nameof(expression));
        }

        members.Reverse();
        return members;
    }

    private static Expression UnwrapConvert(Expression expression)
        => expression is UnaryExpression
        {
            NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
            Operand: var operand,
        }
            ? operand
            : expression;

    private static string ValidateName(string name, string parameterName)
        => string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("STRUCT names must be non-empty.", parameterName)
            : name;
}