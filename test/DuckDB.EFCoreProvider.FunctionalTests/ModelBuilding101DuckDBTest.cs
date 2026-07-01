using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class ModelBuilding101DuckDBTest : ModelBuilding101RelationalTestBase
{
    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void BasicManyToManyTest()
    {
        base.BasicManyToManyTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ManyToManyWithJoinClassHavingPrimaryKeyTest()
    {
        base.ManyToManyWithJoinClassHavingPrimaryKeyTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ManyToManyWithJoinClassTest()
    {
        base.ManyToManyWithJoinClassTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ManyToManyWithNamedFksAndNavsToAndFromJoinClassTest()
    {
        base.ManyToManyWithNamedFksAndNavsToAndFromJoinClassTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ManyToManyWithNavsAndAlternateKeysTest()
    {
        base.ManyToManyWithNavsAndAlternateKeysTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ManyToManyWithNavsToAndFromJoinClassTest()
    {
        base.ManyToManyWithNavsToAndFromJoinClassTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ManyToManyWithNavsToJoinClassTest()
    {
        base.ManyToManyWithNavsToJoinClassTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ManyToManyWithPayloadAndNavsToJoinClassShadowFKsTest()
    {
        base.ManyToManyWithPayloadAndNavsToJoinClassShadowFKsTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ManyToManyWithPayloadAndNavsToJoinClassTest()
    {
        base.ManyToManyWithPayloadAndNavsToJoinClassTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyOptionalNoNavigationsNrtTest()
    {
        base.OneToManyOptionalNoNavigationsNrtTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyOptionalNoNavigationsTest()
    {
        base.OneToManyOptionalNoNavigationsTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredNoNavigationsNrtTest()
    {
        base.OneToManyRequiredNoNavigationsNrtTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredNoNavigationsTest()
    {
        base.OneToManyRequiredNoNavigationsTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredNoNavigationToDependentsTest()
    {
        base.OneToManyRequiredNoNavigationToDependentsTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredNoNavigationToPrincipalNrtTest()
    {
        base.OneToManyRequiredNoNavigationToPrincipalNrtTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredNoNavigationToPrincipalTest()
    {
        base.OneToManyRequiredNoNavigationToPrincipalTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredNoNavigationsNrtTest()
    {
        base.OneToOneRequiredNoNavigationsNrtTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredWithShadowFkWithCompositeKeyTest()
    {
        base.OneToManyRequiredWithShadowFkWithCompositeKeyTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredTest()
    {
        base.OneToManyRequiredTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredNoNavigationToDependentsTest()
    {
        base.OneToOneRequiredNoNavigationToDependentsTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredWithShadowFkWithAlternateKeyTest()
    {
        base.OneToManyRequiredWithShadowFkWithAlternateKeyTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredPkToPkTest()
    {
        base.OneToOneRequiredPkToPkTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredWithAlternateKeyNrtTest()
    {
        base.OneToOneRequiredWithAlternateKeyNrtTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneOptionalNoNavigationsNrtTest()
    {
        base.OneToOneOptionalNoNavigationsNrtTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredWithAlternateKeyTest()
    {
        base.OneToManyRequiredWithAlternateKeyTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredWithoutCascadeDeleteTest()
    {
        base.OneToOneRequiredWithoutCascadeDeleteTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredPkToPkNrtTest()
    {
        base.OneToOneRequiredPkToPkNrtTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredWithShadowFkTest()
    {
        base.OneToOneRequiredWithShadowFkTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredTest()
    {
        base.OneToOneRequiredTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredWithCompositeKeyTest()
    {
        base.OneToManyRequiredWithCompositeKeyTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredWithShadowFkTest()
    {
        base.OneToManyRequiredWithShadowFkTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredNoNavigationToPrincipalTest()
    {
        base.OneToOneRequiredNoNavigationToPrincipalTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredNoNavigationToPrincipalNrtTest()
    {
        base.OneToOneRequiredNoNavigationToPrincipalNrtTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredWithShadowFkAndNoNavigationToPrincipalTest()
    {
        base.OneToManyRequiredWithShadowFkAndNoNavigationToPrincipalTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredWithCompositeKeyTest()
    {
        base.OneToOneRequiredWithCompositeKeyTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredWithShadowFkAndNoNavigationToDependentsTest()
    {
        base.OneToManyRequiredWithShadowFkAndNoNavigationToDependentsTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void UnidirectionalManyToManyTest()
    {
        base.UnidirectionalManyToManyTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredWithAlternateKeyTest()
    {
        base.OneToOneRequiredWithAlternateKeyTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredNoNavigationsTest()
    {
        base.OneToOneRequiredNoNavigationsTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneOneToOneRequiredWithShadowFkWithCompositeKeyTest()
    {
        base.OneToOneOneToOneRequiredWithShadowFkWithCompositeKeyTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneRequiredWithShadowFkAndNoNavigationToDependentsTest()
    {
        base.OneToOneRequiredWithShadowFkAndNoNavigationToDependentsTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredWithAlternateKeyNrtTest()
    {
        base.OneToManyRequiredWithAlternateKeyNrtTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToManyRequiredWithoutCascadeDeleteTest()
    {
        base.OneToManyRequiredWithoutCascadeDeleteTest();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void OneToOneOptionalNoNavigationsTest()
    {
        base.OneToOneOptionalNoNavigationsTest();
    }

    protected override DbContextOptionsBuilder ConfigureContext(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseDuckDB();
}
