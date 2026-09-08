using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MiniApps.OptimizeEngine;

internal static class Program
{
    private const string PayloadResource = "MiniApps.OptimizeEngine.Payload.zip";
    private const string DeploymentMutexName = @"Global\MiniApps.Deployment";
    private const string WorkerMutexName = @"Global\MiniApps.DebloatAppxWorker";
    private const string EngineCommit = "6012b02ea282f23ea943946206762fd430025c6f";

    private static int Main(string[] args)
    {
        try
        {
            if (Has(args, "--version"))
            {
                Console.WriteLine("MiniApps.OptimizeEngine " + Assembly.GetExecutingAssembly().GetName().Version);
                Console.WriteLine("Win11Debloat " + EngineCommit);
                return 0;
            }
            if (Has(args, "--verify"))
            {
                using var payload = OpenPayload();
                var result = VerifyPayload(payload);
                Console.WriteLine($"MINIAPPS_ENGINE_VERIFY:files={result.FileCount};sha256={result.Sha256};commit={EngineCommit}");
                return 0;
            }
            if (!Has(args, "--run") || !Has(args, "--silent"))
            {
                Console.Error.WriteLine("Usage: MiniApps.OptimizeEngine.exe --verify | --version | --run --silent [--work-root <path>] [--caller-holds-deployment-lock]");
                return 64;
            }
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        Mutex? deployment = null;
        var acquired = false;
        if (!Has(args, "--caller-holds-deployment-lock"))
        {
            deployment = new Mutex(false, DeploymentMutexName);
            try { acquired = deployment.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new InvalidOperationException("Another MiniApps install or Optimize run is active.");
        }
        try
        {
            if (IsMutexHeld(WorkerMutexName))
                throw new InvalidOperationException("A previous MiniApps package worker is still active.");
            var requestedRoot = Option(args, "--work-root");
            var ownsRoot = string.IsNullOrWhiteSpace(requestedRoot);
            var workRoot = ownsRoot
                ? Path.Combine(Path.GetTempPath(), "MiniApps", "engine-" + Guid.NewGuid().ToString("N").Substring(0, 12))
                : Path.GetFullPath(requestedRoot!);
            Directory.CreateDirectory(workRoot);
            var payloadRoot = Path.Combine(workRoot, "payload");
            if (Directory.Exists(payloadRoot)) throw new IOException("Optimize payload directory already exists: " + payloadRoot);
            try
            {
                using (var payload = OpenPayload())
                {
                    VerifyPayload(payload);
                    payload.Position = 0;
                    ExtractPayload(payload, payloadRoot);
                }
                var runner = Path.Combine(payloadRoot, "Scripts", "Optimize-Defaults.ps1");
                if (!File.Exists(runner)) throw new FileNotFoundException("Optimize runner is missing from the embedded payload.", runner);
                return RunPowerShell(runner, workRoot);
            }
            finally
            {
                TryDelete(payloadRoot);
                if (ownsRoot) TryDelete(workRoot);
            }
        }
        finally
        {
            if (acquired) deployment!.ReleaseMutex();
            deployment?.Dispose();
        }
    }

    private static int RunPowerShell(string runner, string workRoot)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workRoot,
            Arguments = JoinArguments(new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner })
        };
        start.EnvironmentVariables["TEMP"] = workRoot;
        start.EnvironmentVariables["TMP"] = workRoot;
        using var process = Process.Start(start) ?? throw new IOException("Could not start the embedded Optimize runner.");
        var output = Pump(process.StandardOutput, Console.Out);
        var error = Pump(process.StandardError, Console.Error);
        process.WaitForExit();
        if (!Task.WaitAll(new[] { output, error }, TimeSpan.FromSeconds(3)))
        {
            try { process.StandardOutput.Close(); } catch { }
            try { process.StandardError.Close(); } catch { }
        }
        return process.ExitCode;
    }

    private static Task Pump(StreamReader source, TextWriter destination) => Task.Run(async () =>
    {
        string? line;
        while ((line = await source.ReadLineAsync()) != null) destination.WriteLine(line);
    });

    private static Stream OpenPayload() => Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
        ?? throw new InvalidDataException("Embedded Optimize payload is missing.");

    private static (int FileCount, string Sha256) VerifyPayload(Stream payload)
    {
        string sha;
        using (var hash = SHA256.Create()) sha = BitConverter.ToString(hash.ComputeHash(payload)).Replace("-", "").ToLowerInvariant();
        payload.Position = 0;
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, true);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = ValidateEntryName(entry.FullName);
            if (!names.Add(name)) throw new InvalidDataException("Duplicate Optimize payload entry: " + name);
        }
        foreach (var required in new[]
        {
            "Engine/Debloat/Win11Debloat.ps1", "Engine/Debloat/LICENSE", "Engine/Debloat/UPSTREAM.md",
            "Engine/Debloat/Config/Apps.json", "Engine/Debloat/Config/DefaultSettings.json",
            "Scripts/Optimize-Defaults.ps1", "Scripts/Optimize-TaskBridge.ps1",
            "Scripts/Invoke-MiniAppsAppxWorker.ps1", "Scripts/Invoke-MiniAppsWingetWorker.ps1"
        })
            if (!names.Contains(required)) throw new InvalidDataException("Optimize payload is incomplete: " + required);
        return (names.Count, sha);
    }

    private static void ExtractPayload(Stream payload, string destinationRoot)
    {
        var root = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destinationRoot);
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, true);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = ValidateEntryName(entry.FullName);
            if (!names.Add(name)) throw new InvalidDataException("Duplicate Optimize payload entry: " + name);
            var target = Path.GetFullPath(Path.Combine(destinationRoot, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Optimize payload escaped its destination: " + name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static string ValidateEntryName(string value)
    {
        var name = value.Replace('\\', '/').Trim();
        if (name.Length == 0 || name.StartsWith("/", StringComparison.Ordinal) || name.Contains(":") ||
            name.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."))
            throw new InvalidDataException("Invalid Optimize payload entry: " + value);
        return name;
    }

    private static bool IsMutexHeld(string name)
    {
        using var mutex = new Mutex(false, name);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            return !acquired;
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private static string JoinArguments(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(QuoteArgument));
    private static string QuoteArgument(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    private static bool Has(IEnumerable<string> args, string name) => args.Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static string? Option(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return null;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }
}
