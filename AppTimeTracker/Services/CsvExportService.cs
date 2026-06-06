using System.Text;
using AppTimeTracker.Models;
using System.IO;

namespace AppTimeTracker.Services;

public sealed class CsvExportService
{
    public void Export(string filePath, IEnumerable<UsageSession> sessions)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ProcessName,DisplayName,WindowTitle,StartTime,EndTime,DurationSeconds");

        foreach (var session in sessions)
        {
            builder.Append(Escape(session.ProcessName)).Append(',');
            builder.Append(Escape(session.DisplayName)).Append(',');
            builder.Append(Escape(session.WindowTitle ?? "")).Append(',');
            builder.Append(Escape(session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',');
            builder.Append(Escape(session.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")).Append(',');
            builder.Append(session.DurationSeconds).AppendLine();
        }

        File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
