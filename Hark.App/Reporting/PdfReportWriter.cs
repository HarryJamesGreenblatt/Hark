using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hark.App.Reporting;

/// <summary>Renders a <see cref="SessionReport"/> to PDF by printing the styled HTML report through WebView2.</summary>
public sealed class PdfReportWriter : IReportWriter
{
    public string Extension => ".pdf";
    public string FilterName => "PDF document";

    public async Task WriteAsync(SessionReport report, string path)
    {
        // Reuse the styled HTML (transcript expanded — a printed PDF can't open a collapsed <details>),
        // spilled to a temp file (WebView2's NavigateToString caps around 2 MB).
        var htmlPath = Path.Combine(Path.GetTempPath(), $"hark-pdf-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(htmlPath, HtmlReportWriter.Render(report, transcriptOpen: true));

        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("PDF export requires a running WPF application.");
        try
        {
            // WebView2 must run on the UI thread; the save flow may call this from a background thread.
            await dispatcher.InvokeAsync(() => RenderAsync(htmlPath, path)).Task.Unwrap();
        }
        finally
        {
            try { File.Delete(htmlPath); } catch { /* best-effort temp cleanup */ }
        }
    }

    /// <summary>Hosts an off-screen WebView2, loads the HTML, and prints it to <paramref name="pdfPath"/>. UI thread only.</summary>
    private static async Task RenderAsync(string htmlPath, string pdfPath)
    {
        // A real-sized host shown off-screen so the control lays out and renders (a Hidden window won't).
        var window = new Window
        {
            Width = 1024,
            Height = 768,
            Left = -32000,
            Top = -32000,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        var web = new WebView2();
        window.Content = web;
        window.Show();
        try
        {
            await web.EnsureCoreWebView2Async();

            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNav(object? _, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult(e.IsSuccess);
            web.CoreWebView2.NavigationCompleted += OnNav;
            web.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            await loaded.Task;
            web.CoreWebView2.NavigationCompleted -= OnNav;

            var settings = web.CoreWebView2.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;   // keep the dark theme and card fills
            await web.CoreWebView2.PrintToPdfAsync(pdfPath, settings);
        }
        finally
        {
            window.Close();
            web.Dispose();
        }
    }
}
