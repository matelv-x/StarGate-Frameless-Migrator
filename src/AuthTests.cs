using System.Net;
using System.Text;
using System.Text.Json;

internal static class AuthTests
{
    internal static async Task Run(List<string> report)
    {
        const string fixtureToken = "fixture-test-only-credential";
        bool interactiveRequested = false;
        using (var downloader = new GitHubDownload(Program.Frame, handler: new FixtureHandler(), credentials: (interactive, parent, token) => { interactiveRequested = interactive && parent == new IntPtr(321); return Task.FromResult(fixtureToken); }))
        {
            var result = await downloader.SignInAsync(new IntPtr(321), CancellationToken.None);
            if (!interactiveRequested || result != fixtureToken) throw new Exception("Interactive sign-in did not verify repository access.");
            report.Add("PASS: Sign-in requests user interaction and verifies private repository metadata");
        }
        using (var downloader = new GitHubDownload(Program.Frame, handler: new FixtureHandler(deny: true), credentials: (_, _, _) => Task.FromResult(fixtureToken)))
        {
            try { await downloader.SignInAsync(IntPtr.Zero, CancellationToken.None); throw new Exception("Unauthorized account was accepted."); }
            catch (IOException ex) { if (ex.Message.Contains(fixtureToken)) throw new Exception("Credential leaked in an error."); report.Add("PASS: Sign-in rejects accounts without repository access without exposing credentials"); }
        }
        using (var cancelled = new CancellationTokenSource())
        using (var downloader = new GitHubDownload(Program.Frame, handler: new FixtureHandler(), credentials: (_, _, token) => Task.FromCanceled<string>(token)))
        {
            cancelled.Cancel();
            try { await downloader.SignInAsync(IntPtr.Zero, cancelled.Token); throw new Exception("Cancellation was ignored."); }
            catch (OperationCanceledException) { report.Add("PASS: Sign-in cancellation exits without starting migration"); }
        }
        var redirect = new FixtureHandler(redirect: true);
        using (var downloader = new GitHubDownload(Program.Frame, fixtureToken, redirect)) { await downloader.GetPackage(); }
        if (!redirect.StorageSeen || redirect.LeakedAuthorization) throw new Exception("Authorization was forwarded to redirected storage.");
        report.Add("PASS: GitHub credential is not sent to redirected release asset storage");
    }
    sealed class FixtureHandler(bool deny = false, bool redirect = false) : HttpMessageHandler
    {
        internal bool StorageSeen, LeakedAuthorization;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deny) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            var uri = request.RequestUri!;
            if (uri.Host == "release-assets.githubusercontent.com")
            { StorageSeen = true; LeakedAuthorization |= request.Headers.Authorization != null; return Task.FromResult(Json(Manifest())); }
            if (request.Headers.Authorization == null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (uri.AbsolutePath.EndsWith("/latest")) return Task.FromResult(Json(JsonSerializer.Serialize(new
            {
                tag_name = "fixture-release",
                assets = new[] {
                    new { name = "FramelessSetup-x64.exe", url = "https://api.github.com/repos/matelv-x/Framelesswindow/releases/assets/1", size = 123L },
                    new { name = "Framelesswindow-update.json", url = "https://api.github.com/repos/matelv-x/Framelesswindow/releases/assets/2", size = 300L }
                }
            })));
            if (redirect) { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://release-assets.githubusercontent.com/fixture-manifest"); return Task.FromResult(response); }
            return Task.FromResult(Json(Manifest()));
        }
        static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
        static string Manifest() => JsonSerializer.Serialize(new { Schema = 1, Application = "Framelesswindow", Architecture = "x64", InstallerSha256 = new string('a', 64), ApplicationSha256 = new string('b', 64), InstallerSize = 123L });
    }
}
