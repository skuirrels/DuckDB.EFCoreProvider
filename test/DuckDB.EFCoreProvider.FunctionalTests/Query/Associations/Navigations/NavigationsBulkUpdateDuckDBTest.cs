#if NET11_0_OR_GREATER
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query.Associations.Navigations;

/// <summary>
///     The shared bulk-update specification is written for complex types. With navigations, associates are
///     entity types in their own tables, so EF Core rejects setters that assign a whole navigation or navigation
///     collection, or that touch more than one table, before any SQL reaches the provider. Those cases are
///     characterised as translation failures, and the row counts are corrected for entity-typed associates.
/// </summary>
public class NavigationsBulkUpdateDuckDBTest(NavigationsDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
    : NavigationsBulkUpdateRelationalTestBase<NavigationsDuckDBFixture>(fixture, testOutputHelper)
{
    // Each root has its own required associate row, so deleting them affects all seven.
    public override Task Delete_required_associate()
        => AssertDelete(ss => ss.Set<RootEntity>().Select(c => c.RequiredAssociate), rowsAffectedCount: 7);

    // One root has no optional associate.
    public override Task Delete_optional_associate()
        => AssertDelete(ss => ss.Set<RootEntity>().Select(c => c.OptionalAssociate), rowsAffectedCount: 6);

    // Seven associate rows, minus the one skipped.
    public override Task Update_property_on_projected_associate_with_OrderBy_Skip()
        => AssertUpdate(
            ss => ss.Set<RootEntity>().Select(c => c.RequiredAssociate).OrderBy(a => a.String).Skip(1),
            a => a,
            s => s.SetProperty(c => c.String, "foo_updated"),
            rowsAffectedCount: 6);

    public override Task Update_associate_to_parameter()
        => AssertNotTranslated(() => base.Update_associate_to_parameter());

    public override Task Update_nested_associate_to_parameter()
        => AssertNotTranslated(() => base.Update_nested_associate_to_parameter());

    public override Task Update_associate_to_another_associate()
        => AssertNotTranslated(() => base.Update_associate_to_another_associate());

    public override Task Update_nested_associate_to_another_nested_associate()
        => AssertNotTranslated(() => base.Update_nested_associate_to_another_nested_associate());

    public override Task Update_associate_to_inline()
        => AssertNotTranslated(() => base.Update_associate_to_inline());

    public override Task Update_associate_to_inline_with_lambda()
        => AssertNotTranslated(() => base.Update_associate_to_inline_with_lambda());

    public override Task Update_nested_associate_to_inline_with_lambda()
        => AssertNotTranslated(() => base.Update_nested_associate_to_inline_with_lambda());

    public override Task Update_associate_to_null()
        => AssertNotTranslated(() => base.Update_associate_to_null());

    public override Task Update_associate_to_null_with_lambda()
        => AssertNotTranslated(() => base.Update_associate_to_null_with_lambda());

    public override Task Update_associate_to_null_parameter()
        => AssertNotTranslated(() => base.Update_associate_to_null_parameter());

    public override Task Update_collection_to_parameter()
        => AssertNotTranslated(() => base.Update_collection_to_parameter());

    public override Task Update_nested_collection_to_parameter()
        => AssertNotTranslated(() => base.Update_nested_collection_to_parameter());

    public override Task Update_nested_collection_to_inline_with_lambda()
        => AssertNotTranslated(() => base.Update_nested_collection_to_inline_with_lambda());

    public override Task Update_collection_referencing_the_original_collection()
        => AssertNotTranslated(() => base.Update_collection_referencing_the_original_collection());

    public override Task Update_nested_collection_to_another_nested_collection()
        => AssertNotTranslated(() => base.Update_nested_collection_to_another_nested_collection());

    public override Task Update_inside_structural_collection()
        => AssertNotTranslated(() => base.Update_inside_structural_collection());

    public override Task Update_primitive_collection_to_another_collection()
        => AssertNotTranslated(() => base.Update_primitive_collection_to_another_collection());

    public override Task Update_inside_primitive_collection()
        => AssertNotTranslated(() => base.Update_inside_primitive_collection());

    public override Task Update_multiple_properties_inside_associates_and_on_entity_type()
        => AssertNotTranslated(() => base.Update_multiple_properties_inside_associates_and_on_entity_type());

    public override Task Update_multiple_projected_associates_via_anonymous_type()
        => AssertNotTranslated(() => base.Update_multiple_projected_associates_via_anonymous_type());

    private static async Task AssertNotTranslated(Func<Task> test)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(test);
        Assert.Contains("could not be translated", exception.Message);
    }
}
#endif