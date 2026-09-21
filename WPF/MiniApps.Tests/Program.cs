using System.IO;
using System.Security.Cryptography;
using MiniApps.Models;
using MiniApps.Services;
using MiniApps.ViewModels;

var passed = 0;
if (args.Contains("--information-audit"))
{
    var rows = await InformationService.ReadAsync(CancellationToken.None);
    if (rows.Count != 12 || rows.Any(row => string.IsNullOrWhiteSpace(row.Value))) throw new Exception("Information collection failed.");
    Console.WriteLine("PASS embedded Information script: 12 populated rows; device values omitted.");
    return;
}
void Check(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Reject(Action action) { try { action(); } catch (Exception) { return; } throw new Exception("Expected rejection"); }
async Task WaitWithTimeout(Task task, TimeSpan timeout, CancellationToken token = default)
{
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
    var timeoutTask = Task.Delay(timeout, deadline.Token);
    if (await Task.WhenAny(task, timeoutTask) != task) throw new TimeoutException("Fixture timed out.");
    deadline.Cancel();
    await task;
}
if (args.Contains("--smart-skip-audit", StringComparer.OrdinalIgnoreCase))
{
    var snapshot = InstalledSoftwareDetector.Capture();
    foreach (var app in Catalog.Defaults())
    {
        var result = InstalledSoftwareDetector.Detect(app, snapshot);
        Console.WriteLine($"{app.Id}\t{result.State}\t{result.Reason}\t{result.Evidence}");
    }
    Console.WriteLine($"READ_ERRORS\t{snapshot.ReadErrors.Count}");
    return;
}
Check("Default catalog contains four office suite alternatives", () => { var apps = Catalog.Defaults(); Catalog.Validate(apps); Assert(apps.Count == 14 && apps.Where(a => a.Suite.Length > 0).Select(a => a.Suite).SequenceEqual(new[] { "Office", "WPS", "OnlyOffice", "LibreOffice" })); });
Check("Reject HTTP", () => { var apps = Catalog.Defaults(); apps[0].Url = "http://example.com/test.exe"; Reject(() => Catalog.Validate(apps)); });
Check("Reject duplicate IDs", () => { var apps = Catalog.Defaults(); apps[1].Id = apps[0].Id; Reject(() => Catalog.Validate(apps)); });
Check("Reject path traversal ID", () => { var apps = Catalog.Defaults(); apps[0].Id = "../escape"; Reject(() => Catalog.Validate(apps)); });
Check("Reject non-installer URL", () => { var apps = Catalog.Defaults(); apps[0].Url = "https://example.com/a.ps1"; Reject(() => Catalog.Validate(apps)); });
Check("Installed applications are recognized automatically", () => {
    Assert(DeploymentService.InstalledNameMatches(Catalog.Defaults().Single(a => a.Id == "chrome"), "Google Chrome"));
    Assert(DeploymentService.InstalledNameMatches(Catalog.Defaults().Single(a => a.Id == "office"), "Microsoft Office LTSC Professional Plus 2024 - en-us"));
    Assert(DeploymentService.InstalledNameMatches(Catalog.Defaults().Single(a => a.Id == "office"), "Microsoft 365 Apps for enterprise"));
    Assert(DeploymentService.InstalledNameMatches(Catalog.Defaults().Single(a => a.Id == "vc64"), "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.44.35211"));
    Assert(!DeploymentService.InstalledNameMatches(Catalog.Defaults().Single(a => a.Id == "vc64"), "Microsoft Visual C++ 2013 Redistributable (x64) - 12.0.40664"));
    Assert(DeploymentService.InstalledNameMatches(new AppDefinition { Id = "custom", Name = "7-Zip" }, "7-Zip 24.09 (x64)"));
    Assert(!DeploymentService.InstalledNameMatches(new AppDefinition { Id = "custom", Name = "Zoom" }, "Zoom Outlook Plugin"));
});
Check("Smart Skip distinguishes every default product family", () => {
    var apps = Catalog.Defaults().ToDictionary(app => app.Id);
    var cases = new[] {
        ("evkey", "EVKey 4.6", true), ("evkey", "EVKey Helper", false),
        ("chrome", "Google Chrome", true), ("chrome", "Google Chrome Beta", false),
        ("klite", "K-Lite Codec Pack 19.8.2 Standard", true), ("klite", "Example K-Lite Codec Pack Helper", false),
        ("telegram", "Telegram Desktop", true), ("telegram", "Telegram Desktop Updater", false),
        ("ultraview", "UltraViewer version 6.6.124", true), ("ultraview", "UltraViewer Service", false),
        ("winrar", "WinRAR 7.23 (64-bit)", true), ("winrar", "WinRAR Shell Extension", false),
        ("zalo", "Zalo 26.07.10", true), ("zalo", "Zalo Update", false),
        ("zoom", "Zoom Workplace (64-bit)", true), ("zoom", "Zoom Outlook Plugin", false),
        ("office", "Microsoft Office Home and Student 2021 - en-us", true), ("office", "Office 16 Click-to-Run Extensibility Component", false),
        ("office", "Microsoft 365 Apps for enterprise - en-us", true), ("office", "Microsoft Visio Professional 2024", false),
        ("wps", "WPS Office (12.2.0)", true), ("wps", "WPS Office Update", false),
        ("onlyoffice", "ONLYOFFICE Desktop Editors 8.2.2 (x64)", true), ("onlyoffice", "ONLYOFFICE Desktop Editors Help", false),
        ("libreoffice", "LibreOffice 25.2.5.2", true), ("libreoffice", "LibreOffice 25.2 Help Pack (English)", false),
        ("vc64", "Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247", true), ("vc64", "Microsoft Visual C++ 2013 Redistributable (x64) - 12.0.40664", false),
        ("vc86", "Microsoft Visual C++ 2015-2022 Redistributable (x86) - 14.44.35211", true), ("vc86", "Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247", false)
    };
    foreach (var (id, name, expected) in cases)
        Assert(DeploymentService.InstalledNameMatches(apps[id], name) == expected);
});
Check("Smart Skip uses Office SKU and VC runtime evidence", () => {
    var apps = Catalog.Defaults().ToDictionary(app => app.Id);
    var snapshot = new InstalledSoftwareSnapshot();
    snapshot.OfficeProductReleaseIds.Add("HomeStudent2021Retail");
    snapshot.VisualCRuntimes.Add(new("x64", true, "v14.51.36247.00", "fixture/x64"));
    snapshot.VisualCRuntimes.Add(new("x86", true, "v14.51.36247.00", "fixture/x86"));
    Assert(InstalledSoftwareDetector.Detect(apps["office"], snapshot).State == SoftwareDetectionState.Installed);
    Assert(InstalledSoftwareDetector.Detect(apps["vc64"], snapshot).State == SoftwareDetectionState.Installed);
    Assert(InstalledSoftwareDetector.Detect(apps["vc86"], snapshot).State == SoftwareDetectionState.Installed);
    var oldOnly = new InstalledSoftwareSnapshot();
    oldOnly.VisualCRuntimes.Add(new("x64", true, "v12.0.40664", "fixture/old"));
    Assert(InstalledSoftwareDetector.Detect(apps["vc64"], oldOnly).State == SoftwareDetectionState.NotInstalled);
});
Check("Smart Skip reports incomplete inventory as Unknown", () => {
    var snapshot = new InstalledSoftwareSnapshot();
    snapshot.ReadErrors.Add("fixture access denied");
    var result = InstalledSoftwareDetector.Detect(Catalog.Defaults().Single(app => app.Id == "chrome"), snapshot);
    Assert(result.State == SoftwareDetectionState.Unknown && result.Reason.Contains("Không đọc được"));
});
Check("Reject malformed hash", () => { var apps = Catalog.Defaults(); apps[0].Sha256 = "123"; Reject(() => Catalog.Validate(apps)); });
Check("PowerShell literals cannot inject code", () => Assert(DeploymentService.Quote("a'; exit 0; '") == "'a''; exit 0; '''"));
Check("Framework process arguments use Windows quoting rules", () =>
{
    Assert(ProcessCompatibility.QuoteArgument("") == "\"\"");
    Assert(ProcessCompatibility.QuoteArgument("plain") == "plain");
    Assert(ProcessCompatibility.QuoteArgument("a b") == "\"a b\"");
    Assert(ProcessCompatibility.QuoteArgument("a\"b") == "\"a\\\"b\"");
});
Check("Framework HTTP handler does not impose a catalog connection limit", () =>
{
    using var handler = DeploymentService.CreateDefaultHttpHandler();
    Assert(handler.MaxConnectionsPerServer == int.MaxValue);
});
var root = Path.Combine(Path.GetTempPath(), "MiniApps-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
// Keep Smart Skip decision logs inside the disposable test folder, not on the machine.
DeploymentService.InstallLogDirectory = Path.Combine(root, "install-logs");
ExtensionService.LogDirectory = Path.Combine(root, "extend-logs");
ExtensionService.ToolsDirectory = Path.Combine(root, "extend-tools");
try
{
    Check("Startup failures are logged durably with inner exceptions", () => {
        var logRoot = Path.Combine(root, "startup-logs");
        Exception failure;
        try { throw new InvalidOperationException("inner startup detail"); }
        catch (Exception inner) { failure = new ApplicationException("outer startup failure", inner); }
        var path = StartupFailureLog.TryWrite(failure, logRoot);
        Assert(path != null && File.Exists(path));
        var contents = File.ReadAllText(path!);
        Assert(contents.Contains("ApplicationException") && contents.Contains("outer startup failure") && contents.Contains("InvalidOperationException") && contents.Contains("inner startup detail"));
        var userMessage = StartupFailureLog.BuildUserMessage(failure, path);
        Assert(userMessage.Contains("MiniApps không thể khởi động") && userMessage.Contains(path!));
    });
    Check("All built-ins expose nonempty executable scripts", () => {
        var settings = WindowsSettingsCatalog.Defaults(); WindowsSettingsCatalog.Validate(settings);
        Assert(settings.All(s => !string.IsNullOrWhiteSpace(s.Script)));
        Assert(settings.Single(s => s.Id == "Winget").Script.Contains("https://"));
        Assert(settings.All(s => s.Id != "Debloat" && s.Action != "Debloat"));
        settings[0].Script = ""; Reject(() => WindowsSettingsCatalog.Validate(settings));
    });
    Check("Legacy built-ins migrate without overwriting files or edited commands", () => {
        var dir = Path.Combine(root, "legacy"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "windows.json");
        var legacy = """[{"Id":"Timezone","Name":"Timezone","Action":"Timezone"}]""";
        File.WriteAllText(path, legacy); var store = new SettingsStore(dir);
        var loaded = store.LoadWindows(); Assert(loaded.Count == 2 && loaded[0].Script.Contains("Set-TimeZone") && loaded.Any(x => x.Id == "InfoExe") && File.ReadAllText(path) == legacy);
        File.WriteAllText(path, """{"SchemaVersion":1,"Items":[{"Id":"Timezone","Name":"Timezone","Action":"Timezone","Script":"Write-Output 'edited builtin'"}],"RemovedDefaultIds":["InfoExe"]}""");
        loaded = store.LoadWindows();
        Assert(loaded.Count == 1 && loaded[0].Script == "Write-Output 'edited builtin'" && loaded.All(x => x.Id != "InfoExe"));
        var calls = 0;
        var service = new DeploymentService(run: (command, work, _) => {
            calls++; Assert(command.Contains("setting-Timezone.ps1") && !command.Contains("-Option"));
            Assert(File.ReadAllText(Path.Combine(work, "setting-Timezone.ps1")) == loaded[0].Script);
            return Task.FromResult(0);
        });
        service.RunAsync([], loaded, Path.Combine(dir, "run"), new InlineProgress<DeploymentEvent>(_ => { }), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(calls == 1);
    });
    Check("Future settings schema is rejected without rewriting it", () => {
        var dir = Path.Combine(root, "future"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "windows.json"); var json = """{"SchemaVersion":99,"Items":[],"RemovedDefaultIds":[]}""";
        File.WriteAllText(path, json); Reject(() => new SettingsStore(dir).LoadWindows()); Assert(File.ReadAllText(path) == json);
    });
    Check("Navigation exposes the pages supported by the runtime", () => {
        var vm = new MainViewModel(true);
        Assert(vm.IsInstall && vm.Page == 0);
        vm.Page = 1;
        Assert(vm.Page == 1 && !vm.IsInstall && !vm.IsExtend);
        vm.Page = 2;
        Assert(vm.Page == 2 && vm.IsExtend && !vm.IsInstall);
        vm.Page = 3;
        Assert(vm.Page == 2 && vm.IsExtend);
    });
    Check("EXTEND lists Debloat, the C++ environment and Share LAN", () => {
        var vm = new MainViewModel(true);
        Assert(vm.Extensions.Select(e => e.Id).SequenceEqual(new[] { "debloat", "cpp", "sharelan" }));
        Assert(vm.Extensions.All(e => e.Name.Length > 0 && e.Description.Length > 0 && e.Status == "Sẵn sàng"));
        Assert(vm.Extensions.Where(e => !e.Interactive).All(e => e.ConfirmText.Length > 0) && vm.Extensions.Single(e => e.Interactive).Id == "sharelan");
        Assert(vm.Extensions.All(e => vm.RunExtensionCommand.CanExecute(e) && !vm.OpenExtensionLogCommand.CanExecute(e)));
    });
    Check("EXTEND writes its embedded scripts, logs the run and removes its work folder", () => {
        string? seenScript = null; string? seenWork = null; var siblings = new List<string>();
        var service = new ExtensionService((script, work, log) =>
        {
            seenScript = script; seenWork = work;
            siblings.AddRange(Directory.GetFiles(work).Select(Path.GetFileName)!);
            Assert(File.ReadAllText(script).Contains("-RunDefaults") || File.ReadAllText(script).Contains("mingw-w64-ucrt-x86_64-toolchain"));
            log.Report("dòng thử");
            return Task.FromResult(3);
        });
        var lines = new List<string>();
        var result = service.RunAsync("cpp", new InlineProgress<string>(lines.Add)).GetAwaiter().GetResult();
        Assert(result.ExitCode == 3 && Path.GetFileName(seenScript) == "Install-CppEnvironment.ps1" && !Directory.Exists(seenWork));
        Assert(siblings.OrderBy(x => x).SequenceEqual(new[] { "Install-CppEnvironment.ps1", "Update-Winget.ps1" }));
        // The exit line goes to the log only, so the card keeps the script's own last progress line.
        Assert(lines.SequenceEqual(new[] { "dòng thử" }));
        Assert(result.LogPath.StartsWith(ExtensionService.LogDirectory) && File.ReadAllText(result.LogPath).Contains("dòng thử") &&
            File.ReadAllText(result.LogPath).Contains("Kết thúc với mã 3."));
        result = service.RunAsync("debloat", new InlineProgress<string>(_ => { })).GetAwaiter().GetResult();
        Assert(Path.GetFileName(seenScript) == "Invoke-Win11Debloat.ps1" && result.ExitCode == 3);
        Reject(() => ExtensionService.ScriptsFor("unknown"));
    });
    Check("EXTEND cards show progress lines and keep indented tool output for the log", () => {
        Assert(MainViewModel.IsProgressLine("VS Code: đang tải...") && MainViewModel.IsProgressLine("> Removing selected apps for all users..."));
        Assert(!MainViewModel.IsProgressLine("    winget kết thúc với mã 0.") && !MainViewModel.IsProgressLine("      (12/150) installing gcc"));
        Assert(!MainViewModel.IsProgressLine("") && !MainViewModel.IsProgressLine("   ") && !MainViewModel.IsProgressLine(null));
    });
    Check("Share LAN opens its own window without a confirmation or the install lock", () => {
        var launched = new List<string>();
        var vm = new MainViewModel(false, confirm: _ => throw new Exception("Share LAN must not ask for confirmation"), launchTool: launched.Add);
        var share = vm.Extensions.Single(e => e.Id == "sharelan");
        vm.RunExtensionCommand.Execute(share);
        Assert(launched.SequenceEqual(new[] { "sharelan" }) && share.Status == "Đã mở" && !vm.IsBusy && !share.IsRunning);
        var failing = new MainViewModel(false, launchTool: _ => throw new IOException("no powershell"));
        var failed = failing.Extensions.Single(e => e.Id == "sharelan");
        failing.RunExtensionCommand.Execute(failed);
        Assert(failed.Status == "Không mở được" && failed.Detail == "no powershell");
    });
    Check("Share LAN script is written from the embedded copy of the tool", () => {
        var path = ExtensionService.WriteTool("sharelan");
        Assert(path == Path.Combine(ExtensionService.ToolsDirectory, "Share-LAN.ps1"));
        var text = File.ReadAllText(path);
        Assert(text.Contains("function Invoke-ServerMode") && text.Contains("function Invoke-ClientMode"));
        Assert(ExtensionService.WriteTool("sharelan") == path && File.ReadAllText(path) == text);
    });
    Check("EXTEND scripts run hidden: UTF-8 output, no waiting for a key, real exit code", () => {
        var dir = Path.Combine(root, "extend-script"); Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "fixture.ps1");
        File.WriteAllText(script, string.Join("\n", new[] {
            "[Console]::OutputEncoding = [Text.Encoding]::UTF8",
            "Write-Output 'Tiếng Việt có dấu'",
            "[Console]::Error.WriteLine('lỗi thử')",
            "$null = [Console]::ReadKey()",
            "Write-Output 'sau ReadKey'",
            "exit 7" }), new System.Text.UTF8Encoding(true));
        var lines = new List<string>();
        var run = ExtensionService.RunScriptFileAsync(script, dir, new InlineProgress<string>(line => { lock (lines) lines.Add(line); }));
        WaitWithTimeout(run, TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
        Assert(run.Result == 7 && lines.Contains("Tiếng Việt có dấu") && lines.Contains("lỗi thử") && lines.Contains("sau ReadKey"));
    });
    Check("Information Driver copies serial before opening official support", () => {
        var parsed = InformationService.Parse("""{"OS":"Windows","Serial":"ABC123","Manufacturer":"Dell Inc."}""");
        var serial = parsed.Single(row => row.Name == "Serial");
        var steps = new List<string>();
        MiniApps.InformationView.OpenDriverSupport(serial.Value, serial.DriverUrl, value => steps.Add("copy:" + value), url => steps.Add("open:" + url));
        Assert(steps.Count == 2 && steps[0] == "copy:ABC123" && steps[1].StartsWith("open:https://www.dell.com/"));
        steps.Clear();
        Reject(() => MiniApps.InformationView.OpenDriverSupport("ABC123", serial.DriverUrl, _ => throw new Exception("Clipboard busy"), _ => steps.Add("open")));
        Assert(steps.Count == 0);
        Reject(() => MiniApps.InformationView.OpenDriverSupport("N/A", serial.DriverUrl, _ => steps.Add("copy"), _ => steps.Add("open")));
        Assert(steps.Count == 0);
        Reject(() => MiniApps.InformationView.OpenDriverSupport("ABC123", "", _ => steps.Add("copy"), _ => steps.Add("open")));
        Assert(steps.SequenceEqual(new[] { "copy" }));
    });
    Check("Required packaged settings fail closed", () => {
        var missing = new SettingsStore(Path.Combine(root, "missing-public"));
        Reject(() => missing.Load(required: true));
        Reject(() => missing.LoadWindows(required: true));
    });
    Check("Reject invalid Windows action, duplicate and traversal IDs", () => {
        var settings = WindowsSettingsCatalog.Defaults(); settings[0].Action = "Injected"; Reject(() => WindowsSettingsCatalog.Validate(settings));
        settings = WindowsSettingsCatalog.Defaults(); settings[0].Id = "../test"; Reject(() => WindowsSettingsCatalog.Validate(settings));
        settings = WindowsSettingsCatalog.Defaults(); settings[1].Id = settings[0].Id; Reject(() => WindowsSettingsCatalog.Validate(settings));
    });
    Check("Custom script staged safely and reported in setting namespace", () => {
        var setting = new WindowsSettingDefinition { Id = "chrome", Name = "Test", Script = "Write-Output 'Xin chào'" };
        var dir = Path.Combine(root, "custom"); var outcomes = new List<DeploymentEvent>(); var calls = 0;
        var service = new DeploymentService(run: (command, work, _) => { calls++; Assert(command.Contains("setting-chrome.ps1") && !command.Contains("Xin chào") && File.ReadAllText(Path.Combine(work, "setting-chrome.ps1")) == setting.Script); return Task.FromResult(0); });
        service.RunAsync([], [setting], dir, new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(calls == 1 && outcomes.Any(e => e.Id == "windows:chrome" && e.Finished));
    });
    var exe = Path.Combine(root, "test.exe");
    File.WriteAllBytes(exe, [0x4D, 0x5A, 0, 0, 0, 0, 0, 0]);
    string hash;
    using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(exe))).Replace("-", "");
    Check("Valid header and hash", () => DeploymentService.VerifyFileAsync(exe, hash, default).GetAwaiter().GetResult());
    Check("Hash mismatch rejected", () => Reject(() => DeploymentService.VerifyFileAsync(exe, new string('0', 64), default).GetAwaiter().GetResult()));
    File.WriteAllText(exe, "<html>proxy error</html>");
    Check("HTML disguised as EXE rejected", () => Reject(() => DeploymentService.VerifyFileAsync(exe, "", default).GetAwaiter().GetResult()));
    Check("Owned PowerShell process drains both streams and reports its real exit code", () =>
    {
        var lines = new System.Collections.Concurrent.ConcurrentBag<string>();
        var command = "1..300 | ForEach-Object { Write-Output ('out ' + $_); [Console]::Error.WriteLine('err ' + $_) }; exit 7";
        var code = DeploymentService.RunPowerShellAsync(command, root, new InlineProgress<string>(lines.Add)).GetAwaiter().GetResult();
        Assert(code == 7 && lines.Count(line => line.StartsWith("out ")) == 300 && lines.Count(line => line.StartsWith("err ")) == 300);
    });
    Check("Cancelling a stalled Framework response stream completes promptly", () =>
    {
        using var handler = new StalledHttp();
        using var client = new System.Net.Http.HttpClient(handler);
        using var cancel = new CancellationTokenSource();
        var outcomes = new List<DeploymentEvent>();
        var service = new DeploymentService(client, (_, _, _) => throw new Exception("Cancelled download must not launch"), _ => false);
        var operation = service.RunAsync(Catalog.Defaults().Take(1).ToArray(), [], Path.Combine(root, "stalled"), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), cancel.Token);
        WaitWithTimeout(handler.Started, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        cancel.Cancel();
        WaitWithTimeout(operation, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Assert(clock.Elapsed < TimeSpan.FromSeconds(2) && outcomes.Last().Status == "Đã hủy" && outcomes.Last().Finished && !outcomes.Last().Failed);
    });
    Check("Install includes all remaining Windows settings without selection state", () => { var vm = new MainViewModel(true); Assert(vm.Apps.Count == 10 && vm.WindowsOptions.Count == 11 && vm.WindowsOptions.All(o => !MainViewModel.IsDebloat(o.Definition)) && typeof(WindowsOption).GetProperty("Selected") == null && vm.SelectionText.Contains("11 thiết lập")); });
    Check("Disk setting keeps the CLI partitioning rules", () => {
        var setting = WindowsSettingsCatalog.Defaults().Single(s => s.Id == "Disk");
        foreach (var part in new[] { "SizeD = 50.1GB", "SizeD = 200.1GB", "SizeD = 400.1GB; SizeE = 200.1GB", "$totalGB -gt 1100",
            "-ge 200 -and $totalGB -le 300", "-ge 400 -and $totalGB -le 600", "-ge 800 -and $totalGB -le 1100", "$MinimumC = 30GB",
            "Get-Partition -DriveLetter D", "Get-Partition -DriveLetter E", "Get-BitLockerVolume", "AddMinutes(60)", "powercfg /h off",
            "'OS'", "'LOCAL I'", "'LOCAL II'", "Get-PartitionSupportedSize" })
            Assert(setting.Script.Contains(part));
        // -Quick does not exist on Format-Volume and leaves the volume RAW; only the comment may name it.
        Assert(!setting.Script.Split('\n').Any(line => !line.TrimStart().StartsWith("#") && line.Contains("-Quick")) && WindowsCompatibility.Supports(setting, WindowsCompatibility.MinimumWindowsBuild));
    });
    Check("SMB setting opens sharing on private networks only and keeps the SMB1 server off", () => {
        var setting = WindowsSettingsCatalog.Defaults().Single(s => s.Id == "Smb");
        foreach (var part in new[] { "EnableInsecureGuestLogons $true", "AllowInsecureGuestAuth", "RequireSecuritySignature $false",
            "SMB1Protocol-Client", "SMB1Protocol-Deprecation", "EnableSMB1Protocol $false", "@FirewallAPI.dll,-32752", "@FirewallAPI.dll,-28502",
            "-NetworkCategory Private", "FDResPub" })
            Assert(setting.Script.Contains(part));
        Assert(!setting.Script.Contains("SMB1Protocol-Server") && !setting.Script.Contains("EnableSMB1Protocol $true"));
        Assert(WindowsCompatibility.Supports(setting, WindowsCompatibility.MinimumWindowsBuild));
    });
    Check("Execution policy setting sets Bypass for the machine without prompting", () => {
        var setting = WindowsSettingsCatalog.Defaults().Single(s => s.Id == "ExecutionPolicy");
        Assert(setting.Action == "ExecutionPolicy" && setting.Script.Contains("-ExecutionPolicy Bypass") &&
            setting.Script.Contains("-Scope LocalMachine") && setting.Script.Contains("-Force"));
        Assert(WindowsCompatibility.Supports(setting, WindowsCompatibility.MinimumWindowsBuild));
    });
    Check("Info.exe setting downloads a validated executable to Desktop", () => {
        var info = WindowsSettingsCatalog.Defaults().Single(s => s.Id == "InfoExe");
        Assert(info.Name == "Info.exe" && info.Script.Contains("/info.exe") && info.Script.Contains("GetFolderPath('Desktop')"));
        Assert(info.Script.Contains("0x4D") && info.Script.Contains("0x5A") && info.Script.Contains("Copy-Item"));
        Assert(!info.Script.Contains("Start-Process"));
    });
    Check("Driver brands resolve to official HTTPS support pages", () => {
        var dell = DeviceInfoService.Resolve("PC-01", "Dell Inc.", "Latitude 5450", "ABC123");
        Assert(dell.Brand == "Dell" && dell.DriverUrl.StartsWith("https://www.dell.com/") && dell.Serial == "ABC123");
        Assert(DeviceInfoService.Resolve("PC", "HP", "EliteBook", "S1").DriverUrl.Contains("support.hp.com"));
        Assert(DeviceInfoService.Resolve("PC", "LENOVO", "ThinkPad", "S2").DriverUrl.Contains("pcsupport.lenovo.com"));
        Assert(DeviceInfoService.Resolve("PC", "ASUSTeK COMPUTER INC.", "Zenbook", "S3").Brand == "ASUS");
        Assert(DeviceInfoService.Resolve("PC", "Unknown OEM", "Model", "S4").DriverUrl == "");
        var placeholders = DeviceInfoService.Resolve("PC", "Dell", "System Product Name", "System Serial Number");
        Assert(placeholders.Model == "Không xác định" && placeholders.Serial == "Không xác định");
    });
    Check("Office suite picker defaults to Null and filters the install list", () => {
        var vm = new MainViewModel(true);
        Assert(vm.SelectedSuite == "" && vm.CanChooseSuite && vm.OfficeSuites.Select(o => o.Label).SequenceEqual(new[] { "Microsoft Office", "WPS", "OnlyOffice", "Libre Office", "Null" }));
        Assert(vm.Apps.Count == 10 && vm.Apps.All(a => a.Definition.Suite == "") && vm.SelectionText.StartsWith("10 ứng dụng"));
        foreach (var suite in new[] { "Office", "WPS", "OnlyOffice", "LibreOffice" })
        {
            vm.SelectedSuite = suite;
            Assert(vm.Apps.Count == 11 && vm.Apps.Single(a => a.Definition.Suite.Length > 0).Definition.Suite == suite);
        }
        vm.SelectedSuite = "";
        Assert(vm.Apps.Count == 10 && vm.Apps.All(a => a.Definition.Suite == ""));
    });
    Check("One-click install includes the whole catalog", () => { var vm = new MainViewModel(true); Assert(vm.IsReady && !vm.HasStarted && vm.Apps.Count == 10 && vm.InstallCommand.CanExecute(null)); Assert(typeof(AppRow).GetProperty("Selected") == null); });
    Check("Ready list offers every catalog app for a single install", () => {
        var vm = new MainViewModel(true);
        Assert(vm.ReadyApps.Count == 14 && new[] { "office", "wps", "onlyoffice", "libreoffice" }.All(id => vm.ReadyApps.Any(r => r.Definition.Id == id)));
        Assert(vm.ReadyApps.All(r => r.Status == "Sẵn sàng" && vm.InstallOneCommand.CanExecute(r)) && !vm.InstallOneCommand.CanExecute(null));
    });
    Check("Progress distinguishes download from opaque installer", () => { var row = new AppRow(Catalog.Defaults()[0]) { Status = "Đang tải", Progress = 42 }; Assert(row.ProgressText == "42%"); row.Status = "Đang cài đặt"; Assert(row.IsInstalling && row.ProgressText == "Đang chạy…"); });
    Check("Only Windows summary rows are expandable", () => {
        Assert(new AppRow(new AppDefinition { Id = "windows:summary" }).IsWindowsSummary);
        Assert(!new AppRow(new AppDefinition { Id = "chrome" }).IsWindowsSummary);
    });
    Check("Schema 2 Debloat settings are removed during migration", () => {
        var dir = Path.Combine(root, "debloat-migration"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "windows.json");
        var json = """{"SchemaVersion":2,"Items":[{"Id":"Debloat","Name":"Debloatware","Action":"Debloat","Script":"legacy"},{"Id":"Timezone","Name":"Timezone","Action":"Timezone","Script":"Set-TimeZone -Id 'SE Asia Standard Time'"}],"RemovedDefaultIds":[]}""";
        File.WriteAllText(path, json);
        var loaded = new SettingsStore(dir).LoadWindows();
        Assert(loaded.Count == 1 && loaded[0].Id == "Timezone" && File.ReadAllText(path) == json);
    });
    Check("Windows summary handles partial failures and cancellation", () => {
        var failed = new WindowsProgressTracker(3);
        Assert(failed.Update("a", "Đang áp dụng", false, false) == "Đang áp dụng · 1/3");
        Assert(failed.Update("a", "Thất bại", true, true) == "Đã xử lý · 1/3" && Math.Round(failed.Progress) == 33);
        failed.Update("b", "Hoàn tất", true, false);
        Assert(failed.Update("c", "Hoàn tất", true, false) == "Hoàn tất · 1 lỗi" && failed.Progress == 100);
        var cancelled = new WindowsProgressTracker(2); cancelled.Update("a", "Hoàn tất", true, false);
        Assert(cancelled.Update("b", "Đã hủy", true, false) == "Đã hủy" && cancelled.Progress == 100);
    });
    Check("System tasks route through runner and preserve failure results", () => {
        var commands = new System.Collections.Concurrent.ConcurrentBag<string>(); var outcomes = new System.Collections.Concurrent.ConcurrentBag<DeploymentEvent>();
        var service = new DeploymentService(run: (command, _, _) => { commands.Add(command); return Task.FromResult(0); });
        service.RunAsync([], WindowsSettingsCatalog.Defaults().Where(s => s.Action == "Winget").ToArray(), Path.Combine(root, "system"), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(commands.Count == 1 && commands.Any(c => c.Contains("setting-Winget.ps1")) && outcomes.Any(e => e.Id == "windows:Winget" && e.Finished && !e.Failed));
    });
    Check("All ready EXE installers and Windows settings start together", () =>
    {
        var calls = 0; var completed = 0;
        var allStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new System.Net.Http.HttpClient(new FakeHttp());
        var service = new DeploymentService(client, async (_, _, _) => {
            if (Interlocked.Increment(ref calls) == 7) allStarted.SetResult(null);
            await WaitWithTimeout(allStarted.Task, TimeSpan.FromSeconds(5));
            Interlocked.Increment(ref completed); return 0;
        }, _ => false);
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<DeploymentEvent>();
        service.RunAsync(Catalog.Defaults().Take(4).ToArray(), WindowsSettingsCatalog.Defaults().Take(3).ToArray(), Path.Combine(root, "parallel"), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(calls == 7 && completed == 7 && outcomes.Count(e => e.Finished && !e.Failed) == 7);
    });
    Check("Unsupported Windows setting is skipped while applications continue", () =>
    {
        using var client = new System.Net.Http.HttpClient(new FakeHttp());
        var calls = 0;
        var outcomes = new List<DeploymentEvent>();
        var service = new DeploymentService(client, (_, _, _) => { calls++; return Task.FromResult(0); }, _ => false,
            getWindowsBuild: () => 17762);
        service.RunAsync(Catalog.Defaults().Take(1).ToArray(), WindowsSettingsCatalog.Defaults().Take(1).ToArray(),
            Path.Combine(root, "legacy-skip"), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(calls == 1);
        Assert(outcomes.Any(e => e.Id == "windows:Desktop" && e.Status == "Bỏ qua · Windows không hỗ trợ" && e.Finished && !e.Failed));
        Assert(outcomes.Any(e => e.Id == "evkey" && e.Status == "Hoàn tất" && e.Finished && !e.Failed));
    });
    Check("Windows 10 1809 compatible settings execute", () =>
    {
        Assert(WindowsCompatibility.MinimumBuildFor(WindowsSettingsCatalog.Defaults().Single(s => s.Action == "Winget")) == 17763);
        Assert(WindowsCompatibility.MinimumBuildFor(new WindowsSettingDefinition { Action = "Custom" }) == 17763);
        var calls = 0;
        var outcomes = new List<DeploymentEvent>();
        var service = new DeploymentService(run: (_, _, _) => { calls++; return Task.FromResult(0); },
            getWindowsBuild: () => 17763);
        service.RunAsync([], WindowsSettingsCatalog.Defaults().Take(1).ToArray(), Path.Combine(root, "legacy-supported"),
            new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(calls == 1);
        Assert(outcomes.Any(e => e.Id == "windows:Desktop" && e.Status == "Hoàn tất" && e.Finished && !e.Failed));
    });
    Check("Downloads have no artificial four-item limit", () => {
        using var handler = new ConcurrentHttp(8); using var client = new System.Net.Http.HttpClient(handler);
        var service = new DeploymentService(client, (_, _, _) => Task.FromResult(0), _ => false);
        service.RunAsync(Catalog.Defaults().Take(8).ToArray(), [], Path.Combine(root, "downloads"), new InlineProgress<DeploymentEvent>(_ => { }), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(handler.Calls == 8 && handler.MaxActive == 8);
    });
    Check("MSI installers are serialized while EXE can overlap MSI", () => {
        using var client = new System.Net.Http.HttpClient(new FakeHttp());
        var apps = Catalog.Defaults().Take(3).ToArray();
        apps[0].Url = "https://example.com/one.msi"; apps[1].Url = "https://example.com/two.msi";
        var firstMsi = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exeStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeMsi = 0; var msiCalls = 0;
        var service = new DeploymentService(client, async (command, _, _) => {
            if (command.Contains("msiexec.exe")) {
                Assert(Interlocked.Increment(ref activeMsi) == 1);
                Interlocked.Increment(ref msiCalls); firstMsi.TrySetResult(null);
                await WaitWithTimeout(exeStarted.Task, TimeSpan.FromSeconds(5));
                await Task.Delay(30); Interlocked.Decrement(ref activeMsi);
            } else {
                await WaitWithTimeout(firstMsi.Task, TimeSpan.FromSeconds(5)); exeStarted.SetResult(null);
            }
            return 0;
        }, _ => false);
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<DeploymentEvent>();
        service.RunAsync(apps, [], Path.Combine(root, "mixed"), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(msiCalls == 2 && outcomes.Count(e => e.Finished && !e.Failed) == 3);
    });
    Check("Cancellation keeps current installer alive and stops queued work", () =>
    {
        using var cancel = new CancellationTokenSource();
        using var client = new System.Net.Http.HttpClient(new FakeHttp());
        var calls = 0;
        var service = new DeploymentService(client, async (_, _, _) => { Interlocked.Increment(ref calls); cancel.Cancel(); await Task.Delay(25); return 0; }, _ => false);
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<DeploymentEvent>();
        service.RunAsync(Catalog.Defaults().Take(4).ToArray(), [], Path.Combine(root, "cancel"), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), cancel.Token).GetAwaiter().GetResult();
        Assert(calls >= 1 && calls <= 4 && outcomes.Count(e => e.Status == "Hoàn tất") == calls && outcomes.Count(e => e.Status == "Đã hủy") == 4 - calls);
    });
    Check("1618 retries are bounded and cancellation stops retries", () => {
        using var client = new System.Net.Http.HttpClient(new FakeHttp());
        foreach (var mode in new[] { "recover", "exhaust", "cancel" }) {
            using var cancel = new CancellationTokenSource();
            var calls = 0; var waits = new List<double>(); var outcomes = new List<DeploymentEvent>();
            var service = new DeploymentService(client, (_, _, _) => Task.FromResult(++calls == 2 && mode == "recover" ? 0 : 1618), _ => false,
                (duration, token) => { waits.Add(duration.TotalSeconds); if (mode == "cancel") cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
            service.RunAsync(Catalog.Defaults().Take(1).ToArray(), [], Path.Combine(root, mode), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), cancel.Token).GetAwaiter().GetResult();
            if (mode == "recover") Assert(calls == 2 && waits.SequenceEqual(new[] { 5d }) && outcomes.Last().Finished && !outcomes.Last().Failed);
            if (mode == "exhaust") Assert(calls == 4 && waits.SequenceEqual(new[] { 5d, 10d, 15d }) && outcomes.Last().Failed);
            if (mode == "cancel") Assert(calls == 1 && outcomes.Last().Status == "Đã hủy");
        }
    });
    Check("Cancellation waits for all active parallel installers", () => {
        using var client = new System.Net.Http.HttpClient(new FakeHttp());
        using var cancel = new CancellationTokenSource();
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0; var completed = 0;
        var service = new DeploymentService(client, async (_, _, _) => {
            if (Interlocked.Increment(ref calls) == 5) { cancel.Cancel(); release.SetResult(null); }
            await WaitWithTimeout(release.Task, TimeSpan.FromSeconds(5));
            await Task.Delay(40); Interlocked.Increment(ref completed); return 0;
        }, _ => false);
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<DeploymentEvent>();
        service.RunAsync(Catalog.Defaults().Take(4).ToArray(), WindowsSettingsCatalog.Defaults().Take(1).ToArray(), Path.Combine(root, "cancel-active"), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), cancel.Token).GetAwaiter().GetResult();
        Assert(calls == 5 && completed == 5 && outcomes.Count(e => e.Status == "Hoàn tất") == 5);
    });
    Check("Installer failure and reboot codes are reported truthfully", () =>
    {
        using var client = new System.Net.Http.HttpClient(new FakeHttp());
        var outcomes = new List<DeploymentEvent>();
        var service = new DeploymentService(client, (_, _, _) => Task.FromResult(1603), _ => false);
        service.RunAsync(Catalog.Defaults().Take(1).ToArray(), [], Path.Combine(root, "fail"), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(outcomes.Any(e => e.Failed));
        outcomes.Clear();
        service = new DeploymentService(client, (_, _, _) => Task.FromResult(3010), _ => false);
        service.RunAsync(Catalog.Defaults().Take(1).ToArray(), [], Path.Combine(root, "reboot"), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(outcomes.Any(e => e.Finished && !e.Failed && e.Status.Contains("khởi động lại")));
    });
    Check("Already-installed packages are not downloaded or launched", () =>
    {
        var handler = new FakeHttp();
        using var client = new System.Net.Http.HttpClient(handler);
        var service = new DeploymentService(client, (_, _, _) => throw new Exception("Must not launch"), _ => true);
        service.RunAsync(Catalog.Defaults().Take(1).ToArray(), [], Path.Combine(root, "skip"), new InlineProgress<DeploymentEvent>(_ => { }), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(handler.Calls == 0);
    });
    Check("Each installer is deleted as soon as its app finishes", () =>
    {
        var handler = new FakeHttp();
        using var client = new System.Net.Http.HttpClient(handler);
        var work = Path.Combine(root, "delete-installers");
        var codes = new Dictionary<string, int> { ["chrome"] = 0, ["zalo"] = 1603 };
        var seen = new List<string>();
        var service = new DeploymentService(client, (command, dir, _) =>
        {
            var id = codes.Keys.Single(key => command.Contains(key + ".exe"));
            Assert(File.Exists(Path.Combine(dir, id + ".exe")));
            lock (seen) seen.Add(id);
            return Task.FromResult(codes[id]);
        }, _ => false);
        service.RunAsync(Catalog.Defaults().Where(app => codes.ContainsKey(app.Id)).ToArray(), [], work,
            new InlineProgress<DeploymentEvent>(_ => { }), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(seen.Count == 2 && Directory.Exists(work) && !Directory.EnumerateFiles(work).Any());
    });
    Check("An installer still held open is left for the end-of-run cleanup", () =>
    {
        var handler = new FakeHttp();
        using var client = new System.Net.Http.HttpClient(handler);
        var work = Path.Combine(root, "held-installer");
        FileStream? held = null;
        var lines = new List<string>();
        var outcomes = new List<DeploymentEvent>();
        var service = new DeploymentService(client, (_, dir, _) =>
        {
            held = File.Open(Path.Combine(dir, "chrome.exe"), FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult(0);
        }, _ => false);
        try
        {
            service.RunAsync(Catalog.Defaults().Where(app => app.Id == "chrome").ToArray(), [], work,
                new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(lines.Add), default).GetAwaiter().GetResult();
            Assert(File.Exists(Path.Combine(work, "chrome.exe")) && lines.Any(line => line.Contains("chưa xóa được bộ cài")));
            Assert(outcomes.Last(e => e.Id == "chrome").Status == "Hoàn tất");
        }
        finally { held?.Dispose(); }
    });
    Check("Stale work folders are removed at startup unless in use", () =>
    {
        var tempRoot = Path.Combine(root, "stale-work");
        string MakeFolder(string name)
        {
            var folder = Path.Combine(tempRoot, name);
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllText(Path.Combine(folder, "nested", "setup.exe"), "fixture");
            return folder;
        }
        var stale = MakeFolder("work-" + Guid.NewGuid().ToString("N"));
        var busy = MakeFolder("work-" + Guid.NewGuid().ToString("N"));
        var foreign = MakeFolder("work-not-ours");
        var session = MakeFolder("session");
        // Old Debloat runs left their registry backup in a work folder on purpose: keep it.
        var debloat = MakeFolder("work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(debloat, "debloat-" + Guid.NewGuid().ToString("N")));
        var backup = MakeFolder("work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(backup, "engine", "Backups"));
        // Another window holding the deployment lock is installing: nothing is touched.
        using (var locked = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var owner = new Thread(() =>
            {
                using var mutex = new Mutex(false, DeploymentService.DeploymentLockName);
                mutex.WaitOne(); locked.Set(); release.Wait(); mutex.ReleaseMutex();
            });
            owner.Start(); locked.Wait();
            Assert(WorkFolderCleaner.RemoveStale(tempRoot) == 0 && Directory.Exists(stale));
            release.Set(); owner.Join();
        }
        using (File.Open(Path.Combine(busy, "nested", "setup.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert(WorkFolderCleaner.RemoveStale(tempRoot) == 1);
        Assert(!Directory.Exists(stale) && Directory.Exists(busy) && Directory.Exists(foreign) && Directory.Exists(session) && Directory.Exists(debloat) && Directory.Exists(backup));
        Assert(WorkFolderCleaner.RemoveStale(tempRoot) == 1 && !Directory.Exists(busy) && Directory.Exists(debloat) && Directory.Exists(backup));
        Assert(WorkFolderCleaner.RemoveStale(Path.Combine(root, "missing")) == 0);
    });
    Check("Unknown Smart Skip evidence never installs over an unverified app", () =>
    {
        var handler = new FakeHttp();
        using var client = new System.Net.Http.HttpClient(handler);
        var outcomes = new List<DeploymentEvent>(); var launches = 0; var reads = 0;
        var service = new DeploymentService(client, (_, _, _) => { launches++; return Task.FromResult(0); },
            loadInstalledSoftware: () => { reads++; var snapshot = new InstalledSoftwareSnapshot(); snapshot.ReadErrors.Add("fixture access denied"); return snapshot; });
        service.RunAsync(Catalog.Defaults().Take(1).ToArray(), [], Path.Combine(root, "unknown-skip"),
            new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(reads == 1 && handler.Calls == 0 && launches == 0);
        Assert(outcomes.Any(item => item.Status == "Không xác minh được · không cài" && item.Finished && item.Failed));
    });
    Check("Smart Skip checks again before launching a downloaded installer", () =>
    {
        var handler = new FakeHttp();
        using var client = new System.Net.Http.HttpClient(handler);
        var outcomes = new List<DeploymentEvent>(); var launches = 0; var reads = 0;
        var service = new DeploymentService(client, (_, _, _) => { launches++; return Task.FromResult(0); },
            loadInstalledSoftware: () => {
                var snapshot = new InstalledSoftwareSnapshot();
                if (Interlocked.Increment(ref reads) == 2)
                    snapshot.Entries.Add(new("Google Chrome", "152.0", "Google LLC", "", "fixture"));
                return snapshot;
            });
        service.RunAsync(Catalog.Defaults().Where(app => app.Id == "chrome").ToArray(), [], Path.Combine(root, "race-skip"),
            new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(reads == 2 && handler.Calls == 1 && launches == 0);
        Assert(outcomes.Any(item => item.Status == "Đã cài trong lúc chờ · bỏ qua" && item.Finished && !item.Failed));
    });
    Check("A reused built-in ID cannot silently use the old product detector", () =>
    {
        var changed = new AppDefinition { Id = "chrome", Name = "Chromium", Url = "https://example.com/chromium.exe" };
        var snapshot = new InstalledSoftwareSnapshot();
        snapshot.Entries.Add(new("Google Chrome", "152.0", "Google LLC", "", "fixture"));
        Assert(InstalledSoftwareDetector.Detect(changed, snapshot).State == SoftwareDetectionState.Unknown);
    });
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"{passed} tests passed. No installers or system configuration were executed.");

// Render our own WPF visual tree as a UI integration test (no desktop capture/input).
Exception? renderError = null;
var renderThread = new Thread(() =>
{
    try
    {
        // A plain Application hosts the theme: MiniApps.App would queue its real OnStartup on the first dispatcher pump.
        var application = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("pack://application:,,,/MiniApps;component/Theme.xaml") });
        var vm = new MainViewModel(true);
        var window = new MiniApps.MainWindow(vm) { ShowInTaskbar = false, Left = -20000, Top = -20000 };
        window.Show();
        var targetDir = Path.GetFullPath(Path.Combine("WPF", "artifacts", "qa"));
        Directory.CreateDirectory(targetDir);
        var suitePicker = (System.Windows.Controls.ComboBox)window.FindName("SuitePicker");
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        if (suitePicker.Items.Count != 5 || (string)suitePicker.SelectedValue != "" || !suitePicker.IsEnabled || vm.Apps.Count != 10)
            throw new Exception("The office suite picker must list five choices and default to Null.");
        suitePicker.SelectedValue = "WPS";
        if (vm.SelectedSuite != "WPS" || !vm.Apps.Any(a => a.Definition.Id == "wps") || vm.Apps.Count != 11)
            throw new Exception("Picking a suite in the drop list must update the install list.");
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        bool ShowsText(System.Windows.DependencyObject node, string text)
        {
            if (node is System.Windows.Controls.TextBlock block && block.Text == text) return true;
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                if (ShowsText(System.Windows.Media.VisualTreeHelper.GetChild(node, i), text)) return true;
            return false;
        }
        if (!ShowsText(suitePicker, "WPS")) throw new Exception("The closed drop list must show the selected label.");
        suitePicker.IsDropDownOpen = true;
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        var suitePopup = (System.Windows.Controls.Primitives.Popup)suitePicker.Template.FindName("PART_Popup", suitePicker);
        var popupChild = (System.Windows.FrameworkElement)suitePopup.Child;
        if (!suitePopup.IsOpen || popupChild.ActualWidth < suitePicker.ActualWidth || Enumerable.Range(0, 5).Any(i => suitePicker.ItemContainerGenerator.ContainerFromIndex(i) == null))
            throw new Exception("The suite drop list did not open with its five choices.");
        var popupBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)popupChild.ActualWidth + 16, (int)popupChild.ActualHeight + 16, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        popupBitmap.Render(popupChild);
        var popupEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        popupEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(popupBitmap));
        using (var output = File.Create(Path.Combine(targetDir, "suite-dropdown-open.png"))) popupEncoder.Save(output);
        suitePicker.IsDropDownOpen = false;
        Console.WriteLine("PASS WPF office suite drop list");
        const int renderPages = 3;
        for (var page = 0; page < renderPages; page++)
        {
            vm.Page = page;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            var content = (System.Windows.Media.Visual)window.Content;
            var size = ((System.Windows.FrameworkElement)window.Content).RenderSize;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(targetDir, $"page-{page}.png"));
            encoder.Save(output);
        }
        void Capture(string name)
        {
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            var view = (System.Windows.FrameworkElement)window.Content;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(targetDir, name)); encoder.Save(output);
        }
        vm.Page = 0;
        var chromeRow = vm.ReadyApps.Single(r => r.Definition.Id == "chrome");
        window.Dispatcher.Invoke(() => vm.InstallOneCommand.Execute(chromeRow));
        if (!vm.IsBusy || vm.InstallCommand.CanExecute(null) || vm.InstallOneCommand.CanExecute(vm.ReadyApps[0]))
            throw new Exception("A single install must lock the other install actions while it runs.");
        var singleFrame = new System.Windows.Threading.DispatcherFrame();
        var singleStarted = DateTime.UtcNow;
        var singleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        singleTimer.Tick += (_, _) => { if (!vm.IsBusy || (DateTime.UtcNow - singleStarted).TotalSeconds > 10) singleFrame.Continue = false; };
        singleTimer.Start(); System.Windows.Threading.Dispatcher.PushFrame(singleFrame); singleTimer.Stop();
        if (vm.IsBusy || chromeRow.Status != "Hoàn tất · mô phỏng" || vm.HasStarted || vm.InstallFinished || !vm.InstallCommand.CanExecute(null) ||
            vm.ReadyApps.Where(r => r != chromeRow).Any(r => r.Status != "Sẵn sàng") || vm.Apps.Any(a => a.Status != "Sẵn sàng"))
            throw new Exception("Single install did not run on its own: " + chromeRow.Status);
        Capture("ready-single-install.png");
        Console.WriteLine("PASS WPF single app install from the ready list");
        var installScroll = (System.Windows.Controls.ScrollViewer)window.FindName("InstallScroll");
        installScroll.ScrollToEnd();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        var settingsView = (System.Windows.FrameworkElement)window.Content;
        var settingsBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)settingsView.ActualWidth, (int)settingsView.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        settingsBitmap.Render(settingsView);
        var settingsEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        settingsEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(settingsBitmap));
        using (var output = File.Create(Path.Combine(targetDir, "windows-settings.png"))) settingsEncoder.Save(output);
        installScroll.ScrollToTop();
        var progressList = (System.Windows.Controls.ItemsControl)window.FindName("ProgressList");
        if (progressList.IsVisible) throw new Exception("Application list must be hidden before starting.");
        suitePicker.SelectedValue = "Office";
        window.Dispatcher.Invoke(() => vm.InstallCommand.Execute(null));
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        if (!vm.HasStarted || !progressList.IsVisible) throw new Exception("Progress list must appear after starting.");
        if (suitePicker.IsEnabled) throw new Exception("The suite drop list must lock once installation starts.");
        var frame = new System.Windows.Threading.DispatcherFrame();
        var started = DateTime.UtcNow;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => { if (!vm.IsBusy || (DateTime.UtcNow - started).TotalSeconds > 10) frame.Continue = false; };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
        if (vm.IsBusy || !vm.Summary.StartsWith("Hoàn tất") || vm.Overall != 100 || vm.Apps.Any(a => a.Progress != 100)) throw new Exception("Preview command did not complete: " + vm.Summary);
        if (vm.WindowsTaskDetails.Count != 11 || vm.WindowsTaskDetails.Any(t => t.Progress != 100) || vm.IsWindowsDetailsVisible) throw new Exception("Windows detail rows were not prepared correctly.");
        vm.ToggleWindowsDetailsCommand.Execute(null);
        if (!vm.IsWindowsDetailsVisible) throw new Exception("Windows detail list did not expand.");
        installScroll.ScrollToEnd(); Capture("windows-progress-expanded.png");
        vm.ToggleWindowsDetailsCommand.Execute(null); installScroll.ScrollToTop();
        window.UpdateLayout();
        var progressView = (System.Windows.FrameworkElement)window.Content;
        var progressBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)progressView.ActualWidth, (int)progressView.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        progressBitmap.Render(progressView);
        var progressEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        progressEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(progressBitmap));
        using (var output = File.Create(Path.Combine(targetDir, "install-progress.png"))) progressEncoder.Save(output);
        Console.WriteLine("PASS WPF preview command, async progress and completion");
        if (vm.Apps.Where(a => a.Definition.Suite.Length > 0).Select(a => a.Definition.Suite).SingleOrDefault() != "Office") throw new Exception("The drop list choice did not select the correct suite.");
        if (!vm.InstallFinished || vm.InstallCommand.CanExecute(null) || vm.InstallButtonText != "Đã hoàn tất") throw new Exception("Completed installation must stay disabled.");
        var oldSummary = vm.Summary;
        vm.SelectedSuite = "WPS";
        vm.InstallCommand.Execute(null);
        if (vm.IsBusy || vm.Summary != oldSummary || vm.SelectedSuite != "Office") throw new Exception("Disabled installation started a second run.");
        if (vm.SystemTasks.Count != 1 || vm.SystemTasks[0].Name != "Windows Setting" || vm.SystemTasks[0].Progress != 100 || vm.SystemTasks[0].Status != "Hoàn tất") throw new Exception("Windows settings must be represented by one completed summary row.");
        Console.WriteLine("PASS completed install becomes gray and cannot run again");
        window.Close();
        var publicVm = new MainViewModel(true);
        var publicWindow = new MiniApps.MainWindow(publicVm) { ShowInTaskbar = false, Left = -20000, Top = -20000 };
        publicWindow.Show(); publicWindow.UpdateLayout();
        var navigation = (System.Windows.Controls.ListBox)publicWindow.FindName("NavigationList");
        const string secondPage = "INFORMATION";
        const int expectedPages = 3;
        if (navigation.Items.Count != expectedPages ||
            !string.Equals(((System.Windows.Controls.ListBoxItem)navigation.Items[0]).Content?.ToString(), "INSTALL SOFTWARE", StringComparison.Ordinal) ||
            !string.Equals(((System.Windows.Controls.ListBoxItem)navigation.Items[1]).Content?.ToString(), secondPage, StringComparison.Ordinal) ||
            !string.Equals(((System.Windows.Controls.ListBoxItem)navigation.Items[2]).Content?.ToString(), "EXTEND", StringComparison.Ordinal))
            throw new Exception("Navigation must expose Install Software, the second page and EXTEND.");
        publicVm.Page = 2;
        publicWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        publicWindow.UpdateLayout();
        var extensionList = (System.Windows.Controls.ItemsControl)publicWindow.FindName("ExtensionList");
        if (!publicVm.IsExtend || !extensionList.IsVisible || extensionList.Items.Count != 3 ||
            Enumerable.Range(0, extensionList.Items.Count).Any(i =>
                FindChildren<System.Windows.Controls.Button>((System.Windows.DependencyObject)extensionList.ItemContainerGenerator.ContainerFromIndex(i))
                    .SingleOrDefault(button => button.Name == "RunButton")?.IsEnabled != true))
            throw new Exception("EXTEND must list its add-ons with live run buttons.");
        var extendView = (System.Windows.FrameworkElement)publicWindow.Content;
        var extendBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)extendView.ActualWidth, (int)extendView.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        extendBitmap.Render(extendView);
        var extendEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        extendEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(extendBitmap));
        using (var output = File.Create(Path.Combine(targetDir, "extend.png"))) extendEncoder.Save(output);
        Console.WriteLine("PASS WPF EXTEND page lists add-ons with live run buttons");
        static IEnumerable<T> FindChildren<T>(System.Windows.DependencyObject? node) where T : System.Windows.DependencyObject
        {
            if (node == null) yield break;
            if (node is T match) yield return match;
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var found in FindChildren<T>(System.Windows.Media.VisualTreeHelper.GetChild(node, i))) yield return found;
        }
        var answers = new Queue<bool>(new[] { false, true });
        var extendVm = new MainViewModel(true, confirm: _ => answers.Dequeue());
        var debloatItem = extendVm.Extensions[0];
        publicWindow.Dispatcher.Invoke(() => extendVm.RunExtensionCommand.Execute(debloatItem));
        if (extendVm.IsBusy || debloatItem.Status != "Sẵn sàng" || debloatItem.IsRunning)
            throw new Exception("Declining the confirmation must start nothing.");
        publicWindow.Dispatcher.Invoke(() => extendVm.RunExtensionCommand.Execute(debloatItem));
        if (!extendVm.IsBusy || !debloatItem.IsRunning || extendVm.InstallCommand.CanExecute(null) ||
            extendVm.RunExtensionCommand.CanExecute(extendVm.Extensions[1]) || extendVm.InstallOneCommand.CanExecute(extendVm.ReadyApps[0]) ||
            !extendVm.RunExtensionCommand.CanExecute(extendVm.Extensions[2]))
            throw new Exception("A running add-on must lock every other install action but leave Share LAN available.");
        var extendFrame = new System.Windows.Threading.DispatcherFrame();
        var extendStarted = DateTime.UtcNow;
        var extendTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        extendTimer.Tick += (_, _) => { if (!extendVm.IsBusy || (DateTime.UtcNow - extendStarted).TotalSeconds > 10) extendFrame.Continue = false; };
        extendTimer.Start(); System.Windows.Threading.Dispatcher.PushFrame(extendFrame); extendTimer.Stop();
        if (extendVm.IsBusy || debloatItem.IsRunning || debloatItem.Status != "Hoàn tất" || debloatItem.Detail != "Đang áp dụng · mô phỏng" ||
            !extendVm.InstallCommand.CanExecute(null) || !extendVm.RunExtensionCommand.CanExecute(extendVm.Extensions[1]) || answers.Count != 0)
            throw new Exception($"The add-on run did not finish cleanly: {debloatItem.Status} / {debloatItem.Detail}.");
        Console.WriteLine("PASS WPF EXTEND run asks first, locks installs and reports its result");
        publicVm.Page = 1;
        publicVm.Page = 3;
        publicWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        publicWindow.UpdateLayout();
        if (publicVm.Page != 1 || ((System.Windows.Controls.ListBoxItem)navigation.Items[1]).Content?.ToString() != "INFORMATION")
            throw new Exception("Information must replace Driver.");
        var information = ((System.Windows.Controls.Grid)publicWindow.FindName("PageHost")).Children.OfType<MiniApps.InformationView>().Single();
        if (!information.IsVisible || ((System.Windows.Controls.ItemsControl)information.FindName("InformationList")).Items.Count != 3)
            throw new Exception($"Information did not display its preview data: visible={information.IsVisible}, rows={((System.Windows.Controls.ItemsControl)information.FindName("InformationList")).Items.Count}, status={((System.Windows.Controls.TextBlock)information.FindName("StatusText")).Text}.");
        Console.WriteLine("PASS Information navigation and embedded data view");
        var heldRead = new TaskCompletionSource<IReadOnlyList<InformationRow>>();
        var slowInformation = new MiniApps.InformationView(_ => heldRead.Task);
        var slowWindow = new System.Windows.Window { Content = slowInformation, Width = 1000, Height = 700, ShowInTaskbar = false, Left = -20000, Top = -20000 };
        slowWindow.Show();
        var loadingPanel = (System.Windows.FrameworkElement)slowInformation.FindName("LoadingPanel");
        var seen = new List<int>();
        void PumpFor(TimeSpan span, Func<bool>? until = null)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var end = DateTime.UtcNow + span;
            var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            tick.Tick += (_, _) => { seen.Add(slowInformation.LoadingPercentShown); if (DateTime.UtcNow >= end || (until?.Invoke() ?? false)) frame.Continue = false; };
            tick.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); tick.Stop();
        }
        PumpFor(TimeSpan.FromSeconds(1.5));
        var loadedContent = (System.Windows.FrameworkElement)slowInformation.FindName("InformationContent");
        if (!loadingPanel.IsVisible || loadedContent.IsVisible || slowInformation.LoadingPercentShown < 20 || slowInformation.LoadingPercentShown > 99 ||
            seen.Zip(seen.Skip(1), (a, b) => b >= a).Contains(false) || loadingPanel.ActualWidth <= 0)
            throw new Exception($"The Information loader must sit alone, counting up: shown={slowInformation.LoadingPercentShown}, visible={loadingPanel.IsVisible}.");
        var loaderView = (System.Windows.FrameworkElement)slowWindow.Content;
        var loaderBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)loaderView.ActualWidth, (int)loaderView.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        loaderBitmap.Render(loaderView);
        var loaderEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        loaderEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(loaderBitmap));
        using (var output = File.Create(Path.Combine(targetDir, "information-loading.png"))) loaderEncoder.Save(output);
        PumpFor(TimeSpan.FromSeconds(8));
        if (slowInformation.LoadingPercentShown != 99 || seen.Max() > 99)
            throw new Exception($"A slow read must hold at 99%, got {slowInformation.LoadingPercentShown} (max {seen.Max()}).");
        heldRead.SetResult(InformationService.Parse("{\"OS\":\"Windows · mô phỏng\",\"CPU\":\"CPU · mô phỏng\",\"Serial\":\"DEMO-1\"}"));
        PumpFor(TimeSpan.FromSeconds(3), () => !loadingPanel.IsVisible);
        if (loadingPanel.IsVisible || !loadedContent.IsVisible)
            throw new Exception("The data must replace the loader once the read completes.");
        slowWindow.Close(); slowInformation.Dispose();
        Console.WriteLine("PASS Information loader is centred, counts 1-99% and gives way to the data");
        var layoutRows = InformationService.Parse("""
            {"OS":"Windows 11 Pro","IsActivated":true,"Hostname":"MINI-PC","Model":"ThinkPad T14 Gen 5","Serial":"DEMO-123456","CPU":"Intel Core Ultra 7 155H · 16 cores / 22 threads","RAM":"32 GB DDR5 · 5600 MT/s\n├ Slot 1: 16 GB Samsung · SODIMM\n└ Slot 2: 16 GB Samsung · SODIMM","GraphicsCard":"Intel Arc Graphics · 128 MB\nNVIDIA GeForce RTX 4060 Laptop GPU · 8 GB · 115 W","Storage":"Samsung SSD 990 PRO 1 TB · NVMe\n├ C: Windows · 420 GB / 650 GB available\n└ D: Data · 180 GB / 300 GB available\nKingston SA400S37 480 GB · SATA SSD\n└ E: Backup · 210 GB / 447 GB available","Resolution":"2560 × 1600","RefreshRate":"165 Hz","DateTime":"2026-09-18 18:00:00","RamTotal":"32 GB","RamItems":[{"Slot":"DIMM 0","Capacity":"16 GB","Type":"DDR5","Speed":"5600 MT/s","Manufacturer":"Samsung","FormFactor":"SODIMM"},{"Slot":"DIMM 1","Capacity":"16 GB","Type":"DDR5","Speed":"5600 MT/s","Manufacturer":"Samsung","FormFactor":"SODIMM"}],"GpuItems":[{"Name":"Intel Arc Graphics","Kind":"Tích hợp","Memory":"128 MB"},{"Name":"NVIDIA GeForce RTX 4060 Laptop GPU","Kind":"Rời","Memory":"8 GB","Power":"115 W"}],"StorageItems":[{"Model":"Samsung SSD 990 PRO","Capacity":"1 TB","Connection":"NVMe","Partitions":[{"Letter":"C","FreeBytes":450971566080,"TotalBytes":697932185600},{"Letter":"D","FreeBytes":193273528320,"TotalBytes":322122547200}]},{"Model":"Kingston SA400S37","Capacity":"480 GB","Connection":"SATA","Partitions":[{"Letter":"E","FreeBytes":225485783040,"TotalBytes":479962595328}]}]}
            """);
        ((System.Windows.FrameworkElement)information.FindName("InformationContent")).DataContext = layoutRows.ToDictionary(row => row.Name, row => row.Value);
        ((System.Windows.Controls.ItemsControl)information.FindName("InformationList")).ItemsSource = layoutRows.Where(row => row.Name is "CPU" or "RAM" or "Graphics Card");
        ((System.Windows.Controls.ItemsControl)information.FindName("StorageList")).ItemsSource = layoutRows.Single(row => row.Name == "Storage").Disks;
        if (layoutRows.Single(row => row.Name == "RAM").Ram.Count != 2 || layoutRows.Single(row => row.Name == "Graphics Card").Gpu.Count != 2)
            throw new Exception("Structured hardware items were lost.");
        var fixtureDisks = layoutRows.Single(row => row.Name == "Storage").Disks;
        if (fixtureDisks.Count != 2 || fixtureDisks[0].Partitions.Count != 2 || Math.Abs(fixtureDisks[0].Partitions[0].UsedPercent - 230d / 650 * 100) > 0.01)
            throw new Exception("Disk partitions or capacity calculations are incorrect.");
        if (new StorageVolume("X", 20, 10).HasCapacity || new StorageVolume("X", null, 10).HasCapacity || new StorageVolume("X", 0, 0).HasCapacity)
            throw new Exception("Invalid capacity must not display a usage bar.");
        if (new StorageVolume("X", 0, 10).UsedPercent != 100 || new StorageVolume("X", 10, 10).UsedPercent != 0)
            throw new Exception("Empty/full volume usage is incorrect.");
        Console.WriteLine("PASS structured RAM/GPU/disks and capacity boundaries");
        foreach (var (width, height) in new[] { (1120, 810), (940, 660) })
        {
            publicWindow.Width = width; publicWindow.Height = height;
            publicWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            publicWindow.UpdateLayout();
            var layoutContent = (System.Windows.FrameworkElement)publicWindow.Content;
            var layoutBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)layoutContent.ActualWidth, (int)layoutContent.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            layoutBitmap.Render(layoutContent);
            var layoutEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            layoutEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(layoutBitmap));
            using var layoutOutput = File.Create(Path.Combine(targetDir, $"information-{width}.png"));
            layoutEncoder.Save(layoutOutput);
            if (width == 1120)
            {
                ((System.Windows.Controls.ScrollViewer)((System.Windows.FrameworkElement)information.FindName("InformationContent")).Parent).ScrollToEnd();
                publicWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                publicWindow.UpdateLayout();
                var bottom = new System.Windows.Media.Imaging.RenderTargetBitmap((int)layoutContent.ActualWidth, (int)layoutContent.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bottom.Render(layoutContent);
                var bottomEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                bottomEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bottom));
                using var bottomOutput = File.Create(Path.Combine(targetDir, "information-storage.png"));
                bottomEncoder.Save(bottomOutput);
                ((System.Windows.Controls.ScrollViewer)((System.Windows.FrameworkElement)information.FindName("InformationContent")).Parent).ScrollToTop();
            }
        }
        static IEnumerable<T> DescendantsOf<T>(System.Windows.DependencyObject node) where T : System.Windows.DependencyObject
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                if (child is T match) yield return match;
                foreach (var deeper in DescendantsOf<T>(child)) yield return deeper;
            }
        }
        // RAM rows: values hug their content, stay aligned, and leave the rest of the line empty.
        var ramRows = DescendantsOf<System.Windows.Controls.ItemsControl>(information).Single(control => control.Name == "RamRows");
        var ramGrids = DescendantsOf<System.Windows.Controls.Grid>(ramRows).Where(grid => grid.ColumnDefinitions.Count == 6).ToList();
        if (ramGrids.Count != 2 || ramGrids.Any(grid => grid.ColumnDefinitions[5].ActualWidth < 100) ||
            Enumerable.Range(0, 5).Any(column => Math.Abs(ramGrids[0].ColumnDefinitions[column].ActualWidth - ramGrids[1].ColumnDefinitions[column].ActualWidth) > 0.5))
            throw new Exception("RAM values must sit close together in aligned columns.");
        Console.WriteLine("PASS RAM rows are compact and aligned");
        // The window fits the Information data, and gives the previous height back on other pages.
        var informationScroll = (System.Windows.Controls.ScrollViewer)information.FindName("InformationScroll");
        publicWindow.FitInformationHeight();
        publicWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        publicWindow.UpdateLayout();
        var workHeight = System.Windows.SystemParameters.WorkArea.Height;
        if (Math.Abs(informationScroll.ExtentHeight - informationScroll.ViewportHeight) > 2 && Math.Abs(publicWindow.ActualHeight - workHeight) > 1)
            throw new Exception($"Information must fit its window: extent={informationScroll.ExtentHeight}, viewport={informationScroll.ViewportHeight}, window={publicWindow.ActualHeight}.");
        var fitView = (System.Windows.FrameworkElement)publicWindow.Content;
        var fitBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)fitView.ActualWidth, (int)fitView.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        fitBitmap.Render(fitView);
        var fitEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        fitEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(fitBitmap));
        using (var output = File.Create(Path.Combine(targetDir, "information-fit.png"))) fitEncoder.Save(output);
        publicVm.Page = 0;
        publicWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        if (publicWindow.Height != 810 || publicWindow.MinHeight != 660)
            throw new Exception($"Leaving Information must give the window its height back: {publicWindow.Height}/{publicWindow.MinHeight}.");
        publicVm.Page = 1;
        Console.WriteLine("PASS Information window fits its data and restores the height elsewhere");
        publicWindow.Close();
        Console.WriteLine("PASS WPF navigation exposes the supported pages");
        application.Shutdown();
    }
    catch (Exception ex) { renderError = ex; }
});
renderThread.SetApartmentState(ApartmentState.STA);
renderThread.Start(); renderThread.Join();
if (renderError != null) throw new Exception("WPF rendering failed", renderError);
Console.WriteLine("PASS WPF startup, navigation and render of supported pages");

sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
sealed class FakeHttp : System.Net.Http.HttpMessageHandler
{
    public int Calls;
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { RequestMessage = request, Content = new System.Net.Http.ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith(".msi")
            ? [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1] : [0x4D, 0x5A, 0, 0, 0, 0, 0, 0]) });
    }
}
sealed class ConcurrentHttp(int expected) : System.Net.Http.HttpMessageHandler
{
    private readonly TaskCompletionSource<object?> allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int calls;
    private int active;
    private int maxActive;
    public int Calls => calls;
    public int MaxActive => maxActive;
    protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref active);
        while (true)
        {
            var observed = maxActive;
            if (current <= observed || Interlocked.CompareExchange(ref maxActive, current, observed) == observed) break;
        }
        if (Interlocked.Increment(ref calls) == expected) allStarted.SetResult(null);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = Task.Delay(TimeSpan.FromSeconds(5), deadline.Token);
        if (await Task.WhenAny(allStarted.Task, timeout) != allStarted.Task) throw new TimeoutException("Concurrent HTTP fixture timed out.");
        deadline.Cancel();
        await allStarted.Task;
        Interlocked.Decrement(ref active);
        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { RequestMessage = request, Content = new System.Net.Http.ByteArrayContent([0x4D, 0x5A, 0, 0, 0, 0, 0, 0]) };
    }
}
sealed class StalledHttp : System.Net.Http.HttpMessageHandler
{
    private readonly StalledReadStream stream = new();
    public Task Started => stream.Started;
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new System.Net.Http.StreamContent(stream)
        });
}
sealed class StalledReadStream : Stream
{
    private readonly TaskCompletionSource<object?> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<object?> disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Started => started.Task;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        started.TrySetResult(null);
        await disposed.Task;
        throw new ObjectDisposedException(nameof(StalledReadStream));
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) disposed.TrySetResult(null);
        base.Dispose(disposing);
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
