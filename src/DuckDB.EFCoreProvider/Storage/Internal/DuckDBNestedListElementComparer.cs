using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     EF's list comparers derive from <c>ValueComparer&lt;object&gt;</c>, while an outer list comparer composes
///     typed element comparers. This adapter types an inner list comparer as <c>ValueComparer&lt;T&gt;</c>, so a
///     nested list compares, hashes and snapshots element by element. The single comparer constructor and
///     <see cref="IInfrastructure{T}" /> let compiled models regenerate it as a constructor call.
/// </remarks>
public sealed class DuckDBNestedListElementComparer<T> : ValueComparer<T>, IInfrastructure<ValueComparer>
{
    private readonly ValueComparer _listComparer;

    public DuckDBNestedListElementComparer(ValueComparer listComparer)
        : base(
            (left, right) => listComparer.Equals(left, right),
            value => listComparer.GetHashCode(value!),
            value => (T)listComparer.Snapshot(value)!)
        => _listComparer = listComparer;

    ValueComparer IInfrastructure<ValueComparer>.Instance
        => _listComparer;
}