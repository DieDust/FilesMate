using Loc = FilesMate.App.Localization.StringTable;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace FilesMate.SearchHost;

/// <summary>Bounded read-only OOXML pictures, slide layout and cached chart data.</summary>
internal sealed class OfficeVisuals : IDisposable
{
    private readonly ZipArchive _zip;
    private long _imageBytes;
    internal static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    internal static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    internal static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    internal OfficeVisuals(string path) => _zip = ZipFile.OpenRead(path);
    public void Dispose() => _zip.Dispose();
    internal XDocument Read(string part)
    {
        var entry = _zip.GetEntry(part) ?? throw new InvalidDataException("Missing Office part");
        if(entry.Length>8*1024*1024)throw new InvalidDataException("Office part too large");
        using var input=entry.Open();
        using var reader=XmlReader.Create(input,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=8*1024*1024 });
        return XDocument.Load(reader);
    }
    internal Dictionary<string,string> Links(string part)
    {
        var slash=part.LastIndexOf('/');var rel=part[..(slash+1)]+"_rels/"+part[(slash+1)..]+".rels";
        if(_zip.GetEntry(rel)is null)return new();
        var links=new Dictionary<string,string>();
        foreach(var item in Read(rel).Root!.Elements())
        {
            if((string?)item.Attribute("TargetMode")=="External")continue;
            if(!Uri.TryCreate(new Uri("https://office.local/"+part),(string?)item.Attribute("Target"),out var uri)||uri.Host!="office.local"||uri.Scheme!="https")continue;
            var id=(string?)item.Attribute("Id");if(id is not null)links[id]=Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        }
        return links;
    }
    private static string Esc(string? value) => WebUtility.HtmlEncode(value??"");
    private static string N(double value) => value.ToString("0.###",CultureInfo.InvariantCulture);
    private static double Number(XAttribute? value,double fallback=0) => double.TryParse((string?)value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:fallback;

    internal string Image(string? part)
    {
        if(part is null || _imageBytes>=8*1024*1024)return "";
        var entry=_zip.GetEntry(part);if(entry is null ||entry.Length>4*1024*1024)return "";
        if(Path.GetExtension(part).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff"))return "";
        try
        {
            using var input=entry.Open();using var bytes=new MemoryStream();input.CopyTo(bytes);
            return RasterImage(bytes.ToArray(), ref _imageBytes);
        }
        catch(Exception e)when(e is NotSupportedException or FileFormatException or ArgumentException or IOException){return "";}
    }

    internal static string RasterImage(byte[] data, ref long imageBytes)
    {
        if (data.Length > 4 * 1024 * 1024 || imageBytes >= 8 * 1024 * 1024) return "";
        try
        {
            using var bytes = new MemoryStream(data, writable: false);
            var decoder=BitmapDecoder.Create(bytes,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
            var frame=decoder.Frames[0];if((long)frame.PixelWidth*frame.PixelHeight>16_000_000)return "";
            bytes.Position=0;
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=Math.Min(1280,frame.PixelWidth);image.StreamSource=bytes;image.EndInit();image.Freeze();
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=new MemoryStream();encoder.Save(output);
            if(imageBytes+output.Length>8*1024*1024)return "";imageBytes+=output.Length;
            return "<img loading='lazy' alt='" + Loc.Html("Preview_EmbeddedImage") + "' src='data:image/png;base64,"+Convert.ToBase64String(output.ToArray())+"'>";
        }
        catch(Exception e)when(e is NotSupportedException or FileFormatException or ArgumentException or IOException){return "";}
    }

    internal string Chart(string part)
    {
        var doc=Read(part);var title=string.Join(" ",doc.Descendants(C+"title").Descendants(A+"t").Select(t=>t.Value));
        var series=doc.Descendants(C+"ser").Take(8).Select(s=>new {
            Name=s.Element(C+"tx")?.Descendants(C+"v").FirstOrDefault()?.Value??Loc.Get("Preview_ChartData"),
            Labels=s.Element(C+"cat")?.Descendants(C+"pt").Take(32).Select(p=>p.Element(C+"v")?.Value??"").ToArray()??[],
            Values=(s.Element(C+"val")??s.Element(C+"yVal"))?.Descendants(C+"pt").Take(32).Select(p=>double.TryParse(p.Element(C+"v")?.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:0).ToArray()??[]
        }).Where(s=>s.Values.Length>0).ToArray();
        if(series.Length==0)return "<p class='note'>" + Loc.Html("Preview_ChartEmpty") + "</p>";
        var html=new StringBuilder("<figure class='chart'><figcaption>"+Esc(title)+"</figcaption>");
        var colors=new[]{"#1677d2","#df8433","#359665","#9561bd","#ce5968","#427f8f","#757575","#b69a31"};
        var grouped=doc.Descendants(C+"grouping").Any(e=>(string?)e.Attribute("val") is "stacked" or "percentStacked");
        var bars=!grouped&&doc.Descendants(C+"barChart").Any();var lines=!grouped&&doc.Descendants(C+"lineChart").Any();
        if(bars||lines)
        {
            var min=Math.Min(0,series.Min(s=>s.Values.Min()));var max=Math.Max(0,series.Max(s=>s.Values.Max()));var span=Math.Max(1,max-min);
            double Y(double n)=>250-(n-min)/span*220;
            var count=series.Max(s=>s.Values.Length);var slot=540d/Math.Max(1,count);var baseline=Y(0);
            html.Append("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 620 300' role='img' aria-label='" + Loc.Html("Preview_Chart") + "'>");
            html.Append("<path d='M50 25V250H600' fill='none' stroke='#aaa'/><path d='M50 "+N(baseline)+"H600' stroke='#bbb'/>");
            for(var k=0;k<series.Length;k++)
            {
                var points=new List<string>();
                for(var i=0;i<series[k].Values.Length;i++)
                {
                    var x=55+i*slot;var y=Y(series[k].Values[i]);
                    if(bars)html.Append($"<rect x='{N(x+k*slot/series.Length)}' y='{N(Math.Min(y,baseline))}' width='{N(Math.Max(.5,slot/series.Length-2))}' height='{N(Math.Max(.5,Math.Abs(y-baseline)))}' fill='{colors[k]}'/>");
                    else points.Add(N(x+slot/2)+","+N(y));
                }
                if(lines)html.Append("<polyline fill='none' stroke='"+colors[k]+"' stroke-width='2.5' points='"+string.Join(' ',points)+"'/>");
            }
            for(var i=0;i<count;i+=Math.Max(1,(int)Math.Ceiling(count/8d)))
            {
                var label=series[0].Labels.ElementAtOrDefault(i)??(i+1).ToString();
                html.Append("<text x='"+N(55+(i+.5)*slot)+"' y='273' text-anchor='middle' font-size='11' fill='#555'>"+Esc(label.Length>10?label[..10]+"…":label)+"</text>");
            }
            html.Append("</svg>");
        }
        else if(doc.Descendants(C+"pieChart").Any()||doc.Descendants(C+"doughnutChart").Any())
        {
            var values=series[0].Values;var total=values.Sum();
            if(total>0&&double.IsFinite(total)&&values.All(v=>v>=0))
            {
                html.Append("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 280' role='img' aria-label='" + Loc.Html("Preview_PieChart") + "'>");
                var angle=-Math.PI/2;
                for(var i=0;i<values.Length;i++)
                {
                    if(values[i]<=0)continue;
                    var next=angle+values[i]/total*Math.PI*2;
                    if(values[i]==total)html.Append("<circle cx='200' cy='140' r='110' fill='"+colors[i%colors.Length]+"'/>");
                    else html.Append($"<path d='M200 140 L{N(200+110*Math.Cos(angle))} {N(140+110*Math.Sin(angle))} A110 110 0 {(next-angle>Math.PI?1:0)} 1 {N(200+110*Math.Cos(next))} {N(140+110*Math.Sin(next))} Z' fill='{colors[i%colors.Length]}'/>");
                    angle=next;
                }
                if(doc.Descendants(C+"doughnutChart").Any())html.Append("<circle cx='200' cy='140' r='55' fill='white'/>");
                html.Append("</svg>");
            }
        }
        html.Append("<table><tr><th>" + Loc.Html("Preview_Category") + "</th>");foreach(var s in series)html.Append("<th>"+Esc(s.Name)+"</th>");html.Append("</tr>");
        for(var i=0;i<series.Max(s=>s.Values.Length);i++)
        {
            html.Append("<tr><th>"+Esc(series[0].Labels.ElementAtOrDefault(i)??(i+1).ToString())+"</th>");
            foreach(var s in series)html.Append("<td>"+(i<s.Values.Length?N(s.Values[i]):"")+"</td>");html.Append("</tr>");
        }
        return html.Append("</table><p class='note'>" + Loc.Html("Preview_ChartLimits") + "</p></figure>").ToString();
    }

    internal string SheetVisuals(string sheetName)
    {
        XNamespace s="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbook=Read("xl/workbook.xml");var links=Links("xl/workbook.xml");
        var sheet=workbook.Descendants(s+"sheet").FirstOrDefault(e=>(string?)e.Attribute("name")==sheetName);
        if(sheet is null||!links.TryGetValue((string?)sheet.Attribute(R+"id")??"",out var part))return "";
        var sheetLinks=Links(part);var html=new StringBuilder();
        foreach(var drawing in Read(part).Descendants(s+"drawing").Take(8))
        {
            if(!sheetLinks.TryGetValue((string?)drawing.Attribute(R+"id")??"",out var target))continue;
            var drawingLinks=Links(target);var content=Read(target);
            foreach(var item in content.Descendants().Where(e=>e.Name==A+"blip"||e.Name==C+"chart").Take(24))
            {
                var id=(string?)item.Attribute(item.Name==A+"blip"?R+"embed":R+"id");
                if(id is null||!drawingLinks.TryGetValue(id,out var resource))continue;
                html.Append(item.Name==A+"blip"?"<figure>"+Image(resource)+"</figure>":Chart(resource));
            }
        }
        return html.ToString();
    }

    internal string Slides()
    {
        var presentation=Read("ppt/presentation.xml");var size=presentation.Root?.Element(P+"sldSz");
        var width=Math.Max(1,Number(size?.Attribute("cx"),9144000));var height=Math.Max(1,Number(size?.Attribute("cy"),6858000));
        var links=Links("ppt/presentation.xml");var html=new StringBuilder("<!doctype html><html><head><style>.slide{position:relative;width:100%;background:white;overflow:hidden;border:1px solid #ddd}.shape{position:absolute;overflow:hidden;line-height:1.2}.shape img{width:100%;height:100%;object-fit:contain}.shape p{margin:0 0 .3em}.chart{margin:8px 0}.chart svg{max-height:300px;width:100%}.slide table{width:100%;font-size:12px}.slide .chart table{font-size:10px}</style></head><body><p class='note'>" + Loc.Html("Preview_SlideHint") + "</p>");
        var index=0;
        foreach(var id in presentation.Descendants(P+"sldId").Take(100))
        {
            if(!links.TryGetValue((string?)id.Attribute(R+"id")??"",out var part))continue;
            var slide=Read(part);var resources=Links(part);
            html.Append("<section><h2>"+System.Net.WebUtility.HtmlEncode(Loc.Format("Preview_SlideNumber", ++index))+"</h2><div class='slide' style='aspect-ratio:"+N(width/height)+"'>");
            foreach(var shape in slide.Descendants().Where(e=>e.Name==P+"sp"||e.Name==P+"pic"||e.Name==P+"graphicFrame").Take(150))
            {
                var transform=shape.Descendants().FirstOrDefault(e=>e.Name.LocalName=="xfrm");
                var off=transform?.Elements().FirstOrDefault(e=>e.Name.LocalName=="off");var ext=transform?.Elements().FirstOrDefault(e=>e.Name.LocalName=="ext");
                var x=Number(off?.Attribute("x"));var y=Number(off?.Attribute("y"));var w=Number(ext?.Attribute("cx"),width);var h=Number(ext?.Attribute("cy"),height/5);
                var color=shape.Element(P+"spPr")?.Element(A+"solidFill")?.Element(A+"srgbClr")?.Attribute("val")?.Value;
                html.Append("<div class='shape' style='left:"+N(x/width*100)+"%;top:"+N(y/height*100)+"%;width:"+N(w/width*100)+"%;height:"+N(h/height*100)+"%;"+(color is {Length:6}&&color.All(Uri.IsHexDigit)?"background:#"+color+";":"")+"'>");
                var blip=shape.Descendants(A+"blip").FirstOrDefault();
                if(blip is not null&&resources.TryGetValue((string?)blip.Attribute(R+"embed")??"",out var image))html.Append(Image(image));
                var chart=shape.Descendants(C+"chart").FirstOrDefault();
                if(chart is not null&&resources.TryGetValue((string?)chart.Attribute(R+"id")??"",out var chartPart))html.Append(Chart(chartPart));
                var table=shape.Descendants(A+"tbl").FirstOrDefault();
                if(table is not null)
                {
                    html.Append("<table>");foreach(var row in table.Elements(A+"tr").Take(50)) { html.Append("<tr>");foreach(var cell in row.Elements(A+"tc").Take(20))html.Append("<td>"+Esc(string.Join(" ",cell.Descendants(A+"t").Select(t=>t.Value)))+"</td>");html.Append("</tr>"); }html.Append("</table>");
                }
                else foreach(var p in shape.Elements(P+"txBody").Elements(A+"p"))
                {
                    var text=string.Concat(p.Descendants(A+"t").Select(t=>t.Value));var font=Number(p.Descendants(A+"rPr").FirstOrDefault()?.Attribute("sz"),1800)/100;
                    html.Append("<p style='font-size:clamp(9px,"+N(Math.Clamp(font,6,96)/720*100)+"vw,64px)'>"+Esc(text)+"</p>");
                }
                html.Append("</div>");
            }
            html.Append("</div></section>");if(html.Length>16*1024*1024)break;
        }
        return html.Append("<p class='note'>" + Loc.Html("Preview_SlideLimits") + "</p></body></html>").ToString();
    }
}
