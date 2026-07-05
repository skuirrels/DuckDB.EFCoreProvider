using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Configures a tiered-storage aggregate root: its cold-reporting read model and its aggregate children.
/// </summary>
/// <typeparam name="TRoot">The aggregate-root entity type.</typeparam>
public sealed class TieredStoreBuilder<TRoot>
    where TRoot : class
{
    private readonly ModelBuilder _modelBuilder;
    private readonly IMutableEntityType _root;
    private readonly string _archivePath;
    private readonly string _controlKey;

    internal TieredStoreBuilder(ModelBuilder modelBuilder, IMutableEntityType root, string archivePath, string controlKey)
    {
        _modelBuilder = modelBuilder;
        _root = root;
        _archivePath = archivePath;
        _controlKey = controlKey;
    }

    /// <summary>
    ///     Declares the keyless read-model type used to query the root's hot table and cold archive together.
    ///     Its properties must mirror the hot entity's mapped columns.
    /// </summary>
    public TieredStoreBuilder<TRoot> WithReadModel<TReadModel>()
        where TReadModel : class
    {
        DuckDBTieredStoreExtensions.MapReadModel(_modelBuilder, _root, typeof(TReadModel));
        return this;
    }

    /// <summary>
    ///     Disables the "root is hot" semijoin guard on this aggregate's child views. By default a child row is
    ///     shown as hot only if its root is on the hot side of the watermark, which keeps reads correct in the
    ///     brief window between a crashed archive's <c>COPY</c> and its delete. Disabling it makes child views a
    ///     plain <c>SELECT * FROM child</c> (faster, especially for deep aggregates) at the cost of a transient
    ///     double-count if the process dies mid-archive; the next archive still self-heals.
    /// </summary>
    public TieredStoreBuilder<TRoot> WithoutHotChildFilter()
    {
        _root.SetAnnotation(DuckDBAnnotationNames.TieredStoreHotChildFilter, false);
        return this;
    }

    /// <summary>
    ///     Declares a collection navigation as an aggregate child that is archived and purged together with the
    ///     root. Nest <see cref="TieredChildBuilder{TChild}.Including{TGrandchild}" /> for deeper aggregates.
    /// </summary>
    public TieredStoreBuilder<TRoot> Including<TChild>(
        Expression<Func<TRoot, IEnumerable<TChild>>> navigation,
        Action<TieredChildBuilder<TChild>>? configure = null)
        where TChild : class
    {
        var navigationName = DuckDBTieredStoreExtensions.GetPropertyName(navigation);
        var child = DuckDBTieredStoreExtensions.AddChild(
            _modelBuilder, _root, _root, typeof(TChild), navigationName, _archivePath, _controlKey);
        configure?.Invoke(new TieredChildBuilder<TChild>(_modelBuilder, child, _root, _archivePath, _controlKey));
        return this;
    }
}

/// <summary>
///     Configures a tiered-storage aggregate child: its cold-reporting read model and any deeper children.
/// </summary>
/// <typeparam name="TChild">The child entity type.</typeparam>
public sealed class TieredChildBuilder<TChild>
    where TChild : class
{
    private readonly ModelBuilder _modelBuilder;
    private readonly IMutableEntityType _child;
    private readonly IMutableEntityType _root;
    private readonly string _archivePath;
    private readonly string _controlKey;

    internal TieredChildBuilder(ModelBuilder modelBuilder, IMutableEntityType child, IMutableEntityType root, string archivePath, string controlKey)
    {
        _modelBuilder = modelBuilder;
        _child = child;
        _root = root;
        _archivePath = archivePath;
        _controlKey = controlKey;
    }

    /// <summary>Declares the keyless read-model type used to query this child's hot table and cold archive together.</summary>
    public TieredChildBuilder<TChild> WithReadModel<TReadModel>()
        where TReadModel : class
    {
        DuckDBTieredStoreExtensions.MapReadModel(_modelBuilder, _child, typeof(TReadModel));
        return this;
    }

    /// <summary>Declares a deeper aggregate child (grandchild of the root).</summary>
    public TieredChildBuilder<TChild> Including<TGrandchild>(
        Expression<Func<TChild, IEnumerable<TGrandchild>>> navigation,
        Action<TieredChildBuilder<TGrandchild>>? configure = null)
        where TGrandchild : class
    {
        var navigationName = DuckDBTieredStoreExtensions.GetPropertyName(navigation);
        var grandchild = DuckDBTieredStoreExtensions.AddChild(
            _modelBuilder, _child, _root, typeof(TGrandchild), navigationName, _archivePath, _controlKey);
        configure?.Invoke(new TieredChildBuilder<TGrandchild>(_modelBuilder, grandchild, _root, _archivePath, _controlKey));
        return this;
    }
}
