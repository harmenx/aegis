using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Aegis
{
    public enum ListenState
    {
        Off,
        Listening,
        Failed
    }

    // What the feed has seen so far. Read by the dashboard status and GET /health;
    // written from the main thread and the HTTP thread.
    public static class FeedStatus
    {
        static long accepted;
        static long rejected;
        static long lastCustomTicks;

        public static long Accepted => Interlocked.Read(ref accepted);
        public static long Rejected => Interlocked.Read(ref rejected);
        public static ListenState Listen { get; internal set; }
        public static string ListenDetail { get; internal set; } = "";

        public static DateTime LastCustomPointUtc => new DateTime(Interlocked.Read(ref lastCustomTicks), DateTimeKind.Utc);

        public static double SecondsSinceCustomPoint
        {
            get
            {
                long ticks = Interlocked.Read(ref lastCustomTicks);
                return ticks == 0 ? double.PositiveInfinity : (DateTime.UtcNow.Ticks - ticks) / (double)TimeSpan.TicksPerSecond;
            }
        }

        internal static void CountAccepted(int count)
        {
            if (count <= 0)
                return;
            Interlocked.Add(ref accepted, count);
            Interlocked.Exchange(ref lastCustomTicks, DateTime.UtcNow.Ticks);
        }

        internal static void CountRejected(int count)
        {
            if (count > 0)
                Interlocked.Add(ref rejected, count);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlayMode()
        {
            Interlocked.Exchange(ref accepted, 0);
            Interlocked.Exchange(ref rejected, 0);
            Interlocked.Exchange(ref lastCustomTicks, 0);
            Listen = ListenState.Off;
            ListenDetail = "";
        }
    }

    public static class AegisFeed
    {
        public static bool KeepDemo { get; private set; }

        static bool announced;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlayMode()
        {
            KeepDemo = false;
            announced = false;
        }

        public static void UseDemo(bool enabled)
        {
            KeepDemo = enabled;
            EventSimulator.DemoEnabled = enabled;
        }

        // Main thread only.
        public static void Submit(DataPoint point)
        {
            if (!KeepDemo)
                EventSimulator.DemoEnabled = false;
            if (!announced)
            {
                announced = true;
                Log.Info(KeepDemo
                    ? "Drawing custom points alongside the demo."
                    : "Drawing custom points. The built-in demo is paused; AegisFeed.UseDemo(true) mixes it back in.");
            }
            FeedStatus.CountAccepted(1);
            EventBus.Publish(ToEvent(point));
        }

        // Main thread only. Returns how many points were drawn; problems are logged.
        public static int SubmitJson(string json)
        {
            return SubmitJson(json, "SubmitJson");
        }

        public static int SubmitJson(string json, string source)
        {
            PointParseResult result = PointParser.Parse(json);
            Report(result, source);
            foreach (DataPoint point in result.Points)
                Submit(point);
            return result.Points.Count;
        }

        internal static void Report(PointParseResult result, string source)
        {
            if (!result.Ok)
            {
                FeedStatus.CountRejected(1);
                Log.WarnOnce(source + "|" + result.FatalError, source + ": " + result.FatalError);
                return;
            }
            if (result.Rejected == 0)
                return;
            FeedStatus.CountRejected(result.Rejected);
            Log.WarnOnce(source + "|" + result.Errors[0], source + ": skipped " + result.Rejected + " point(s). " + string.Join(" ", result.Errors));
        }

        public static GitEvent ToEvent(DataPoint point)
        {
            ApplyCategory(point.Category, out GitEventType type, out Color tint);
            bool hasTarget = point.HasTarget
                || !string.IsNullOrEmpty(point.TargetPlace)
                || Mathf.Abs(point.TargetLatitude) > 0.0001f
                || Mathf.Abs(point.TargetLongitude) > 0.0001f;

            float latitude = point.Latitude;
            float longitude = point.Longitude;
            bool placed = point.HasLocation
                || Mathf.Abs(latitude) > 0.0001f
                || Mathf.Abs(longitude) > 0.0001f;
            if (!placed && TryCity(point.Place, out float cityLat, out float cityLon))
            {
                latitude = cityLat;
                longitude = cityLon;
                placed = true;
            }
            if (!placed && TryCity(point.Label, out cityLat, out cityLon))
            {
                latitude = cityLat;
                longitude = cityLon;
                placed = true;
            }

            float targetLat = point.TargetLatitude;
            float targetLon = point.TargetLongitude;
            if (hasTarget && Mathf.Abs(targetLat) < 0.0001f && Mathf.Abs(targetLon) < 0.0001f
                && TryCity(point.TargetPlace, out float foundLat, out float foundLon))
            {
                targetLat = foundLat;
                targetLon = foundLon;
            }

            string label = First(point.Label, point.Place, "point");
            string series = First(point.Series, point.Place, "");
            string place = First(point.Place, point.Label, "");
            int cityIndex = placed ? NearestCity(latitude, longitude) : 0;

            return new GitEvent
            {
                Time = DateTime.UtcNow,
                Type = type,
                Actor = label,
                Repo = series,
                Language = string.IsNullOrEmpty(point.Category) ? "Data" : point.Category,
                Additions = Mathf.Max(0, Mathf.RoundToInt(point.Value)),
                Deletions = Mathf.Max(0, Mathf.RoundToInt(point.Secondary)),
                Latitude = latitude,
                Longitude = longitude,
                City = place,
                CityIndex = cityIndex,
                HasTarget = hasTarget && placed,
                TargetLatitude = targetLat,
                TargetLongitude = targetLon,
                TargetPlace = point.TargetPlace,
                Weight = point.Value > 0f ? point.Value / 80f : 1f,
                Category = string.IsNullOrEmpty(point.Category) ? "point" : point.Category.Trim(),
                Tint = tint,
                SkipGlobe = !placed
            };
        }

        static void ApplyCategory(string category, out GitEventType type, out Color tint)
        {
            tint = default;
            string key = string.IsNullOrWhiteSpace(category) ? "" : category.Trim().ToLowerInvariant();
            switch (key)
            {
                case "pull":
                case "pullrequest":
                case "pr":
                case "request":
                    type = GitEventType.PullRequest;
                    return;
                case "fork":
                case "copy":
                    type = GitEventType.Fork;
                    return;
                case "create":
                case "open":
                case "new":
                    type = GitEventType.Create;
                    return;
                case "merge":
                case "join":
                    type = GitEventType.Merge;
                    return;
                case "delete":
                case "remove":
                case "close":
                    type = GitEventType.Delete;
                    return;
                case "review":
                case "comment":
                case "resolve":
                case "resolved":
                case "ok":
                case "stable":
                    type = GitEventType.Review;
                    return;
                case "alert":
                case "error":
                case "fail":
                case "threat":
                    type = GitEventType.Push;
                    tint = Palette.Red;
                    return;
                case "":
                case "push":
                case "update":
                case "write":
                    type = GitEventType.Push;
                    return;
                default:
                    type = GitEventType.Push;
                    tint = Palette.FromName(category);
                    return;
            }
        }

        static bool TryCity(string name, out float latitude, out float longitude)
        {
            latitude = 0f;
            longitude = 0f;
            if (string.IsNullOrWhiteSpace(name))
                return false;
            for (int i = 0; i < Catalog.Cities.Length; i++)
            {
                if (!string.Equals(Catalog.Cities[i].Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                latitude = Catalog.Cities[i].Latitude;
                longitude = Catalog.Cities[i].Longitude;
                return true;
            }
            return false;
        }

        static int NearestCity(float latitude, float longitude)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Catalog.Cities.Length; i++)
            {
                float dLat = Catalog.Cities[i].Latitude - latitude;
                float dLon = Catalog.Cities[i].Longitude - longitude;
                float distance = dLat * dLat + dLon * dLon;
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        static string First(string preferred, string fallback, string empty)
        {
            if (!string.IsNullOrWhiteSpace(preferred))
                return preferred.Trim();
            if (!string.IsNullOrWhiteSpace(fallback))
                return fallback.Trim();
            return empty;
        }
    }
}
