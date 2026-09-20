using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using FilesMate.Search;
using FilesMate.SearchHost;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class PreviewInteractionSmoke
{
    internal static async Task Run(Window window,string profile,string fixtures)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? Call(string method,params object[] args)=>window.GetType().GetMethod(method,flags)!.Invoke(window,args);
        WebView2? View()=>(WebView2?)window.GetType().GetField("_documentPreview",flags)!.GetValue(window);
        void Assert(bool value,string message){if(!value)throw new Exception(message);}
        window.GetType().GetField("_opening",flags)!.SetValue(window,true);
        ((System.Windows.Controls.TextBox)window.FindName("QueryBox")).Text="fixture";
        await Task.Delay(700);
        async Task<WebView2> Open(string file,string ready)
        {
            Call("ClearPreview");Call("QueuePreview",new SearchRow(new NameHit(Path.GetFileName(file),file,false),new DrawingImage()),0);
            for(var i=0;i<200;i++)
            {
                await Task.Delay(100);
                if(View() is {CoreWebView2:not null} view)
                {
                    try{if(await view.CoreWebView2.ExecuteScriptAsync(ready)=="true")return view;}catch{}
                }
            }
            throw new Exception("Preview not ready: "+file+" "+(View() is {} last?await last.CoreWebView2.ExecuteScriptAsync("document.body.innerText + document.body.dataset.error"):"no webview"));
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_PDF_REGRESSION") is { } actual)
        {
            var real = await Open(actual,"document.body.dataset.ready==='true'");
            var page = Environment.GetEnvironmentVariable("FILESMATE_PDF_PAGE") ?? "1";
            await real.CoreWebView2.ExecuteScriptAsync($"document.getElementById('page').value={page};document.getElementById('page').dispatchEvent(new Event('change'))");
            await Task.Delay(400);
            for (var i=0;i<600 && await real.CoreWebView2.ExecuteScriptAsync("document.body.dataset.ready==='true'")!="true";i++) await Task.Delay(100);
            var pixels = await real.CoreWebView2.ExecuteScriptAsync("(()=>{const c=document.querySelector('.pdf-page[data-page=\"'+document.getElementById('page').value+'\"] canvas');if(!c)return -1;const d=c.getContext('2d').getImageData(0,0,c.width,c.height).data;let n=0;for(let i=0;i<d.length;i+=16)if(d[i]<230||d[i+1]<230||d[i+2]<230)n++;return n})()");
            Assert(int.Parse(pixels)>100,"Real PDF rendered blank: "+pixels+" "+await real.CoreWebView2.ExecuteScriptAsync("document.body.dataset.error"));
            var canvases=await real.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('canvas').length");
            Assert(int.Parse(canvases)<=3,"Too many retained PDF canvases");
            await using(var png=File.Create(Path.Combine(profile,"real-pdf.png"))) await real.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,png);
            File.WriteAllText(Path.Combine(profile,"real-pdf-result.json"),JsonSerializer.Serialize(new{Passed=true,Page=page,NonWhiteSamples=pixels,Canvases=canvases}));
            Call("ClearPreview"); return;
        }
        var pdf=await Open(Path.Combine(fixtures,"reading.pdf"),"document.body.dataset.ready==='true'");
        await pdf.CoreWebView2.ExecuteScriptAsync("document.body.dataset.ready='';document.getElementById('plus').click()");
        await Task.Delay(700);
        await pdf.CoreWebView2.ExecuteScriptAsync("document.body.dataset.ready='';document.getElementById('plus').click()");
        await Task.Delay(700);
        Assert(await pdf.CoreWebView2.ExecuteScriptAsync("document.querySelector('#viewport').scrollWidth>document.querySelector('#viewport').clientWidth")=="true","PDF did not zoom");
        await pdf.CoreWebView2.ExecuteScriptAsync("document.querySelector('#viewport').scrollLeft=50");
        var before=double.Parse(await pdf.CoreWebView2.ExecuteScriptAsync("document.querySelector('#viewport').scrollLeft"),System.Globalization.CultureInfo.InvariantCulture);
        async Task Mouse(string type,int x,string button,int buttons)=>await pdf.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x,y=150,button,buttons,clickCount=1}));
        await Mouse("mousePressed",200,"left",1);await Mouse("mouseMoved",110,"left",1);await Mouse("mouseReleased",110,"left",0);
        var after=double.Parse(await pdf.CoreWebView2.ExecuteScriptAsync("document.querySelector('#viewport').scrollLeft"),System.Globalization.CultureInfo.InvariantCulture);
        Assert(after>before+60,"Left drag did not pan PDF: "+before+" -> "+after);
        Assert(await pdf.CoreWebView2.ExecuteScriptAsync("document.querySelector('canvas').width*document.querySelector('canvas').height<=4000000")=="true","PDF raster exceeds budget");
        Assert(await pdf.CoreWebView2.ExecuteScriptAsync("document.querySelector('.textLayer').childElementCount>0")=="true","PDF text layer missing");
        await pdf.CoreWebView2.ExecuteScriptAsync("document.body.dataset.ready='';document.getElementById('next').click()");
        for(var i=0;i<60 && await pdf.CoreWebView2.ExecuteScriptAsync("document.body.dataset.ready==='true'")!="true";i++)await Task.Delay(100);
        Assert(await pdf.CoreWebView2.ExecuteScriptAsync("document.getElementById('page').value==='2'")=="true","PDF page navigation failed");
        await using(var png=File.Create(Path.Combine(profile,"pdf-drag.png")))await pdf.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,png);
        foreach(var file in new[]{"visuals.xlsx","visuals.pptx"})
        {
            var office=await Open(Path.Combine(fixtures,file),"document.querySelectorAll('img').length>0 && [...document.images].every(x=>x.complete&&x.naturalWidth>0) && !!document.querySelector('table') && !!document.querySelector('svg')");
            Assert(!office.CoreWebView2.Settings.IsScriptEnabled,"Office scripts enabled");
            await using var png=File.Create(Path.Combine(profile,file+".png"));await office.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,png);
            if(file.EndsWith("pptx"))
            {
                office.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(office), Environment.TickCount, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                await Task.Delay(200);Assert(View()is null,"Office Escape did not close preview");
            }
        }
        Call("ClearPreview");Assert(View() is null,"Closed preview retained WebView");
        File.WriteAllText(Path.Combine(profile,"interaction-result.json"),JsonSerializer.Serialize(new{Passed=true,PdfPanBefore=before,PdfPanAfter=after,PdfTextLayer=true,PdfRasterBounded=true,ExcelImagesCharts=true,PowerPointImagesChartsTables=true,OfficeScriptsDisabled=true,ViewReleased=true}));
    }
}
