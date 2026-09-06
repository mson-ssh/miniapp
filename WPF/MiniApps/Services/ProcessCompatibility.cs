using System.Diagnostics;
using System.Text;

namespace MiniApps.Services;

internal static class ProcessCompatibility
{
    // Quote one argv element according to CommandLineToArgvW/CRT parsing rules.
    internal static string QuoteArgument(string value)
    {
        if (value.Length == 0) return "\"\"";
        if (value.IndexOfAny(new[] { ' ', '\t', '\"' }) < 0) return value;

        var result = new StringBuilder(value.Length + 2).Append('\"');
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }

            if (character == '\"')
                result.Append('\\', slashes * 2 + 1);
            else
                result.Append('\\', slashes);
            result.Append(character);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('\"');
        return result.ToString();
    }

    internal static string JoinArguments(IEnumerable<string> arguments) =>
        string.Join(" ", arguments.Select(QuoteArgument));

    internal static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken = default)
    {
        if (process.HasExited) return;
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler exited = (_, _) => completion.TrySetResult(null);
        process.EnableRaisingEvents = true;
        process.Exited += exited;
        try
        {
            if (process.HasExited) completion.TrySetResult(null);
            using (cancellationToken.Register(() => completion.TrySetCanceled()))
                await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            process.Exited -= exited;
        }
    }
}
