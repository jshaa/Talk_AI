using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

// Usage: dotnet run --project tools/WebViewerHost -- [publishedWwwroot] [port]
// Publish first: dotnet publish src/TalkPro.Web -c Release -o artifacts/web
var root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("artifacts", "web", "wwwroot"));
var port = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5080;
if (!File.Exists(Path.Combine(root, "index.html")))
{
    Console.WriteLine("index.html not found. Publish the viewer first: dotnet publish src/TalkPro.Web -c Release -o artifacts/web");
    return 2;
}

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = root, WebRootPath = root });
builder.Logging.ClearProviders(); // No request logs: nothing about the viewer's use is recorded.
builder.WebHost.UseUrls(string.Create(CultureInfo.InvariantCulture, $"http://0.0.0.0:{port}"));
var app = builder.Build();

app.Use(async (context, next) =>
{
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return;
    }

    var headers = context.Response.Headers;
    headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'";
    headers["Referrer-Policy"] = "no-referrer";
    headers.XContentTypeOptions = "nosniff";
    headers.CacheControl = "no-store";
    await next();
});

var types = new FileExtensionContentTypeProvider();
types.Mappings[".dat"] = "application/octet-stream";
types.Mappings[".blat"] = "application/octet-stream";
types.Mappings[".webcil"] = "application/octet-stream";
var files = new PhysicalFileProvider(root);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types });
app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });

Console.WriteLine("TalkPro web viewer (test). Open on the iPhone (same Wi-Fi):");
foreach (var address in LocalAddresses())
{
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  http://{address}:{port}/"));
}

Console.WriteLine("Chat files are processed in the phone's browser only; this host serves the app files and nothing else. Ctrl+C to stop.");
await app.RunAsync();
return 0;

static IEnumerable<IPAddress> LocalAddresses() =>
    NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork);
