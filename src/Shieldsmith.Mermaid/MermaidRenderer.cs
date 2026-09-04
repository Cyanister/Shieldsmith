using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Shieldsmith.Mermaid;

/// <summary>
/// Renders Mermaid source to real SVG and PNG using the bundled mermaid.js in a
/// headless WebView2 host. Entirely offline: mermaid.js is an embedded resource
/// and nothing is fetched at run time.
///
/// Availability is a supported state, not an error. If the WebView2 runtime is
/// missing, callers fall back to Shieldsmith's own layout engine.
/// </summary>
public sealed class MermaidRenderer
{
    private const string ResourceName = "Shieldsmith.Mermaid.Assets.mermaid.min.js";
    private static readonly object AssetLock = new();
    private static string? _assetDirectory;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);
    /// <summary>Extra pixel density for the PNG capture, so raster output stays crisp.</summary>
    public double RasterScale { get; set; } = 2.0;

    /// <summary>Whether a WebView2 runtime is present. Never throws.</summary>
    public static bool IsAvailable => RuntimeVersion is not null;

    public static string? RuntimeVersion
    {
        get
        {
            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return string.IsNullOrEmpty(version) ? null : version;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Renders Mermaid source, writing "<paramref name="baseName"/>.svg" and
    /// ".png" into <paramref name="outputDirectory"/>.
    /// </summary>
    public MermaidRenderResult Render(string mermaidSource, string outputDirectory, string baseName)
    {
        if (!IsAvailable)
            return MermaidRenderResult.Unavailable("The WebView2 runtime is not installed on this machine.");

        Directory.CreateDirectory(outputDirectory);

        try
        {
            var (svg, width, height) = RenderOnStaThread(mermaidSource, outputDirectory, baseName);
            if (svg is null)
                return MermaidRenderResult.Unavailable("Mermaid produced no diagram for this source.");

            var svgPath = Path.Combine(outputDirectory, baseName + ".svg");
            File.WriteAllText(svgPath, svg, new UTF8Encoding(false));

            var pngPath = Path.Combine(outputDirectory, baseName + ".png");
            return File.Exists(pngPath)
                ? MermaidRenderResult.Ok(svgPath, pngPath, width, height)
                : MermaidRenderResult.Ok(svgPath, null, width, height);
        }
        catch (Exception ex)
        {
            return MermaidRenderResult.Unavailable($"Mermaid rendering failed: {ex.Message}");
        }
    }

    private (string? Svg, double Width, double Height) RenderOnStaThread(
        string mermaidSource, string outputDirectory, string baseName)
    {
        string? svg = null;
        double width = 0, height = 0;
        Exception? failure = null;

        // WebView2 needs an STA thread with a running message pump; a hidden
        // WinForms host is the least fragile way to provide one from a console
        // app, a WPF app, or a test runner alike.
        var thread = new Thread(() =>
        {
            Form? form = null;
            try
            {
                form = new Form
                {
                    ShowInTaskbar = false,
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new System.Drawing.Point(-4000, -4000),
                    Size = new System.Drawing.Size(1200, 900),
                    Opacity = 0,
                };

                var webView = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(webView);

                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var pngPath = Path.Combine(outputDirectory, baseName + ".png");
                        (svg, width, height) = await RenderInWebViewAsync(webView, mermaidSource, pngPath);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        form!.Close();
                    }
                };

                Application.Run(form);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
            finally
            {
                form?.Dispose();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(Timeout))
            throw new TimeoutException("Mermaid rendering timed out.");
        if (failure is not null)
            throw failure;

        return (svg, width, height);
    }

    private async Task<(string? Svg, double Width, double Height)> RenderInWebViewAsync(
        WebView2 webView, string mermaidSource, string pngPath)
    {
        var assets = EnsureAssets();
        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(Path.GetTempPath(), "Shieldsmith", "webview2"));
        await webView.EnsureCoreWebView2Async(environment);

        // The page must be served from the same virtual host as mermaid.min.js.
        // Navigating to a file:// URL and pulling the script from https://
        // is a cross-origin load and the script is silently blocked.
        var pageFolder = "render-" + Guid.NewGuid().ToString("N");
        var pageDirectory = Path.Combine(assets, pageFolder);
        Directory.CreateDirectory(pageDirectory);
        try
        {
            var html = BuildHtml(mermaidSource);
            File.WriteAllText(Path.Combine(pageDirectory, "index.html"), html, new UTF8Encoding(false));

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "shieldsmith.assets", assets, CoreWebView2HostResourceAccessKind.Allow);

            var navigation = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>();
            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) =>
                navigation.TrySetResult(e);
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.Navigate($"https://shieldsmith.assets/{pageFolder}/index.html");
            var navigationResult = await navigation.Task;
            webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;

            if (!navigationResult.IsSuccess)
                throw new InvalidOperationException(
                    $"The render page failed to load ({navigationResult.WebErrorStatus}).");

            // The page sets window.__shieldsmith once mermaid has finished or failed.
            var deadline = DateTime.UtcNow + Timeout;
            string? payload = null;
            while (DateTime.UtcNow < deadline)
            {
                // window.__shieldsmith is already a JSON string. ExecuteScriptAsync
                // JSON-encodes whatever the script returns, so returning it
                // as-is means exactly one layer to peel; stringifying it here
                // as well would hand JsonDocument a string, not an object.
                var raw = await webView.CoreWebView2.ExecuteScriptAsync("window.__shieldsmith || null");
                if (!string.IsNullOrEmpty(raw) && raw != "null")
                {
                    payload = JsonSerializer.Deserialize<string>(raw);
                    break;
                }
                await Task.Delay(60);
            }

            if (payload is null) throw new TimeoutException("Mermaid did not finish rendering.");

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.GetString() ?? "Mermaid reported an error.");

            var svg = root.GetProperty("svg").GetString();
            var width = root.TryGetProperty("width", out var w) ? w.GetDouble() : 0;
            var height = root.TryGetProperty("height", out var h) ? h.GetDouble() : 0;

            await CapturePngAsync(webView, pngPath, width, height);
            return (svg, width, height);
        }
        finally
        {
            try { Directory.Delete(pageDirectory, recursive: true); } catch { /* swept later */ }
        }
    }

    /// <summary>
    /// Rasterises the rendered diagram.
    ///
    /// CapturePreviewAsync only grabs the visible viewport, so a diagram taller
    /// than the host window comes out cropped with scrollbars baked in. The
    /// DevTools Page.captureScreenshot method takes an explicit clip rectangle
    /// and a device scale, which captures the whole diagram at high resolution
    /// no matter how big it is or how small the window is.
    /// </summary>
    private async Task CapturePngAsync(WebView2 webView, string pngPath, double width, double height)
    {
        try
        {
            if (width <= 0 || height <= 0) return;

            var scale = Math.Clamp(RasterScale, 1.0, 3.0);
            // Keep the output within what Chromium will actually paint: a
            // runaway diagram degrades in resolution rather than failing or,
            // worse, coming out part-blank. The area cap matters more than the
            // edge cap: a wide ERD fits both edges and still breaks the paint
            // budget on area alone.
            var maxEdge = 12000.0;
            var maxPixels = 24_000_000.0;
            if (width * scale > maxEdge) scale = maxEdge / width;
            if (height * scale > maxEdge) scale = Math.Min(scale, maxEdge / height);
            var area = width * height * scale * scale;
            if (area > maxPixels) scale *= Math.Sqrt(maxPixels / area);
            scale = Math.Max(scale, 0.05);

            var outputWidth = (int)(Math.Ceiling(width * scale) + 16);
            var outputHeight = (int)(Math.Ceiling(height * scale) + 16);

            // Two hard-won facts drive this shape. captureBeyondViewport
            // extends the capture downwards but does not paint to the RIGHT of
            // the viewport, so a wide ERD came out as a painted strip with
            // blank whiteness beside it. And a viewport override the full size
            // of a huge diagram exceeds the compositor's paint budget, which
            // silently leaves the BOTTOM unpainted. So the page is shrunk to
            // the final output size with CSS zoom, the viewport is overridden
            // to exactly that size, and the capture is 1:1 over a surface small
            // enough to paint completely.
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"document.body.style.zoom = '{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}';");
            await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Emulation.setDeviceMetricsOverride", JsonSerializer.Serialize(new
                {
                    width = outputWidth,
                    height = outputHeight,
                    deviceScaleFactor = 1,
                    mobile = false,
                }));
            try
            {
                // A frame for the zoomed layout to settle before rasterising.
                await Task.Delay(120);

                var clip = new { x = 0, y = 0, width = outputWidth, height = outputHeight, scale = 1 };
                var parameters = JsonSerializer.Serialize(new
                {
                    format = "png",
                    captureBeyondViewport = true,
                    fromSurface = true,
                    clip,
                });

                var response = await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Page.captureScreenshot", parameters);
                using var document = JsonDocument.Parse(response);
                if (!document.RootElement.TryGetProperty("data", out var data)) return;

                var bytes = Convert.FromBase64String(data.GetString() ?? string.Empty);
                if (bytes.Length == 0) return;
                await File.WriteAllBytesAsync(pngPath, bytes);
            }
            finally
            {
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Emulation.clearDeviceMetricsOverride", "{}");
                await webView.CoreWebView2.ExecuteScriptAsync("document.body.style.zoom = '';");
            }
        }
        catch
        {
            // PNG is a convenience: the SVG is the authoritative output, and
            // callers fall back to their own raster renderer.
            try { if (File.Exists(pngPath)) File.Delete(pngPath); } catch { /* ignore */ }
        }
    }

    private static string BuildHtml(string mermaidSource)
    {
        var encoded = JsonSerializer.Serialize(mermaidSource);
        return $$"""
        <!doctype html>
        <html>
          <head>
            <meta charset="utf-8"/>
            <style>
              html, body { margin: 0; padding: 8px; background: #FFFFFF;
                font-family: 'Inter', 'Segoe UI', system-ui, sans-serif;
                overflow: hidden; }
              #out svg { display: block; }
            </style>
            <script src="../mermaid.min.js"></script>
          </head>
          <body>
            <div id="out"></div>
            <script>
              (async () => {
                try {
                  mermaid.initialize({
                    startOnLoad: false,
                    securityLevel: 'strict',
                    // Mermaid's defaults are sized for a web page, not a
                    // hundred-table solution: at the default 50000 characters
                    // it silently draws a "Maximum text size in diagram
                    // exceeded" card instead of throwing.
                    maxTextSize: 5000000,
                    maxEdges: 5000,
                    theme: 'base',
                    fontFamily: "'Inter', 'Segoe UI', system-ui, sans-serif",
                    themeVariables: {
                      primaryColor: '#F0FAFA',
                      primaryBorderColor: '#00AAAA',
                      primaryTextColor: '#0F172A',
                      lineColor: '#64748B',
                      secondaryColor: '#E8F7F7',
                      tertiaryColor: '#C8EEEE',
                      background: '#FFFFFF',
                      mainBkg: '#F0FAFA',
                      nodeBorder: '#00AAAA',
                      clusterBkg: '#F8FAFC',
                      titleColor: '#0F172A',
                      edgeLabelBackground: '#FFFFFF'
                    }
                  });
                  const source = {{encoded}};
                  const { svg } = await mermaid.render('shieldsmithDiagram', source);
                  document.getElementById('out').innerHTML = svg;
                  const el = document.querySelector('#out svg');

                  // Mermaid does not always throw when it gives up. For a size
                  // limit, a parse error or an unsupported construct it renders
                  // a picture of the error instead and returns it as a perfectly
                  // valid SVG. That once shipped as a solution's entity
                  // relationship diagram. Detect it and fail properly, so the
                  // caller falls back to the built-in engine.
                  const roleDescription = el.getAttribute('aria-roledescription') || '';
                  const rendered = (el.textContent || '');
                  const looksLikeError =
                    roleDescription.toLowerCase() === 'error' ||
                    /Maximum text size in diagram exceeded/i.test(rendered) ||
                    /Syntax error in (graph|text)/i.test(rendered) ||
                    /^\s*mermaid version\s/i.test(rendered);
                  if (looksLikeError) {
                    window.__shieldsmith = JSON.stringify({
                      error: 'mermaid rendered an error diagram: ' +
                             rendered.replace(/\s+/g, ' ').trim().slice(0, 200)
                    });
                    return;
                  }

                  // Mermaid emits width="100%" plus a max-width style, so the
                  // rendered size depends on the window rather than the diagram.
                  // Pin the element to its viewBox first: measuring before this
                  // normalisation reports a size the capture clip cannot match.
                  const vb = el.viewBox && el.viewBox.baseVal;
                  const naturalWidth = vb && vb.width ? vb.width : el.getBoundingClientRect().width;
                  const naturalHeight = vb && vb.height ? vb.height : el.getBoundingClientRect().height;
                  el.removeAttribute('style');
                  el.setAttribute('width', naturalWidth);
                  el.setAttribute('height', naturalHeight);

                  const box = el.getBoundingClientRect();
                  window.__shieldsmith = JSON.stringify({
                    svg: el.outerHTML,
                    width: Math.ceil(box.width),
                    height: Math.ceil(box.height)
                  });
                } catch (e) {
                  window.__shieldsmith = JSON.stringify({ error: String(e && e.message ? e.message : e) });
                }
              })();
            </script>
          </body>
        </html>
        """;
    }

    /// <summary>Extracts the embedded mermaid.js to a stable temp folder, once.</summary>
    private static string EnsureAssets()
    {
        lock (AssetLock)
        {
            if (_assetDirectory is not null && File.Exists(Path.Combine(_assetDirectory, "mermaid.min.js")))
                return _assetDirectory;

            var directory = Path.Combine(Path.GetTempPath(), "Shieldsmith", "mermaid-assets");
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, "mermaid.min.js");

            var assembly = Assembly.GetExecutingAssembly();
            using var resource = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{ResourceName}' is missing from Shieldsmith.Mermaid.");

            if (!File.Exists(target) || new FileInfo(target).Length != resource.Length)
            {
                using var file = File.Create(target);
                resource.CopyTo(file);
            }

            _assetDirectory = directory;
            return directory;
        }
    }
}

public sealed record MermaidRenderResult(
    bool Success, string? SvgPath, string? PngPath, double Width, double Height, string? Message)
{
    public static MermaidRenderResult Ok(string svgPath, string? pngPath, double width, double height) =>
        new(true, svgPath, pngPath, width, height, null);

    public static MermaidRenderResult Unavailable(string message) =>
        new(false, null, null, 0, 0, message);
}
