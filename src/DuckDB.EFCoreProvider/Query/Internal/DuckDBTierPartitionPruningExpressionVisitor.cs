using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

internal sealed class DuckDBTierPartitionPruningExpressionVisitor : ExpressionVisitor
{
    private readonly IReadOnlyDictionary<string, DuckDBTierPartitionPruningPlan> _plansByView;
    private readonly ISqlExpressionFactory _sql;
    private readonly RelationalTypeMapping _boolMapping;
    private readonly RelationalTypeMapping _dateMapping;
    private readonly RelationalTypeMapping _timestampMapping;

    public DuckDBTierPartitionPruningExpressionVisitor(
        IModel model,
        ISqlExpressionFactory sql,
        IRelationalTypeMappingSource typeMappingSource)
    {
        _plansByView = DuckDBTierPartitionPruningMetadata.ResolveAll(model);
        _sql = sql;
        _boolMapping = typeMappingSource.FindMapping(typeof(bool), model)
            ?? throw new InvalidOperationException("DuckDB BOOLEAN mapping is required for tier partition validation.");
        _dateMapping = typeMappingSource.FindMapping(typeof(DateOnly), model)
            ?? throw new InvalidOperationException("DuckDB DATE mapping is required for tier partition pruning.");
        _timestampMapping = typeMappingSource.FindMapping(typeof(DateTime), model)
            ?? throw new InvalidOperationException("DuckDB TIMESTAMP mapping is required for tier partition pruning.");
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is ShapedQueryExpression shapedQuery)
        {
            return shapedQuery.Update(Visit(shapedQuery.QueryExpression), shapedQuery.ShaperExpression);
        }

        var visited = base.VisitExtension(node);
        if (visited is SelectExpression selectExpression && selectExpression.Predicate is { } predicate)
        {
            ApplyPartitionPredicates(selectExpression, predicate);
        }

        return visited;
    }

    private void ApplyPartitionPredicates(SelectExpression selectExpression, SqlExpression predicate)
    {
        foreach (var table in selectExpression.Tables.OfType<TableExpression>())
        {
            if (!_plansByView.TryGetValue(table.Name, out var plan))
            {
                continue;
            }

            var contractValidated = false;
            foreach (var comparison in ConjunctiveComparisons(predicate))
            {
                foreach (var partition in plan.Partitions.Where(
                             candidate => candidate.Transform != Metadata.TierPartitionTransform.Value || candidate.IsAliased))
                {
                    if (!TryMatchSourceComparison(
                            comparison,
                            table.Alias,
                            partition.SourceColumn,
                            out var sourceColumn,
                            out var operation,
                            out var value)
                        || ContainsColumn(value))
                    {
                        continue;
                    }

                    var partitionColumn = new ColumnExpression(
                        partition.Name,
                        table.Alias,
                        partition.Transform == Metadata.TierPartitionTransform.Value
                            ? sourceColumn.Type
                            : typeof(DateOnly),
                        partition.Transform == Metadata.TierPartitionTransform.Value
                            ? sourceColumn.TypeMapping
                            : _dateMapping,
                        nullable: true);
                    var bucket = BucketValue(value, partition.Transform);
                    var partitionPredicate = operation switch
                    {
                        ExpressionType.Equal => _sql.Equal(partitionColumn, bucket),
                        ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
                            => _sql.GreaterThanOrEqual(partitionColumn, bucket),
                        ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                            => _sql.LessThanOrEqual(partitionColumn, bucket),
                        _ => null,
                    };

                    if (partitionPredicate is not null)
                    {
                        if (!contractValidated && plan.ValidationColumn is { } validationColumn)
                        {
                            var contractColumn = new ColumnExpression(
                                validationColumn,
                                table.Alias,
                                typeof(bool),
                                _boolMapping,
                                nullable: false);
                            selectExpression.ApplyPredicate(
                                _sql.Equal(contractColumn, _sql.Constant(true, _boolMapping)));
                            contractValidated = true;
                        }

                        selectExpression.ApplyPredicate(partitionPredicate);
                    }
                }
            }
        }
    }

    private SqlExpression BucketValue(SqlExpression value, Metadata.TierPartitionTransform transform)
    {
        if (transform == Metadata.TierPartitionTransform.Value)
        {
            return value;
        }

        if (transform == Metadata.TierPartitionTransform.Day)
        {
            return _sql.Convert(value, typeof(DateOnly), _dateMapping);
        }

        var precision = transform == Metadata.TierPartitionTransform.Year ? "year" : "month";
        var truncated = _sql.Function(
            "date_trunc",
            [_sql.Constant(precision), value],
            nullable: true,
            argumentsPropagateNullability: [false, true],
            typeof(DateTime),
            _timestampMapping);
        return _sql.Convert(truncated, typeof(DateOnly), _dateMapping);
    }

    private static IEnumerable<SqlBinaryExpression> ConjunctiveComparisons(SqlExpression expression)
    {
        if (expression is SqlBinaryExpression { OperatorType: ExpressionType.AndAlso } and)
        {
            return ConjunctiveComparisons(and.Left).Concat(ConjunctiveComparisons(and.Right));
        }

        return expression is SqlBinaryExpression
        {
            OperatorType: ExpressionType.Equal
                or ExpressionType.GreaterThan
                or ExpressionType.GreaterThanOrEqual
                or ExpressionType.LessThan
                or ExpressionType.LessThanOrEqual,
        } comparison
            ? [comparison]
            : [];
    }

    private static bool TryMatchSourceComparison(
        SqlBinaryExpression comparison,
        string tableAlias,
        string sourceColumn,
        out ColumnExpression matchedColumn,
        out ExpressionType operation,
        out SqlExpression value)
    {
        if (Unwrap(comparison.Left) is ColumnExpression left
            && left.TableAlias == tableAlias
            && left.Name == sourceColumn)
        {
            matchedColumn = left;
            operation = comparison.OperatorType;
            value = comparison.Right;
            return true;
        }

        if (Unwrap(comparison.Right) is ColumnExpression right
            && right.TableAlias == tableAlias
            && right.Name == sourceColumn)
        {
            matchedColumn = right;
            operation = Reverse(comparison.OperatorType);
            value = comparison.Left;
            return true;
        }

        matchedColumn = null!;
        operation = default;
        value = null!;
        return false;
    }

    private static SqlExpression Unwrap(SqlExpression expression)
        => expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } convert
            ? convert.Operand
            : expression;

    private static ExpressionType Reverse(ExpressionType operation)
        => operation switch
        {
            ExpressionType.GreaterThan => ExpressionType.LessThan,
            ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
            ExpressionType.LessThan => ExpressionType.GreaterThan,
            ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
            _ => operation,
        };

    private static bool ContainsColumn(SqlExpression expression)
    {
        var visitor = new ColumnFindingExpressionVisitor();
        visitor.Visit(expression);
        return visitor.Found;
    }

    private sealed class ColumnFindingExpressionVisitor : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is ColumnExpression)
            {
                Found = true;
                return node;
            }

            return base.VisitExtension(node);
        }
    }
}