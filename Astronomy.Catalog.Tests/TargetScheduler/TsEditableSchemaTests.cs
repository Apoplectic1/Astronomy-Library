using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Catalog.Tests;

// The declarative reference is the editor's write whitelist and the consumer's UI dictionary, so its integrity
// (no duplicate columns, correct cadence flags, enums named) is worth pinning.
public sealed class TsEditableSchemaTests
{
    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.NotNull(TsEditableSchema.Find(TsTable.Project, "MinimumAltitude"));
        Assert.NotNull(TsEditableSchema.Find(TsTable.Target, "ACTIVE"));
    }

    [Fact]
    public void Find_OmittedOrStatColumn_ReturnsNull()
    {
        Assert.Null(TsEditableSchema.Find(TsTable.Target, "name"));        // deliberately omitted (matcher round-trip)
        Assert.Null(TsEditableSchema.Find(TsTable.ExposurePlan, "acquired")); // a stat, not user-editable
        Assert.Null(TsEditableSchema.Find(TsTable.Project, "no_such_column"));
    }

    [Fact]
    public void IsCadenceBreaking_FlagsOnlyTheKnownBreakers()
    {
        Assert.True(TsEditableSchema.IsCadenceBreaking(TsTable.ExposurePlan, "enabled"));
        Assert.True(TsEditableSchema.IsCadenceBreaking(TsTable.Project, "filterswitchfrequency"));
        Assert.False(TsEditableSchema.IsCadenceBreaking(TsTable.Target, "active"));   // target enable IS cadence-safe
        Assert.False(TsEditableSchema.IsCadenceBreaking(TsTable.Project, "state"));
        Assert.False(TsEditableSchema.IsCadenceBreaking(TsTable.Project, "no_such_column")); // unknown => false
    }

    [Fact]
    public void CadenceClearScopes_MatchTheTsSourcePaths()
    {
        Assert.Equal(TsCadenceClear.Target, TsEditableSchema.Find(TsTable.ExposurePlan, "enabled")!.Clears);
        Assert.Equal(TsCadenceClear.Project, TsEditableSchema.Find(TsTable.Project, "filterswitchfrequency")!.Clears);
        Assert.All(
            TsEditableSchema.Fields.Where(f => f is not ({ Table: TsTable.ExposurePlan, Column: "enabled" }
                                            or { Table: TsTable.Project, Column: "filterswitchfrequency" })),
            f => Assert.Equal(TsCadenceClear.None, f.Clears));   // IsCadenceBreaking ≡ Clears != None
    }

    [Fact]
    public void TableName_MapsEachTable()
    {
        Assert.Equal("project", TsEditableSchema.TableName(TsTable.Project));
        Assert.Equal("target", TsEditableSchema.TableName(TsTable.Target));
        Assert.Equal("exposureplan", TsEditableSchema.TableName(TsTable.ExposurePlan));
        Assert.Equal("exposuretemplate", TsEditableSchema.TableName(TsTable.ExposureTemplate));
    }

    [Fact]
    public void Fields_HaveUniqueTableColumnPairs()
    {
        int distinct = TsEditableSchema.Fields
            .Select(f => (f.Table, f.Column.ToLowerInvariant())).Distinct().Count();
        Assert.Equal(TsEditableSchema.Fields.Count, distinct);
    }

    [Fact]
    public void EnumFields_NameAnEnum()
    {
        Assert.All(
            TsEditableSchema.Fields.Where(f => f.Type == TsFieldType.Enum),
            f => Assert.False(string.IsNullOrWhiteSpace(f.EnumName)));
    }

    [Fact]
    public void EnumValues_EveryEnumFieldResolvesToANonEmptyMap()
    {
        Assert.All(
            TsEditableSchema.Fields.Where(f => f.Type == TsFieldType.Enum),
            f => Assert.NotEmpty(TsEditableSchema.EnumValues(f.EnumName)));
    }

    [Fact]
    public void EnumValues_TargetPriorityIncludesDefaultAtMinusOne()
    {
        IReadOnlyList<TsEnumValue> values = TsEditableSchema.EnumValues("TargetPriority");
        Assert.Equal(new TsEnumValue(-1, "Default"), values[0]);
        Assert.Equal(4, values.Count);
    }

    [Fact]
    public void EnumValues_IsCaseInsensitive_AndEmptyForUnknown()
    {
        Assert.NotEmpty(TsEditableSchema.EnumValues("projectstate"));
        Assert.Empty(TsEditableSchema.EnumValues("NoSuchEnum"));
        Assert.Empty(TsEditableSchema.EnumValues(null));
    }

    [Fact]
    public void Sentinels_DeclareTsDeferToDefaultColumns()
    {
        // exposureplan.exposure defers to the template; template gain/offset/readoutmode defer to the camera.
        Assert.Equal(-1, TsEditableSchema.Find(TsTable.ExposurePlan, "exposure")!.Sentinel);
        Assert.Equal("template default", TsEditableSchema.Find(TsTable.ExposurePlan, "exposure")!.SentinelLabel);
        foreach (string column in new[] { "gain", "offset", "readoutmode" })
        {
            TsField field = TsEditableSchema.Find(TsTable.ExposureTemplate, column)!;
            Assert.Equal(-1, field.Sentinel);
            Assert.Equal("camera default", field.SentinelLabel);
        }
        Assert.Null(TsEditableSchema.Find(TsTable.ExposurePlan, "desired")!.Sentinel);
    }

    [Fact]
    public void Sentinels_AlwaysSitOutsideTheFieldsBounds()
    {
        // A consumer must exempt the sentinel from Min/Max clamping — pin that the reference keeps every
        // sentinel outside its own bounds, so clamped == sentinel can never be ambiguous.
        Assert.All(
            TsEditableSchema.Fields.Where(f => f.Sentinel is not null),
            f =>
            {
                Assert.NotNull(f.SentinelLabel);
                if (f.Min is double min) Assert.True(f.Sentinel < min);
            });
    }

    [Fact]
    public void Guarded_FlagsRotationOnly()
    {
        Assert.True(TsEditableSchema.Find(TsTable.Target, "rotation")!.Guarded);
        Assert.All(
            TsEditableSchema.Fields.Where(f => f is not { Table: TsTable.Target, Column: "rotation" }),
            f => Assert.False(f.Guarded));
    }

    [Fact]
    public void For_ReturnsOnlyThatTablesFields()
    {
        IReadOnlyList<TsField> target = TsEditableSchema.For(TsTable.Target);
        Assert.Contains(target, f => f.Column == "active");
        Assert.All(target, f => Assert.Equal(TsTable.Target, f.Table));
    }

    [Fact]
    public void ExposureTemplate_CarriesTheFullSchedulingSurface()
    {
        // The 7 camera/filter columns plus the 11 scheduling-condition columns (twilight, moon suite,
        // dither, humidity) — a consumer's template form renders all of these from the reference alone.
        IReadOnlyList<TsField> fields = TsEditableSchema.For(TsTable.ExposureTemplate);
        Assert.Equal(18, fields.Count);
        foreach (string column in new[]
        {
            "twilightlevel", "minutesoffset", "moonavoidanceenabled", "moonavoidanceseparation",
            "moonavoidancewidth", "moonrelaxscale", "moonrelaxmaxaltitude", "moonrelaxminaltitude",
            "moondownenabled", "ditherevery", "maximumhumidity",
        })
            Assert.NotNull(TsEditableSchema.Find(TsTable.ExposureTemplate, column));
        Assert.All(fields, f => Assert.Equal(TsCadenceClear.None, f.Clears));   // template columns never clear the TS cadence
    }

    [Fact]
    public void ExposureTemplate_SchedulingBounds_MatchTsSemantics()
    {
        Assert.Equal(TsFieldType.Enum, TsEditableSchema.Find(TsTable.ExposureTemplate, "twilightlevel")!.Type);
        TsField minutes = TsEditableSchema.Find(TsTable.ExposureTemplate, "minutesoffset")!;
        Assert.Equal(-720, minutes.Min);                        // negative offsets are legal in TS
        TsField relaxMin = TsEditableSchema.Find(TsTable.ExposureTemplate, "moonrelaxminaltitude")!;
        Assert.Equal(-90, relaxMin.Min);                        // TS ships -15 — the bound must not exclude it
        TsField humidity = TsEditableSchema.Find(TsTable.ExposureTemplate, "maximumhumidity")!;
        Assert.Equal(0, humidity.Min);
        Assert.Equal(100, humidity.Max);
        Assert.Equal(180, TsEditableSchema.Find(TsTable.ExposureTemplate, "moonavoidanceseparation")!.Max);
        TsField dither = TsEditableSchema.Find(TsTable.ExposureTemplate, "ditherevery")!;
        Assert.Equal(-1, dither.Sentinel);                      // -1 = defer to the project (TS planner tests >= 0)
        Assert.Equal("project default", dither.SentinelLabel);
    }

    [Fact]
    public void EnumValues_TwilightLevel_MatchesTheTsSourceCodes()
    {
        IReadOnlyList<TsEnumValue> values = TsEditableSchema.EnumValues("TwilightLevel");
        Assert.Equal(
            [new(0, "Nighttime"), new(1, "Astronomical"), new(2, "Nautical"), new(3, "Civil")],
            values);
    }
}
