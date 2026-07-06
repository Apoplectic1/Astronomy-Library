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
    public void For_ReturnsOnlyThatTablesFields()
    {
        IReadOnlyList<TsField> target = TsEditableSchema.For(TsTable.Target);
        Assert.Contains(target, f => f.Column == "active");
        Assert.All(target, f => Assert.Equal(TsTable.Target, f.Table));
    }
}
