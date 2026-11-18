using System;

namespace Aegis
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
}
