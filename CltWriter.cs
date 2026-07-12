using System.Globalization;
using System.Text;
using System.Xml;

namespace VpyAudioCutter;

public static class CltWriter
{
    public static void Write(string path, double framerate, string style, IReadOnlyList<TrimSection> sections)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false
        };

        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartElement("Cuts");
        writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        writer.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");
        writer.WriteElementString("Framerate", framerate.ToString("0.###############", CultureInfo.InvariantCulture));
        writer.WriteElementString("Style", style);
        writer.WriteStartElement("AllCuts");

        foreach (var section in sections)
        {
            writer.WriteStartElement("CutSection");
            writer.WriteElementString("startFrame", section.StartFrame.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("endFrame", section.EndFrame.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}
