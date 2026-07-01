using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace System.Linq.Expressions;

internal static class ExpressionVisitorExtensions
{
    public static IReadOnlyList<T> VisitAndConvert<T>(
        this ExpressionVisitor visitor,
        IReadOnlyList<T> nodes,
        [CallerMemberName] string? callerName = null)
        where T : Expression
    {
        T[]? newNodes = null;
        for (int i = 0, n = nodes.Count; i < n; i++)
        {
            if (visitor.Visit(nodes[i]) is not T node)
            {
                throw new InvalidOperationException(CoreStrings.MustRewriteToSameNode(callerName, typeof(T).Name));
            }

            if (newNodes is not null)
            {
                newNodes[i] = node;
            }
            else if (!ReferenceEquals(node, nodes[i]))
            {
                newNodes = new T[n];
                for (var j = 0; j < i; j++)
                {
                    newNodes[j] = nodes[j];
                }

                newNodes[i] = node;
            }
        }

        return newNodes ?? nodes;
    }
}
