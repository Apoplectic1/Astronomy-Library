using System.Reflection;
using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract test for the editor's write-surface shape — CONSUMERS.md assumption #9's
/// structural half. The 2026-07-24 audit found five public raw setters
/// (SetTargetActive / SetField / SetTargetField / SetPlanField / SetProjectField) that
/// bypassed every TrySetField gate; they were removed/internalized the same day. This pin
/// keeps the door shut: if a public ungated writer ever reappears on the editor, the
/// assumption "HasRequiredColumns gates all writes" silently stops being true again.
/// </summary>
public sealed class EditorWriteSurfaceContractTests
{
    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #9 (structural half):
    //   "TrySetField is the editor's only public write path."
    // Reflection over the public instance surface: no public Set* method may exist,
    // and TrySetField must. (SetField survives as the internal engine — visible to
    // Astronomy.Catalog.Tests via InternalsVisibleTo, invisible to consumers.)
    // ---------------------------------------------------------------------------

    [Fact]
    public void TargetSchedulerEditor_OnlyPublicWritePath_IsTrySetField()
    {
        MethodInfo[] publicMethods = typeof(TargetSchedulerEditor)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        string[] publicSetters = publicMethods
            .Where(m => m.Name.StartsWith("Set", StringComparison.Ordinal))
            .Select(m => m.Name)
            .ToArray();

        Assert.Empty(publicSetters);                                          // no ungated public writer
        Assert.Contains(publicMethods, m => m.Name == "TrySetField");         // the guarded gate exists
    }
}
