using System;
using System.Collections.Generic;
using System.Linq;
using UrDeliveries.Utilities.Geospatial;

namespace UrDeliveries.Models.SpiderLegModels
{

    public class Site
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public List<double> Load { get; set; }
        public Route Route { get; set; }
        public Route OriginalRoute { get; set; }
        public bool IsLockedToRoute { get; set; }
        public List<int> OrderList { get; set; }
    }

    public class Route
    {
        public string Id { get; set; }
        public List<Site> Sites { get; set; }
        public string TruckTypeId { get; set; }

        /// <summary>
        /// The actual capacity of the route
        /// </summary>
        public List<double> Capacity { get; set; } = new List<double>();

        /// <summary>
        /// The target capacity to load the route
        /// </summary>
        public List<double> TargetCapacity { get; set; } = new List<double>();

        /// <summary>
        /// Modeled max capacity. May be larger than the actual capacity
        /// to allow generating routes that break capacity constraints.
        /// </summary>
        public List<double> MaxCapacity { get; set; } = new List<double>();

        public List<RouteAdjacency> Adjacencies { get; set; } = new List<RouteAdjacency>();

        public Point Centroid { get; set; }

        public double DistanceKm { get; set; }
        public double DriveTimeH { get; set; }
        public bool RequiresDriveTimeUpdate { get; set; }

        public int NumCapacityConstraints => Capacity.Count;

        public List<Site> UnlockedSites => Sites.Where(s => !s.IsLockedToRoute).ToList();

        public List<Site> LockedSites => Sites.Where(s => s.IsLockedToRoute).ToList();

        public int[] arrSOrders { get; set; }

        public int numSOrders { get; set; }

        public List<double> Load
        {
            get
            {
                var loads = new List<double>();
                for (var i = 0; i < NumCapacityConstraints; i++)
                {
                    loads.Add(Sites.Sum(s => s.Load[i]));
                }
                return loads;
            }
        }

        public List<double> UnlockedLoad
        {
            get
            {
                var loads = new List<double>();
                for (var i = 0; i < NumCapacityConstraints; i++)
                {
                    loads.Add(UnlockedSites.Sum(s => s.Load[i]));
                }
                return loads;
            }
        }

        public bool HasLockedSites
        {
            get
            {
                return Sites.Any(s => s.IsLockedToRoute);
            }
        }

        public double AverageLoadPercentage()
        {
            return Load.Select((l, i) => l / MaxCapacity[i]).Average();
        }

        public double AverageUnlockedLoadPercentage()
        {
            return UnlockedLoad.Select((l, i) => l / MaxCapacity[i]).Average();
        }

        public double AverageLoadPercentage(int loadIndex)
        {
            return Load[loadIndex] / MaxCapacity[loadIndex];
        }

        /// <summary>
        /// Recomputes the centroid of the route
        /// </summary>
        public void UpdateCentroid()
        {
            if (Centroid == null)
            {
                Centroid = new Point();
            }

            try
            {
                Centroid.Latitude = Sites.Average(s => s.Latitude);
                Centroid.Longitude = Sites.Average(s => s.Longitude);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error updating centroid", e);
            }
        }

        /// <summary>
        /// Add site to route
        /// </summary>
        /// <param name="site"></param>
        public void AddSite(Site site)
        {
            site.Route?.RemoveSite(site);
            site.Route = this;
            Sites.Add(site);
            RequiresDriveTimeUpdate = true;
        }

        /// <summary>
        /// Remove site from route
        /// </summary>
        /// <param name="site"></param>
        public void RemoveSite(Site site)
        {
            site.Route = null;
            Sites.Remove(site);
            RequiresDriveTimeUpdate = true;
        }

        /// <summary>
        /// Returns true if the input site can be added to the route without exceeding
        /// the target capacity (without including the total route time).
        /// </summary>
        /// <param name="site"></param>
        /// <returns></returns>
        public bool CanAddSite(Site site)
        {
            for (var i = 0; i < NumCapacityConstraints; i++)
            {
                if (Load[i] + site.Load[i] > TargetCapacity[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns load indices where the capacity will be exceeded if the input site
        /// is added to the route.
        /// </summary>
        /// <param name="site"></param>
        /// <returns></returns>
        public List<int> GetExceededCapacityIndicesOnAddSite(Site site)
        {
            var indices = new List<int>();
            for (var i = 0; i < NumCapacityConstraints; i++)
            {
                if (Load[i] + site.Load[i] > TargetCapacity[i])
                {
                    indices.Add(i);
                }
            }
            return indices;
        }

        /// <summary>
        /// Returns true if the site can be added without exceeding the target capacity
        /// by the specified percentage. If a loadIndex is provided, only that
        /// capacity component will be checked.
        /// </summary>
        /// <param name="site"></param>
        /// <param name="percentage"></param>
        /// <param name="loadIndex"></param>
        /// <returns></returns>
        public bool CanAddSiteWithinConstraints(Site site, int percentage, int? loadIndex = null)
        {
            if (loadIndex != null)
            {
                var i = (int)loadIndex;
                var limit = Math.Min(MaxCapacity[i], TargetCapacity[i]) * (1 + percentage / 100.0);
                return site.Load[i] == 0 || Load[i] + site.Load[i] <= limit;
            }
            for (var i = 0; i < NumCapacityConstraints; i++)
            {
                var limit = Math.Min(MaxCapacity[i], TargetCapacity[i]) * (1 + percentage / 100.0);
                if (site.Load[i] > 0 && Load[i] + site.Load[i] > limit)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns true if an existing site can be replaced by a new site without
        /// exceeding the target capacity by the specified percentage. If a loadIndex
        /// is provided, only that capacity component will be checked.
        /// </summary>
        /// <param name="siteToRemove"></param>
        /// <param name="siteToAdd"></param>
        /// <param name="percentage"></param>
        /// <param name="loadIndex"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public bool CanSwapSitesWithinConstraints(
            Site siteToRemove, Site siteToAdd, int percentage, int? loadIndex = null)
        {
            if (Sites.Contains(siteToAdd) || !Sites.Contains(siteToRemove))
                throw new Exception();

            var differenceSite = new Site
            {
                Load = new List<double>()
            };
            for (var i = 0; i < siteToRemove.Load.Count; i++)
            {
                differenceSite.Load.Add(siteToAdd.Load[i] - siteToRemove.Load[i]);
            }

            return CanAddSiteWithinConstraints(differenceSite, percentage, loadIndex)
                && CanRemoveSiteWithinConstraints(differenceSite, percentage, loadIndex);
        }

        /// <summary>
        /// Returns true if the site can be removed without going below the target capacity
        /// by the specified percentage. If a loadIndex is provided, only that
        /// capacity component will be checked.
        /// </summary>
        /// <param name="site"></param>
        /// <param name="percentage"></param>
        /// <param name="loadIndex"></param>
        /// <returns></returns>
        public bool CanRemoveSiteWithinConstraints(Site site, int percentage, int? loadIndex = null)
        {
            if (loadIndex != null)
            {
                var i = (int)loadIndex;
                var limit = TargetCapacity[i] * (1 - percentage / 100.0);
                return site.Load[i] == 0 || Load[i] - site.Load[i] >= limit;
            }
            for (int i = 0; i < NumCapacityConstraints; i++)
            {
                var limit = TargetCapacity[i] * (1 - percentage / 100.0);
                if (site.Load[i] > 0 && Load[i] - site.Load[i] < limit)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns true if the site can be added without exceeding the maximum capacity.
        /// </summary>
        /// <param name="site"></param>
        /// <returns></returns>
        public bool CanAddSiteUnderMaxCapacity(Site site)
        {
            for (int i = 0; i < NumCapacityConstraints; i++)
            {
                if (Load[i] + site.Load[i] > MaxCapacity[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns true if an existing site can be replaced by a new site without
        /// exceeding the maximum capacity.
        /// </summary>
        /// <param name="siteToRemove"></param>
        /// <param name="siteToAdd"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public bool CanSwapSitesUnderMaxCapacity(Site siteToRemove, Site siteToAdd)
        {
            if (Sites.Contains(siteToAdd) || !Sites.Contains(siteToRemove))
                throw new Exception();

            var differenceSite = new Site
            {
                Load = new List<double>()
            };
            for (var i = 0; i < siteToRemove.Load.Count; i++)
            {
                differenceSite.Load.Add(siteToAdd.Load[i] - siteToRemove.Load[i]);
            }

            return CanAddSiteUnderMaxCapacity(differenceSite);
        }

        /// <summary>
        /// Returns a subset of sites where any of the load components exceed
        /// the specified threshold fraction of the maximum capacity.
        /// </summary>
        /// <param name="threshold">Value between 0 and 1</param>
        /// <returns></returns>
        public List<Site> GetLargeSites(double threshold)
        {
            return Sites
                .Where(s => IsLargeSite(s, threshold))
                .ToList();
        }

        /// <summary>
        /// Returns true if any of the load components of the input site exceeds
        /// the specified threshold fraction of the maximum capacity.
        /// </summary>
        /// <param name="site"></param>
        /// <param name="threshold"></param>
        /// <returns></returns>
        public bool IsLargeSite(Site site, double threshold)
        {
            return site.Load.Where((t, i) => t / (MaxCapacity[i] + 1e-6) > threshold).Any();
        }

        /// <summary>
        /// Returns the available capacity based on the target capacity.
        /// </summary>
        public List<double?> AvailableCapacity
        {
            get
            {
                var availableCapacity = new List<double?>();
                for (int i = 0; i < NumCapacityConstraints; i++)
                {
                    availableCapacity.Add(TargetCapacity[i] - Load[i]);
                }
                return availableCapacity;
            }
        }

        /// <summary>
        /// Returns true if all load components are less than the target capacity.
        /// </summary>
        public bool IsUnderCapacity
        {
            get
            {
                for (int i = 0; i < NumCapacityConstraints; i++)
                {
                    if (Load[i] >= TargetCapacity[i])
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Returns true if any of the load components exceeds the target capacity.
        /// </summary>
        public bool IsOverCapacity
        {
            get
            {
                for (int i = 0; i < NumCapacityConstraints; i++)
                {
                    if (Load[i] > TargetCapacity[i])
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Returns true if any of the load components exceeds the maximum capacity.
        /// </summary>
        public bool IsOverMaxCapacity
        {
            get
            {
                for (int i = 0; i < NumCapacityConstraints; i++)
                {
                    if (Load[i] > MaxCapacity[i])
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Returns true if the route exceeds the maximum capacity, excluding
        /// the capacity component indicated by the excludedIndex.
        /// </summary>
        /// <param name="excludedIndex"></param>
        /// <returns></returns>
        public bool IsOverMaxCapacityExcludeIndex(int? excludedIndex)
        {
            if (excludedIndex == null) return IsOverMaxCapacity;
            for (int i = 0; i < NumCapacityConstraints; i++)
            {
                if (Load[i] > MaxCapacity[i] && i != excludedIndex)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True if all of its sites are over-capacity sites
        /// </summary>
        public bool IsOverCapacitySitesRoute => TargetCapacity.Count > 0 && Sites.Count > 0 &&
            (
                Sites.All(s => s.Load.Where((l, i) => l > TargetCapacity[i]).Any())
                || (Sites.All(s => s.IsLockedToRoute) && Load.Where((l, i) => l > TargetCapacity[i]).Any())
            );
    }

    public class RouteAdjacency
    {
        public Route InnerRoute { get; set; }
        public Route OuterRoute { get; set; }
        public int Sequence { get; set; }

        public Route GetOppositeRoute(Site site)
        {
            return site.Route == InnerRoute ? OuterRoute : InnerRoute;
        }

        public void TransferSite(Site site)
        {
            var targetRoute = GetOppositeRoute(site);
            targetRoute.AddSite(site);
        }

        protected Dictionary<string, double> InnerRouteSitesDistancesToOuterRoute;
        protected Dictionary<string, double> OuterRouteSitesDistancesToInnerRoute;

        public void UpdateDistances()
        {
            InnerRouteSitesDistancesToOuterRoute = new Dictionary<string, double>();
            var v1x = InnerRoute.Centroid.Longitude - OuterRoute.Centroid.Longitude;
            var v1y = InnerRoute.Centroid.Latitude - OuterRoute.Centroid.Latitude;
            var v1d = Math.Sqrt(v1x * v1x + v1y * v1y);
            InnerRoute.Sites.ForEach(s =>
            {
                var v2x = s.Longitude - OuterRoute.Centroid.Longitude;
                var v2y = s.Latitude - OuterRoute.Centroid.Latitude;
                var projectedDistance = (v2x * v1x + v2y * v1y) / v1d;
                InnerRouteSitesDistancesToOuterRoute.Add(s.Id, projectedDistance);
            });

            OuterRouteSitesDistancesToInnerRoute = new Dictionary<string, double>();
            v1x = OuterRoute.Centroid.Longitude - InnerRoute.Centroid.Longitude;
            v1y = OuterRoute.Centroid.Latitude - InnerRoute.Centroid.Latitude;
            v1d = Math.Sqrt(v1x * v1x + v1y * v1y);
            OuterRoute.Sites.ForEach(s =>
            {
                var v2x = s.Longitude - InnerRoute.Centroid.Longitude;
                var v2y = s.Latitude - InnerRoute.Centroid.Latitude;
                var projectedDistance = (v2x * v1x + v2y * v1y) / v1d;
                OuterRouteSitesDistancesToInnerRoute.Add(s.Id, projectedDistance);
            });
        }

        public List<string> InnerRouteSiteIdsSortedByDistance
        {
            get
            {
                return InnerRouteSitesDistancesToOuterRoute
                    .OrderBy(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();
            }
        }

        public List<string> OuterRouteSiteIdsSortedByDistance
        {
            get
            {
                return OuterRouteSitesDistancesToInnerRoute
                    .OrderBy(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();
            }
        }

    }

    public class TransitMatrix
    {
        public Dictionary<(string, string), double> Distances { get; set; }
            = new Dictionary<(string, string), double>();

        public double GetDistanceBetween(string siteId1, string siteId2)
        {
            if (siteId1 == siteId2) return 0;
            return Distances[GetKey(siteId1, siteId2)];
        }

        public double GetUndirectedDistanceBetween(string siteId1, string siteId2)
        {
            var ab = Distances[GetKey(siteId1, siteId2)];
            var ba = Distances[GetKey(siteId2, siteId1)];
            return Math.Min(ab, ba);
        }

        public void AddEntry(string siteId1, string siteId2, double distance)
        {
            Distances.Add(GetKey(siteId1, siteId2), distance);
        }

        private readonly Func<string, string, (string, string)> GetKey = (string id1, string id2) => (id1, id2);

    }

}
