using System.Text;
using System.Text.Json;
using System.Xml;
using SimpleEventViewer.Models;

namespace SimpleEventViewer.Services;

/// <summary>
/// Single source of truth for turning a list of <see cref="EventLogEntry"/>
/// into a serialised string. Used both by the right-click "Copy as…" menu
/// and the toolbar "Export current view" button so the on-disk and
/// on-clipboard payloads stay identical.
/// </summary>
public static class EventExporter
{
    public enum Format { Csv, Json, Xml }

    public static string Serialize(IEnumerable<EventLogEntry> entries, Format format) => format switch
    {
        Format.Json => ToJson(entries),
        Format.Xml  => ToXml(entries),
        _           => ToCsv(entries)
    };

    public static string DefaultExtension(Format format) => format switch
    {
        Format.Json => ".json",
        Format.Xml  => ".xml",
        _           => ".csv"
    };

    public static string FormatLabel(Format format) => format switch
    {
        Format.Json => "JSON",
        Format.Xml  => "XML",
        _           => "CSV"
    };

    private static string ToCsv(IEnumerable<EventLogEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Time,Level,ID,Source,User,Computer,Process,Thread,Channel,Task,Keywords,Message");
        foreach (var e in entries)
        {
            sb.Append(Escape(e.TimeCreatedDisplay)).Append(',');
            sb.Append(Escape(e.LevelName)).Append(',');
            sb.Append(e.Id).Append(',');
            sb.Append(Escape(e.ProviderName)).Append(',');
            sb.Append(Escape(e.Username)).Append(',');
            sb.Append(Escape(e.Computer)).Append(',');
            sb.Append(e.ProcessId).Append(',');
            sb.Append(e.ThreadId).Append(',');
            sb.Append(Escape(e.Channel)).Append(',');
            sb.Append(Escape(e.TaskName)).Append(',');
            sb.Append(Escape(e.Keywords)).Append(',');
            sb.AppendLine(Escape(e.Message));
        }
        return sb.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static string ToJson(IEnumerable<EventLogEntry> entries)
    {
        var simplified = entries.Select(e => new
        {
            time = e.TimeCreated,
            level = e.LevelName,
            id = e.Id,
            source = e.ProviderName,
            provider_guid = e.ProviderGuid,
            channel = e.Channel,
            task = e.TaskName,
            keywords = e.Keywords,
            user = e.Username,
            process_id = e.ProcessId,
            thread_id = e.ThreadId,
            computer = e.Computer,
            message = e.Message,
            xml = e.Xml
        });

        return JsonSerializer.Serialize(simplified, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ToXml(IEnumerable<EventLogEntry> entries)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = false,
            Encoding = Encoding.UTF8,
            CheckCharacters = false // event messages can contain otherwise-invalid XML chars
        };

        using var sw = new StringWriter();
        using (var w = XmlWriter.Create(sw, settings))
        {
            w.WriteStartDocument();
            w.WriteStartElement("Events");
            foreach (var e in entries)
            {
                w.WriteStartElement("Event");
                WriteAttr(w, "Time", e.TimeCreated.ToString("o"));
                WriteAttr(w, "Level", e.LevelName);
                WriteAttr(w, "Id", e.Id.ToString());
                WriteElem(w, "Source", e.ProviderName);
                WriteElem(w, "ProviderGuid", e.ProviderGuid);
                WriteElem(w, "Channel", e.Channel);
                WriteElem(w, "Task", e.TaskName);
                WriteElem(w, "Keywords", e.Keywords);
                WriteElem(w, "User", e.Username);
                WriteElem(w, "ProcessId", e.ProcessId.ToString());
                WriteElem(w, "ThreadId", e.ThreadId.ToString());
                WriteElem(w, "Computer", e.Computer);
                WriteElem(w, "Message", e.Message);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        return sw.ToString();
    }

    private static void WriteAttr(XmlWriter w, string name, string? value) => w.WriteAttributeString(name, value ?? string.Empty);
    private static void WriteElem(XmlWriter w, string name, string? value) => w.WriteElementString(name, value ?? string.Empty);
}
