using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for CONSUMERS.md "Semantic assumptions" #21 (enum CODES are the persisted TS ints;
/// For/Find are the exact editable-column set consumers' schema-driven editors generate from) and the
/// classification half of #22 (IsCadenceBreaking ⇔ Clears ≠ None — which fields warn/confirm is decided
/// here, once). Everything below is compiler-invisible: renumbering an enum code or flipping a Clears
/// scope compiles cleanly and silently corrupts a consumer's TS DB or skips a required cadence clear.
/// </summary>
public sealed class TsEditableSchemaContractTests
{
    // ---------------------------------------------------------------------------
    // #21 — enum codes are the ints TS persists (authored from the TS source enums:
    // ProjectState/ProjectPriority/TargetPriority in Database/Schema/Project.cs,
    // TwilightLevel in Astrometry/TwilightCircumstances.cs). Pinned value-by-value:
    // a consumer binds EnumValues to a dropdown and writes .Code straight into the DB.
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("ProjectState", new[] { 0, 1, 2, 3 }, new[] { "Draft", "Active", "Inactive", "Closed" })]
    [InlineData("ProjectPriority", new[] { 0, 1, 2 }, new[] { "Low", "Normal", "High" })]
    [InlineData("TargetPriority", new[] { -1, 0, 1, 2 }, new[] { "Default", "Low", "Normal", "High" })]
    [InlineData("TwilightLevel", new[] { 0, 1, 2, 3 }, new[] { "Nighttime", "Astronomical", "Nautical", "Civil" })]
    public void EnumValues_CodesAndLabels_MatchTheTsPersistedInts(string enumName, int[] codes, string[] labels)
    {
        IReadOnlyList<TsEnumValue> values = TsEditableSchema.EnumValues(enumName);

        Assert.Equal(codes.Length, values.Count);
        for (int i = 0; i < codes.Length; i++)
        {
            Assert.Equal(codes[i], values[i].Code);
            Assert.Equal(labels[i], values[i].Label);
        }
    }

    [Fact]
    public void EnumValues_UnknownOrNullName_IsEmptyNeverAThrow()
    {
        Assert.Empty(TsEditableSchema.EnumValues(null));
        Assert.Empty(TsEditableSchema.EnumValues("NoSuchEnum"));
    }

    [Fact]
    public void EnumValues_NameLookup_IsCaseInsensitive()
    {
        Assert.Equal(4, TsEditableSchema.EnumValues("projectstate").Count);
    }

    [Fact]
    public void EveryEnumTypedField_NamesAResolvableEnum()
    {
        foreach (TsField field in TsEditableSchema.Fields.Where(f => f.Type == TsFieldType.Enum))
            Assert.NotEmpty(TsEditableSchema.EnumValues(field.EnumName));
    }

    // ---------------------------------------------------------------------------
    // #21 — For/Find agreement: For(table) is the editor-generation set, Find is the
    // write whitelist; they must be the same reference, case-insensitively addressable.
    // ---------------------------------------------------------------------------

    [Fact]
    public void Find_ReturnsEveryFieldForListedByFor_AndIsCaseInsensitive()
    {
        foreach (TsTable table in Enum.GetValues<TsTable>())
        {
            foreach (TsField field in TsEditableSchema.For(table))
            {
                Assert.Same(field, TsEditableSchema.Find(table, field.Column));
                Assert.Same(field, TsEditableSchema.Find(table, field.Column.ToUpperInvariant()));
            }
        }
    }

    [Fact]
    public void Find_NonEditableColumn_ReturnsNull()
    {
        // Identity/stat columns are deliberately outside the editable surface.
        Assert.Null(TsEditableSchema.Find(TsTable.Target, "name"));
        Assert.Null(TsEditableSchema.Find(TsTable.ExposurePlan, "acquired"));
        Assert.Null(TsEditableSchema.Find(TsTable.Target, "guid"));
    }

    [Fact]
    public void EveryTable_HasEditableFields_AndAKnownSqliteName()
    {
        foreach (TsTable table in Enum.GetValues<TsTable>())
        {
            Assert.NotEmpty(TsEditableSchema.For(table));
            Assert.False(string.IsNullOrWhiteSpace(TsEditableSchema.TableName(table)));
        }

        // The four SQLite literals the editor interpolates — a rename here must break loudly.
        Assert.Equal("project", TsEditableSchema.TableName(TsTable.Project));
        Assert.Equal("target", TsEditableSchema.TableName(TsTable.Target));
        Assert.Equal("exposureplan", TsEditableSchema.TableName(TsTable.ExposurePlan));
        Assert.Equal("exposuretemplate", TsEditableSchema.TableName(TsTable.ExposureTemplate));
    }

    [Fact]
    public void ExposurePlanExposure_CarriesTheTemplateDefaultSentinel()
    {
        // #19/#20 hang off this metadata: -1 in exposureplan.exposure means "use the template's
        // default" and is legal to write back despite Min = 0 (sentinels are bounds-exempt).
        TsField exposure = TsEditableSchema.Find(TsTable.ExposurePlan, "exposure")!;
        Assert.Equal(-1.0, exposure.Sentinel);
        Assert.NotNull(exposure.SentinelLabel);
    }

    // ---------------------------------------------------------------------------
    // #22 (classification half) — IsCadenceBreaking ⇔ Clears ≠ None, and the exact
    // breaking set is pinned: a consumer's warn/confirm UX keys off this; a field
    // silently joining or leaving the set changes what gets confirmed (or cleared).
    // ---------------------------------------------------------------------------

    [Fact]
    public void IsCadenceBreaking_AgreesWithClearsScope_ForEveryField()
    {
        foreach (TsField field in TsEditableSchema.Fields)
        {
            Assert.Equal(
                field.Clears != TsCadenceClear.None,
                TsEditableSchema.IsCadenceBreaking(field.Table, field.Column));
        }

        Assert.False(TsEditableSchema.IsCadenceBreaking(TsTable.Target, "no-such-column"));
    }

    [Fact]
    public void TheCadenceBreakingSet_IsExactlyTheTwoKnownFields()
    {
        (TsTable Table, string Column, TsCadenceClear Clears)[] breaking = TsEditableSchema.Fields
            .Where(f => f.Clears != TsCadenceClear.None)
            .Select(f => (f.Table, f.Column, f.Clears))
            .ToArray();

        Assert.Equal(
            [
                (TsTable.Project, "filterswitchfrequency", TsCadenceClear.Project),
                (TsTable.ExposurePlan, "enabled", TsCadenceClear.Target),
            ],
            breaking);
    }
}
