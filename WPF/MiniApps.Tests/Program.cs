using System.IO;
using System.Security.Cryptography;
using MiniApps.Models;
using MiniApps.Services;
using MiniApps.ViewModels;

var passed = 0;
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
Check("Default catalog contains Office/WPS alternatives", () => { var apps = Catalog.Defaults(); Catalog.Validate(apps); Assert(apps.Count == 12 && apps.Count(a => a.Suite.Length > 0) == 2); });
Check("Reject HTTP", () => { var apps = Catalog.Defaults(); apps[0].Url = "http://example.com/test.exe"; Reject(() => Catalog.Validate(apps)); });
Check("Reject duplicate IDs", () => { var apps = Catalog.Defaults(); apps[1].Id = apps[0].Id; Reject(() => Catalog.Validate(apps)); });
Check("Reject path traversal ID", () => { var apps = Catalog.Defaults(); apps[0].Id = "../escape"; Reject(() => Catalog.Validate(apps)); });
Check("Reject non-installer URL", () => { var apps = Catalog.Defaults(); apps[0].Url = "https://example.com/a.ps1"; Reject(() => Catalog.Validate(apps)); });
Check("Installed applications are recognized automatically", () => {
    Assert(DeploymentService.InstalledNameMatches(Catalog.Defaults().Single(a => a.Id == "chrome"), "Google Chrome"));
    Assert(DeploymentService.InstalledNameMatches(Catalog.Defaults().Single(a => a.Id == "office"), "Microsoft 365 Apps for enterprise"));
    Assert(DeploymentService.InstalledNameMatches(new AppDefinition { Id = "custom", Name = "7-Zip" }, "7-Zip 24.09 (x64)"));
    Assert(!DeploymentService.InstalledNameMatches(new AppDefinition { Id = "custom", Name = "Zoom" }, "Zoom Outlook Plugin"));
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
try
{
#if NET48
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
#endif
    Check("All built-ins expose nonempty executable scripts", () => {
        var settings = WindowsSettingsCatalog.Defaults(); WindowsSettingsCatalog.Validate(settings);
        Assert(settings.All(s => !string.IsNullOrWhiteSpace(s.Script)));
        Assert(settings.Single(s => s.Id == "Winget").Script.Contains("https://"));
        Assert(settings.Single(s => s.Id == "Debloat").Script.Contains("RunDefaults"));
        settings[0].Script = ""; Reject(() => WindowsSettingsCatalog.Validate(settings));
    });
    Check("Legacy built-ins migrate without overwriting files or edited commands", () => {
        var dir = Path.Combine(root, "legacy"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "windows.json");
        var legacy = """[{"Id":"Timezone","Name":"Timezone","Action":"Timezone"}]""";
        File.WriteAllText(path, legacy); var store = new SettingsStore(dir);
        var loaded = store.LoadWindows(); Assert(loaded.Count == 2 && loaded[0].Script.Contains("Set-TimeZone") && loaded.Any(x => x.Id == "InfoExe") && File.ReadAllText(path) == legacy);
        loaded[0].Script = "Write-Output 'edited builtin'"; store.SaveWindows(loaded);
        Assert(store.LoadWindows()[0].Script == loaded[0].Script);
        loaded.RemoveAll(x => x.Id == "InfoExe"); store.SaveWindows(loaded);
        Assert(store.LoadWindows().All(x => x.Id != "InfoExe") && File.ReadAllText(path).Contains("RemovedDefaultIds"));
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
    Check("Public edition hides and locks Setting", () => {
        var vm = new MainViewModel(true, developerEdition: false);
        Assert(!vm.IsDeveloperEdition && !vm.IsSetting && vm.Page == 0);
        vm.Page = 3;
        Assert(vm.Page == 0 && !vm.IsSetting);
        Assert(vm.EditableApps.Count == 0 && vm.EditableWindows.Count == 0);
        Assert(!vm.AddCommand.CanExecute(null) && !vm.AddWindowsCommand.CanExecute(null) && !vm.SaveCommand.CanExecute(null));
        vm.AddCommand.Execute(null); vm.AddWindowsCommand.Execute(null); vm.SaveCommand.Execute(null);
        Assert(vm.EditableApps.Count == 0 && vm.EditableWindows.Count == 0);
    });
    Check("Required packaged settings fail closed", () => {
        var missing = new SettingsStore(Path.Combine(root, "missing-public"));
        Reject(() => missing.Load(required: true));
        Reject(() => missing.LoadWindows(required: true));
    });
    Check("Atomic settings round trip", () => { var store = new SettingsStore(root); var apps = Catalog.Defaults(); apps.Add(new() { Name = "Test", Url = "https://example.com/test.exe" }); store.Save(apps); Assert(store.Load().Count == 13); });
    Check("Invalid settings cannot overwrite saved config", () => { var store = new SettingsStore(root); var apps = Catalog.Defaults(); apps[0].Url = "bad"; Reject(() => store.Save(apps)); Assert(store.Load().Count == 13); });
    Check("Windows settings persist add/edit/delete independently of apps", () => {
        var store = new SettingsStore(root); var settings = WindowsSettingsCatalog.Defaults();
        settings.RemoveAt(0); settings[0].Name = "Múi giờ tùy chỉnh";
        settings.Add(new() { Name = "Test script", Description = "Harmless test", Script = "Write-Output 'test'" });
        store.SaveWindows(settings); var loaded = store.LoadWindows();
        Assert(loaded.Count == 9 && loaded[0].Name == "Múi giờ tùy chỉnh" && loaded[loaded.Count - 1].Script == "Write-Output 'test'" && store.Load().Count == 13);
        loaded[loaded.Count - 1].Script = ""; Reject(() => store.SaveWindows(loaded)); Assert(store.LoadWindows()[store.LoadWindows().Count - 1].Script.Length > 0);
        store.SaveWindows([]); Assert(store.LoadWindows().Count == 0);
    });
    Check("Reject invalid Windows action, duplicate and traversal IDs", () => {
        var settings = WindowsSettingsCatalog.Defaults(); settings[0].Action = "Injected"; Reject(() => WindowsSettingsCatalog.Validate(settings));
        settings = WindowsSettingsCatalog.Defaults(); settings[0].Id = "../test"; Reject(() => WindowsSettingsCatalog.Validate(settings));
        settings = WindowsSettingsCatalog.Defaults(); settings[1].Id = settings[0].Id; Reject(() => WindowsSettingsCatalog.Validate(settings));
    });
    Check("App delete confirmation and draft save boundary", () => {
        var allow = false; var vm = new MainViewModel(true, confirmDelete: _ => allow);
        var deletedId = vm.SelectedApp!.Id; vm.DeleteCommand.Execute(null); Assert(vm.EditableApps.Count == 12);
        allow = true; vm.DeleteCommand.Execute(null); Assert(vm.EditableApps.Count == 11 && vm.Apps.Any(a => a.Definition.Id == deletedId));
        vm.SaveCommand.Execute(null); Assert(!vm.Apps.Any(a => a.Definition.Id == deletedId));
        vm.AddCommand.Execute(null); vm.SelectedApp!.Url = "https://example.com/new.exe"; vm.SelectedApp.Name = "Added app";
        Assert(!vm.Apps.Any(a => a.Name == "Added app")); vm.SaveCommand.Execute(null); Assert(vm.Apps.Any(a => a.Name == "Added app"));
    });
    Check("Windows CRUD uses isolated drafts and includes all saved settings", () => {
        var vm = new MainViewModel(true, confirmDelete: _ => true) { SettingsTab = 1 };
        vm.SelectedWindows!.Name = "Changed desktop";
        Assert(vm.WindowsOptions[0].Name != "Changed desktop"); vm.SaveCommand.Execute(null);
        Assert(vm.WindowsOptions[0].Name == "Changed desktop" && vm.WindowsOptions.Count == 8);
        vm.AddWindowsCommand.Execute(null); vm.SelectedWindows!.Script = "Write-Output 'hello'"; vm.SelectedWindows.Name = "New custom";
        vm.SaveCommand.Execute(null); Assert(vm.WindowsOptions.Any(o => o.Name == "New custom"));
        vm.DeleteWindowsCommand.Execute(null); Assert(vm.WindowsOptions.Any(o => o.Name == "New custom"));
        vm.SaveCommand.Execute(null); Assert(!vm.WindowsOptions.Any(o => o.Name == "New custom"));
        while (vm.SelectedWindows != null) vm.DeleteWindowsCommand.Execute(null);
        vm.SaveCommand.Execute(null); Assert(vm.WindowsOptions.Count == 0 && !vm.DeleteWindowsCommand.CanExecute(null));
    });
    Check("Save only affects active section and allows empty app catalog", () => {
        var vm = new MainViewModel(true, confirmDelete: _ => true); vm.EditableWindows[0].Name = "Unsaved setting";
        while (vm.SelectedApp != null) vm.DeleteCommand.Execute(null);
        vm.SaveCommand.Execute(null); Assert(vm.Apps.Count == 0 && vm.InstallCommand.CanExecute(null) && vm.WindowsOptions[0].Name != "Unsaved setting");
        vm.SettingsTab = 1; vm.SaveCommand.Execute(null); Assert(vm.WindowsOptions[0].Name == "Unsaved setting");
    });
    Check("Setting search is case-insensitive and keeps drafts", () => {
        var vm = new MainViewModel(true);
        vm.SelectedApp = vm.EditableApps.Single(a => a.Name == "Google Chrome");
        vm.SelectedApp.Arguments = "/changed";
        vm.AppSearch = "CHROME";
        Assert(vm.FilteredAppCount == 1 && vm.FilteredApps.Cast<AppDefinition>().Single().Name == "Google Chrome");
        vm.SettingsTab = 1; vm.WindowsSearch = "múi GIỜ";
        Assert(vm.FilteredWindowsCount == 1 && vm.EditableApps.Single(a => a.Name == "Google Chrome").Arguments == "/changed");
    });
    Check("Discard and save are isolated to the active Setting tab", () => {
        var vm = new MainViewModel(true);
        var originalApp = vm.SelectedApp!.Name;
        vm.SelectedApp.Name = "App draft";
        vm.EditableWindows[0].Name = "Windows draft";
        Assert(vm.AppsDirty && vm.WindowsDirty && vm.SaveCommand.CanExecute(null));
        vm.DiscardCommand.Execute(null);
        Assert(!vm.AppsDirty && vm.WindowsDirty && vm.EditableApps[0].Name == originalApp);
        vm.SettingsTab = 1;
        Assert(vm.IsCurrentDirty && vm.SaveStatus == "Chưa lưu thay đổi");
        vm.SaveCommand.Execute(null);
        Assert(!vm.WindowsDirty && vm.WindowsOptions[0].Name == "Windows draft");
    });
    Check("Invalid Setting draft is identified and cannot be saved", () => {
        var vm = new MainViewModel(true);
        vm.SelectedApp!.Url = "http://invalid.example/app.exe";
        Assert(vm.AppsDirty && vm.HasValidationError && vm.CurrentValidationError.Contains(vm.SelectedApp.Name) && !vm.SaveCommand.CanExecute(null));
        vm.SelectedApp.Url = "https://example.com/app.exe";
        Assert(!vm.HasValidationError && vm.SaveCommand.CanExecute(null));
    });
    Check("Custom script staged safely and reported in setting namespace", () => {
        var setting = new WindowsSettingDefinition { Id = "chrome", Name = "Test", Script = "Write-Output 'Xin chào'" };
        var dir = Path.Combine(root, "custom"); var outcomes = new List<DeploymentEvent>(); var calls = 0;
        var service = new DeploymentService(run: (command, work, _) => { calls++; Assert(command.Contains("setting-chrome.ps1") && !command.Contains("Xin chào") && File.ReadAllText(Path.Combine(work, "setting-chrome.ps1")) == setting.Script); return Task.FromResult(0); });
        service.RunAsync([], [setting], dir, new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(calls == 1 && outcomes.Any(e => e.Id == "windows:chrome" && e.Finished));
    });
    Check("Cancel custom script starts nothing", () => {
        var asks = 0; var vm = new MainViewModel(true, () => { asks++; return OfficeChoice.Cancel; }) { SettingsTab = 1 };
        vm.AddWindowsCommand.Execute(null); vm.SelectedWindows!.Script = "throw 'must not run'"; vm.SaveCommand.Execute(null);
        vm.InstallCommand.Execute(null); Assert(asks == 1 && !vm.IsBusy && !vm.HasStarted && vm.Details == "");
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
    Check("Install includes Windows settings except Debloat without selection state", () => { var vm = new MainViewModel(true); Assert(vm.Apps.Count == 11 && vm.WindowsOptions.Count == 8 && vm.WindowsOptions.All(o => !MainViewModel.IsDebloat(o.Definition)) && typeof(WindowsOption).GetProperty("Selected") == null && vm.SelectionText.Contains("8 thiết lập")); });
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
    Check("Office Cancel starts no work, including Windows options", () => { var asks = 0; var vm = new MainViewModel(true, () => { asks++; return OfficeChoice.Cancel; }); vm.InstallCommand.Execute(null); Assert(asks == 1 && !vm.IsBusy && !vm.HasStarted && vm.Overall == 0 && vm.Details == "" && vm.Apps.All(a => a.Status == "Sẵn sàng")); });
    Check("Settings drafts don't mutate deployment catalog", () => { var vm = new MainViewModel(true); vm.EditableApps[0].Url = "broken"; Assert(vm.Apps[0].Definition.Url.StartsWith("https://")); });
    Check("One-click install includes the whole catalog", () => { var vm = new MainViewModel(true); Assert(vm.IsReady && !vm.HasStarted && vm.Apps.Count == 11 && vm.InstallCommand.CanExecute(null)); Assert(typeof(AppRow).GetProperty("Selected") == null); });
    Check("Progress distinguishes download from opaque installer", () => { var row = new AppRow(Catalog.Defaults()[0]) { Status = "Đang tải", Progress = 42 }; Assert(row.ProgressText == "42%"); row.Status = "Đang cài đặt"; Assert(row.IsInstalling && row.ProgressText == "Đang chạy…"); });
    Check("Only Windows summary rows are expandable", () => {
        Assert(new AppRow(new AppDefinition { Id = "windows:summary" }).IsWindowsSummary);
        Assert(!new AppRow(new AppDefinition { Id = "chrome" }).IsWindowsSummary);
    });
    Check("Debloat remains editable but is excluded from Install", () => {
        var vm = new MainViewModel(true, () => OfficeChoice.Cancel);
        Assert(vm.EditableWindows.Any(s => s.Id == "Debloat") && vm.WindowsOptions.All(o => o.Id != "Debloat"));
        Assert(MainViewModel.IsDebloat(new WindowsSettingDefinition { Id = "legacy-debloat", Action = "Debloat" }));
        Assert(MainViewModel.IsDebloat(new WindowsSettingDefinition { Id = "Debloat", Action = "Custom" }));
        vm.InstallCommand.Execute(null); Assert(!vm.HasStarted && vm.SystemTasks.Count == 0);
    });
    Check("Optimize protocol exposes bundled-engine prepare and apply stages", () => {
        Assert(OptimizeService.TryParseProtocolLine("MINIAPPS_STAGE:Preparing", out var prepare) && prepare.Stage == OptimizeStage.Preparing);
        Assert(OptimizeService.TryParseProtocolLine("MINIAPPS_STAGE:Applying", out var apply) && apply.Stage == OptimizeStage.Applying);
        Assert(OptimizeService.TryParseProtocolLine("MINIAPPS_LOG:C:\\Logs", out var log) && log.LogDirectory == "C:\\Logs");
        Assert(OptimizeService.TryParseProtocolLine("MINIAPPS_TASK_JSON:{\"event\":\"START\",\"id\":\"DisableTelemetry\",\"message\":\"running\"}", out var task) && task.Task?.Event == OptimizeTaskEvent.Start && task.Task.Id == "DisableTelemetry");
        Assert(!OptimizeService.TryParseProtocolLine("MINIAPPS_TASK_JSON:{\"event\":\"999\",\"id\":\"DisableTelemetry\"}", out _));
        Assert(!OptimizeService.TryParseProtocolLine("MINIAPPS_TASK_JSON:{broken", out _));
        Assert(!OptimizeService.TryParseProtocolLine("ordinary upstream output", out _));
    });
    Check("Optimize service rejects incomplete task telemetry", () => {
        var fixture = Path.Combine(root, "optimize-incomplete"); Directory.CreateDirectory(fixture);
        var script = Path.Combine(fixture, "Optimize-Defaults.ps1"); File.WriteAllText(script, "fixture only");
        var service = new OptimizeService(
            runPowerShell: (_, _, output) => {
                output.Report("MINIAPPS_STAGE:Applying");
                output.Report("MINIAPPS_TASK_JSON:{\"event\":\"DONE\",\"id\":\"RemoveApps\",\"message\":\"done\"}");
                output.Report("MINIAPPS_STAGE:Completed");
                return Task.FromResult(0);
            }, isAdministrator: () => true, createMutex: () => new Mutex(false), scriptPath: script, tempRoot: fixture);
        var events = new List<OptimizeProgress>();
        var result = service.RunAsync(new InlineProgress<OptimizeProgress>(events.Add), default).GetAwaiter().GetResult();
        Assert(!result.Succeeded && result.Message.Contains("16 tác vụ") && events.All(item => item.Stage != OptimizeStage.Completed));
    });
    Check("Optimize service treats a task ERROR as failure even with exit code zero", () => {
        var fixture = Path.Combine(root, "optimize-task-error"); Directory.CreateDirectory(fixture);
        var script = Path.Combine(fixture, "Optimize-Defaults.ps1"); File.WriteAllText(script, "fixture only");
        var service = new OptimizeService(
            runPowerShell: (_, _, output) => {
                foreach (var task in OptimizeTaskCatalog.Defaults())
                {
                    var eventName = task.Id == "DisableRecall" ? "ERROR" : "DONE";
                    output.Report($"MINIAPPS_TASK_JSON:{{\"event\":\"{eventName}\",\"id\":\"{task.Id}\",\"message\":\"fixture\"}}");
                }
                return Task.FromResult(0);
            }, isAdministrator: () => true, createMutex: () => new Mutex(false), scriptPath: script, tempRoot: fixture);
        var result = service.RunAsync(new InlineProgress<OptimizeProgress>(_ => { }), default).GetAwaiter().GetResult();
        Assert(!result.Succeeded && result.Message.Contains("1 tác vụ báo lỗi") && result.Message.Contains("DisableRecall"));
    });
    Check("Optimize service reports real runner output and durable log for prepare failure", () => {
        var fixture = Path.Combine(root, "optimize-runner"); Directory.CreateDirectory(fixture);
        var script = Path.Combine(fixture, "Optimize-Defaults.ps1"); File.WriteAllText(script, "fixture only");
        var durableLog = Path.Combine(fixture, "durable-log"); Directory.CreateDirectory(durableLog);
        var stages = new List<OptimizeStage>();
        var service = new OptimizeService(
            runPowerShell: (_, work, output) => {
                Assert(work.StartsWith(fixture, StringComparison.OrdinalIgnoreCase));
                output.Report("MINIAPPS_LOG:" + durableLog);
                output.Report("MINIAPPS_STAGE:Preparing");
                output.Report("Bundled Win11Debloat engine is incomplete: missing Config\\Apps.json");
                return Task.FromResult(9);
            },
            isAdministrator: () => true,
            createMutex: () => new Mutex(false),
            scriptPath: script,
            tempRoot: fixture);
        var result = service.RunAsync(new InlineProgress<OptimizeProgress>(p => stages.Add(p.Stage)), default).GetAwaiter().GetResult();
        Assert(!result.Succeeded && result.Message.Contains("chuẩn bị") && result.Message.Contains("Bundled Win11Debloat engine is incomplete") && result.LogDirectory == durableLog && stages.Contains(OptimizeStage.Error));
        Assert(stages.Where(stage => stage != OptimizeStage.Ready && stage != OptimizeStage.Error).SequenceEqual(new[] { OptimizeStage.Preparing }));
        Assert(Directory.GetDirectories(fixture, "optimize-*").Length == 0);
    });
    Check("Optimize lifecycle locks conflicting actions and completes once", () => {
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeOptimizeService(async progress => {
            progress.Report(new(OptimizeStage.Preparing, "prepare"));
            progress.Report(new(OptimizeStage.Applying, "apply"));
            var tasks = OptimizeTaskCatalog.Defaults();
            foreach (var task in tasks) progress.Report(new(OptimizeStage.Ready, "", "", new(OptimizeTaskEvent.Queued, task.Id, "Đang chờ")));
            progress.Report(new(OptimizeStage.Applying, "first", "", new(OptimizeTaskEvent.Start, tasks[0].Id, "Đang chạy")));
            progress.Report(new(OptimizeStage.Applying, "first done", "", new(OptimizeTaskEvent.Done, tasks[0].Id, "Đã áp dụng")));
            progress.Report(new(OptimizeStage.Applying, "second", "", new(OptimizeTaskEvent.Start, tasks[1].Id, "Đang chạy")));
            await release.Task;
            progress.Report(new(OptimizeStage.Applying, "second done", "", new(OptimizeTaskEvent.Done, tasks[1].Id, "Đã áp dụng")));
            foreach (var task in tasks.Skip(2))
            {
                progress.Report(new(OptimizeStage.Applying, task.Name, "", new(OptimizeTaskEvent.Start, task.Id, "Đang chạy")));
                progress.Report(new(OptimizeStage.Applying, task.Name, "", new(OptimizeTaskEvent.Done, task.Id, "Đã áp dụng")));
            }
            return new(true, "completed", "");
        });
        var vm = new MainViewModel(false, developerEdition: false, optimizeService: fake) { Page = 1 };
        Assert(vm.OptimizeTasks.Count == 17 && vm.OptimizeTasks.All(t => t.Status == "Sẵn sàng" && t.Progress == 0));
        vm.OptimizeCommand.Execute(null);
        if (!SpinWait.SpinUntil(() => vm.IsOptimizeRunning && vm.OptimizeStage == OptimizeStage.Applying, TimeSpan.FromSeconds(2)))
            throw new Exception($"Optimize did not reach Applying: running={vm.IsOptimizeRunning}, stage={vm.OptimizeStage}, busy={vm.IsBusy}");
        if (!vm.IsBusy || vm.OptimizeCommand.CanExecute(null) || vm.InstallCommand.CanExecute(null))
            throw new Exception($"Conflicting commands not locked: busy={vm.IsBusy}, optimize={vm.OptimizeCommand.CanExecute(null)}, install={vm.InstallCommand.CanExecute(null)}");
        var lastTask = vm.OptimizeTasks[vm.OptimizeTasks.Count - 1];
        if (vm.OptimizeTasks[0].Id != "DisableTelemetry" || !vm.OptimizeTasks[0].IsRunning || lastTask.Id != "RemoveApps" || !lastTask.IsSucceeded)
            throw new Exception("Optimize task list did not keep the running row first and move the completed row last.");
        if (vm.ShowInstallCancel || vm.CancelCommand.CanExecute(null) || !vm.InstallBlockingMessage.Contains("Optimize Windows"))
            throw new Exception($"Install cancel/block state incorrect: cancelVisible={vm.ShowInstallCancel}, cancel={vm.CancelCommand.CanExecute(null)}, message={vm.InstallBlockingMessage}");
        vm.Page = 2; Assert(vm.IsDriver);
        vm.Page = 0; Assert(vm.IsInstall);
        vm.Page = 3; Assert(vm.IsInstall);
        release.SetResult(null);
        if (!SpinWait.SpinUntil(() => !vm.IsBusy, TimeSpan.FromSeconds(2))) throw new Exception("Optimize completion timed out.");
        if (!vm.OptimizeFinished || vm.OptimizeStage != OptimizeStage.Completed || vm.OptimizeButtonText != "Đã tối ưu" || vm.OptimizeCommand.CanExecute(null))
            throw new Exception($"Completed state incorrect: finished={vm.OptimizeFinished}, stage={vm.OptimizeStage}, button={vm.OptimizeButtonText}, command={vm.OptimizeCommand.CanExecute(null)}");
        if (!vm.OptimizeTasks.All(t => t.IsSucceeded && t.Progress == 100)) throw new Exception("Optimize task rows did not complete.");
    });
    Check("Optimize failure identifies the stage and remains retryable", () => {
        var attempts = 0;
        var fake = new FakeOptimizeService(progress => {
            attempts++;
            progress.Report(new(OptimizeStage.Preparing, "prepare"));
            return Task.FromResult(new OptimizeResult(false, "Không thể giải nén ở bước chuẩn bị.", ""));
        });
        var vm = new MainViewModel(false, developerEdition: false, optimizeService: fake) { Page = 1 };
        vm.OptimizeCommand.Execute(null);
        if (!SpinWait.SpinUntil(() => !vm.IsBusy && attempts == 1, TimeSpan.FromSeconds(2)))
            throw new Exception($"First failure run timed out: attempts={attempts}, busy={vm.IsBusy}, stage={vm.OptimizeStage}");
        if (vm.OptimizeFinished || vm.OptimizeStage != OptimizeStage.Error || !vm.OptimizeSummary.Contains("chuẩn bị") || !vm.OptimizeCommand.CanExecute(null))
            throw new Exception($"Unexpected retry state: finished={vm.OptimizeFinished}, stage={vm.OptimizeStage}, busy={vm.IsBusy}, command={vm.OptimizeCommand.CanExecute(null)}, summary={vm.OptimizeSummary}");
        vm.OptimizeCommand.Execute(null);
        if (!SpinWait.SpinUntil(() => attempts == 2 && vm.OptimizeStage == OptimizeStage.Error && !vm.IsBusy, TimeSpan.FromSeconds(2)))
            throw new Exception($"Retry timed out: attempts={attempts}, busy={vm.IsBusy}, stage={vm.OptimizeStage}");
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
        var service = new DeploymentService(run: (command, _, _) => { commands.Add(command); return Task.FromResult(command.Contains("Debloat") ? 1 : 0); });
        service.RunAsync([], WindowsSettingsCatalog.Defaults().Where(s => s.Action is "Winget" or "Debloat").ToArray(), Path.Combine(root, "system"), new InlineProgress<DeploymentEvent>(outcomes.Add), new InlineProgress<string>(_ => { }), default).GetAwaiter().GetResult();
        Assert(commands.Count == 2 && commands.Any(c => c.Contains("setting-Winget.ps1")) && outcomes.Any(e => e.Id == "windows:Winget" && e.Finished && !e.Failed) && outcomes.Any(e => e.Id == "windows:Debloat" && e.Failed));
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
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"{passed} tests passed. No installers or system configuration were executed.");

// Render our own WPF visual tree as a UI integration test (no desktop capture/input).
Exception? renderError = null;
var renderThread = new Thread(() =>
{
    try
    {
        var application = new MiniApps.App();
        application.InitializeComponent();
        var nextChoice = OfficeChoice.Office;
        var askCount = 0;
        var vm = new MainViewModel(true, () => { askCount++; return nextChoice; }, readDeviceInfo: () => Task.FromResult(DeviceInfoService.Resolve("MINI-PC", "Dell Inc.", "Latitude 5450", "ABC1234")));
        var window = new MiniApps.MainWindow(vm) { ShowInTaskbar = false, Left = -20000, Top = -20000 };
        window.Show();
        var targetDir = Path.GetFullPath(Path.Combine("WPF", "artifacts", "qa"));
        Directory.CreateDirectory(targetDir);
        foreach (var (button, expected) in new[] { ("OfficeButton", OfficeChoice.Office), ("WpsButton", OfficeChoice.Wps), ("CancelButton", OfficeChoice.Cancel), ("", OfficeChoice.Cancel) })
        {
            var dialog = new MiniApps.OfficeChoiceDialog() { ShowInTaskbar = false, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            dialog.Dispatcher.BeginInvoke(() =>
            {
                dialog.UpdateLayout();
                if (button == "OfficeButton")
                {
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(dialog);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(targetDir, "office-choice.png")); encoder.Save(output);
                }
                if (button == "") dialog.Close();
                else ((System.Windows.Controls.Button)dialog.FindName(button)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            });
            dialog.ShowDialog();
            if (dialog.Choice != expected) throw new Exception("Incorrect Office dialog choice: " + button);
        }
        Console.WriteLine("PASS Office dialog buttons and close-as-Cancel");
        for (var page = 0; page < 4; page++)
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
        vm.Page = 1;
        vm.OptimizeCommand.Execute(null);
        var optimizeFrame = new System.Windows.Threading.DispatcherFrame();
        var optimizeDeadline = DateTime.UtcNow.AddSeconds(5);
        var optimizeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        optimizeTimer.Tick += (_, _) =>
        {
            if (vm.OptimizeTasks.Any(task => task.IsRunning) && vm.OptimizeTasks.Any(task => task.IsSucceeded) || DateTime.UtcNow >= optimizeDeadline) optimizeFrame.Continue = false;
        };
        optimizeTimer.Start(); System.Windows.Threading.Dispatcher.PushFrame(optimizeFrame); optimizeTimer.Stop();
        if (!vm.IsOptimizeRunning || vm.OptimizeStage != OptimizeStage.Applying || !vm.OptimizeIndeterminate || vm.ShowInstallCancel)
            throw new Exception("Optimize preview did not expose the Applying UI state.");
        var optimizeList = (System.Windows.Controls.ItemsControl)window.FindName("OptimizeTaskList");
        var movedTask = vm.OptimizeTasks[vm.OptimizeTasks.Count - 1];
        var movedContainer = (System.Windows.FrameworkElement?)optimizeList.ItemContainerGenerator.ContainerFromItem(movedTask);
        if (!movedTask.IsSucceeded || movedContainer?.RenderTransform is not System.Windows.Media.TranslateTransform movedTransform || !movedTransform.HasAnimatedProperties)
            throw new Exception("The completed Optimize row was not animated when it moved to the bottom.");
        Capture("optimize-applying.png");
        var optimizeCompletionFrame = new System.Windows.Threading.DispatcherFrame();
        var optimizeCompletionTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        optimizeCompletionTimer.Tick += (_, _) =>
        {
            if (!vm.IsBusy || DateTime.UtcNow >= optimizeDeadline) optimizeCompletionFrame.Continue = false;
        };
        optimizeCompletionTimer.Start(); System.Windows.Threading.Dispatcher.PushFrame(optimizeCompletionFrame); optimizeCompletionTimer.Stop();
        if (vm.IsBusy || !vm.OptimizeFinished || vm.OptimizeButtonText != "Đã xem trước" || vm.OptimizeCommand.CanExecute(null))
            throw new Exception("Optimize preview completion UI is incorrect.");
        Capture("optimize-completed.png");
        Console.WriteLine("PASS WPF Optimize lifecycle and render");
        vm.Page = 3; vm.SettingsTab = 1;
        Capture("settings-windows.png");
        var originalAction = vm.SelectedWindows!.Action;
        var originalScript = vm.SelectedWindows.Script;
        vm.SelectedWindows.Action = "Custom"; vm.SelectedWindows.Script = "Write-Output 'Thiết lập mẫu'";
        Capture("settings-custom.png");
        vm.SelectedWindows.Action = originalAction; vm.SelectedWindows.Script = originalScript;
        vm.SelectedWindows = null;
        Capture("settings-empty-selection.png");
        vm.SelectedWindows = vm.EditableWindows[0]; vm.SettingsTab = 0;
        window.Width = 940; window.Height = 660;
        Capture("settings-minimum.png");
        window.Width = 1120; window.Height = 810;
        vm.Page = 0;
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
        window.Dispatcher.Invoke(() => vm.InstallCommand.Execute(null));
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        if (!vm.HasStarted || !progressList.IsVisible) throw new Exception("Progress list must appear after starting.");
        var frame = new System.Windows.Threading.DispatcherFrame();
        var started = DateTime.UtcNow;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => { if (!vm.IsBusy || (DateTime.UtcNow - started).TotalSeconds > 10) frame.Continue = false; };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
        if (vm.IsBusy || !vm.Summary.StartsWith("Hoàn tất") || vm.Overall != 100 || vm.Apps.Any(a => a.Progress != 100)) throw new Exception("Preview command did not complete: " + vm.Summary);
        if (vm.WindowsTaskDetails.Count != 8 || vm.WindowsTaskDetails.Any(t => t.Progress != 100) || vm.IsWindowsDetailsVisible) throw new Exception("Windows detail rows were not prepared correctly.");
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
        if (!vm.Apps.Any(a => a.Definition.Suite == "Office") || vm.Apps.Any(a => a.Definition.Suite == "WPS")) throw new Exception("Office choice did not select the correct catalog.");
        if (!vm.InstallFinished || vm.InstallCommand.CanExecute(null) || vm.InstallButtonText != "Đã hoàn tất") throw new Exception("Completed installation must stay disabled.");
        var oldSummary = vm.Summary; var oldAskCount = askCount;
        nextChoice = OfficeChoice.Wps;
        vm.InstallCommand.Execute(null);
        if (vm.IsBusy || vm.Summary != oldSummary || askCount != oldAskCount) throw new Exception("Disabled installation started a second run.");
        if (vm.SystemTasks.Count != 1 || vm.SystemTasks[0].Name != "Windows Setting" || vm.SystemTasks[0].Progress != 100 || vm.SystemTasks[0].Status != "Hoàn tất") throw new Exception("Windows settings must be represented by one completed summary row.");
        Console.WriteLine("PASS completed install becomes gray and cannot run again");
        window.Close();
        var publicVm = new MainViewModel(true, developerEdition: false);
        var publicWindow = new MiniApps.MainWindow(publicVm) { ShowInTaskbar = false, Left = -20000, Top = -20000 };
        publicWindow.Show(); publicWindow.UpdateLayout();
        var navigation = (System.Windows.Controls.ListBox)publicWindow.FindName("NavigationList");
        if (navigation.Items.Count != 4 || ((System.Windows.Controls.ListBoxItem)navigation.Items[3]).Visibility != System.Windows.Visibility.Collapsed)
            throw new Exception("Public navigation must hide Setting.");
        publicVm.Page = 3;
        if (publicVm.Page != 0 || publicVm.IsSetting) throw new Exception("Public navigation reached Setting.");
        publicWindow.Close();
        Console.WriteLine("PASS Public WPF navigation hides and blocks Setting");
        application.Shutdown();
    }
    catch (Exception ex) { renderError = ex; }
});
renderThread.SetApartmentState(ApartmentState.STA);
renderThread.Start(); renderThread.Join();
if (renderError != null) throw new Exception("WPF rendering failed", renderError);
Console.WriteLine("PASS WPF startup, navigation and render of all 4 pages");

sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
sealed class FakeOptimizeService(Func<IProgress<OptimizeProgress>, Task<OptimizeResult>> run) : IOptimizeService
{
    public Task<OptimizeResult> RunAsync(IProgress<OptimizeProgress> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return run(progress);
    }
}
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
