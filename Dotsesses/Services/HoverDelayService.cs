namespace Dotsesses.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Centralized service for managing hover behavior with velocity-based delay.
/// Tracks mouse velocity and delays hover activation based on movement speed.
/// Slow/deliberate movement activates quickly, fast traversal activates slowly.
/// </summary>
public partial class HoverDelayService : ObservableObject
{
    // Configuration
    private const double MinDelayMs = 200;      // Delay for slow/deliberate mouse
    private const double MaxDelayMs = 2000;     // Delay for fast/traversing mouse
    private const double MinVelocityPxPerSec = 100;  // Below this, use MinDelay
    private const double MaxVelocityPxPerSec = 500;  // Above this, use MaxDelay
    private const double StabilityTolerancePx = 5;   // "Hasn't moved" threshold
    private const int PositionHistorySize = 10;      // Number of samples for velocity calc

    // Position history for velocity calculation
    private readonly Queue<(Point Position, DateTime Timestamp)> _positionHistory = new();

    // Pending hover state
    private int? _pendingStudentId;
    private DispatcherTimer? _timer;

    // Position when hover candidate was first reported (for stability check)
    private Point _positionAtCandidateStart;

    /// <summary>
    /// Current smoothed velocity in pixels per second.
    /// Observable for debug display.
    /// </summary>
    [ObservableProperty]
    private double _currentVelocity;

    /// <summary>
    /// The currently active (confirmed) hovered student ID.
    /// </summary>
    [ObservableProperty]
    private int? _activeHoveredStudentId;

    /// <summary>
    /// Event fired when hover is activated (after delay and stability check).
    /// </summary>
    public event Action<int?>? OnHoverActivated;

    /// <summary>
    /// Reports a mouse position for velocity tracking.
    /// Should be called on every mouse move at the window level.
    /// </summary>
    public void ReportMousePosition(Point position)
    {
        var now = DateTime.UtcNow;

        // Add to history
        _positionHistory.Enqueue((position, now));

        // Trim old entries
        while (_positionHistory.Count > PositionHistorySize)
        {
            _positionHistory.Dequeue();
        }

        // Calculate weighted velocity
        CurrentVelocity = CalculateWeightedVelocity();
    }

    /// <summary>
    /// Gets the current mouse position (most recent from history).
    /// </summary>
    private Point GetCurrentPosition()
    {
        return _positionHistory.Count > 0 ? _positionHistory.Last().Position : default;
    }

    /// <summary>
    /// Reports a hover candidate (mouse is over a specific student's point).
    /// The service will delay activation based on current velocity.
    /// Pass null to indicate mouse is no longer over any point (ignored - use ClearHover for explicit clear).
    /// </summary>
    public void ReportHoverCandidate(int? studentId, Point position)
    {
        // Ignore null candidates - we require explicit clear
        if (studentId == null)
        {
            return;
        }

        // If same as current pending, ignore
        if (studentId == _pendingStudentId)
        {
            return;
        }

        // Cancel existing timer
        _timer?.Stop();

        // Set new pending candidate
        _pendingStudentId = studentId;

        // Record the global mouse position at the time the candidate was reported
        // This is used for the stability check (comparing global coords to global coords)
        _positionAtCandidateStart = GetCurrentPosition();

        // Calculate delay based on current velocity
        var delay = CalculateDelay(CurrentVelocity);

        // Start timer
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delay)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    /// <summary>
    /// Explicitly clears the current hover selection.
    /// </summary>
    public void ClearHover()
    {
        _timer?.Stop();
        _timer = null;
        _pendingStudentId = null;

        ActiveHoveredStudentId = null;
        OnHoverActivated?.Invoke(null);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer?.Stop();

        // Check stability - has mouse moved significantly since candidate was reported?
        // Both positions are in window coordinates (global)
        var currentPos = GetCurrentPosition();
        var distance = CalculateDistance(currentPos, _positionAtCandidateStart);

        if (distance <= StabilityTolerancePx)
        {
            // Mouse is stable - activate the hover
            ActiveHoveredStudentId = _pendingStudentId;
            OnHoverActivated?.Invoke(_pendingStudentId);
        }
        // If mouse moved, don't activate - the candidate is stale
        // A new candidate will be reported if mouse is over a different point

        _pendingStudentId = null;
    }

    /// <summary>
    /// Calculates weighted velocity from position history.
    /// More recent samples have higher weight.
    /// </summary>
    private double CalculateWeightedVelocity()
    {
        var samples = _positionHistory.ToArray();

        if (samples.Length < 2)
        {
            return 0;
        }

        double weightedSum = 0;
        double weightSum = 0;

        // Calculate velocity between each consecutive pair
        // Weight increases linearly: oldest pair gets weight 1, newest gets weight (n-1)
        for (int i = 1; i < samples.Length; i++)
        {
            var prev = samples[i - 1];
            var curr = samples[i];

            var distance = CalculateDistance(prev.Position, curr.Position);
            var timeDelta = (curr.Timestamp - prev.Timestamp).TotalSeconds;

            if (timeDelta > 0)
            {
                var velocity = distance / timeDelta;
                var weight = i; // Linear weight: 1, 2, 3, ..., n-1

                weightedSum += velocity * weight;
                weightSum += weight;
            }
        }

        return weightSum > 0 ? weightedSum / weightSum : 0;
    }

    /// <summary>
    /// Calculates delay in milliseconds based on velocity.
    /// Linearly interpolates between MinDelay and MaxDelay.
    /// </summary>
    private double CalculateDelay(double velocityPxPerSec)
    {
        // Clamp velocity to range
        var clampedVelocity = Math.Clamp(velocityPxPerSec, MinVelocityPxPerSec, MaxVelocityPxPerSec);

        // Normalize to 0-1
        var normalized = (clampedVelocity - MinVelocityPxPerSec) / (MaxVelocityPxPerSec - MinVelocityPxPerSec);

        // Interpolate delay
        return MinDelayMs + normalized * (MaxDelayMs - MinDelayMs);
    }

    private static double CalculateDistance(Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
