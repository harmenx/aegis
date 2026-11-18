using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Aegis
{
    public sealed class PointParseResult
    {
        public readonly List<DataPoint> Points = new List<DataPoint>();
        public readonly List<string> Errors = new List<string>();

        // Set when the body could not be read at all, as opposed to individual points failing.
        public string FatalError;

        public bool Ok => FatalError == null;
        public int Rejected => Errors.Count;
    }

    // Reads and validates points. Has no Unity main-thread dependencies, so the HTTP
    // listener can call it and answer a bad request before anything reaches the scene.
    public static class PointParser
    {
        public const int MaxTextLength = 120;
        const int MaxReportedErrors = 20;

        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        static readonly Regex WrapperKey = new Regex("\"points\"\\s*:", RegexOptions.CultureInvariant);

        public static PointParseResult Parse(string json, int maxPoints = int.MaxValue)
        {
            var result = new PointParseResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                result.FatalError = "Body is empty. Send a point object, an array of points, or {\"points\":[...]}.";
                return result;
            }

            PointDto[] records;
            try
            {
                records = Read(json.Trim());
            }
            catch (Exception error)
            {
                result.FatalError = "Body is not valid JSON. " + error.Message;
                return result;
            }

            if (records == null)
            {
                result.FatalError = "Expected a point object, an array of points, or {\"points\":[...]}.";
                return result;
            }
            if (records.Length > maxPoints)
            {
                result.FatalError = "Too many points in one request (" + records.Length + "). The limit is " + maxPoints + ".";
                return result;
            }

            int errorCount = 0;
            for (int i = 0; i < records.Length; i++)
            {
                if (TryConvert(records[i], out DataPoint point, out string problem))
                {
                    result.Points.Add(point);
                    continue;
                }
                errorCount++;
                if (result.Errors.Count < MaxReportedErrors)
                    result.Errors.Add("points[" + i + "]: " + problem);
            }
            if (errorCount > result.Errors.Count)
                result.Errors.Add("…and " + (errorCount - result.Errors.Count) + " more.");
            return result;
        }

        static PointDto[] Read(string json)
        {
            if (json.StartsWith("[", StringComparison.Ordinal))
                return JsonUtility.FromJson<PointBatch>("{\"points\":" + json + "}")?.points;

            if (!json.StartsWith("{", StringComparison.Ordinal))
                throw new FormatException("It must start with { or [.");

            // A wrapper object has a "points" key; anything else is a single point. JsonUtility
            // fills a missing array with an empty one, so the key itself has to be checked.
            if (WrapperKey.IsMatch(json))
                return JsonUtility.FromJson<PointBatch>(json)?.points;
            PointDto single = JsonUtility.FromJson<PointDto>(json);
            return single == null ? null : new[] { single };
        }

        static bool TryConvert(PointDto record, out DataPoint point, out string problem)
        {
            point = default;
            if (record == null)
            {
                problem = "is null.";
                return false;
            }

            if (!Finite(record.latitude, "latitude", out problem)
                || !Finite(record.longitude, "longitude", out problem)
                || !Finite(record.targetLatitude, "targetLatitude", out problem)
                || !Finite(record.targetLongitude, "targetLongitude", out problem)
                || !Finite(record.value, "value", out problem)
                || !Finite(record.secondary, "secondary", out problem))
                return false;

            if (!InRange(record.latitude, 90f, "latitude", out problem)
                || !InRange(record.longitude, 180f, "longitude", out problem)
                || !InRange(record.targetLatitude, 90f, "targetLatitude", out problem)
                || !InRange(record.targetLongitude, 180f, "targetLongitude", out problem))
                return false;

            if (record.value < 0f || record.secondary < 0f)
            {
                problem = "value and secondary must be zero or more.";
                return false;
            }

            point = new DataPoint
            {
                Id = TextFormat.Clean(record.id, MaxTextLength),
                Label = TextFormat.Clean(record.label, MaxTextLength),
                Series = TextFormat.Clean(record.series, MaxTextLength),
                Category = TextFormat.Clean(record.category, 40),
                Place = TextFormat.Clean(record.place, MaxTextLength),
                Latitude = record.latitude,
                Longitude = record.longitude,
                HasLocation = record.hasLocation,
                TargetPlace = TextFormat.Clean(record.targetPlace, MaxTextLength),
                TargetLatitude = record.targetLatitude,
                TargetLongitude = record.targetLongitude,
                HasTarget = record.hasTarget,
                Value = record.value,
                Secondary = record.secondary
            };

            bool named = !string.IsNullOrEmpty(point.Label) || !string.IsNullOrEmpty(point.Place)
                || !string.IsNullOrEmpty(point.Series) || !string.IsNullOrEmpty(point.Id);
            bool located = point.HasLocation || point.Latitude != 0f || point.Longitude != 0f;
            if (!named && !located)
            {
                problem = "needs at least a label, place, series, id, or coordinates.";
                return false;
            }

            problem = null;
            return true;
        }

        static bool Finite(float value, string field, out string problem)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                problem = field + " must be a finite number.";
                return false;
            }
            problem = null;
            return true;
        }

        static bool InRange(float value, float limit, string field, out string problem)
        {
            if (value < -limit || value > limit)
            {
                problem = field + " " + value.ToString("0.###", Invariant) + " is outside -" + limit + " to " + limit + ".";
                return false;
            }
            problem = null;
            return true;
        }

        // Identity of a point for de-duplicating rewritten files. A point with the same id and
        // values is the same point; changing any value draws it again.
        public static string Fingerprint(DataPoint point)
        {
            return string.Join("|",
                point.Id,
                point.Label,
                point.Series,
                point.Category,
                point.Place,
                point.Latitude.ToString("R", Invariant),
                point.Longitude.ToString("R", Invariant),
                point.TargetPlace,
                point.TargetLatitude.ToString("R", Invariant),
                point.TargetLongitude.ToString("R", Invariant),
                point.HasTarget ? "1" : "0",
                point.Value.ToString("R", Invariant),
                point.Secondary.ToString("R", Invariant));
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
