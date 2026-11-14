using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Many-to-many join entities configured with <c>UsingEntity</c> and no explicit key get a composite key from
///     their join foreign keys, as EF Core's key discovery convention provides.
/// </summary>
public sealed class JoinEntityKeyConventionTests : DuckDBTestBase
{
    [ConditionalFact]
    public void Join_entity_without_an_explicit_key_uses_its_foreign_keys_as_the_primary_key()
    {
        using var context = new SchoolContext(FileOptions<SchoolContext>());

        AssertJoinKeyAndRoundTrip(context);
    }

    [ConditionalFact]
    public void Join_entity_without_an_explicit_key_round_trips_on_DuckLake()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_join_key_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "data"));

        try
        {
            var options = new DbContextOptionsBuilder<SchoolContext>()
                .UseDuckLake(Path.Combine(root, "metadata.ducklake"), duckLake => duckLake.DataPath(Path.Combine(root, "data")))
                .Options;

            using var context = new SchoolContext(options);

            AssertJoinKeyAndRoundTrip(context);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertJoinKeyAndRoundTrip(SchoolContext context)
    {
        var key = context.Model.FindEntityType(typeof(Enrolment))!.FindPrimaryKey()!;
        Assert.Equal(
            [nameof(Enrolment.CourseCode), nameof(Enrolment.CourseYear), nameof(Enrolment.StudentId)],
            key.Properties.Select(property => property.Name).Order());

        context.Database.EnsureCreated();
        var maths = new Course { Code = "MATH", Year = 2026, Title = "Maths" };
        var physics = new Course { Code = "PHYS", Year = 2026, Title = "Physics" };
        context.AddRange(
            new Student { Id = 1, Name = "Ada", Courses = { maths, physics } },
            new Student { Id = 2, Name = "Alan", Courses = { maths } });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var counts = context.Set<Course>()
            .OrderBy(course => course.Code)
            .Select(course => new { course.Title, Students = course.Students.Count })
            .ToList();

        Assert.Equal([("Maths", 2), ("Physics", 1)], counts.Select(row => (row.Title, row.Students)).ToList());
        Assert.Equal(3, context.Set<Enrolment>().Count());
    }

    private sealed class SchoolContext(DbContextOptions<SchoolContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Student>().Property(student => student.Id).ValueGeneratedNever();
            modelBuilder.Entity<Course>().HasKey(course => new { course.Code, course.Year });
            modelBuilder.Entity<Student>()
                .HasMany(student => student.Courses)
                .WithMany(course => course.Students)
                .UsingEntity<Enrolment>(
                    // The composite course foreign key is left to convention, as in EF Core's many-to-many
                    // specification model.
                    right => right.HasOne(enrolment => enrolment.Course).WithMany(course => course.Enrolments),
                    left => left.HasOne(enrolment => enrolment.Student).WithMany(student => student.Enrolments).HasForeignKey(enrolment => enrolment.StudentId));
        }
    }

    private sealed class Student
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public List<Course> Courses { get; } = [];
        public List<Enrolment> Enrolments { get; } = [];
    }

    private sealed class Course
    {
        public required string Code { get; set; }
        public int Year { get; set; }
        public required string Title { get; set; }
        public List<Student> Students { get; } = [];
        public List<Enrolment> Enrolments { get; } = [];
    }

    private sealed class Enrolment
    {
        public int StudentId { get; set; }
        public required string CourseCode { get; set; }
        public int CourseYear { get; set; }
        public Student? Student { get; set; }
        public Course? Course { get; set; }
    }
}
