using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gitmos
{
    // One record from an outside system. Latitude and longitude place it on the globe.
    // A target draws an arc. Value raises the activity bars and the line counter.
    [Serializable]
    public struct DataPoint
    {
        public string Id;
        public string Label;
        public string Series;
        public string Category;
        public float Latitude;
        public float Longitude;
        public string Place;
        public bool HasLocation;
        public float TargetLatitude;
        public float TargetLongitude;
        public string TargetPlace;
        public bool HasTarget;
        public float Value;
        public float Secondary;
    }

    public static class AegisFeed
    {
        public static bool KeepDemo { get; private set; }

        static bool announced;

        public static void UseDemo(bool enabled)
        {
            KeepDemo = enabled;
            EventSimulator.DemoEnabled = enabled;
        }

        public static void Submit(DataPoint point)
        {
            if (!KeepDemo)
                EventSimulator.DemoEnabled = false;
            if (!announced)
            {
                announced = true;
                Debug.Log("Aegis is drawing custom points. The built-in demo is paused. AegisFeed.UseDemo(true) mixes them back in.");
            }
            EventBus.Publish(ToEvent(point));
        }

        public static int SubmitJson(string json)
        {
            return SubmitJson(json, null);
        }

        public static int SubmitJson(string json, HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(json))
                return 0;

            PointDto[] records = Parse(json);
            if (records == null || records.Length == 0)
                return 0;

            int submitted = 0;
            for (int i = 0; i < records.Length; i++)
            {
                PointDto record = records[i];
                if (record == null)
                    continue;
                if (seen != null)
                {
                    string fingerprint = Fingerprint(record);
                    if (!seen.Add(fingerprint))
                        continue;
                }
                Submit(FromDto(record));
                submitted++;
            }
            return submitted;
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

        static PointDto[] Parse(string json)
        {
            try
            {
                string trimmed = json.Trim();
                if (trimmed.StartsWith("["))
                    trimmed = "{\"points\":" + trimmed + "}";
                else if (trimmed.StartsWith("{") && trimmed.IndexOf("\"points\"", StringComparison.Ordinal) < 0)
                    trimmed = "{\"points\":[" + trimmed + "]}";

                PointBatch batch = JsonUtility.FromJson<PointBatch>(trimmed);
                if (batch == null || batch.points == null)
                    return null;
                return batch.points;
            }
            catch (Exception error)
            {
                Debug.LogWarning("Aegis could not read points. " + error.Message);
                return null;
            }
        }

        static DataPoint FromDto(PointDto record)
        {
            return new DataPoint
            {
                Id = record.id,
                Label = record.label,
                Series = record.series,
                Category = record.category,
                Latitude = record.latitude,
                Longitude = record.longitude,
                Place = record.place,
                HasLocation = record.hasLocation,
                TargetLatitude = record.targetLatitude,
                TargetLongitude = record.targetLongitude,
                TargetPlace = record.targetPlace,
                HasTarget = record.hasTarget,
                Value = record.value,
                Secondary = record.secondary
            };
        }

        static string Fingerprint(PointDto record)
        {
            return string.Join("|",
                record.id,
                record.label,
                record.series,
                record.category,
                record.place,
                record.latitude.ToString("0.###"),
                record.longitude.ToString("0.###"),
                record.targetPlace,
                record.targetLatitude.ToString("0.###"),
                record.targetLongitude.ToString("0.###"),
                record.hasTarget ? "1" : "0",
                record.value.ToString("0.###"),
                record.secondary.ToString("0.###"));
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

        [Serializable]
        class PointBatch
        {
            public PointDto[] points;
        }

        [Serializable]
        class PointDto
        {
            public string id;
            public string label;
            public string series;
            public string category;
            public float latitude;
            public float longitude;
            public string place;
            public bool hasLocation;
            public float targetLatitude;
            public float targetLongitude;
            public string targetPlace;
            public bool hasTarget;
            public float value;
            public float secondary;
        }
    }
}
