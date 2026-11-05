using System.Text.Json;
using System.Diagnostics;
using Microsoft.Playwright;

namespace BookingApp.Tests;

public sealed class FrontendInteractionTests
{
    private static readonly SemaphoreSlim BrowserInstallGate = new(1, 1);

    [Fact]
    public async Task UserCanSubmitBookingAndSeeConfirmation()
    {
        using var playwright = await Playwright.CreateAsync();
        await EnsureChromiumInstalledAsync(playwright);
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        await page.RouteAsync("**/api/rooms", route => route.FulfillAsync(new() { ContentType = "application/json", Body = "[{\"id\":1,\"name\":\"Vikas\"}]" }));
        await page.RouteAsync("**/api/bookings?date=**", route => route.FulfillAsync(new() { ContentType = "application/json", Body = "[]" }));
        string? submitted = null;
        await page.RouteAsync("**/api/bookings", async route =>
        {
            if (route.Request.Method == "POST")
            {
                submitted = route.Request.PostData;
                await route.FulfillAsync(new() { Status = 201, ContentType = "application/json", Body = "{\"id\":7,\"roomId\":1,\"roomName\":\"Vikas\",\"name\":\"Alex Morgan\",\"startUtc\":\"2026-11-04T10:00:00Z\",\"endUtc\":\"2026-11-04T11:00:00Z\",\"isCancelled\":false}" });
            }
            else await route.FulfillAsync(new() { ContentType = "application/json", Body = "[]" });
        });
        var html = await File.ReadAllTextAsync(Path.Combine(wwwroot, "index.html"));
        await page.SetContentAsync(html.Replace("<head>", "<head><base href=\"http://localhost/\">"));
        await page.AddScriptTagAsync(new() { Content = await File.ReadAllTextAsync(Path.Combine(wwwroot, "app.js")) });
        await page.Locator("input[name=name]").FillAsync("Alex Morgan");
        await page.Locator("input[name=date]").FillAsync("2026-11-04");
        await page.Locator("input[name=startTime]").FillAsync("10:00");
        await page.Locator("input[name=endTime]").FillAsync("11:00");
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm booking" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Room booked.");
        Assert.NotNull(submitted);
        using var json = JsonDocument.Parse(submitted!);
        Assert.Equal("Alex Morgan", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("2026-11-04", json.RootElement.GetProperty("date").GetString());
        Assert.Equal("10:00", json.RootElement.GetProperty("startTime").GetString());
        Assert.Equal("11:00", json.RootElement.GetProperty("endTime").GetString());
    }

    private static async Task EnsureChromiumInstalledAsync(IPlaywright playwright)
    {
        var executable = playwright.Chromium.ExecutablePath;
        if (File.Exists(executable)) return;

        await BrowserInstallGate.WaitAsync();
        try
        {
            if (File.Exists(executable)) return;
            var windows = OperatingSystem.IsWindows();
            var script = Path.Combine(AppContext.BaseDirectory, windows ? "playwright.ps1" : "playwright.sh");
            if (!File.Exists(script)) throw new FileNotFoundException("Playwright's browser installer was not copied to the test output.", script);

            var start = new ProcessStartInfo
            {
                FileName = windows ? "powershell.exe" : "/bin/sh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (windows)
            {
                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-ExecutionPolicy");
                start.ArgumentList.Add("Bypass");
                start.ArgumentList.Add("-File");
                start.ArgumentList.Add(script);
            }
            else start.ArgumentList.Add(script);
            start.ArgumentList.Add("install");
            start.ArgumentList.Add("chromium");

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Playwright browser installer.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = $"{await stdout}{await stderr}";
            if (process.ExitCode != 0 || !File.Exists(executable))
                throw new InvalidOperationException($"Playwright could not install Chromium. Check network access and retry.\n{output}");
        }
        finally { BrowserInstallGate.Release(); }
    }
}
