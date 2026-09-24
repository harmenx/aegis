using System;
using UnityEngine;

namespace Gitmos
{
    public enum GitEventType
    {
        Push,
        PullRequest,
        Fork,
        Create,
        Merge,
        Delete,
        Review
    }

    public struct GitEvent
    {
        public DateTime Time;
        public GitEventType Type;
        public string Actor;
        public string Repo;
        public string Language;
        public int Additions;
        public int Deletions;
        public float Latitude;
        public float Longitude;
        public string City;
        public int CityIndex;
        public bool HasTarget;
        public float TargetLatitude;
        public float TargetLongitude;
        public string TargetPlace;
        public float Weight;
        public string Category;
        public Color Tint;
        public bool SkipGlobe;
    }
}
