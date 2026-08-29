using System.Globalization;

namespace Lodestone.MLTests;

internal sealed class OuladTestDataset : IDisposable
{
    private OuladTestDataset(string directoryPath) => DirectoryPath = directoryPath;

    public string DirectoryPath { get; }

    public static OuladTestDataset CreateTraining(
        int positiveStudents = 15,
        int negativeStudents = 15,
        bool separable = true)
    {
        var fixture = CreateEmpty();
        var students = Enumerable.Range(1, positiveStudents + negativeStudents).ToArray();
        Write(fixture, "courses.csv", "code_module,code_presentation,module_presentation_length\nAAA,2014J,70\n");
        Write(fixture, "assessments.csv", "code_module,code_presentation,id_assessment,date\nAAA,2014J,1,20\n");
        Write(fixture, "vle.csv",
            "id_site,code_module,code_presentation,activity_type\n" +
            "1,AAA,2014J,forumng\n" +
            "2,AAA,2014J,resource\n");

        var studentInfo = new List<string> { "code_module,code_presentation,id_student,final_result" };
        var registrations = new List<string>
        {
            "code_module,code_presentation,id_student,date_registration,date_unregistration"
        };
        var submissions = new List<string> { "id_assessment,id_student,date_submitted,is_banked" };
        var activity = new List<string>
        {
            "code_module,code_presentation,id_student,id_site,date,sum_click"
        };

        foreach (var student in students)
        {
            var positive = student <= positiveStudents;
            studentInfo.Add($"AAA,2014J,{student},{(positive ? "Withdrawn" : "Pass")}");
            registrations.Add($"AAA,2014J,{student},0,{(positive ? "55" : string.Empty)}");

            if (separable && !positive)
            {
                submissions.Add($"1,{student},18,0");
                foreach (var day in new[] { 0, 7, 14, 21, 27, 34, 41 })
                {
                    activity.Add($"AAA,2014J,{student},1,{day},2");
                    activity.Add($"AAA,2014J,{student},2,{day},8");
                }
            }
        }

        Write(fixture, "studentInfo.csv", string.Join('\n', studentInfo) + "\n");
        Write(fixture, "studentRegistration.csv", string.Join('\n', registrations) + "\n");
        Write(fixture, "studentAssessment.csv", string.Join('\n', submissions) + "\n");
        Write(fixture, "studentVle.csv", string.Join('\n', activity) + "\n");
        return fixture;
    }

    public static OuladTestDataset CreateFeatureSemantics()
    {
        var fixture = CreateEmpty();
        Write(fixture, "courses.csv", "code_module,code_presentation,module_presentation_length\nAAA,2014J,70\n");
        Write(fixture, "assessments.csv", "code_module,code_presentation,id_assessment,date\nAAA,2014J,1,20\n");
        Write(fixture, "vle.csv",
            "id_site,code_module,code_presentation,activity_type\n" +
            "1,AAA,2014J,forumng\n" +
            "2,AAA,2014J,resource\n");
        Write(fixture, "studentInfo.csv",
            "code_module,code_presentation,id_student,final_result\n" +
            "AAA,2014J,1,Withdrawn\n" +
            "AAA,2014J,2,Pass\n");
        Write(fixture, "studentRegistration.csv",
            "code_module,code_presentation,id_student,date_registration,date_unregistration\n" +
            "AAA,2014J,1,0,55\n" +
            "AAA,2014J,2,0,\n");
        Write(fixture, "studentAssessment.csv",
            "id_assessment,id_student,date_submitted,is_banked\n" +
            "1,1,25,0\n" +
            "1,2,18,0\n");
        Write(fixture, "studentVle.csv",
            "code_module,code_presentation,id_student,id_site,date,sum_click\n" +
            "AAA,2014J,1,1,0,3\n" +
            "AAA,2014J,1,2,27,5\n");
        return fixture;
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
            Directory.Delete(DirectoryPath, recursive: true);
    }

    private static OuladTestDataset CreateEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lodestone-oulad-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new OuladTestDataset(path);
    }

    private static void Write(OuladTestDataset fixture, string fileName, string content)
        => File.WriteAllText(Path.Combine(fixture.DirectoryPath, fileName), content);
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"lodestone-ml-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
