using System.Text.RegularExpressions;

namespace BodyLife.Crm.Infrastructure.Tests.Architecture;

public sealed class BusinessTimeZoneOwnershipTests
{
    [Fact]
    public void ProductionBusinessDateDerivationIsCentralizedInBusinessTimeZone()
    {
        var solutionRoot = FindSolutionRoot();
        var sourceRoots = new[]
        {
            Path.Combine(solutionRoot, "src", "BodyLife.Crm"),
            Path.Combine(solutionRoot, "src", "BodyLife.Crm.Infrastructure"),
        };
        var violations = sourceRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith(
                Path.Combine("SharedKernel", "BusinessTimeZone.cs"),
                StringComparison.Ordinal))
            .Where(path => ContainsBusinessDateDerivation(File.ReadAllText(path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Business date conversion must use BusinessTimeZone; direct UTC/local "
            + "DateTime derivation or UTC-midnight helpers were found:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static bool ContainsBusinessDateDerivation(string source)
    {
        return Regex.IsMatch(
                   source,
                   @"DateOnly\.FromDateTime\s*\([^;]*(?:\.UtcDateTime|\.DateTime)",
                   RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                source,
                @"(?:UtcStartOfDay|ToUtcStartOfDay)\s*\(",
                RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                source,
                @"ToDateTime\s*\(\s*TimeOnly\.MinValue\s*,\s*DateTimeKind\.Utc",
                RegexOptions.CultureInvariant);
    }

    private static string FindSolutionRoot()
    {
        for (var current = new DirectoryInfo(Directory.GetCurrentDirectory());
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "BodyLife.Crm.sln")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the BodyLife CRM solution root.");
    }
}
