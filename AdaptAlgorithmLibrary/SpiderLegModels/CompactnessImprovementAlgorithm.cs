//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using UrDeliveries.Utilities.Geospatial;

//namespace UrDeliveries.Models.SpiderLegModels
//{
//    public class CompactnessImprovementAlgorithm
//    {
//        private SpiderLegProblem SpiderLegProblem { get; set; }

        
//        public CompactnessImprovementAlgorithm(SpiderLegProblem spiderLegProblem) // OSRMService osrmService)
//        {
//            SpiderLegProblem = spiderLegProblem;
//            //OsrmService = osrmService;
//        }

//        public void Compact(
//            int maxIterations = 10,
//            bool allowBreakingConstraints = false,
//            int? allowedImbalancePercentage = null,
//            bool enableHullCompact = false)
//        {
//            var numTransfers = 0;
//            var numIterations = 0;
//            do
//            {
//                numTransfers = 0;
//                var anchorPerRoute = new Dictionary<Route, Site>();
//                var outlierThresholdPerRoute = new Dictionary<Route, double>();
//                var outliers = new Dictionary<Site, double>();
                
//                // Compute anchors and detect outliers in each route
//                SpiderLegProblem.Routes.ForEach(route =>
//                {
//                    // Compute distances and determine anchor
//                    var distancesToCenter = GetDistancesToCenter(route.Sites);
//                    var anchorSite = GetAnchorSite(distancesToCenter);
//                    anchorPerRoute.Add(route, anchorSite);
//                    var distancesToAnchor = GetDistancesToAnchor(route.Sites, anchorSite);
//                    var distanceToAnchorThreshold = GetOutlierDistanceThreshold(distancesToAnchor.Values.ToList());
//                    var distanceToCenterThreshold = GetOutlierDistanceThreshold(distancesToCenter.Values.ToList());
//                    outlierThresholdPerRoute.Add(route, distanceToAnchorThreshold);

//                    // Outliers by distance
//                    var routeOutliers = GetOutliersByDistance(route, distancesToAnchor, distancesToCenter, distanceToAnchorThreshold, distanceToCenterThreshold);
//                    routeOutliers.ToList().ForEach(kvp => outliers[kvp.Key] = kvp.Value);

//                    // Attempt to reduce convex hull if enabled
//                    if (enableHullCompact)
//                    {
//                        var routeConvexHullOutliers = GetConvexHullSitesAsOutliers(route, distancesToAnchor);
//                        routeConvexHullOutliers.ToList().ForEach(kvp => outliers[kvp.Key] = kvp.Value);
//                    }
//                });
                
//                numTransfers += TransferOutliers(
//                    outliers, anchorPerRoute, outlierThresholdPerRoute, allowBreakingConstraints, allowedImbalancePercentage);
                
//                numIterations++;
                
//            } while (numTransfers > 0 && numIterations < maxIterations);

//            SpiderLegProblem.Logger.Information("Compact done in {it} iterations", numIterations - 1);

//            // Update adjacencies
//            if (SpiderLegProblem.IsAutoAdjustAdjacenciesEnabled)
//            {
//                SpiderLegProblem.AdjustAdjacencies();
//            }
//            else
//            {
//                SpiderLegProblem.Routes.ForEach(r => r.UpdateCentroid());
//                SpiderLegProblem.RouteAdjacencies.ForEach(adj => adj.UpdateDistances());
//            }
//        }
        
//        private Dictionary<Site, double> GetDistancesToCenter(List<Site> sites)
//        {
//            var distancesToCenter = new Dictionary<Site, double>();
//            var avgLat = sites.Select(s => s.Latitude).Average();
//            var avgLng = sites.Select(s => s.Longitude).Average();
//            var geometricCenter = new Point { Latitude = avgLat, Longitude = avgLng };
//            sites.ForEach(s =>
//            {
//                var sitePoint = new Point { Latitude = s.Latitude, Longitude = s.Longitude };
//                var distance = GeospatialFormula.Distance(sitePoint, geometricCenter);
//                distancesToCenter.Add(s, distance / 1000.0);
//            });
//            return distancesToCenter;
//        }

//        private Site GetAnchorSite(Dictionary<Site, double> distancesToRouteCenter)
//        {
//            return distancesToRouteCenter.OrderBy(d => d.Value).First().Key;
//        }

//        private Dictionary<Site, double> GetDistancesToAnchor(List<Site> sites, Site anchorSite)
//        {
//            var distancesToAnchor = new Dictionary<Site, double>();
//            sites.ForEach(site =>
//            {
//                var d = SpiderLegProblem.MasterTransitMatrix.GetUndirectedDistanceBetween(anchorSite.Id, site.Id);
//                distancesToAnchor.Add(site, d);
//            });
//            return distancesToAnchor;
//        }

//        private double GetOutlierDistanceThreshold(List<double> distances)
//        {
//            // Twice the median distance
//            return 2 * distances
//                .OrderBy(d => d)
//                .ElementAt(distances.Count / 2);
//        }

