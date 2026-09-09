using System.Text.Json;
using FluentAssertions;
using VidroApi.Domain.Enums;

namespace VidroApi.UnitTests.Contracts;

/// <summary>
/// VidroFront mirrors these six enums by hand (<c>VidroFront/src/shared/types.ts</c>) because the
/// API serializes them as integers. Nothing in the type system connects the two sides, so a member
/// renamed or renumbered here would reach the browser as a silently wrong value —
/// <c>ReactionType</c> starting at 1 instead of 0 is exactly that kind of detail.
///
/// <c>contracts/enums.json</c> is the contract. This proves the backend matches it;
/// <c>VidroFront/src/tests/enum-contract.test.ts</c> proves the front matches the same file.
/// Changing a member means changing the golden and both sides in the same commit —
/// see contracts/README.md and the root CLAUDE.md.
/// </summary>
public class EnumContractTests
{
    private static readonly Dictionary<string, Dictionary<string, int>> Golden = LoadGolden();

    private static Dictionary<string, Dictionary<string, int>> LoadGolden()
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "contracts", "enums.json");
        var json = File.ReadAllText(goldenPath);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json)!;
    }

    // Every enum the front mirrors. One added here but not in the golden fails the test below.
    public static TheoryData<string, Type> MirroredEnums() => new()
    {
        { nameof(VideoStatus), typeof(VideoStatus) },
        { nameof(VideoVisibility), typeof(VideoVisibility) },
        { nameof(ReactionType), typeof(ReactionType) },
        { nameof(PlaylistVisibility), typeof(PlaylistVisibility) },
        { nameof(PlaylistScope), typeof(PlaylistScope) },
        { nameof(CommentSortOrder), typeof(CommentSortOrder) },
    };

    [Theory]
    [MemberData(nameof(MirroredEnums))]
    public void Enum_ShouldMatchTheSharedGolden(string name, Type enumType)
    {
        Golden.Should().ContainKey(name,
            "contracts/enums.json is the contract with VidroFront — add the enum there too");

        var actual = Enum.GetValues(enumType)
            .Cast<object>()
            .ToDictionary(value => value.ToString()!, value => (int)value);

        actual.Should().BeEquivalentTo(Golden[name],
            $"{name} is mirrored by hand in VidroFront/src/shared/types.ts");
    }

    [Fact]
    public void Golden_ShouldNotDescribeEnumsTheBackendNoLongerHas()
    {
        var mirroredNames = MirroredEnums().Select(row => (string)row[0]!);

        Golden.Keys.Should().BeEquivalentTo(mirroredNames,
            "a golden entry with no enum behind it is a contract nobody checks");
    }
}
