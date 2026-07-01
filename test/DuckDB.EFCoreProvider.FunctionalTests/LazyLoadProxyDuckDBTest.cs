using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Data.Common;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class LazyLoadProxyDuckDBTest : LazyLoadProxyRelationalTestBase<LazyLoadProxyDuckDBTest.LoadDuckDBFixture>
{
    public LazyLoadProxyDuckDBTest(LoadDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_many_to_one_reference_to_principal(EntityState state, bool useAttach, bool useDetach)
    {
        base.Lazy_load_many_to_one_reference_to_principal(state, useAttach, useDetach);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_reference_to_principal(EntityState state, bool useAttach, bool useDetach)
    {
        base.Lazy_load_one_to_one_reference_to_principal(state, useAttach, useDetach);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_reference_to_dependent(EntityState state, bool useAttach, bool useDetach)
    {
        base.Lazy_load_one_to_one_reference_to_dependent(state, useAttach, useDetach);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_reference_with_recursive_property(EntityState state)
    {
        base.Lazy_load_one_to_one_reference_with_recursive_property(state);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_PK_to_PK_reference_to_principal(EntityState state)
    {
        base.Lazy_load_one_to_one_PK_to_PK_reference_to_principal(state);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Eager_load_one_to_one_non_virtual_reference_to_owned_type()
    {
        base.Eager_load_one_to_one_non_virtual_reference_to_owned_type();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Non_virtual_reference_to_dependent_is_not_lazy_loaded()
    {
        base.Non_virtual_reference_to_dependent_is_not_lazy_loaded();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_serialize_proxies_to_JSON()
    {
        base.Can_serialize_proxies_to_JSON();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Eager_load_one_to_many_non_virtual_collection_of_owned_types()
    {
        base.Eager_load_one_to_many_non_virtual_collection_of_owned_types();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Eager_load_one_to_many_non_virtual_collection_of_owned_types_with_explicit_lazy_load()
    {
        base.Eager_load_one_to_many_non_virtual_collection_of_owned_types_with_explicit_lazy_load();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Eager_load_one_to_many_virtual_collection_of_owned_types()
    {
        base.Eager_load_one_to_many_virtual_collection_of_owned_types();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Eager_load_one_to_many_virtual_collection_of_owned_types_with_explicit_lazy_load()
    {
        base.Eager_load_one_to_many_virtual_collection_of_owned_types_with_explicit_lazy_load();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Eager_load_one_to_one_virtual_reference_to_owned_type()
    {
        base.Eager_load_one_to_one_virtual_reference_to_owned_type();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Non_virtual_collection_is_not_lazy_loaded()
    {
        base.Non_virtual_collection_is_not_lazy_loaded();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_reference_to_principal_null_FK(EntityState state)
    {
        base.Lazy_load_one_to_one_reference_to_principal_null_FK(state);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_reference_to_principal_not_found(EntityState state)
    {
        base.Lazy_load_one_to_one_reference_to_principal_not_found(state);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_reference_to_dependent_not_found(EntityState state)
    {
        base.Lazy_load_one_to_one_reference_to_dependent_not_found(state);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_reference_to_principal_already_loaded(EntityState state)
    {
        base.Lazy_load_one_to_one_reference_to_principal_already_loaded(state);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_reference_to_dependent_already_loaded(EntityState state, CascadeTiming cascadeDeleteTiming)
    {
        base.Lazy_load_one_to_one_reference_to_dependent_already_loaded(state, cascadeDeleteTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_PK_to_PK_reference_to_principal_already_loaded(EntityState state)
    {
        base.Lazy_load_one_to_one_PK_to_PK_reference_to_principal_already_loaded(state);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_PK_to_PK_reference_to_dependent_already_loaded(EntityState state)
    {
        base.Lazy_load_one_to_one_PK_to_PK_reference_to_dependent_already_loaded(state);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_loading_finds_correct_entity_type_with_already_loaded_owned_types()
    {
        base.Lazy_loading_finds_correct_entity_type_with_already_loaded_owned_types();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_loading_finds_correct_entity_type_with_multiple_queries()
    {
        base.Lazy_loading_finds_correct_entity_type_with_multiple_queries();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_loading_finds_correct_entity_type_with_opaque_predicate_and_multiple_queries()
    {
        base.Lazy_loading_finds_correct_entity_type_with_opaque_predicate_and_multiple_queries();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_loading_finds_correct_entity_type_with_alternate_model()
    {
        base.Lazy_loading_finds_correct_entity_type_with_alternate_model();
    }

    public class ThrowingInterceptor : DbCommandInterceptor
    {
        public bool Throw { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            if (Throw)
            {
                throw new Exception("Bang!");
            }

            return base.ReaderExecuting(command, eventData, result);
        }
    }

    public class LoadDuckDBFixture : LoadRelationalFixtureBase
    {
        public ThrowingInterceptor Interceptor { get; } = new();

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder.UseLazyLoadingProxies().AddInterceptors(Interceptor));

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}