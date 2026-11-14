using System;
using System.Collections.Generic;
using System.Linq;
using SwapMid.Scheduling;

namespace SwapMid.Hangfire;

/// <summary>
/// 摊平异常树的语义叶，兼容 Hangfire/反射包装与 AggregateException。
/// 只有完整遍历且每个叶子均满足条件时才允许抑制补偿；诊断文本按叶去重并总长截断。
/// </summary>
internal static class ScheduledJobRecordedFailureInspector
{
    private const int MaxNodes = 128;
    private const int MaxDiagnosticLeaves = 16;
    private const int MaxLeafMessageLength = 512;

    public static bool AllFailuresAreRecorded(Exception? exception)
        => AllLeavesMatch(
            exception,
            leaf => leaf is ScheduledJobRecordedFailureException
            {
                TerminalStateRecorded: true,
            });

    public static bool AllFailureNotificationsAreHandled(Exception? exception)
        => AllLeavesMatch(
            exception,
            leaf => leaf is ScheduledJobRecordedFailureException
            {
                TerminalStateRecorded: true,
                FailureNotificationHandled: true,
            });

    public static bool AllLeavesAreCancellation(Exception? exception)
        => AllLeavesMatch(
            exception,
            leaf => leaf is OperationCanceledException);

    public static bool AllLeavesAreCancellationMarkers(Exception? exception)
        => AllLeavesMatch(
            exception,
            leaf => leaf is ScheduledJobRecordedCancellationException);

    public static bool AllCancellationsAreRecorded(Exception? exception)
        => AllLeavesMatch(
            exception,
            leaf => leaf is ScheduledJobRecordedCancellationException
            {
                TerminalStateRecorded: true,
            });

    public static bool AllLeavesAreUnconfirmedSuccessMarkers(Exception? exception)
        => AllLeavesMatch(
            exception,
            leaf => leaf is ScheduledJobUnconfirmedSuccessException);

    public static bool AllLeavesAreUnconfirmedSkippedMarkers(Exception? exception)
        => AllLeavesMatch(
            exception,
            leaf => leaf is ScheduledJobUnconfirmedSkippedException);

    public static IReadOnlyList<IScheduledJobTerminalStateMarker> GetTerminalStateMarkers(
        Exception? exception)
    {
        var traversal = TraverseLeaves(exception);
        return traversal.Leaves
            .OfType<IScheduledJobTerminalStateMarker>()
            .GroupBy(marker => (marker.GetType(), marker.ExecutionId))
            .Select(group => group.First())
            .ToList();
    }

    public static string DescribeLeaves(Exception? exception, int maxLength = 2048)
    {
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        var traversal = TraverseLeaves(exception);
        var descriptions = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var diagnosticLeavesTruncated = false;

        foreach (var leaf in traversal.Leaves)
        {
            var message = NormalizeMessage(leaf.Message);
            var description = string.IsNullOrWhiteSpace(message)
                ? leaf.GetType().Name
                : $"{leaf.GetType().Name}: {message}";
            if (!seen.Add(description))
            {
                continue;
            }

            if (descriptions.Count >= MaxDiagnosticLeaves)
            {
                diagnosticLeavesTruncated = true;
                break;
            }

            descriptions.Add(description);
        }

        if (!traversal.Complete || diagnosticLeavesTruncated)
        {
            descriptions.Add("异常叶已截断");
        }

        var result = descriptions.Count == 0
            ? "未知错误"
            : string.Join(" | ", descriptions);
        return result.Length <= maxLength
            ? result
            : result[..Math.Max(0, maxLength - 1)] + "…";
    }

    private static bool AllLeavesMatch(
        Exception? exception,
        Func<Exception, bool> predicate)
    {
        var traversal = TraverseLeaves(exception);
        return traversal.Complete
            && traversal.Leaves.Count > 0
            && traversal.Leaves.All(predicate);
    }

    private static ExceptionLeafTraversal TraverseLeaves(Exception? exception)
    {
        if (exception == null)
        {
            return new ExceptionLeafTraversal(Array.Empty<Exception>(), Complete: true);
        }

        var leaves = new List<Exception>();
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);

        while (pending.Count > 0)
        {
            if (visited.Count >= MaxNodes)
            {
                return new ExceptionLeafTraversal(leaves, Complete: false);
            }

            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            // Runner marker 自身就是语义叶，且按契约绝不持有原始 InnerException。
            if (current is IScheduledJobTerminalStateMarker)
            {
                leaves.Add(current);
                continue;
            }

            if (current is AggregateException aggregate
                && aggregate.InnerExceptions.Count > 0)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }

                continue;
            }

            if (current.InnerException != null)
            {
                pending.Push(current.InnerException);
                continue;
            }

            leaves.Add(current);
        }

        return new ExceptionLeafTraversal(leaves, Complete: true);
    }

    private static string NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var bounded = message.Length <= MaxLeafMessageLength
            ? message
            : message[..MaxLeafMessageLength] + "…";
        return bounded
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private readonly record struct ExceptionLeafTraversal(
        IReadOnlyList<Exception> Leaves,
        bool Complete);
}
