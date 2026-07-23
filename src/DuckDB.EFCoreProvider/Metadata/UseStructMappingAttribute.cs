namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Declares that a complex property is backed by a DuckDB <c>STRUCT</c> column.
///     When applied, the <see cref="Conventions.DuckDBStructFieldConvention" />
///     automatically infers <c>HasColumnName</c> and <c>HasStructField</c> metadata from the
///     complex property hierarchy at model finalization time.
/// </summary>
/// <remarks>
///     <para>
///         Place this attribute on the complex property in the entity class:
///     </para>
///     <code>
///         public class Customer
///         {
///             [UseStructMapping]
///             public required Location Location { get; set; }
///         }
///     </code>
///     <para>
///         For programmatic configuration, use the fluent API:
///         <c>modelBuilder.Entity&lt;Customer&gt;().ComplexProperty(c =&gt; c.Location).UseStructMapping()</c>.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class UseStructMappingAttribute : Attribute;