//        private int TransferOutliers(
//            Dictionary<Site, double> outliers, 
//            Dictionary<Route, Site> anchorPerRoute, 
//            Dictionary<Route, double> outlierThresholdPerRoute,
//            bool allowBreakingConstraints,
//            int? allowedImbalancePercentage)
//        {
//            var numTransfers = 0;
//            if (outliers.Count == 0) return numTransfers;
            
//            // Get road distances
//            var outliersToProcess = outliers
//                .Where(kvp => !kvp.Key.IsLockedToRoute)
//                .OrderByDescending(kvp => kvp.Key.Route.AverageLoadPercentage())
//                .ThenByDescending(kvp => kvp.Value)
//                .Take(200 - SpiderLegProblem.Routes.Count)
//                .ToList();

//            var outliersAndAnchors = outliersToProcess.Select(kvp => kvp.Key)
//                .Concat(anchorPerRoute.Select(kvp => kvp.Value))
//                .Select(s => new Point { Latitude = s.Latitude, Longitude = s.Longitude })
//                .ToList();
//            var request = await OsrmService.GetTable(outliersAndAnchors,
//                Enumerable.Range(0, outliersToProcess.Count).ToList(),
//                Enumerable.Range(outliersToProcess.Count, anchorPerRoute.Count).ToList());
//            var outlierDistanceToAnchors = request.Distances;

//            // Process outliers
//            for (var i = 0; i < outliersToProcess.Count; i++)
//            {
//                var outlierSite = outliersToProcess[i].Key;
//                var distances = outlierDistanceToAnchors[i];
//                var originalDistance = distances[SpiderLegProblem.Routes.IndexOf(outlierSite.Route)];
//                var sortedRoutes = SpiderLegProblem.Routes.ToArray();
//                var sortedDistances = distances.ToArray();
//                Array.Sort(sortedDistances, sortedRoutes);
//                for (var j = 0; j < sortedRoutes.Length; j++)
//                {
//                    var targetRoute = sortedRoutes[j];
//                    var sourceRoute = outlierSite.Route;
//                    if (targetRoute == sourceRoute) break;
//                    var isCloser = sortedDistances[j] <= originalDistance;
//                    var doesNotBecomeOutlier =
//                        sortedDistances[j] < 1.5 * outlierThresholdPerRoute[targetRoute] * 1000;
//                    var keepsBalance =
//                        allowBreakingConstraints
//                        || allowedImbalancePercentage == null
//                        || (targetRoute.CanAddSiteWithinConstraints(
//                                outlierSite, (int)allowedImbalancePercentage, SpiderLegProblem.LastBalanceParameterIndex)
//                            && sourceRoute.CanRemoveSiteWithinConstraints(
//                                outlierSite, (int)allowedImbalancePercentage, SpiderLegProblem.LastBalanceParameterIndex));
//                    var isUnderMaxConstraints =
//                        allowBreakingConstraints ||
//                        targetRoute.CanAddSiteUnderMaxCapacity(outlierSite);
//                    if (isCloser && doesNotBecomeOutlier && keepsBalance && isUnderMaxConstraints)
//                    {
//                        targetRoute.AddSite(outlierSite);
//                        numTransfers++;
//                        break;
//                    }
//                }
//            }

//            return numTransfers;
//        }

//        private Dictionary<Site, double> GetOutliersByDistance(
//            Route route, 
//            Dictionary<Site, double> distancesToAnchor,
//            Dictionary<Site, double> distancesToCenter,
//            double distanceToAnchorThreshold,
//            double distanceToCenterThreshold)
//        {
//            var outliers = new Dictionary<Site, double>();
//            if (route.Sites.Count <= 5) return outliers;
            
//            route.Sites.ForEach(site =>
//            {
//                if (distancesToAnchor[site] > distanceToAnchorThreshold || distancesToCenter[site] > distanceToCenterThreshold)
//                {
//                    outliers.Add(site, distancesToAnchor[site]);
//                }
//            });
//            return outliers;
//        }

//        private Dictionary<Site, double> GetConvexHullSitesAsOutliers(Route route, Dictionary<Site, double> distancesToAnchor)
//        {
//            var outliers = new Dictionary<Site, double>();
//            var points = route.Sites
//                .Select(s => new Point { Latitude = s.Latitude, Longitude = s.Longitude })
//                .ToList();
//            var convexHullPoints = ConvexHull.GetConvexHull(points);
//            if (convexHullPoints.Count < route.Sites.Count)
//            {
//                convexHullPoints
//                    .Select(p => route.Sites.FirstOrDefault(s => s.Latitude == p.Latitude && s.Longitude == p.Longitude))
//                    .ToList()
//                    .ForEach(s =>
//                    {
//                        var entry = distancesToAnchor.FirstOrDefault(kvp => kvp.Key == s);
//                        if (!outliers.ContainsKey(entry.Key))
//                        {
//                            outliers.Add(entry.Key, entry.Value);
//                        }
//                    });
//            }
//            return outliers;
//        }

//    }
//}