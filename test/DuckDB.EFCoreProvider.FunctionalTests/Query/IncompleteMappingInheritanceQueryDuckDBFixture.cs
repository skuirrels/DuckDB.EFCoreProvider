namespace Microsoft.EntityFrameworkCore.Query;

public class IncompleteMappingInheritanceQueryDuckDBFixture : TPHInheritanceQueryDuckDBFixture
{
    public override bool IsDiscriminatorMappingComplete
        => false;
}