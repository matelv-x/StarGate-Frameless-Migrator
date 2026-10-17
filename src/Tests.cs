using System.Text.Json;
static class Tests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "migration-tests-" + Guid.NewGuid().ToString("N"));
        var report = new List<string>();
        try
        {
            Directory.CreateDirectory(root);
            var local = Path.Combine(root, "local"); var scan = Path.Combine(root, "drive"); Directory.CreateDirectory(scan);
            var gate = Path.Combine(scan, "StarGateLauncher"); Directory.CreateDirectory(gate); File.WriteAllText(Path.Combine(gate, Program.Gate.Exe), "test fixture");
            var frame = Path.Combine(scan, "Frameless"); Directory.CreateDirectory(frame); File.WriteAllText(Path.Combine(frame, Program.Frame.Exe), "test fixture");
            var found = Program.Detect(local, new[] { scan }); Check(found.Count == 2 && found.Any(x => x.Product == Program.Gate) && found.Any(x => x.Product == Program.Frame), "Detect both legacy applications and keep separate choices", report);
            var custom = Path.Combine(root, "custom-folder"); Directory.CreateDirectory(custom); File.WriteAllText(Path.Combine(custom, Program.Gate.Exe), "fixture");
            var pointer = Path.Combine(local, "StarGate_Launcher"); Directory.CreateDirectory(pointer); File.WriteAllText(Path.Combine(pointer, "launcher.json"), JsonSerializer.Serialize(new { InstalledBaseDir = custom }));
            Check(Program.Detect(local, Array.Empty<string>()).Single().Root == custom, "Discover custom installation through legacy launcher.json", report);
            File.WriteAllText(Path.Combine(custom, "project.csproj"), "fixture"); ExpectReject(() => Program.ValidateRoot(Program.Gate, custom), "Reject source repository as an installed application", report);
            File.WriteAllText(Path.Combine(frame, Program.Gate.Exe), "fixture"); ExpectReject(() => Program.ValidateRoot(Program.Frame, frame), "Reject shared application folder", report);
            var frameData = Path.Combine(local, "FramelessWindow"); Directory.CreateDirectory(frameData); var original = "{\"LastUrl\":\"https://example.org:8443/retro/dial.html\",\"SetupCompleted\":true}"; File.WriteAllText(Path.Combine(frameData, "config.json"), original);
            var backup = Path.Combine(root, "backup"); var saved = Program.BackupSettings(Program.Frame, local, backup);
            Check(saved.Count == 1 && Program.Hash(saved[0].Original) == Program.Hash(saved[0].Copy), "Back up address configuration byte for byte", report);
            Program.ImportFrame(local); var profile = Path.Combine(frameData, "Profiles", "example_org-8443.json");
            using (var doc = JsonDocument.Parse(File.ReadAllText(profile))) Check(doc.RootElement.GetProperty("LastUrl").GetString() == "https://example.org:8443/retro/dial.html", "Import flat Frameless address with full path and port", report);
            Check(File.ReadAllText(Path.Combine(frameData, "config.json")) == original, "Do not modify original saved-address file", report);
            Program.ImportFrame(local); Check(Directory.GetFiles(Path.Combine(frameData, "Profiles")).Length == 1, "Repeated import is idempotent", report);
            File.WriteAllText(profile, "{\"LastUrl\":\"https://example.org:8443/other\"}"); Program.ImportFrame(local);
            Check(Directory.GetFiles(Path.Combine(frameData, "Profiles")).Length == 2 && File.ReadAllText(profile).Contains("/other"), "Preserve existing same-host profile instead of overwriting", report);
            File.WriteAllText(Path.Combine(frameData, "config.json"), "{\"LastMode\":\"IpDial9\",\"LastIpOrHost\":\"gate.local/retro/dial9.html\",\"LastPort\":8080}"); Program.ImportFrame(local);
            var converted = Path.Combine(frameData, "Profiles", "gate_local-8080.json"); Check(File.ReadAllText(converted).Contains("http://gate.local:8080/retro/dial9.html"), "Import oldest IP/host and port configuration", report);
            var bootstrap = Path.Combine(root, "bootstrap.json"); File.WriteAllText(bootstrap, "{}");
            Check(MigrationForm.FindNewInstallation(bootstrap, Program.Gate, gate, gate + ".legacy-test") == null, "Installer cancellation does not count as successful installation", report);
            var installed = Path.Combine(root, "new-install"); Directory.CreateDirectory(Path.Combine(installed, "app-x64")); File.WriteAllText(Path.Combine(installed, "app-x64", Program.Gate.Exe), "fixture"); File.WriteAllText(Path.Combine(installed, "launcher-package.json"), "{\"PackageId\":\"wrong-app\"}"); File.WriteAllText(bootstrap, JsonSerializer.Serialize(new { InstalledBaseDir = installed }));
            Check(MigrationForm.FindNewInstallation(bootstrap, Program.Gate, gate, gate + ".legacy-test") == null, "Reject installation of wrong package", report);
            Check(!GitHubDownload.Trusted(new Uri("http://github.com/x")) && !GitHubDownload.Trusted(new Uri("https://github.com.evil.example/x")) && !GitHubDownload.Trusted(new Uri("https://user@github.com/x")) && GitHubDownload.Trusted(new Uri("https://release-assets.githubusercontent.com/x")), "Restrict download transport and redirect hosts", report);
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), report); return 0;
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), report); return 1; }
        finally
        {
            var resolved = Path.GetFullPath(root); if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("migration-tests-") && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
    static void Check(bool pass, string name, List<string> report) { if (!pass) throw new Exception(name); report.Add("PASS: " + name); }
    static void ExpectReject(Action action, string name, List<string> report) { try { action(); } catch (IOException) { report.Add("PASS: " + name); return; } throw new Exception(name); }
}
