using DotnetEfCoreMcp.Server.AssemblyLoading;
using DotnetEfCoreMcp.Server.Compilation;
using DotnetEfCoreMcp.Server.DbContextDiscovery;
using DotnetEfCoreMcp.Server.Tests.TestSupport;

namespace DotnetEfCoreMcp.Server.Tests.Compilation;

/// <summary>Repro coverage for issue #118: query COMPILATION against a target that references a
/// newer EF Core patch than the server.
///
/// <para>#116 fixed the runtime bind for this situation. <see cref="SharedAssemblyResolver"/>
/// resolves shared assemblies by simple name, which ignores the requested version while still
/// yielding one shared copy, so <c>load_assembly</c> discovers contexts again and the version gap
/// is surfaced as a warning rather than silently dropping every <c>DbContext</c>-derived
/// type.</para>
///
/// <para>The compile path got no equivalent treatment. <c>QueryCompiler.GetReferences</c> builds
/// its <c>MetadataReference</c> set from the server's OWN loaded assemblies - <c>typeof(DbContext)
/// .Assembly</c> plus everything in <see cref="SharedFrameworkAssemblyNames"/> found via
/// <c>AppDomain.CurrentDomain.GetAssemblies()</c>. Roslyn then compiles the generated
/// <c>UserQuery</c> against the server's older reference assemblies while the target assembly it
/// must bind declares a dependency on the newer one. Roslyn applies the same exact-or-higher
/// version rule as the CLR binder and rejects the reference outright, so every query fails to
/// compile with "uses ... which has a higher version than referenced assembly", repeated once per
/// reference that transitively reaches EF Core.</para>
///
/// <para><c>QueryCompilerTests</c> only ever loads <c>SampleApp</c>, which inherits the server's
/// EF Core version through Central Package Management. Host and target therefore always agree
/// there and no existing compilation test can observe a version gap - which is why #116 could land
/// green while leaving this path broken. <c>NewerEfCoreApp</c> already pins one patch above the
/// server deliberately; it was simply never used by a compilation test.</para></summary>
public sealed class QueryCompilerNewerEfCoreVersionTests
{
    /// <summary><c>CatalogDbContext</c> derives from plain <c>DbContext</c>, so this isolates the
    /// EF Core version gap on its own, with no ASP.NET Core shared-framework involvement.</summary>
    [Fact]
    public async Task CompileAsync_TargetReferencesNewerEfCore_Compiles()
    {
        var handle = new AssemblyLoaderService().Load(FixturePaths.NewerEfCoreAppDllPath);
        var descriptor = DbContextScanner.FindDbContextTypes(handle.Assembly).Descriptors
            .Single(d => d.Name == "CatalogDbContext");
        var source = UserQuerySourceGenerator.Generate(descriptor.ClrType, "Products.Take(1)", "newerefcore1");
        var compiler = new QueryCompiler(new QueryCompilationOptions());

        var compiled = await compiler.CompileAsync(source, handle, CancellationToken.None);

        Assert.NotEmpty(compiled.Pe);
    }

    /// <summary><c>AccountDbContext</c> derives from <c>IdentityDbContext&lt;IdentityUser&gt;</c>,
    /// so its inherited member surface additionally reaches
    /// Microsoft.AspNetCore.Identity.EntityFrameworkCore. Both assemblies are on the hard-share
    /// list, so both are referenced from the server's copies and both report the version
    /// gap.</summary>
    [Fact]
    public async Task CompileAsync_IdentityTargetReferencesNewerEfCore_Compiles()
    {
        var handle = new AssemblyLoaderService().Load(FixturePaths.NewerEfCoreAppDllPath);
        var descriptor = DbContextScanner.FindDbContextTypes(handle.Assembly).Descriptors
            .Single(d => d.Name == "AccountDbContext");
        var source = UserQuerySourceGenerator.Generate(descriptor.ClrType, "Products.Take(1)", "newerefcore2");
        var compiler = new QueryCompiler(new QueryCompilationOptions());

        var compiled = await compiler.CompileAsync(source, handle, CancellationToken.None);

        Assert.NotEmpty(compiled.Pe);
    }
}
