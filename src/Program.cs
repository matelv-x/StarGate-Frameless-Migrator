using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

record Product(string Name, string Exe, string Launcher, string Package, string Installer, string Settings);
record Installation(Product Product, string Root) { public override string ToString() => Product.Name + " — " + Root; }
record Asset(Uri Url, long Size);
record Package(Asset Installer, string Hash, long Size, string AppHash, string Tag);
static class Program
{
    internal static readonly Product Gate = new("StarGateWebView", "StarGateWebView.exe", "StarGateLauncherAll", "Stargate-x64", "StarGateSetup-x64.exe", "StarGate_Launcher");
    internal static readonly Product Frame = new("Framelesswindow", "Framelesswindow.exe", "FramelessLauncherAll", "Frameless-x64", "FramelessSetup-x64.exe", "FramelessWindow");
    internal static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    [STAThread] static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { Environment.ExitCode = Tests.Run(); return; }
        if (args.Contains("--check-releases"))
        {
            try { var report = new List<string>(); foreach (var product in new[] { Gate, Frame }) { using var download = new GitHubDownload(product); var package = download.GetPackage().GetAwaiter().GetResult(); report.Add(product.Name + ": " + package.Tag + ", " + package.Size + " bytes, manifest valid"); } File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "release-check-results.txt"), report); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "release-check-results.txt"), ex.Message); Environment.ExitCode = 1; } return;
        }
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(false, @"Local\StarGate-Frameless-Migrator");
        bool owns; try { owns = mutex.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
        if (!owns) { MessageBox.Show("Instalator migracyjny jest już uruchomiony."); return; }
        try { Application.Run(new MigrationForm()); } finally { mutex.ReleaseMutex(); }
    }
    internal static bool Same(string a, string b) => Path.GetFullPath(a).TrimEnd('\\').Equals(Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    internal static bool Within(string path, string root) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    internal static void NoLinks(string path)
    {
        for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            if (Directory.Exists(p) && File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Folder jest dowiązaniem: " + p);
    }
    internal static string[] RelativeExes(Product p) => new[] { p.Exe, "app-x64/" + p.Exe, "StarGate_Launcher-x64/" + p.Exe, "StarGate_Launcher-arm64/" + p.Exe };
    internal static bool HasApp(Product p, string root) => RelativeExes(p).Any(r => File.Exists(Path.Combine(root, r)));
    internal static void ValidateRoot(Product p, string root)
    {
        root = Path.GetFullPath(root).TrimEnd('\\'); NoLinks(root);
        if (root.Length < 4 || Same(root, Path.GetPathRoot(root)!) || !HasApp(p, root)) throw new IOException("Nie rozpoznano folderu programu: " + root);
        foreach (var s in new[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.Windows, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments })
        { var special = Environment.GetFolderPath(s); if (special.Length > 0 && Same(root, special)) throw new IOException("To jest folder systemowy lub folder użytkownika."); }
        if (Within(AppContext.BaseDirectory, root) || Same(root, AppContext.BaseDirectory)) throw new IOException("Uruchom migrator spoza starego katalogu programu.");
        if (Directory.Exists(Path.Combine(root, ".git")) || Directory.EnumerateFiles(root, "*.csproj").Any() || Directory.EnumerateFiles(root, "*.sln").Any()) throw new IOException("Wybrany folder wygląda jak projekt źródłowy, nie instalacja.");
        var other = p == Gate ? Frame : Gate;
        if (HasApp(other, root)) throw new IOException("Wspólny folder obu programów wymaga osobnej migracji. Nie zostanie przeniesiony.");
    }
    internal static List<string> LauncherConfigs(string local)
    {
        var files = new List<string>();
        foreach (var folder in new[] { "Universal_Launcher", "StarGate_Launcher", "FramelessWindow", "Frameless_Launcher" })
        {
            var root = Path.Combine(local, folder); if (!Directory.Exists(root)) continue;
            try { NoLinks(root); files.AddRange(Directory.GetFiles(root, "launcher.json")); foreach (var sub in Directory.GetDirectories(root)) { NoLinks(sub); files.AddRange(Directory.GetFiles(sub, "launcher.json")); } } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return files;
    }
    internal static string? ReadString(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    internal static List<Installation> Detect(string local, IEnumerable<string> scanRoots)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in LauncherConfigs(local)) try { using var j = JsonDocument.Parse(File.ReadAllText(f)); var path = ReadString(j.RootElement, "InstalledBaseDir"); if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)) candidates.Add(path); } catch { }
        foreach (var scan in scanRoots)
        {
            if (!Directory.Exists(scan)) continue;
            try { foreach (var d in Directory.GetDirectories(scan)) { if (Regex.IsMatch(Path.GetFileName(d), @"(stargate|frameless|^SGL[-_])", RegexOptions.IgnoreCase) && !Regex.IsMatch(d, @"(\.legacy-|\.backup-|copy$|kopia$)", RegexOptions.IgnoreCase)) candidates.Add(d); } } catch (UnauthorizedAccessException) { } catch (IOException) { }
        }
        var found = new List<Installation>();
        foreach (var c in candidates) foreach (var p in new[] { Gate, Frame }) try { ValidateRoot(p, c); if (!found.Any(i => i.Product == p && Same(i.Root, c))) found.Add(new(p, Path.GetFullPath(c).TrimEnd('\\'))); } catch { }
        return found;
    }
    internal static string Hash(string file) { using var s = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(s)); }
    internal static List<(string Original, string Copy)> BackupSettings(Product p, string local, string backup)
    {
        var result = new List<(string, string)>();
        foreach (var dir in new[] { Path.Combine(local, p.Settings), Path.Combine(local, p == Gate ? "StarGateWebView" : "FramelessWindow", "Profiles") }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue; NoLinks(dir);
            var files = Directory.GetFiles(dir, "*.json").ToList();
            var profiles = Path.Combine(dir, "Profiles"); if (Directory.Exists(profiles)) { NoLinks(profiles); files.AddRange(Directory.GetFiles(profiles, "*.json")); }
            foreach (var f in files.Distinct(StringComparer.OrdinalIgnoreCase))
            { if (File.GetAttributes(f).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Plik konfiguracji jest dowiązaniem."); var dest = Path.Combine(backup, "settings", Path.GetRelativePath(local, f)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(f, dest, false); if (Hash(f) != Hash(dest)) throw new IOException("Nie udało się zweryfikować kopii adresów."); result.Add((f, dest)); }
        }
        return result;
    }
    internal static bool AddressesEqual(string current, string saved)
    {
        using var a = JsonDocument.Parse(File.ReadAllText(current)); using var b = JsonDocument.Parse(File.ReadAllText(saved));
        foreach (var key in new[] { "LastUrl", "LastMode", "LastIpOrHost", "LastPort" })
        {
            var hasA = a.RootElement.TryGetProperty(key, out var av); var hasB = b.RootElement.TryGetProperty(key, out var bv);
            if (hasA != hasB || (hasA && av.GetRawText() != bv.GetRawText())) return false;
        }
        return true;
    }
    internal static void ImportFrame(string local)
    {
        var old = Path.Combine(local, "FramelessWindow", "config.json"); if (!File.Exists(old)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(old)); var cfg = doc.RootElement;
        var address = ReadString(cfg, "LastUrl");
        if (string.IsNullOrWhiteSpace(address))
        {
            var mode = ReadString(cfg, "LastMode");
            if (mode is "LocalDial9" or "LocalDial") address = "http://stargate.local/retro/" + (mode == "LocalDial9" ? "dial9.html" : "dial.html");
            else if (ReadString(cfg, "LastIpOrHost") is string host && !string.IsNullOrWhiteSpace(host))
            { var ub = new UriBuilder(host.Contains("://") ? host : "http://" + host); if (cfg.TryGetProperty("LastPort", out var port) && port.TryGetInt32(out int value)) ub.Port = value; address = ub.Uri.ToString(); }
        }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return;
        var key = Regex.Replace(uri.Host.ToLowerInvariant() + (uri.Port != 80 && uri.Port != 443 ? "-" + uri.Port : ""), @"[^a-z0-9\-_]", "_");
        var profiles = Path.Combine(local, "FramelessWindow", "Profiles"); Directory.CreateDirectory(profiles); NoLinks(profiles);
        var target = Path.Combine(profiles, key + ".json");
        if (File.Exists(target))
        { using var existing = JsonDocument.Parse(File.ReadAllText(target)); if (ReadString(existing.RootElement, "LastUrl") == address) return; target = Path.Combine(profiles, key + "-legacy-" + Guid.NewGuid().ToString("N")[..8] + ".json"); }
        var props = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(cfg.GetRawText())!;
        props["LastUrl"] = JsonSerializer.SerializeToElement(address); props["SetupCompleted"] = JsonSerializer.SerializeToElement(true);
        File.WriteAllText(target, JsonSerializer.Serialize(props, Json));
    }
    internal static void EnsureStopped(Product p)
    {
        foreach (var name in new[] { Path.GetFileNameWithoutExtension(p.Exe), p.Launcher })
        foreach (var process in Process.GetProcessesByName(name)) { process.Dispose(); throw new IOException("Zamknij " + p.Name + " oraz jego stary launcher i spróbuj ponownie."); }
    }
}
sealed class MigrationForm : Form
{
    readonly ComboBox choices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = false };
    readonly Button start = new() { Text = "Pobierz i migruj", AutoSize = true };
    readonly Button browse = new() { Text = "Wskaż stary folder…", AutoSize = true };
    bool busy;
    public MigrationForm()
    {
        Text = "StarGate / Frameless — migracja starszej wersji"; Width = 780; Height = 390; MinimumSize = new(650, 350); StartPosition = FormStartPosition.CenterScreen; Padding = new(18);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 }; buttons.Controls.AddRange(new Control[] { start, browse });
        var intro = new Label { Text = "Wybierz program do aktualizacji. Gdy zainstalowane są oba, aktualizowany będzie tylko wybrany.\nStary folder zostanie przeniesiony do kopii .legacy-…; adresy zostaną zachowane.\nPo pobraniu uruchomi się kreator aktualnej wersji. Wybierz nowy, pusty katalog.", Dock = DockStyle.Top, Height = 82 };
        Controls.Add(status); Controls.Add(choices); Controls.Add(intro); Controls.Add(buttons);
        var roots = new[] { Path.GetPathRoot(Environment.SystemDirectory)!, Path.Combine(Program.Local, "Programs") };
        foreach (var i in Program.Detect(Program.Local, roots)) choices.Items.Add(i);
        if (choices.Items.Count > 0) choices.SelectedIndex = 0;
        status.Text = choices.Items.Count == 0 ? "Nie znaleziono instalacji. Wskaż jej stary folder przyciskiem poniżej." : "Rozpoznano instalacje: " + choices.Items.Count + ". Kopia starej instalacji nie zostanie automatycznie usunięta.";
        browse.Click += (_, _) => { using var dlg = new FolderBrowserDialog { Description = "Wybierz folder starej instalacji (z plikiem EXE lub folderem architektury)", UseDescriptionForTitle = true }; if (dlg.ShowDialog(this) != DialogResult.OK) return; foreach (var p in new[] { Program.Gate, Program.Frame }) try { Program.ValidateRoot(p, dlg.SelectedPath); var i = new Installation(p, dlg.SelectedPath); choices.Items.Add(i); choices.SelectedItem = i; return; } catch { } MessageBox.Show(this, "Nie rozpoznano osobnego folderu instalacji StarGateWebView ani Framelesswindow. Folder projektu nie jest instalacją."); };
        start.Click += async (_, _) => await Migrate();
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; MessageBox.Show(this, "Poczekaj na zakończenie lub zamknij/anuluj uruchomiony kreator instalacji."); } };
    }
    async Task Migrate()
    {
        if (choices.SelectedItem is not Installation installation || busy) return;
        var p = installation.Product;
        if (MessageBox.Show(this, "Zaktualizować " + p.Name + "?\n\nStara instalacja: " + installation.Root + "\nZostanie przeniesiona do folderu kopii obok. Adresy zostaną zachowane.\n\nPo uruchomieniu kreatora wybierz nowy, pusty folder. Przy anulowaniu przywrócimy stary folder.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        busy = true; start.Enabled = browse.Enabled = choices.Enabled = false;
        string? moved = null, bootstrap = null, bootstrapBackup = null; bool committed = false;
        string session = Path.Combine(Program.Local, "StarGate-Frameless-Migration", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Program.ValidateRoot(p, installation.Root); Program.EnsureStopped(p);
            Directory.CreateDirectory(session);
            status.Text = "Pobieranie informacji z GitHuba…";
            using var network = new GitHubDownload(p);
            var package = await network.GetPackage();
            string installer = Path.Combine(session, p.Installer);
            status.Text = "Pobieranie " + package.Tag + "… Stara instalacja pozostaje bez zmian.";
            await network.Download(package, installer, n => status.Text = $"Pobieranie: {n / 1048576.0:F1} / {package.Size / 1048576.0:F1} MB");
            Program.EnsureStopped(p);
            var saved = Program.BackupSettings(p, Program.Local, session);
            if (p == Program.Frame) Program.ImportFrame(Program.Local);
            // Force the new package's own installation wizard, not its old installed-location shortcut.
            bootstrap = Path.Combine(Program.Local, "Universal_Launcher", p.Launcher + "-" + p.Package, "launcher.json");
            Program.NoLinks(Path.GetDirectoryName(bootstrap)!);
            if (File.Exists(bootstrap)) { bootstrapBackup = Path.Combine(session, "bootstrap-original.json"); File.Copy(bootstrap, bootstrapBackup, false); }
            // No recursive deletion: rename only the validated application root on the same volume.
            moved = installation.Root.TrimEnd('\\') + ".legacy-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
            File.WriteAllText(Path.Combine(session, "recovery.json"), JsonSerializer.Serialize(new { Application = p.Name, OldRoot = installation.Root, MovedRoot = moved, Bootstrap = bootstrap, BootstrapBackup = bootstrapBackup }, Program.Json));
            Directory.Move(installation.Root, moved);
            Directory.CreateDirectory(Path.GetDirectoryName(bootstrap)!); File.WriteAllText(bootstrap, "{}");
            status.Text = "Uruchomiono kreator nowej wersji. Wybierz nowy, pusty katalog instalacji.\nKopia starej wersji: " + moved;
            using (var setup = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, WorkingDirectory = session }) ?? throw new IOException("Nie udało się uruchomić instalatora.")) { await setup.WaitForExitAsync(); if (setup.ExitCode != 0) throw new IOException("Instalator zgłosił błąd."); }
            var installed = FindNewInstallation(bootstrap, p, installation.Root, moved);
            if (installed == null) throw new IOException("Nie potwierdzono zakończonej instalacji nowej wersji. Stary folder zostanie przywrócony, o ile jego miejsce jest wolne.");
            var dll = Path.Combine(installed, "app-x64", Path.ChangeExtension(p.Exe, ".dll"));
            if (!File.Exists(dll) || !Program.Hash(dll).Equals(package.AppHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Nowa aplikacja nie odpowiada pobranemu wydaniu.");
            foreach (var pair in saved) if (!File.Exists(pair.Original) || !Program.AddressesEqual(pair.Original, pair.Copy))
                throw new IOException("Plik zapisanych adresów zmienił się podczas instalacji. Jego kopia jest w " + session);
            RepairShortcuts(p, installation.Root, moved, Path.Combine(installed, "app-x64", p.Exe), session);
            committed = true;
            File.WriteAllText(Path.Combine(session, "completed.txt"), "New installation: " + installed + "\nOld installation backup: " + moved);
            status.Text = "Gotowe. Nowa wersja: " + installed + "\nStara wersja przeniesiona do: " + moved + "\nKopia konfiguracji: " + session;
            MessageBox.Show(this, status.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            var recovery = "";
            if (!committed && moved != null && Directory.Exists(moved))
            {
                try { if (Directory.Exists(installation.Root)) recovery = "\nMiejsce starego folderu jest zajęte. Kopia pozostaje w: " + moved; else { Directory.Move(moved, installation.Root); recovery = "\nPrzywrócono stary folder."; } } catch (Exception restore) { recovery = "\nKopia starej wersji: " + moved + "\nPrzywrócenie: " + restore.Message; }
            }
            if (!committed && bootstrap != null) try { if (bootstrapBackup != null) File.Copy(bootstrapBackup, bootstrap, true); else if (File.Exists(bootstrap) && File.ReadAllText(bootstrap).Trim() == "{}") File.Delete(bootstrap); } catch { recovery += "\nZachowano pliki konfiguracji do ręcznego przywrócenia w " + session; }
            status.Text = "Migracja nie została zakończona. " + ex.Message + recovery + "\nDane odzyskiwania: " + session;
            MessageBox.Show(this, status.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { busy = false; start.Enabled = browse.Enabled = choices.Enabled = true; }
    }
    internal static string? FindNewInstallation(string bootstrap, Product p, string oldRoot, string backup)
    {
        if (!File.Exists(bootstrap)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(bootstrap)); var root = Program.ReadString(doc.RootElement, "InstalledBaseDir");
        if (root == null || !Path.IsPathFullyQualified(root) || Program.Same(root, backup) || Program.Within(root, backup)) return null;
        var manifest = Path.Combine(root, "launcher-package.json"); if (!File.Exists(manifest)) return null;
        using var m = JsonDocument.Parse(File.ReadAllText(manifest)); return Program.ReadString(m.RootElement, "PackageId") == p.Package && File.Exists(Path.Combine(root, "app-x64", p.Exe)) ? root : null;
    }
    static void RepairShortcuts(Product p, string old, string backup, string target, string session)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Brak obsługi skrótów Windows."); dynamic shell = Activator.CreateInstance(shellType)!;
        foreach (var dir in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Startup })
        foreach (var file in Directory.Exists(Environment.GetFolderPath(dir)) ? Directory.GetFiles(Environment.GetFolderPath(dir), "*.lnk") : Array.Empty<string>())
        {
            dynamic link = shell.CreateShortcut(file); string previous = (string)link.TargetPath;
            if (!(Program.Within(previous, old) || Program.Within(previous, backup))) continue;
            var name = Path.GetFileName(previous);
            if (!name.Equals(p.Exe, StringComparison.OrdinalIgnoreCase) && !name.Equals(p.Launcher + ".exe", StringComparison.OrdinalIgnoreCase) && !(p == Program.Gate && name.Equals("StarGateLauncherAll.exe", StringComparison.OrdinalIgnoreCase))) continue;
            var copy = Path.Combine(session, "shortcuts", dir.ToString(), Path.GetFileName(file)); Directory.CreateDirectory(Path.GetDirectoryName(copy)!); File.Copy(file, copy, false);
            link.TargetPath = target; link.WorkingDirectory = Path.GetDirectoryName(target); link.IconLocation = target + ",0"; link.Save();
        }
    }
}
sealed class GitHubDownload : IDisposable
{
    readonly Product product; readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(15) };
    string? token;
    public GitHubDownload(Product p) { product = p; http.DefaultRequestHeaders.UserAgent.ParseAdd("StarGate-Frameless-Migrator/1.0"); }
    internal static bool Trusted(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(uri.Host);
    async Task<HttpResponseMessage> Get(Uri uri, bool asset)
    {
        for (int redirects = 0; redirects < 6; redirects++)
        {
            if (!Trusted(uri)) throw new IOException("Niezaufany adres pobierania.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(asset ? "application/octet-stream" : "application/vnd.github+json")); request.Headers.CacheControl = new() { NoCache = true };
            if (token != null && uri.Host == "api.github.com") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (uri.Host == "api.github.com" && product == Program.Frame && token == null && (int)response.StatusCode is 401 or 404)
            { response.Dispose(); token = await Credential(); redirects--; continue; }
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            { var next = response.Headers.Location; response.Dispose(); if (next == null) throw new IOException("Brak adresu przekierowania."); uri = next.IsAbsoluteUri ? next : new Uri(uri, next); continue; }
            if (!response.IsSuccessStatusCode) { var code = (int)response.StatusCode; response.Dispose(); throw new IOException("GitHub: HTTP " + code + ". Dla prywatnego Framelesswindow wymagany jest dostęp przez Git Credential Manager."); }
            return response;
        }
        throw new IOException("Zbyt wiele przekierowań.");
    }
    static async Task<string> Bounded(HttpResponseMessage response, int maximum)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(); using var ms = new MemoryStream(); var b = new byte[8192]; int n;
        while ((n = await stream.ReadAsync(b)) > 0) { if (ms.Length + n > maximum) throw new IOException("Zbyt duży plik informacji o aktualizacji."); ms.Write(b, 0, n); } return Encoding.UTF8.GetString(ms.ToArray());
    }
    public async Task<Package> GetPackage()
    {
        var api = "https://api.github.com/repos/matelv-x/" + product.Name + "/releases";
        using var response = await Get(new Uri(api + "/latest"), false); using var release = JsonDocument.Parse(await Bounded(response, 262144));
        Asset? installer = null, manifest = null;
        foreach (var a in release.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString(); if (name != product.Installer && name != product.Name + "-update.json") continue;
            var url = new Uri(a.GetProperty("url").GetString()!);
            if (!url.AbsoluteUri.StartsWith(api + "/assets/", StringComparison.Ordinal) || !long.TryParse(url.Segments.Last(), out _)) throw new IOException("Błędny adres zasobu aktualizacji.");
            var asset = new Asset(url, a.GetProperty("size").GetInt64()); if (name == product.Installer) installer = asset; else manifest = asset;
        }
        if (installer == null || manifest == null) throw new IOException("Wydanie nie zawiera instalatora lub manifestu aktualizacji.");
        using var mr = await Get(manifest.Url, true); using var m = JsonDocument.Parse(await Bounded(mr, 16384)); var e = m.RootElement;
        var fields = e.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
        string hash = fields["InstallerSha256"].GetString()!, appHash = fields["ApplicationSha256"].GetString()!; long size = fields["InstallerSize"].GetInt64();
        if (fields["Schema"].GetInt32() != 1 || fields["Application"].GetString() != product.Name || fields["Architecture"].GetString() != "x64" || size != installer.Size || size <= 0 || size > 1073741824 || !Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$") || !Regex.IsMatch(appHash, "^[a-fA-F0-9]{64}$")) throw new IOException("Manifest nie pasuje do wybranego programu.");
        return new(installer, hash, size, appHash, release.RootElement.GetProperty("tag_name").GetString()!);
    }
    public async Task Download(Package package, string path, Action<long> progress)
    {
        using var response = await Get(package.Installer.Url, true); await using var source = await response.Content.ReadAsStreamAsync();
        await using (var dest = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        { byte[] buffer = new byte[81920]; long total = 0, displayed = 0; int n; while ((n = await source.ReadAsync(buffer)) > 0) { total += n; if (total > package.Size) throw new IOException("Pakiet jest większy niż w manifeście."); await dest.WriteAsync(buffer.AsMemory(0, n)); if (total - displayed > 1048576) { progress(total); displayed = total; } } if (total != package.Size) throw new IOException("Niepełne pobranie."); }
        if (!Program.Hash(path).Equals(package.Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Suma SHA-256 instalatora jest nieprawidłowa. Stara instalacja nie została zmieniona.");
    }
    static async Task<string> Credential()
    {
        var candidates = new List<string>();
        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryHive.LocalMachine }) foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
        { using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view); using var key = root.OpenSubKey(@"SOFTWARE\GitForWindows"); if (key?.GetValue("InstallPath") is string install) candidates.Add(Path.Combine(install, "cmd", "git.exe")); }
        candidates.AddRange(new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git/cmd/git.exe"), Path.Combine(Program.Local, "Programs/Git/cmd/git.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache/codex-runtimes/codex-primary-runtime/dependencies/native/git/cmd/git.exe") });
        var git = candidates.FirstOrDefault(File.Exists) ?? throw new IOException("Zaloguj konto z dostępem do Framelesswindow w Git Credential Manager.");
        var info = new ProcessStartInfo(git, "credential fill") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        string gitRoot = Path.GetDirectoryName(Path.GetDirectoryName(git))!;
        info.Environment["PATH"] = string.Join(Path.PathSeparator, Path.GetDirectoryName(git), Path.Combine(gitRoot, "mingw64", "bin"), Environment.GetFolderPath(Environment.SpecialFolder.System), Environment.GetEnvironmentVariable("PATH") ?? "");
        info.Environment["GIT_TERMINAL_PROMPT"] = "0"; info.Environment["GCM_INTERACTIVE"] = "never";
        using var process = Process.Start(info) ?? throw new IOException("Nie można odczytać dostępu GitHub."); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.StandardInput.WriteAsync("protocol=https\nhost=github.com\nusername=matelv-x\n\n"); process.StandardInput.Close(); var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token); await process.WaitForExitAsync(timeout.Token); var value = (await stdout).Split('\n').FirstOrDefault(l => l.StartsWith("password=")); await stderr; if (process.ExitCode == 0 && value is { Length: > 9 }) return value[9..].TrimEnd('\r'); throw new IOException("Brak zapisanego dostępu GitHub do prywatnego Framelesswindow."); }
        finally { if (!process.HasExited) process.Kill(); }
    }
    public void Dispose() { token = null; http.Dispose(); }
}



