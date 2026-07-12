using Microsoft.EntityFrameworkCore.TestModels.UpdatesModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Update;

public class UpdatesDuckDBTest : UpdatesRelationalTestBase<UpdatesDuckDBTest.UpdatesDuckDBFixture>
{
    public UpdatesDuckDBTest(UpdatesDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_change_enums_with_conversion()
    {
        return base.Can_change_enums_with_conversion();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_change_type_of__dependent_by_replacing_with_new_dependent(bool async)
    {
        return base.Can_change_type_of__dependent_by_replacing_with_new_dependent(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_change_type_of_pk_to_pk_dependent_by_replacing_with_new_dependent(bool async)
    {
        return base.Can_change_type_of_pk_to_pk_dependent_by_replacing_with_new_dependent(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_shared_columns_with_conversion()
    {
        return base.Can_use_shared_columns_with_conversion();
    }

    public override void Identifiers_are_generated_correctly()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(
            typeof(
                LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectly
            ))!;
        Assert.Equal(
            "LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectly",
            entityType.GetTableName());
        Assert.Equal(
            "PK_LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectly",
            entityType.GetKeys().Single().GetName());
        Assert.Equal(
            "FK_LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectly_Profile_ProfileId_ProfileId1_ProfileId3_ProfileId4_ProfileId5_ProfileId6_ProfileId7_ProfileId8_ProfileId9_ProfileId10_ProfileId11_ProfileId12_ProfileId13_ProfileId14",
            entityType.GetForeignKeys().Single().GetConstraintName());
        Assert.Equal(
            "IX_LoginEntityTypeWithAnExtremelyLongAndOverlyConvolutedNameThatIsUsedToVerifyThatTheStoreIdentifierGenerationLengthLimitIsWorkingCorrectly_ProfileId_ProfileId1_ProfileId3_ProfileId4_ProfileId5_ProfileId6_ProfileId7_ProfileId8_ProfileId9_ProfileId10_ProfileId11_ProfileId12_ProfileId13_ProfileId14_ExtraProperty",
            entityType.GetIndexes().Single().GetDatabaseName());
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Save_with_shared_foreign_key()
    {
        return base.Save_with_shared_foreign_key();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.ReferencedRowsCannotBeUpdated)]
    public new Task SaveChanges_processes_all_tracked_entities(bool async)
    {
        return base.SaveChanges_processes_all_tracked_entities(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.ReferencedRowsCannotBeUpdated)]
    public new Task SaveChanges_false_processes_all_tracked_entities_without_calling_AcceptAllChanges(bool async)
    {
        return base.SaveChanges_false_processes_all_tracked_entities_without_calling_AcceptAllChanges(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.ReferencedRowsCannotBeUpdated)]
    public override Task Save_replaced_principal()
    {
        return base.Save_replaced_principal();
    }

    public class UpdatesDuckDBFixture : UpdatesRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}