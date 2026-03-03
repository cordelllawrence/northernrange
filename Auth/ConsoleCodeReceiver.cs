using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;

namespace NorthernRange.Auth;

/// <summary>
/// OAuth2 code receiver that always prints the authorization URL to the console
/// and attempts browser opening as best-effort. Works on headless servers where
/// no display/browser is available.
/// </summary>
public class ConsoleCodeReceiver : ICodeReceiver
{
    private const string SuccessHtml = """
        <html><body>
        <h2>Authentication successful!</h2>
        <p>You may close this window and return to the terminal.</p>
        </body></html>
        """;

    private const string ErrorHtml = """
        <html><body>
        <h2>Authentication failed</h2>
        <p>An error occurred during authentication. Please check the terminal for details.</p>
        </body></html>
        """;

    private readonly int _port;

    public ConsoleCodeReceiver()
    {
        // Grab an OS-assigned ephemeral port (FR-AUTH-002)
        using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        sock.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        _port = ((IPEndPoint)sock.LocalEndPoint!).Port;
    }

    public string RedirectUri => $"http://127.0.0.1:{_port}/authorize/";

    public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(
        AuthorizationCodeRequestUrl url,
        CancellationToken taskCancellationToken)
    {
        var authorizationUrl = url.Build().ToString();

        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();

        // Always print the URL so it can be copied on headless systems
        Console.Error.WriteLine();
        Console.Error.WriteLine("Open this URL in your browser to authenticate:");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"  {authorizationUrl}");
        Console.Error.WriteLine();

        var browserOpened = TryOpenBrowser(authorizationUrl);

        if (browserOpened)
        {
            Console.Error.WriteLine("A browser window should have opened. If not, copy the URL above.");
        }
        else
        {
            Console.Error.WriteLine("Could not open a browser automatically.");
            Console.Error.WriteLine("If you are on a remote server, either:");
            Console.Error.WriteLine($"  1. Use SSH port forwarding:  ssh -L {_port}:127.0.0.1:{_port} yourserver");
            Console.Error.WriteLine("     then open the URL in your local browser.");
            Console.Error.WriteLine("  2. Or, run 'nr auth login' on a machine with a browser and copy the");
            Console.Error.WriteLine("     token file to this machine (see README for details).");
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("Waiting for authentication...");

        // Wait for the OAuth redirect
        var context = await listener.GetContextAsync().WaitAsync(taskCancellationToken);
        var queryString = context.Request.QueryString;

        var code = queryString["code"];
        var error = queryString["error"];

        // Send a response page to the browser
        var html = string.IsNullOrEmpty(error) ? SuccessHtml : ErrorHtml;
        var buffer = System.Text.Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, taskCancellationToken);
        context.Response.Close();

        if (!string.IsNullOrEmpty(error))
            return new AuthorizationCodeResponseUrl { Error = error };

        return new AuthorizationCodeResponseUrl { Code = code };
    }

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
                return true;
            }

            // Linux: check for DISPLAY or WAYLAND_DISPLAY before attempting xdg-open
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var display = Environment.GetEnvironmentVariable("DISPLAY");
                var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

                if (string.IsNullOrEmpty(display) && string.IsNullOrEmpty(wayland))
                    return false; // No display server — don't even try

                Process.Start("xdg-open", url);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
