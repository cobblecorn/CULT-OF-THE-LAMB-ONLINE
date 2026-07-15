using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal static class BridgePerfProfiler
    {
        private static readonly Dictionary<string, float> LastRecordedAt = new Dictionary<string, float>();
        private static bool _enabled;
        private static float _slowThresholdMs;

        public static void Configure(bool enabled, float slowThresholdMs)
        {
            _enabled = enabled;
            _slowThresholdMs = Mathf.Max(1f, slowThresholdMs);
            LastRecordedAt.Clear();
            WorldTrace.Record("perf.config", "enabled=" + enabled + " slowThresholdMs=" + WorldTrace.FormatFloat(_slowThresholdMs));
        }

        public static void Measure(string step, Action action)
        {
            if (!_enabled)
            {
                action();
                return;
            }

            long started = Stopwatch.GetTimestamp();
            try
            {
                action();
            }
            finally
            {
                double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs >= _slowThresholdMs && ShouldRecord(step))
                {
                    WorldTrace.Record(
                        "perf.tick_slow",
                        "step=" + Clean(step)
                        + " ms=" + elapsedMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                        + " frame=" + Time.frameCount
                        + " scene=" + Clean(SceneManager.GetActiveScene().name)
                        + " unscaledTime=" + WorldTrace.FormatFloat(Time.unscaledTime));
                }
            }
        }

        private static bool ShouldRecord(string step)
        {
            float now = Time.realtimeSinceStartup;
            if (LastRecordedAt.TryGetValue(step, out float previous) && now - previous < 2f)
            {
                return false;
            }

            LastRecordedAt[step] = now;
            return true;
        }

        private static string Clean(string value)
        {
            return string.IsNullOrEmpty(value)
                ? "unknown"
                : value.Replace(" ", "_").Replace("\t", "_").Replace("\r", "_").Replace("\n", "_");
        }
    }
}
