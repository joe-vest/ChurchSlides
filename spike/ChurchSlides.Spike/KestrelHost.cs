using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace ChurchSlides.Spike;

/// <summary>
/// A minimal localhost web server, started before the window opens with --kestrel (S7).
/// Kept in its own class and never inlined, so ASP.NET assemblies load only when asked for.
/// </summary>
internal static class KestrelHost
{
    private static WebApplication? app;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Start()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        app = builder.Build();
        app.MapGet("/", () => Microsoft.AspNetCore.Http.Results.Text("ChurchSlides spike"));
        Task.Run(() => app.StartAsync()).GetAwaiter().GetResult();
        return string.Join(" ", app.Urls);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Stop()
    {
        if (app is null)
            return;
        Task.Run(() => app.StopAsync()).GetAwaiter().GetResult();
    }
}
