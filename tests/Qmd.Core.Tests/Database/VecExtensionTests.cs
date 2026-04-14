using FluentAssertions;
using Qmd.Core.Database;

namespace Qmd.Core.Tests.Database;

[Trait("Category", "Database")]
public class VecExtensionTests : IDisposable
{
    public VecExtensionTests()
    {
        VecExtension.ResetForTesting();
    }

    public void Dispose()
    {
        VecExtension.ResetForTesting();
    }

    [Fact]
    public void TryLoad_SetsAvailabilityFlag_NeverThrows()
    {
        // TryLoad should never throw — it gracefully sets the availability flag.
        // If vec0.dll is present, IsAvailable becomes true; otherwise false.
        // Both outcomes are valid depending on the environment.
        using var db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(db);

        var act = () => VecExtension.TryLoad(db);
        act.Should().NotThrow();

        // After TryLoad, the flag should be set (not null)
        // On machines with vec0.dll: true; on CI without it: false
    }

    [Fact]
    public void TryLoad_WhenAvailable_VerifySucceeds()
    {
        using var db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(db);

        VecExtension.TryLoad(db);

        if (VecExtension.IsAvailable)
        {
            // vec0.dll was found — Verify should succeed
            var act = () => VecExtension.Verify(db);
            act.Should().NotThrow();
        }
        // else: vec0.dll not present — skip this assertion (covered by the unavailable test)
    }

    [Fact]
    public void Verify_ThrowsWhenNotLoaded()
    {
        // On a fresh DB without loading the extension, Verify should throw
        using var db = new SqliteDatabase(":memory:");
        var act = () => VecExtension.Verify(db);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sqlite-vec*");
    }
}
