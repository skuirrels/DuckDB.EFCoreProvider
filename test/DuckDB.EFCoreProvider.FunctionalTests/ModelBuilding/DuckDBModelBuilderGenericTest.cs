using Xunit;

namespace Microsoft.EntityFrameworkCore.ModelBuilding;

public class DuckDBModelBuilderGenericTest : DuckDBModelBuilderTestBase
{
    public class DuckDBGenericNonRelationship : DuckDBNonRelationship
    {
        public DuckDBGenericNonRelationship(DuckDBModelBuilderFixture fixture) : base(fixture)
        {
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        protected override void Mapping_ignores_ignored_three_dimensional_array()
        {
            base.Mapping_ignores_ignored_three_dimensional_array();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        protected override void Mapping_ignores_ignored_two_dimensional_array()
        {
            base.Mapping_ignores_ignored_two_dimensional_array();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        protected override void Mapping_throws_for_non_ignored_three_dimensional_array()
        {
            base.Mapping_throws_for_non_ignored_three_dimensional_array();
        }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class DuckDBGenericComplexType : DuckDBComplexType
    {
        public DuckDBGenericComplexType(DuckDBModelBuilderFixture fixture) : base(fixture)
        {
        }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class DuckDBGenericComplexCollection : DuckDBComplexCollection
    {
        public DuckDBGenericComplexCollection(DuckDBModelBuilderFixture fixture) : base(fixture)
        {
        }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class DuckDBGenericInheritance : DuckDBInheritance
    {
        public DuckDBGenericInheritance(DuckDBModelBuilderFixture fixture) : base(fixture)
        {
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Index_removed_when_covered_by_an_inherited_foreign_key()
        {
            base.Index_removed_when_covered_by_an_inherited_foreign_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Index_removed_when_covered_by_an_inherited_index()
        {
            base.Index_removed_when_covered_by_an_inherited_index();
        }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class DuckDBGenericOneToMany : DuckDBOneToMany
    {
        public DuckDBGenericOneToMany(DuckDBModelBuilderFixture fixture) : base(fixture)
        {
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_exclude_navigation_pointed_by_foreign_key_attribute_from_explicit_configuration()
        {
            base.Can_exclude_navigation_pointed_by_foreign_key_attribute_from_explicit_configuration();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_exclude_navigation_with_foreign_key_attribute_from_explicit_configuration()
        {
            base.Can_exclude_navigation_with_foreign_key_attribute_from_explicit_configuration();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_exclude_navigation_with_foreign_key_attribute_on_principal_type_from_explicit_configuration()
        {
            base.Can_exclude_navigation_with_foreign_key_attribute_on_principal_type_from_explicit_configuration();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_both_convention_properties_specified()
        {
            base.Can_have_both_convention_properties_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_both_convention_properties_specified_in_any_order()
        {
            base.Can_have_both_convention_properties_specified_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_FK_by_convention_specified_with_explicit_principal_key()
        {
            base.Can_have_FK_by_convention_specified_with_explicit_principal_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_FK_by_convention_specified_with_explicit_principal_key_in_any_order()
        {
            base.Can_have_FK_by_convention_specified_with_explicit_principal_key_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_FK_semi_specified_with_explicit_PK()
        {
            base.Can_have_FK_semi_specified_with_explicit_PK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_principal_key_by_convention_specified_with_explicit_PK()
        {
            base.Can_have_principal_key_by_convention_specified_with_explicit_PK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_principal_key_by_convention_specified_with_explicit_PK_in_any_order()
        {
            base.Can_have_principal_key_by_convention_specified_with_explicit_PK_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_principal_key_by_convention_replaced_with_primary_key()
        {
            base.Can_have_principal_key_by_convention_replaced_with_primary_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_specify_requiredness_after_OnDelete()
        {
            base.Can_specify_requiredness_after_OnDelete();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_alternate_composite_key()
        {
            base.Can_use_alternate_composite_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_alternate_composite_key_in_any_order()
        {
            base.Can_use_alternate_composite_key_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_explicitly_specified_PK()
        {
            base.Can_use_explicitly_specified_PK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_non_PK_principal()
        {
            base.Can_use_non_PK_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_creates_composite_FK_specified()
        {
            base.Creates_both_navigations_and_creates_composite_FK_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_creates_FK_specified()
        {
            base.Creates_both_navigations_and_creates_FK_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_creates_shadow_FK()
        {
            base.Creates_both_navigations_and_creates_shadow_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_matches_shadow_FK_property_by_convention()
        {
            base.Creates_both_navigations_and_matches_shadow_FK_property_by_convention();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_overrides_existing_FK_when_uniqueness_does_not_match()
        {
            base.Creates_both_navigations_and_overrides_existing_FK_when_uniqueness_does_not_match();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_uses_existing_composite_FK()
        {
            base.Creates_both_navigations_and_uses_existing_composite_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_uses_specified_FK_even_if_found_by_convention()
        {
            base.Creates_both_navigations_and_uses_specified_FK_even_if_found_by_convention();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_uses_existing_FK_not_found_by_convention()
        {
            base.Creates_both_navigations_and_uses_existing_FK_not_found_by_convention();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_uses_existing_FK()
        {
            base.Creates_both_navigations_and_uses_existing_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_overlapping_foreign_keys_with_different_nullability()
        {
            base.Creates_overlapping_foreign_keys_with_different_nullability();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_on_existing_FK_is_using_different_principal_key()
        {
            base.Creates_relationship_on_existing_FK_is_using_different_principal_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_on_existing_FK_is_using_different_principal_key_different_order()
        {
            base.Creates_relationship_on_existing_FK_is_using_different_principal_key_different_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_both_navigations()
        {
            base.Creates_relationship_with_both_navigations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_navigation_to_dependent()
        {
            base.Creates_relationship_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_navigation_to_principal()
        {
            base.Creates_relationship_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_no_navigations()
        {
            base.Creates_relationship_with_no_navigations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_no_navigations_and_specified_composite_FK()
        {
            base.Creates_relationship_with_no_navigations_and_specified_composite_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_no_navigations_and_specified_FK()
        {
            base.Creates_relationship_with_no_navigations_and_specified_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_shadow_FK_with_navigation_to_dependent()
        {
            base.Creates_shadow_FK_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_shadow_FK_with_navigation_to_principal()
        {
            base.Creates_shadow_FK_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_shadow_FK_with_no_navigation()
        {
            base.Creates_shadow_FK_with_no_navigation();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_specified_composite_FK_with_navigation_to_dependent()
        {
            base.Creates_specified_composite_FK_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_specified_composite_FK_with_navigation_to_principal()
        {
            base.Creates_specified_composite_FK_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_specified_FK_with_navigation_to_dependent()
        {
            base.Creates_specified_FK_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_specified_FK_with_navigation_to_principal()
        {
            base.Creates_specified_FK_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Explicit_principal_key_is_not_replaced_with_new_primary_key()
        {
            base.Explicit_principal_key_is_not_replaced_with_new_primary_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Finds_existing_navigation_to_dependent_and_uses_associated_FK()
        {
            base.Finds_existing_navigation_to_dependent_and_uses_associated_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Finds_existing_navigation_to_principal_and_uses_associated_FK()
        {
            base.Finds_existing_navigation_to_principal_and_uses_associated_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Finds_existing_navigations_and_uses_associated_FK()
        {
            base.Finds_existing_navigations_and_uses_associated_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Finds_existing_navigations_and_uses_associated_FK_with_fields()
        {
            base.Finds_existing_navigations_and_uses_associated_FK_with_fields();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Non_nullable_FK_are_required_by_default()
        {
            base.Non_nullable_FK_are_required_by_default();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Nullable_FK_can_be_made_required()
        {
            base.Nullable_FK_can_be_made_required();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Nullable_FK_overrides_NRT_navigation()
        {
            base.Nullable_FK_overrides_NRT_navigation();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void One_to_many_relationship_has_no_ambiguity_explicit()
        {
            base.One_to_many_relationship_has_no_ambiguity_explicit();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Principal_key_by_convention_is_not_replaced_with_new_incompatible_primary_key()
        {
            base.Principal_key_by_convention_is_not_replaced_with_new_incompatible_primary_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Removes_existing_unidirectional_one_to_one_relationship()
        {
            base.Removes_existing_unidirectional_one_to_one_relationship();
        }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class DuckDBGenericManyToOne : DuckDBManyToOne
    {
        public DuckDBGenericManyToOne(DuckDBModelBuilderFixture fixture) : base(fixture)
        {
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_both_convention_properties_specified()
        {
            base.Can_have_both_convention_properties_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_both_convention_properties_specified_in_any_order()
        {
            base.Can_have_both_convention_properties_specified_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_FK_by_convention_specified_with_explicit_principal_key()
        {
            base.Can_have_FK_by_convention_specified_with_explicit_principal_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_FK_by_convention_specified_with_explicit_principal_key_in_any_order()
        {
            base.Can_have_FK_by_convention_specified_with_explicit_principal_key_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_principal_key_by_convention_specified_with_explicit_PK()
        {
            base.Can_have_principal_key_by_convention_specified_with_explicit_PK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_principal_key_by_convention_specified_with_explicit_PK_in_any_order()
        {
            base.Can_have_principal_key_by_convention_specified_with_explicit_PK_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_alternate_composite_key()
        {
            base.Can_use_alternate_composite_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_alternate_composite_key_in_any_order()
        {
            base.Can_use_alternate_composite_key_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_non_PK_principal()
        {
            base.Can_use_non_PK_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_explicitly_specified_PK()
        {
            base.Can_use_explicitly_specified_PK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_creates_composite_FK_specified()
        {
            base.Creates_both_navigations_and_creates_composite_FK_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_creates_FK_specified()
        {
            base.Creates_both_navigations_and_creates_FK_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_creates_new_FK()
        {
            base.Creates_both_navigations_and_creates_new_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_creates_shadow_FK()
        {
            base.Creates_both_navigations_and_creates_shadow_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_does_not_use_existing_FK()
        {
            base.Creates_both_navigations_and_does_not_use_existing_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_finds_existing_composite_FK()
        {
            base.Creates_both_navigations_and_finds_existing_composite_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_matches_shadow_FK_by_convention()
        {
            base.Creates_both_navigations_and_matches_shadow_FK_by_convention();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_overrides_existing_FK_if_uniqueness_does_not_match()
        {
            base.Creates_both_navigations_and_overrides_existing_FK_if_uniqueness_does_not_match();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_uses_specified_FK_even_if_found_by_convention()
        {
            base.Creates_both_navigations_and_uses_specified_FK_even_if_found_by_convention();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_with_existing_FK_not_found_by_convention()
        {
            base.Creates_both_navigations_with_existing_FK_not_found_by_convention();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_on_existing_FK_is_using_different_principal_key()
        {
            base.Creates_relationship_on_existing_FK_is_using_different_principal_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_on_existing_FK_is_using_different_principal_key_different_order()
        {
            base.Creates_relationship_on_existing_FK_is_using_different_principal_key_different_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_navigation_to_dependent()
        {
            base.Creates_relationship_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_navigation_to_principal()
        {
            base.Creates_relationship_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_no_navigations()
        {
            base.Creates_relationship_with_no_navigations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_no_navigations_and_specified_composite_FK()
        {
            base.Creates_relationship_with_no_navigations_and_specified_composite_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_no_navigations_and_specified_FK()
        {
            base.Creates_relationship_with_no_navigations_and_specified_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_shadow_FK_with_navigation_to_dependent()
        {
            base.Creates_shadow_FK_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_shadow_FK_with_navigation_to_principal()
        {
            base.Creates_shadow_FK_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_shadow_FK_with_no_navigations_with()
        {
            base.Creates_shadow_FK_with_no_navigations_with();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_specified_composite_FK_with_navigation_to_dependent()
        {
            base.Creates_specified_composite_FK_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_specified_composite_FK_with_navigation_to_principal()
        {
            base.Creates_specified_composite_FK_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_specified_FK_with_navigation_to_dependent()
        {
            base.Creates_specified_FK_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_specified_FK_with_navigation_to_principal()
        {
            base.Creates_specified_FK_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Finds_existing_navigation_to_dependent_and_uses_associated_FK()
        {
            base.Finds_existing_navigation_to_dependent_and_uses_associated_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Finds_existing_navigation_to_principal_and_uses_associated_FK()
        {
            base.Finds_existing_navigation_to_principal_and_uses_associated_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Finds_existing_navigations_and_uses_associated_FK()
        {
            base.Finds_existing_navigations_and_uses_associated_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Finds_existing_navigations_and_uses_associated_FK_with_fields()
        {
            base.Finds_existing_navigations_and_uses_associated_FK_with_fields();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Non_nullable_FK_are_required_by_default()
        {
            base.Non_nullable_FK_are_required_by_default();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Nullable_FK_can_be_made_required()
        {
            base.Nullable_FK_can_be_made_required();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void One_to_many_relationship_has_no_ambiguity_explicit()
        {
            base.One_to_many_relationship_has_no_ambiguity_explicit();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Relationships_discovered_when_ambiguity_on_the_inverse_is_resolved()
        {
            base.Relationships_discovered_when_ambiguity_on_the_inverse_is_resolved();
        }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class DuckDBGenericOneToOne : DuckDBOneToOne
    {
        public DuckDBGenericOneToOne(DuckDBModelBuilderFixture fixture) : base(fixture)
        {
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_be_defined_before_the_PK_from_dependent()
        {
            base.Can_be_defined_before_the_PK_from_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_create_relationship_if_user_specifies_principal_key_property()
        {
            base.Can_create_relationship_if_user_specifies_principal_key_property();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_both_alternate_keys_specified_explicitly()
        {
            base.Can_have_both_alternate_keys_specified_explicitly();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_both_alternate_keys_specified_explicitly_in_any_order()
        {
            base.Can_have_both_alternate_keys_specified_explicitly_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_both_keys_specified_explicitly()
        {
            base.Can_have_both_keys_specified_explicitly();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_have_both_keys_specified_explicitly_in_any_order()
        {
            base.Can_have_both_keys_specified_explicitly_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_alternate_composite_key()
        {
            base.Can_use_alternate_composite_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_alternate_composite_key_in_any_order()
        {
            base.Can_use_alternate_composite_key_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_alternate_principal_key()
        {
            base.Can_use_alternate_principal_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_pk_as_fk_if_principal_end_is_specified()
        {
            base.Can_use_pk_as_fk_if_principal_end_is_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Configuring_FK_properties_as_PK_sets_DeleteBehavior_Cascade()
        {
            base.Configuring_FK_properties_as_PK_sets_DeleteBehavior_Cascade();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_creates_composite_FK_specified()
        {
            base.Creates_both_navigations_and_creates_composite_FK_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_creates_new_FK_when_not_specified()
        {
            base.Creates_both_navigations_and_creates_new_FK_when_not_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_new_FK()
        {
            base.Creates_both_navigations_and_new_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_new_FK_over_PK_by_convention()
        {
            base.Creates_both_navigations_and_new_FK_over_PK_by_convention();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_overrides_existing_FK_when_uniqueness_does_not_match()
        {
            base.Creates_both_navigations_and_overrides_existing_FK_when_uniqueness_does_not_match();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_shadow_FK_if_existing_FK()
        {
            base.Creates_both_navigations_and_shadow_FK_if_existing_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_specified_FK()
        {
            base.Creates_both_navigations_and_specified_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_uses_existing_composite_FK()
        {
            base.Creates_both_navigations_and_uses_existing_composite_FK();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_uses_existing_FK_not_found_by_convention()
        {
            base.Creates_both_navigations_and_uses_existing_FK_not_found_by_convention();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_both_navigations_and_uses_specified_FK_even_if_found_by_convention()
        {
            base.Creates_both_navigations_and_uses_specified_FK_even_if_found_by_convention();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_composite_FK_when_specified_on_principal_with_navigation_to_dependent()
        {
            base.Creates_composite_FK_when_specified_on_principal_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_composite_FK_when_specified_on_principal_with_navigation_to_principal()
        {
            base.Creates_composite_FK_when_specified_on_principal_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_composite_FK_when_specified_on_principal_with_no_navigations()
        {
            base.Creates_composite_FK_when_specified_on_principal_with_no_navigations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_FK_when_principal_and_foreign_key_specified_on_dependent()
        {
            base.Creates_FK_when_principal_and_foreign_key_specified_on_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_FK_when_principal_and_foreign_key_specified_on_dependent_in_reverse_order()
        {
            base.Creates_FK_when_principal_and_foreign_key_specified_on_dependent_in_reverse_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_FK_when_specified_on_dependent()
        {
            base.Creates_FK_when_specified_on_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_FK_when_specified_on_dependent_with_navigation_to_dependent()
        {
            base.Creates_FK_when_specified_on_dependent_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_FK_when_specified_on_dependent_with_navigation_to_principal()
        {
            base.Creates_FK_when_specified_on_dependent_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_FK_when_specified_on_dependent_with_no_navigations()
        {
            base.Creates_FK_when_specified_on_dependent_with_no_navigations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_FK_when_specified_on_principal_with_navigation_to_principal()
        {
            base.Creates_FK_when_specified_on_principal_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_FK_when_specified_on_principal_with_no_navigations()
        {
            base.Creates_FK_when_specified_on_principal_with_no_navigations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_index_even_if_covered_by_an_alternate_key()
        {
            base.Creates_index_even_if_covered_by_an_alternate_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_principal_key_when_specified_on_dependent()
        {
            base.Creates_principal_key_when_specified_on_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_principal_key_when_specified_on_dependent_with_navigation_to_dependent()
        {
            base.Creates_principal_key_when_specified_on_dependent_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_principal_key_when_specified_on_dependent_with_navigation_to_principal()
        {
            base.Creates_principal_key_when_specified_on_dependent_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_principal_key_when_specified_on_dependent_with_no_navigations()
        {
            base.Creates_principal_key_when_specified_on_dependent_with_no_navigations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_principal_key_when_specified_on_principal_with_navigation_to_dependent()
        {
            base.Creates_principal_key_when_specified_on_principal_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_principal_key_when_specified_on_principal_with_navigation_to_principal()
        {
            base.Creates_principal_key_when_specified_on_principal_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_principal_key_when_specified_on_principal_with_no_navigations()
        {
            base.Creates_principal_key_when_specified_on_principal_with_no_navigations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_on_existing_FK_if_using_different_principal_key()
        {
            base.Creates_relationship_on_existing_FK_if_using_different_principal_key();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_on_existing_FK_if_using_different_principal_key_different_order()
        {
            base.Creates_relationship_on_existing_FK_if_using_different_principal_key_different_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_navigation_to_dependent_and_new_FK_from_dependent()
        {
            base.Creates_relationship_with_navigation_to_dependent_and_new_FK_from_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_navigation_to_dependent_and_new_FK_from_principal()
        {
            base.Creates_relationship_with_navigation_to_dependent_and_new_FK_from_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_no_navigations()
        {
            base.Creates_relationship_with_no_navigations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_specified_FK_with_navigation_to_dependent()
        {
            base.Creates_relationship_with_specified_FK_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_specified_FK_with_navigation_to_principal()
        {
            base.Creates_relationship_with_specified_FK_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_relationship_with_specified_FK_with_no_navigations()
        {
            base.Creates_relationship_with_specified_FK_with_no_navigations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_self_referencing_FK_by_convention()
        {
            base.Creates_self_referencing_FK_by_convention();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_self_referencing_FK_by_convention_inverted()
        {
            base.Creates_self_referencing_FK_by_convention_inverted();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_self_referencing_FK_with_navigation_to_dependent()
        {
            base.Creates_self_referencing_FK_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Creates_self_referencing_FK_with_navigation_to_principal()
        {
            base.Creates_self_referencing_FK_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Does_not_use_existing_FK_when_principal_key_specified()
        {
            base.Does_not_use_existing_FK_when_principal_key_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Non_nullable_FK_are_required_by_default()
        {
            base.Non_nullable_FK_are_required_by_default();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void OneToOne_can_have_PK_explicitly_specified()
        {
            base.OneToOne_can_have_PK_explicitly_specified();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Principal_and_dependent_can_be_flipped_when_self_referencing()
        {
            base.Principal_and_dependent_can_be_flipped_when_self_referencing();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Principal_and_dependent_can_be_flipped_when_self_referencing_with_navigation_to_dependent()
        {
            base.Principal_and_dependent_can_be_flipped_when_self_referencing_with_navigation_to_dependent();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Principal_and_dependent_can_be_flipped_when_self_referencing_with_navigation_to_principal()
        {
            base.Principal_and_dependent_can_be_flipped_when_self_referencing_with_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Replaces_existing_navigation_to_principal()
        {
            base.Replaces_existing_navigation_to_principal();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Unspecified_FK_can_be_made_optional()
        {
            base.Unspecified_FK_can_be_made_optional();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Unspecified_FK_can_be_made_optional_in_any_order()
        {
            base.Unspecified_FK_can_be_made_optional_in_any_order();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Unspecified_FK_can_be_made_required()
        {
            base.Unspecified_FK_can_be_made_required();
        }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class DuckDBGenericManyToMany : DuckDBManyToMany
    {
        public DuckDBGenericManyToMany(DuckDBModelBuilderFixture fixture) : base(fixture)
        {
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_implicit_shared_type_as_join_entity()
        {
            base.Can_use_implicit_shared_type_as_join_entity();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_implicit_shared_type_with_default_name_and_implicit_relationships_as_join_entity()
        {
            base.Can_use_implicit_shared_type_with_default_name_and_implicit_relationships_as_join_entity();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_shared_type_as_join_entity()
        {
            base.Can_use_shared_type_as_join_entity();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void FK_properties_matching_navigations_are_discovered_on_explicit_join_entity()
        {
            base.FK_properties_matching_navigations_are_discovered_on_explicit_join_entity();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void ForeignKeyAttribute_configures_the_properties()
        {
            base.ForeignKeyAttribute_configures_the_properties();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void ForeignKeyAttribute_does_not_force_convention_join_table_inclusion_mismatching_key_names()
        {
            base.ForeignKeyAttribute_does_not_force_convention_join_table_inclusion_mismatching_key_names();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Many_to_many_with_a_shadow_navigation()
        {
            base.Many_to_many_with_a_shadow_navigation();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void FK_properties_matching_types_are_discovered_on_explicit_join_entity()
        {
            base.FK_properties_matching_types_are_discovered_on_explicit_join_entity();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_use_ForeignKeyAttribute_with_InversePropertyAttribute()
        {
            base.Can_use_ForeignKeyAttribute_with_InversePropertyAttribute();
        }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class DuckDBGenericOwnedTypes : DuckDBOwnedTypes
    {
        public DuckDBGenericOwnedTypes(DuckDBModelBuilderFixture fixture) : base(fixture)
        {
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_chain_owned_type_collection_configurations()
        {
            base.Can_chain_owned_type_collection_configurations();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_configure_on_derived_type_first()
        {
            base.Can_configure_on_derived_type_first();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_configure_one_to_one_owned_type_with_fields()
        {
            base.Can_configure_one_to_one_owned_type_with_fields();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_configure_owned_type_collection()
        {
            base.Can_configure_owned_type_collection();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_configure_owned_type_collection_from_an_owned_type()
        {
            base.Can_configure_owned_type_collection_from_an_owned_type();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_configure_owned_type_collection_using_nested_closure()
        {
            base.Can_configure_owned_type_collection_using_nested_closure();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_configure_owned_type_collection_with_one_call()
        {
            base.Can_configure_owned_type_collection_with_one_call();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_configure_owned_type_inverse()
        {
            base.Can_configure_owned_type_inverse();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_configure_owned_type_using_nested_closure()
        {
            base.Can_configure_owned_type_using_nested_closure();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_configure_relationship_with_PK_ValueConverter()
        {
            base.Can_configure_relationship_with_PK_ValueConverter();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Can_map_base_of_owned_type()
        {
            base.Can_map_base_of_owned_type();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override void Shared_type_entity_types_with_FK_to_another_entity_works()
        {
            base.Shared_type_entity_types_with_FK_to_another_entity_works();
        }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }
}
