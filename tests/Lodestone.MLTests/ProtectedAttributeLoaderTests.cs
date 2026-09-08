using FluentAssertions;
using Lodestone.ML.Evaluation;
using Xunit;

namespace Lodestone.MLTests;

public sealed class ProtectedAttributeLoaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"lodestone-fairness-{Guid.NewGuid():N}");

    [Fact]
    public void Load_KeysRowsExactlyAsTheObservationEnrollmentKeyIsFormatted()
    {
        // StudentActivityObservation.EnrollmentKey renders as "module/presentation/studentId". If
        // this drifts, every row fails to join and the audit silently reports on nobody.
        WriteStudentInfo("\"AAA\",\"2013J\",\"11391\",\"M\",\"Scotland\",\"HE Qualification\",\"90-100%\",\"55<=\",\"N\"");

        var attributes = ProtectedAttributeLoader.Load(_directory);

        attributes.Should().ContainKey("AAA/2013J/11391");
        attributes["AAA/2013J/11391"].Gender.Should().Be("M");
        attributes["AAA/2013J/11391"].Region.Should().Be("Scotland");
    }

    [Fact]
    public void Load_RepairsTheDeprivationBandOuladWritesWithoutAPercentSign()
    {
        // OULAD stores "10-20" where every sibling band carries a "%". Left alone it would report
        // as a category of its own, splitting one band into two and inventing a gap.
        WriteStudentInfo(
            "\"AAA\",\"2013J\",\"1\",\"M\",\"Scotland\",\"HE Qualification\",\"10-20\",\"0-35\",\"N\"",
            "\"AAA\",\"2013J\",\"2\",\"F\",\"Wales\",\"A Level or Equivalent\",\"20-30%\",\"0-35\",\"Y\"");

        var attributes = ProtectedAttributeLoader.Load(_directory);

        attributes["AAA/2013J/1"].DeprivationBand.Should().Be("10-20%");
        attributes["AAA/2013J/2"].DeprivationBand.Should().Be("20-30%");
    }

    [Fact]
    public void Load_KeepsAMissingValueAsItsOwnGroupRatherThanDroppingTheStudent()
    {
        // Students with no recorded deprivation band are a real population. Dropping them would
        // quietly shrink the audited cohort; naming them keeps the omission visible in the report.
        WriteStudentInfo("\"AAA\",\"2013J\",\"3\",\"M\",\"Wales\",\"No Formal quals\",\"\",\"0-35\",\"N\"");

        var attributes = ProtectedAttributeLoader.Load(_directory);

        attributes["AAA/2013J/3"].DeprivationBand.Should().Be(ProtectedAttributeLoader.NotRecorded);
    }

    [Fact]
    public void Load_ExposesEveryAuditedAttributeByName()
    {
        WriteStudentInfo("\"AAA\",\"2013J\",\"4\",\"F\",\"Ireland\",\"Post Graduate Qualification\",\"40-50%\",\"35-55\",\"Y\"");

        var attributes = ProtectedAttributeLoader.Load(_directory)["AAA/2013J/4"];

        // The report iterates ProtectedAttributes.Names, so an indexer gap would throw mid-audit.
        foreach (var name in ProtectedAttributes.Names)
        {
            attributes[name].Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Load_FailsLoudlyWhenTheDatasetIsAbsent()
    {
        var act = () => ProtectedAttributeLoader.Load(Path.Combine(_directory, "missing"));

        act.Should().Throw<FileNotFoundException>();
    }

    private void WriteStudentInfo(params string[] rows)
    {
        Directory.CreateDirectory(_directory);
        var header = "\"code_module\",\"code_presentation\",\"id_student\",\"gender\",\"region\"," +
                     "\"highest_education\",\"imd_band\",\"age_band\",\"disability\"";
        File.WriteAllLines(Path.Combine(_directory, "studentInfo.csv"), rows.Prepend(header));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
