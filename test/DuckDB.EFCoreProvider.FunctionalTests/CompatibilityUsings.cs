// EF10 specifications use xUnit v2; EF11 specifications use xUnit v3.
#if NET11_0_OR_GREATER
global using ITestOutputHelper = Xunit.ITestOutputHelper;
// Our overrides only use unconditional or explicitly skipped attributes, never member conditions.
global using ConditionalFactAttribute = Xunit.FactAttribute;
global using ConditionalTheoryAttribute = Xunit.TheoryAttribute;
global using Microsoft.EntityFrameworkCore.Query.Inheritance;
global using Microsoft.EntityFrameworkCore.BulkUpdates.Inheritance;
#else
global using ITestOutputHelper = Xunit.Abstractions.ITestOutputHelper;
#endif