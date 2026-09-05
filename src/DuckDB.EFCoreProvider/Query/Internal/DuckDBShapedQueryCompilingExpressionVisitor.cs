using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

internal sealed class DuckDBShapedQueryCompilingExpressionVisitor(
    ShapedQueryCompilingExpressionVisitorDependencies dependencies,
    RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
    QueryCompilationContext queryCompilationContext)
    : RelationalShapedQueryCompilingExpressionVisitor(dependencies, relationalDependencies, queryCompilationContext)
{
    private static readonly MethodInfo GetValueMethod
        = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue), [typeof(int)])!;

    private static readonly PropertyInfo FieldCountProperty
        = typeof(DbDataReader).GetProperty(nameof(DbDataReader.FieldCount))!;

    private bool _requiresRuntimeRawSqlArguments;

    // EF's pregeneration parameter locator only finds SqlParameterExpression, not the
    // QueryParameterExpression containing FromSql's object[] arguments. Resolve those
    // commands at runtime, when EF can expand the array and map its actual values.
    protected override int MaxNullableParametersForPregeneratedSql
        => _requiresRuntimeRawSqlArguments ? -1 : base.MaxNullableParametersForPregeneratedSql;

    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        var selectExpression = shapedQueryExpression.QueryExpression as SelectExpression;
        var previousRequiresRuntimeArguments = _requiresRuntimeRawSqlArguments;
        _requiresRuntimeRawSqlArguments |= QueryCompilationContext.IsPrecompiling
            && RuntimeRawSqlArgumentsFinder.Contains(shapedQueryExpression.QueryExpression);
        Expression result;
        try
        {
            result = base.VisitShapedQuery(shapedQueryExpression);
        }
        finally
        {
            _requiresRuntimeRawSqlArguments = previousRequiresRuntimeArguments;
        }

        if (selectExpression is null)
        {
            return result;
        }

        var slots = FindSharedStructSlots(selectExpression);
        if (slots.Count == 0)
        {
            return result;
        }

        var presences = FindStructPresences(shapedQueryExpression.ShaperExpression, slots);

        // Split-query children inherit the parent's struct placeholder and root projections,
        // but EF can prune the unconsumed root column from their SQL while keeping dead leaf
        // guards in the shaper. Each rewrite is therefore guarded by a runtime FieldCount
        // check: readers that still carry the root projection extract from it, while pruned
        // readers fall back to the original leaf reads.
        return new DuckDBStructReaderExpressionVisitor(slots, presences).Visit(result)!;
    }

    protected override Expression InjectStructuralTypeMaterializers(Expression expression)
        => base.InjectStructuralTypeMaterializers(expression);

    private static Dictionary<int, StructSlot> FindSharedStructSlots(SelectExpression selectExpression)
    {
        var roots = new List<(int Index, DuckDBWholeStructExpression Expression)>();
        for (var i = 0; i < selectExpression.Projection.Count; i++)
        {
            if (selectExpression.Projection[i].Expression is DuckDBWholeStructExpression
                {
                    FieldPath.Count: 0,
                    SuppressSource: false
                } root)
            {
                roots.Add((i, root));
            }
        }

        var sharedSlots = new Dictionary<int, StructSlot>();
        for (var i = 0; i < selectExpression.Projection.Count; i++)
        {
            if (selectExpression.Projection[i].Expression is not DuckDBWholeStructExpression
                {
                    FieldPath.Count: > 0,
                    SuppressSource: true
                } leaf)
            {
                continue;
            }

            var root = roots.FirstOrDefault(candidate => AreSameStructSource(candidate.Expression.Source, leaf.Source));
            if (root.Expression is not null
                && leaf.ExtractionTypeMapping is DuckDBStructKeyTypeMapping extractionMapping)
            {
                sharedSlots[i] = new StructSlot(root.Index, extractionMapping);
            }
        }

        return sharedSlots;
    }

    private static IReadOnlyList<StructPresence> FindStructPresences(
        Expression shaperExpression,
        IReadOnlyDictionary<int, StructSlot> slots)
    {
        var shaperFinder = new StructuralTypeShaperFindingVisitor();
        shaperFinder.Visit(shaperExpression);

        var presences = new List<StructPresence>();
        foreach (var shaper in shaperFinder.Shapers)
        {
            if (shaper.ValueBufferExpression is not ProjectionBindingExpression binding
                || binding.QueryExpression is not SelectExpression bindingSelect
                || bindingSelect.GetProjection(binding) is not ConstantExpression
                {
                    Value: Dictionary<IPropertyBase, int> propertyIndexes
                })
            {
                continue;
            }

            foreach (var complex in EnumerateComplexes(shaper.StructuralType))
            {
                var leafProjectionIndexes = new HashSet<int>();
                var rootProjectionIndex = -1;
                var valid = true;

                foreach (var property in complex.Type.GetFlattenedProperties())
                {
                    if (!propertyIndexes.TryGetValue(property, out var projectionIndex)
                        || !slots.TryGetValue(projectionIndex, out var slot))
                    {
                        valid = false;
                        break;
                    }

                    if (rootProjectionIndex == -1)
                    {
                        rootProjectionIndex = slot.RootProjectionIndex;
                    }
                    else if (rootProjectionIndex != slot.RootProjectionIndex)
                    {
                        valid = false;
                        break;
                    }

                    leafProjectionIndexes.Add(projectionIndex);
                }

                if (valid && rootProjectionIndex >= 0 && leafProjectionIndexes.Count > 0)
                {
                    presences.Add(
                        new StructPresence(
                            complex.Type.ClrType,
                            complex.FieldPath,
                            rootProjectionIndex,
                            leafProjectionIndexes,
                            complex.Type.GetComplexProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)));
                }
            }
        }

        return presences;
    }

    private static IEnumerable<StructComplex> EnumerateComplexes(ITypeBase structuralType)
    {
        if (structuralType is IComplexType complexType)
        {
            var chain = new List<IComplexProperty>();
            var current = complexType;
            while (true)
            {
                chain.Add(current.ComplexProperty);
                if (current.ComplexProperty.DeclaringType is not IComplexType parent)
                {
                    break;
                }

                current = parent;
            }

            chain.Reverse();
            var fieldPath = new List<string>();
            for (var i = 1; i < chain.Count; i++)
            {
                if (chain[i].GetStructMapping()?.FieldName is not { } fieldName)
                {
                    yield break;
                }

                fieldPath.Add(fieldName);
            }

            foreach (var nested in EnumerateComplexes(complexType, fieldPath))
            {
                yield return nested;
            }

            yield break;
        }

        foreach (var complexProperty in structuralType.GetComplexProperties())
        {
            foreach (var complex in EnumerateComplexes(complexProperty, []))
            {
                yield return complex;
            }
        }
    }

    private static IEnumerable<StructComplex> EnumerateComplexes(
        IComplexProperty complexProperty,
        IReadOnlyList<string> fieldPath)
    {
        if (complexProperty.GetStructMapping() is null)
        {
            yield break;
        }

        foreach (var complex in EnumerateComplexes(complexProperty.ComplexType, fieldPath))
        {
            yield return complex;
        }
    }

    private static IEnumerable<StructComplex> EnumerateComplexes(
        IComplexType complexType,
        IReadOnlyList<string> fieldPath)
    {
        yield return new StructComplex(complexType, fieldPath);

        foreach (var complexProperty in complexType.GetComplexProperties())
        {
            if (complexProperty.GetStructMapping()?.FieldName is not { } fieldName)
            {
                continue;
            }

            foreach (var nested in EnumerateComplexes(
                         complexProperty.ComplexType,
                         fieldPath.Concat([fieldName]).ToArray()))
            {
                yield return nested;
            }
        }
    }

    private static bool AreSameStructSource(SqlExpression left, SqlExpression right)
        => left is ColumnExpression leftColumn
            && right is ColumnExpression rightColumn
            && string.Equals(leftColumn.TableAlias, rightColumn.TableAlias, StringComparison.Ordinal)
            && string.Equals(leftColumn.Name, rightColumn.Name, StringComparison.Ordinal);

    private sealed record StructSlot(int RootProjectionIndex, DuckDBStructKeyTypeMapping ExtractionMapping);

    private sealed record StructComplex(IComplexType Type, IReadOnlyList<string> FieldPath);

    private sealed record StructPresence(
        Type ClrType,
        IReadOnlyList<string> FieldPath,
        int RootProjectionIndex,
        IReadOnlySet<int> LeafProjectionIndexes,
        IReadOnlySet<string> NestedComplexPropertyNames);

    private sealed class RuntimeRawSqlArgumentsFinder : ExpressionVisitor
    {
        private bool _found;

        public static bool Contains(Expression expression)
        {
            var finder = new RuntimeRawSqlArgumentsFinder();
            finder.Visit(expression);
            return finder._found;
        }

        public override Expression? Visit(Expression? node)
            => _found ? node : base.Visit(node);

        protected override Expression VisitExtension(Expression node)
        {
            if (node is FromSqlExpression { Arguments: QueryParameterExpression })
            {
                _found = true;
                return node;
            }

            return base.VisitExtension(node);
        }
    }

    private sealed class StructuralTypeShaperFindingVisitor : ExpressionVisitor
    {
        public List<RelationalStructuralTypeShaperExpression> Shapers { get; } = [];

        public override Expression? Visit(Expression? node)
            => node is ShapedQueryExpression
                ? node
                : base.Visit(node);

        protected override Expression VisitExtension(Expression node)
        {
            if (node is RelationalStructuralTypeShaperExpression shaper
                && !Shapers.Contains(shaper))
            {
                Shapers.Add(shaper);
            }

            return base.VisitExtension(node);
        }
    }

    private static bool TryGetReaderProjection(
        MethodCallExpression node,
        out Expression reader,
        out int projectionIndex)
    {
        reader = null!;
        projectionIndex = 0;
        if (node.Object is not { Type: { } objectType }
            || !typeof(DbDataReader).IsAssignableFrom(objectType)
            || node.Arguments.Count != 1
            || node.Arguments[0] is not ConstantExpression { Value: int index })
        {
            return false;
        }

        reader = node.Object;
        projectionIndex = index;
        return true;
    }

    private sealed class ReaderOrdinalScanner : ExpressionVisitor
    {
        public static HashSet<int> Scan(Expression expression)
        {
            var scanner = new ReaderOrdinalScanner();
            scanner.Visit(expression);
            return scanner.ordinals;
        }

        private readonly HashSet<int> ordinals = [];

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (TryGetReaderProjection(node, out _, out var ordinal))
            {
                ordinals.Add(ordinal);
            }

            return base.VisitMethodCall(node);
        }
    }

    private sealed class DuckDBStructReaderExpressionVisitor(
        IReadOnlyDictionary<int, StructSlot> slots,
        IReadOnlyList<StructPresence> presences) : ExpressionVisitor
    {
        protected override Expression VisitConditional(ConditionalExpression node)
        {
            var visited = base.VisitConditional(node);

            if (node.Test is MethodCallExpression test
                && TryGetReaderProjection(test, out var reader, out var projectionIndex)
                && test.Method.Name == nameof(DbDataReader.IsDBNull)
                && slots.TryGetValue(projectionIndex, out var slot)
                && slot.ExtractionMapping.LeafTypeMapping is { } leafTypeMapping)
            {
                var rootValue = Expression.Call(
                    reader,
                    GetValueMethod,
                    Expression.Constant(slot.RootProjectionIndex));
                var extracted = DuckDBStructKeyTypeMapping.CreateReadExpression(
                    rootValue,
                    leafTypeMapping,
                    slot.ExtractionMapping.FieldPath);

                if (extracted.Type != node.Type)
                {
                    extracted = Expression.Convert(extracted, node.Type);
                }

                return GuardByFieldCount(reader, slot.RootProjectionIndex, extracted, visited);
            }

            if (TryGetNullableStructPresence(node, visited, out var presenceTest))
            {
                var visitedConditional = (ConditionalExpression)visited;
                return visitedConditional.Update(presenceTest, visitedConditional.IfTrue, visitedConditional.IfFalse);
            }

            return visited;
        }

        private bool TryGetNullableStructPresence(
            ConditionalExpression original,
            Expression visited,
            out Expression presenceTest)
        {
            presenceTest = null!;
            if (original.IfTrue is not DefaultExpression { Type: var complexType }
                || original.Type != complexType
                || original.IfFalse.Type != complexType
                || !TryGetReaderProjectionFromExpression(original.Test, out var reader)
                || !TryGetReaderOrdinals(original.Test, out var ordinals))
            {
                return false;
            }

            var materializedClrType = Nullable.GetUnderlyingType(complexType) ?? complexType;
            var matchingPresences = presences
                .Where(
                    presence => presence.ClrType == materializedClrType
                        && ordinals.All(presence.LeafProjectionIndexes.Contains)
                        && ordinals.All(
                            ordinal => slots.TryGetValue(ordinal, out var slot)
                                && slot.RootProjectionIndex == presence.RootProjectionIndex))
                .ToList();
            if (matchingPresences.Count == 0)
            {
                return false;
            }

            var nestedPropertyNames = new NestedPropertyNameFindingVisitor().Find(original.IfFalse);
            var presence = matchingPresences
                .OrderByDescending(candidate => candidate.NestedComplexPropertyNames.Count(nestedPropertyNames.Contains))
                .ThenByDescending(
                    candidate => CommonPrefixLength(
                        candidate.FieldPath,
                        ordinals.Select(ordinal => slots[ordinal].ExtractionMapping.FieldPath)))
                .ThenByDescending(candidate => candidate.FieldPath.Count)
                .First();

            var rootValue = Expression.Call(
                reader,
                GetValueMethod,
                Expression.Constant(presence.RootProjectionIndex));
            var fromRoot = Expression.Not(
                DuckDBStructKeyTypeMapping.CreatePresenceExpression(rootValue, presence.FieldPath));
            var visitedConditional = (ConditionalExpression)visited;
            presenceTest = GuardByFieldCount(
                reader,
                presence.RootProjectionIndex,
                fromRoot,
                visitedConditional.Test);
            return true;
        }

        private static bool TryGetReaderProjectionFromExpression(Expression expression, out Expression reader)
        {
            reader = null!;
            var finder = new ReaderProjectionFindingVisitor();
            finder.Visit(expression);
            if (finder.Reader is null)
            {
                return false;
            }

            reader = finder.Reader;
            return true;
        }

        private static bool TryGetReaderOrdinals(Expression expression, out IReadOnlySet<int> ordinals)
        {
            var found = ReaderOrdinalScanner.Scan(expression);
            ordinals = found;
            return found.Count > 0;
        }

        private static int CommonPrefixLength(
            IReadOnlyList<string> candidate,
            IEnumerable<IReadOnlyList<string>> physicalPaths)
        {
            var fields = physicalPaths.ToArray();
            var length = fields.Length == 0
                ? 0
                : Math.Min(candidate.Count, fields.Min(path => path.Count));
            for (var i = 0; i < length; i++)
            {
                if (fields.Any(path => !string.Equals(candidate[i], path[i], StringComparison.Ordinal)))
                {
                    return i;
                }
            }

            return length;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(DbDataReader.IsDBNull))
            {
                return base.VisitMethodCall(node);
            }

            if (!TryGetReaderProjection(node, out var reader, out var projectionIndex)
                || !slots.TryGetValue(projectionIndex, out var slot)
                || slot.ExtractionMapping.LeafTypeMapping is not { } leafTypeMapping
                || !ReaderMethodMatches(node.Method, leafTypeMapping.GetDataReaderMethod()))
            {
                return base.VisitMethodCall(node);
            }

            var rootValue = Expression.Call(
                reader,
                GetValueMethod,
                Expression.Constant(slot.RootProjectionIndex));
            var extracted = DuckDBStructKeyTypeMapping.CreateProviderReadExpression(
                rootValue,
                leafTypeMapping,
                slot.ExtractionMapping.FieldPath);

            if (extracted.Type != node.Type)
            {
                extracted = Expression.Convert(extracted, node.Type);
            }

            return GuardByFieldCount(reader, slot.RootProjectionIndex, extracted, node);
        }

        private static Expression GuardByFieldCount(
            Expression reader,
            int rootProjectionIndex,
            Expression fromRoot,
            Expression fallback)
            => Expression.Condition(
                Expression.GreaterThan(
                    Expression.Property(reader, FieldCountProperty),
                    Expression.Constant(rootProjectionIndex)),
                fromRoot,
                fallback);

        private static bool ReaderMethodMatches(MethodInfo actual, MethodInfo expected)
        {
            if (actual == expected)
            {
                return true;
            }

            if (actual.IsGenericMethod
                && expected.IsGenericMethod
                && actual.GetGenericMethodDefinition() == expected.GetGenericMethodDefinition()
                && actual.GetGenericArguments().SequenceEqual(expected.GetGenericArguments()))
            {
                return true;
            }

            return actual.GetBaseDefinition() == expected.GetBaseDefinition();
        }
    }

    private sealed class ReaderProjectionFindingVisitor : ExpressionVisitor
    {
        public Expression? Reader { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (Reader is null && TryGetReaderProjection(node, out var reader, out _))
            {
                Reader = reader;
            }

            return base.VisitMethodCall(node);
        }
    }

    private sealed class NestedPropertyNameFindingVisitor : ExpressionVisitor
    {
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        public IReadOnlySet<string> Find(Expression expression)
        {
            _names.Clear();
            Visit(expression);
            return _names;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            _names.Add(node.Member.Name);
            return base.VisitMember(node);
        }
    }
}