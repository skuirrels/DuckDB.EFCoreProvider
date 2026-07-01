using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

public partial class DuckDBQueryableMethodTranslatingExpressionVisitor
{

    protected override ShapedQueryExpression? TransformJsonQueryToTable(JsonQueryExpression jsonQueryExpression)
    {
        var structuralType = jsonQueryExpression.StructuralType;
        var textTypeMapping = _typeMappingSource.FindMapping(typeof(string));

        var lastNamedPathSegment = jsonQueryExpression.Path.LastOrDefault(ps => ps.PropertyName is not null);
        var tableAlias = _sqlAliasManager.GenerateTableAlias(lastNamedPathSegment.PropertyName ?? jsonQueryExpression.JsonColumn.Name);

        var jsonEachExpression = new DuckDBJsonEachExpression(tableAlias, jsonQueryExpression.JsonColumn, jsonQueryExpression.Path);

#pragma warning disable EF1001 // Internal EF Core API usage.
        var selectExpression = CreateSelect(
            jsonQueryExpression,
            jsonEachExpression,
            JsonEachKeyColumnName,
            typeof(int),
            _typeMappingSource.FindMapping(typeof(int))!);
#pragma warning restore EF1001 // Internal EF Core API usage.

        selectExpression.AppendOrdering(
            new OrderingExpression(
                selectExpression.CreateColumnExpression(
                    jsonEachExpression,
                    JsonEachKeyColumnName,
                    typeof(int),
                    typeMapping: _typeMappingSource.FindMapping(typeof(int)),
                    columnNullable: false),
                ascending: true));

        var propertyJsonScalarExpression = new Dictionary<ProjectionMember, Expression>();

        // json_each exposes each element value as a string column; per-property nullability is applied as the
        // individual properties are projected out of it below.
        var jsonColumn = selectExpression.CreateColumnExpression(
            jsonEachExpression, JsonEachValueColumnName, typeof(string), _typeMappingSource.FindMapping(typeof(string)));

        foreach (var property in structuralType.GetPropertiesInHierarchy())
        {
            if (property.GetJsonPropertyName() is { } jsonPropertyName)
            {
                var projectionMember = new ProjectionMember().Append(new FakeMemberInfo(jsonPropertyName));

                propertyJsonScalarExpression[projectionMember] = new JsonScalarExpression(
                    jsonColumn,
                    [new PathSegment(property.GetJsonPropertyName()!)],
                    property.ClrType.UnwrapNullableType(),
                    property.GetRelationalTypeMapping(),
                    property.IsNullable);
            }
        }

        if (structuralType is IEntityType entityType)
        {
            foreach (var navigation in entityType.GetNavigationsInHierarchy()
                         .Where(n => n.ForeignKey.IsOwnership
                             && n.TargetEntityType.IsMappedToJson()
                             && n.ForeignKey.PrincipalToDependent == n))
            {
                var jsonNavigationName = navigation.TargetEntityType.GetJsonPropertyName();
                Debug.Assert(jsonNavigationName is not null, "Invalid navigation found on JSON-mapped entity");

                var projectionMember = new ProjectionMember().Append(new FakeMemberInfo(jsonNavigationName));

                propertyJsonScalarExpression[projectionMember] = new JsonScalarExpression(
                    jsonColumn,
                    [new PathSegment(jsonNavigationName)],
                    typeof(string),
                    textTypeMapping,
                    !navigation.ForeignKey.IsRequiredDependent);
            }
        }

        foreach (var complexProperty in structuralType.GetComplexProperties())
        {
            var jsonNavigationName = complexProperty.ComplexType.GetJsonPropertyName();
            Debug.Assert(jsonNavigationName is not null, "Invalid complex property found on JSON-mapped structural type");

            var projectionMember = new ProjectionMember().Append(new FakeMemberInfo(jsonNavigationName));

            propertyJsonScalarExpression[projectionMember] = new JsonScalarExpression(
                jsonColumn,
                [new PathSegment(jsonNavigationName)],
                typeof(string),
                textTypeMapping,
                jsonQueryExpression.IsNullable || complexProperty.IsNullable);
        }

        selectExpression.ReplaceProjection(propertyJsonScalarExpression);

        selectExpression.PushdownIntoSubquery();
        var subquery = selectExpression.Tables[0];

#pragma warning disable EF1001 // Internal EF Core API usage.
        var newOuterSelectExpression = CreateSelect(
            jsonQueryExpression,
            subquery,
            JsonEachKeyColumnName,
            typeof(int),
            _typeMappingSource.FindMapping(typeof(int))!);
#pragma warning restore EF1001 // Internal EF Core API usage.

        newOuterSelectExpression.AppendOrdering(
            new OrderingExpression(
                selectExpression.CreateColumnExpression(
                    subquery,
                    JsonEachKeyColumnName,
                    typeof(int),
                    typeMapping: _typeMappingSource.FindMapping(typeof(int)),
                    columnNullable: false),
                ascending: true));

        return new ShapedQueryExpression(
            newOuterSelectExpression,
            new RelationalStructuralTypeShaperExpression(
                jsonQueryExpression.StructuralType,
                new ProjectionBindingExpression(
                    newOuterSelectExpression,
                    new ProjectionMember(),
                    typeof(ValueBuffer)),
                false));
    }
}
