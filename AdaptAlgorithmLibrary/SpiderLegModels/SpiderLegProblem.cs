using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using ProjNet.CoordinateSystems.Transformations;
using UrDeliveries.Utilities.Geospatial;


namespace UrDeliveries.Models.SpiderLegModels
{
    public class SpiderLegProblem
    {
        public Site Base { get; set; }
        public List<Route> Routes { get; set; } = new List<Route>();
        private List<Route> OriginalRoutes { get; set; } = new List<Route>();
        public List<RouteAdjacency> RouteAdjacencies { get; set; } = new List<RouteAdjacency>();
        public TransitMatrix MasterTransitMatrix { get; set; }
        public bool IsAutoAdjustAdjacenciesEnabled { get; set; }
        public int MaxIterations { get; set; } = 250;
        public int NClosestTransferCandidates { get; set; } = 10;
        public bool IsBalanceEnabled { get; set; }
        public int AddedRouteIdx { get; set; }
        public int? BalanceParameterIndex { get; set; } = null;
        public int LastBalanceParameterIndex { get; set; }
        public Dictionary<string, double> MaxTotalTimeH { get; set; }
        public double? MaxServiceTimeH { get; set; }
        public int? ServiceTimeHParameterIndex { get; set; }
        private int NumberOfRoutesBeforeBalance { get; set; }

        private double CurrentCapacityIncrement { get; set; }

        public bool SkipNextAdjacencyAdjustment { get; set; } = false;
        public int AllowedSkipAdjacencyAdjustments { get; set; } = 3;

        public bool EnableAxisSplit { get; set; }

        public ISpiderLegBuilder SpiderLegBuilder = new SpiderLegBuilder();

        private List<string> IterationSignatures { get; set; }

        private bool ShouldBreakIterationsDueToRepeatedSignatures { get; set; }

        public ILogger Logger { get; set; }

        public List<int> LimitingCapacitiesIndices { get; set; }

        public int TargetNumberOfRoutes { get; set; }

        public List<Site> UnroutedSites { get; set; } = new List<Site>();

        private Dictionary<string, (double X, double Y)> SiteXYs { get; set; }


        public SpiderLegProblem()
        {
            if (Logger == null)
            {
                Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.Console()
                    .CreateLogger();
            }
        }

        /// new scilog objects
        /// 
        public ScilogInterfaces.ScilogInterfaces.IHierarchicalRouteConstruction sHierarchicalRouteConstruction { get; set; }

        public void AdjustNumberOfRoutes(int numberOfRoutes)
        {
            if (numberOfRoutes == 0)
            {
                return;
            }
            var enableNNReassignment = (double)numberOfRoutes / Routes.Count > 1.5;
            var minRoutes = Routes.Count(r => r.HasLockedSites);
            while (Routes.Count > numberOfRoutes && Routes.Count > minRoutes)
            {
                var outermostAdjacency = RouteAdjacencies
                    .FirstOrDefault(a => !a.OuterRoute.HasLockedSites || !a.InnerRoute.HasLockedSites);
                if (outermostAdjacency == null) break;

                var sourceRoute = !outermostAdjacency.OuterRoute.HasLockedSites ?
                    outermostAdjacency.OuterRoute : outermostAdjacency.InnerRoute;
                var targetRoute = !outermostAdjacency.OuterRoute.HasLockedSites ?
                    outermostAdjacency.InnerRoute : outermostAdjacency.OuterRoute;
                var sites = sourceRoute.Sites.ToList();
                sites.ForEach(s =>
                {
                    targetRoute.AddSite(s);
                });
                RemoveEmptyRoutes();
                AdjustAdjacencies();
            }
            while (Routes.Count < numberOfRoutes)
            {
                Routes.ForEach(r => r.UpdateCentroid());
                RouteAdjacencies.ForEach(adj => adj.UpdateDistances());
                var largestRoute = Routes
                    .Where(r => r.Sites.Count > 4 && !r.IsOverCapacitySitesRoute)
                    .OrderByDescending(r => r.AverageUnlockedLoadPercentage())
                    .First();
                var newRoute = SplitRoute(largestRoute);
                // NN Reassignment
                // largestRoute.Sites.Select(s => s).ToList().ForEach(s =>
                // {
                //     var point = new Point { Latitude = s.Latitude, Longitude = s.Longitude };
                //     var distanceToCurrentRoute = GeospatialFormula.Distance(point, largestRoute.Centroid);
                //     var distanceToNewRoute = GeospatialFormula.Distance(point, newRoute.Centroid);
                //     if (distanceToNewRoute < distanceToCurrentRoute)
                //     {
                //         newRoute.AddSite(s);
                //     }
                // });
                if (enableNNReassignment)
                {
                    var sites = Routes.SelectMany(r => r.UnlockedSites).ToList();
                    AssignToNearestRoute(sites, Routes);
                }
                RemoveEmptyRoutes();
                AdjustAdjacencies();
            }
            Routes.ForEach(r =>
            {
                for (var i = 0; i < r.NumCapacityConstraints; i++)
                {
                    //r.MaxCapacity[i] = 1e9;
                }
            });
            AdjustAdjacencies();
            AdjustTargetCapacities();
        }


        /// <summary>
        /// Solve the spider leg problem
        /// </summary>
        /// <returns>
        /// The routes after transferring the sites using the spider leg algorithm.
        /// </returns>
        public List<Route> Solve()
        {
            var sw = new Stopwatch();
            sw.Start();

            Initialize();

            CurrentCapacityIncrement = 0.0;

            for (var it = 0; it < MaxIterations; it++)
            {
                Logger.Information("========== Iteration #{@i} ==========", it);


                // Prepare model state before iteration
                // Get all current sites in the system into a hash
                var sites = new HashSet<String>();
                foreach (Route route in Routes)
                {
                    foreach (Site site in route.Sites)
                        sites.Add(site.Id);
                }

                PrepareIteration(it);

                // Process each pair of adjacent routes and count the number of transfers
                var numTransfers = RouteAdjacencies.Sum(ProcessAdjacency);
                //updateSArrOrder();
                //updateSTrucks();
                //updateRouteTimeAndDistance();

                Logger.Information("Iteration {@iteration} - Num transfers {@numTransfers} - Num routes {@numRoutes}",
                    it, numTransfers, Routes.Count);

                // Sites were transferred from one route to another 
                if (numTransfers > 0)
                {
                    var noOverCapacityRoutes = !Routes.Any(r => r.IsOverCapacity && !r.IsOverCapacitySitesRoute);
                    // Check for repeating configurations and break if necessary
                    PerformIterationSignatureCheck();
                    if (ShouldBreakIterationsDueToRepeatedSignatures)
                    {
                        if (!noOverCapacityRoutes)
                        {
                            continue;
                        } else
                        {
                        break;
                    }

                    }

                    // Packing, no overcapacity routes - continue iterating until stabilized
                    if (noOverCapacityRoutes && !IsBalanceEnabled)
                    {
                        continue;
                    }
                }

                // Verify if there is an over-capacity route left
                var overCapacityRoute = IsBalanceEnabled
                    ? Routes.FirstOrDefault(r => r.IsOverCapacity && !r.IsOverCapacitySitesRoute)
                    : Routes.FirstOrDefault(r => r.IsOverMaxCapacity && !r.IsOverCapacitySitesRoute);
                if (overCapacityRoute == null) break;

                // If there is an over-capacity route, check if it is possible to increase capacity.
                // Should break route if increasing the balance capacity will not help
                // (it is not a limiting capacity):
                var shouldBreakRoute = numTransfers == 0 && !IsLimitingCapacity(BalanceParameterIndex ?? 0);
                if (IsBalanceEnabled && !shouldBreakRoute)
                {
                    var increased = TryIncreaseTargetCapacityOfRoutes();
                    if (increased)
                    {
                        continue;
                    }
                    // TODO: Check if this is necessary
                    if (Routes.All(r => !r.IsOverMaxCapacity))
                    {
                        Logger.Information("Cannot increase balance capacity. But all routes are feasible!");
                        break;
                    }
                }

                // If it is not possible to increase the route capacities,
                // try to split the over-capacity route
                Logger.Information("Break over capacity route R{@Route}", overCapacityRoute.Id);
                RemoveEmptyRoutes();
                Routes.ForEach(r => r.UpdateCentroid());
                RouteAdjacencies.ForEach(adj => adj.UpdateDistances());
                var newRoute = SplitRoute(overCapacityRoute);

                // If it was not possible to split the route, then stop iterating, nothing else to do
                if (newRoute == null) break;

                // Otherwise, reset the capacity increment and continue iterating
                CurrentCapacityIncrement = 0;
            }

            // Adjust adjacencies if necessary, before returning
            if (IsAutoAdjustAdjacenciesEnabled)
            {
                AdjustAdjacencies(true);
            }
            RemoveEmptyRoutes(checkFeasibility: true);

            sw.Stop();


            Logger.Information("========================================");
            Logger.Information("Total stage time {time} ms", sw.ElapsedMilliseconds);
            Logger.Information("========================================");
            updateSArrOrder();
            updateSTrucks(false);
            updateRouteTimeAndDistance(null,true);
            return Routes;
        }

        //var routeIds = routesToEvaluate.Select(r => r.Id).ToArray();

        /// <summary>
        /// Prepares the model state at the beginning of an iteration
        /// </summary>
        /// <param name="iterationNumber"></param>
        public void PrepareIteration(int iterationNumber)
        {
            RemoveEmptyRoutes();

            AutoAdjustAdjacencies();

            Routes.ForEach(r => r.UpdateCentroid());

            RouteAdjacencies.OrderBy(a => a.Sequence).ToList().ForEach(a => a.UpdateDistances());

            // Update service time constraints every N iterations if max total time is specified
            if (MaxTotalTimeH != null)
            {
                UpdateServiceTimeConstraints(Routes, filterRoutes: iterationNumber > 0, maxCapacityOnly: IsBalanceEnabled);
                    AdjustTargetCapacities(CurrentCapacityIncrement);

                }

            ClearLimitingCapacitiesIndices();
        }



        /// <summary>
        /// Initialize the variables
        /// </summary>
        /// <summary>
        /// Returns true if balancing should not be applied
        /// </summary>
        /// <returns></returns>
        public bool ShouldSkipBalance()
        {
            if (Routes.Count == 0) return true;

            // Return false if there are over-capacity routes
            if (Routes.Any(r => r.IsOverMaxCapacity && !r.IsOverCapacitySitesRoute)) return false;

            for (var i = 0; i < Routes.First().NumCapacityConstraints; i++)
            {
                // Return true if there is any load that is over 90% in every route 
                if (Routes.All(r => r.Load[i] / r.MaxCapacity[i] > 0.9))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Initialize the variables
        /// </summary>
        private void Initialize()
        {

            // If the max capacity is not set, copy the capacity
            Routes.ForEach(r =>
            {
                r.Sites.ForEach(s => s.Route = r);
                if (r.TargetCapacity == null || r.TargetCapacity.Count != r.Capacity.Count)
                {
                    r.TargetCapacity = r.Capacity.Select(c => c).ToList();
                }
                if (r.MaxCapacity == null || r.MaxCapacity.Count != r.TargetCapacity.Count)
                {
                    r.MaxCapacity = r.TargetCapacity.Select(c => c).ToList();
                }
                r.UpdateCentroid();
            });

            AssignToNearestRoute(UnroutedSites, Routes);
            UnroutedSites = new List<Site>();

            if (EnableAxisSplit)
            {
                ComputeSitesXY(Routes.SelectMany(r => r.Sites).ToList());
            }

            // Keep a copy of the original routes
            if (OriginalRoutes.Count == 0)
            {
                OriginalRoutes = Routes.Select(r => new Route
                {
                    Id = r.Id,
                    Capacity = r.Capacity.Select(c => c).ToList(),
                    TargetCapacity = r.TargetCapacity.Select(c => c).ToList(),
                    MaxCapacity = r.MaxCapacity.Select(c => c).ToList(),
                    TruckTypeId = r.TruckTypeId,
                }).ToList();
                Routes.ForEach(r =>
                {
                    var originalRoute = OriginalRoutes.Find(or => or.Id == r.Id);
                    r.Sites.ForEach(s => s.OriginalRoute = originalRoute);
                });
            }

            // For balance problems, make sure that the over-capacity customers are isolated from the beginning,
            // so that the capacities are correctly adjusted
            if (IsBalanceEnabled)
            {
                if (IsAutoAdjustAdjacenciesEnabled)
                {
                    AdjustAdjacencies();
                }

                Routes.ForEach(r =>
                {
                    // Detect over-capacity sites
                    var sitesOverMaxCapacity = r.Sites
                        .Where(s => s.Load.Where((t, i) => t > r.MaxCapacity[i]).Any())
                        .ToList();
                    if (!sitesOverMaxCapacity.Any()) return;

                    // Keep the over-capacity sites in their original routes,
                    // and move the rest of the sites to an adjacent route
                    var adjacency = RouteAdjacencies.FirstOrDefault(adj => adj.OuterRoute == r)
                                    ?? RouteAdjacencies.FirstOrDefault(adj => adj.InnerRoute == r);
                    r.UnlockedSites.Where(s => !sitesOverMaxCapacity.Contains(s)).ToList().ForEach(s =>
                    {
                        adjacency?.TransferSite(s);
                    });
                });

                // Capture the number of routes
                NumberOfRoutesBeforeBalance = Routes.Count;
            }

            IterationSignatures = new List<string>();
            AllowedSkipAdjacencyAdjustments = 3;
            SkipNextAdjacencyAdjustment = false;
            UpdateServiceTimeConstraints(Routes, filterRoutes: false, maxCapacityOnly: IsBalanceEnabled);
            AdjustTargetCapacities();

            if (TargetNumberOfRoutes > 0)
            {
                AdjustNumberOfRoutes(TargetNumberOfRoutes);
            }
        }

        private void RemoveEmptyRoutes(bool checkFeasibility = false)
        {
            Routes = Routes.Where(r => r.Sites.Count > 0).ToList();
            RouteAdjacencies = RouteAdjacencies.Where(ra => ra.InnerRoute.Sites.Count > 0 && ra.OuterRoute.Sites.Count > 0).ToList();

            var availableRoutes = OriginalRoutes
                .Where(or => !Routes.Select(r => r.Id).Contains(or.Id))
                .ToList();
            var routesToUpdateServiceTimeConstraints = new List<Route>();
            Routes.Where(r => r.Id.StartsWith("ADAPT-X")).ToList().ForEach((r =>
            {
                if (availableRoutes.Count == 0) return;
                var match = availableRoutes
                    .OrderByDescending(or => r.Sites.Count(s => s.OriginalRoute == or))
                    .First();
                var tempCapacity = r.MaxCapacity;
                r.MaxCapacity = match.MaxCapacity.Select(val => val).ToList();
                if (!checkFeasibility || !r.IsOverMaxCapacityExcludeIndex(ServiceTimeHParameterIndex))
                {
                    r.Id = match.Id;
                    r.TruckTypeId = match.TruckTypeId;
                    availableRoutes.Remove(match);
                    if (MaxTotalTimeH != null)
                    {
                        routesToUpdateServiceTimeConstraints.Add(r);
                    }
                    return;
                }

                r.MaxCapacity = tempCapacity;
            }));
            UpdateServiceTimeConstraints(routesToUpdateServiceTimeConstraints, false);
        }

        /// <summary>
        /// Processes adjacent routes by moving sites between the inner and 
        /// the outer route, so that the outer route is as close as possible
        /// to full capacity.
        /// </summary>
        /// <returns>The adjacency.</returns>
        /// <param name="adjacency">Adjacency.</param>
        private int ProcessAdjacency(RouteAdjacency adjacency)
        {
            //Routes.ForEach(r => r.UpdateCenterOfMass());
            //RouteAdjacencies.ForEach(adj => adj.UpdateDistances());

            Logger.Debug("Processing R{@OuterRoute} -> R{@InnerRoute}",
                adjacency.OuterRoute.Id, adjacency.InnerRoute.Id);

            if (adjacency.OuterRoute.IsUnderCapacity)
            {
                return ProcessUnderCapacityOuterRoute(adjacency);
            }

            if (adjacency.OuterRoute.IsOverCapacity)
            {
                return ProcessOverCapacityOuterRoute(adjacency);
            }

            return 0;
        }

        /// <summary>
        /// Processes an under capacity outer route by moving sites from the inner route
        /// into the outer route
        /// </summary>
        /// <returns>The number of transferred sites.</returns>
        /// <param name="adjacency">Adjacency.</param>
        private int ProcessUnderCapacityOuterRoute(RouteAdjacency adjacency)
        {
            var numTransfers = 0;
            var innerRoute = adjacency.InnerRoute;
            var outerRoute = adjacency.OuterRoute;

            while (outerRoute.IsUnderCapacity && innerRoute.Sites.Count > 0)
            {
                Logger.Debug("Outer route {@Route} is under capacity {@AvailableCapacity}",
                    outerRoute.Id, outerRoute.AvailableCapacity);

                //var transferCandidateIds = GetTransferCandidatesByAffinity(innerRoute, outerRoute);
                var transferCandidateIds = GetTransferCandidatesFromInnerRoute(adjacency);
                bool transferred = false;

                foreach (var candidateId in transferCandidateIds)
                {
                    var candidate = innerRoute.Sites.Find(s => s.Id == candidateId);
                    var willEmptyBalanceRoute = innerRoute.Sites.Count == 1 && IsBalanceEnabled;
                    if (candidate != null && adjacency.OuterRoute.CanAddSite(candidate) && !willEmptyBalanceRoute)
                    {
                        Logger.Debug("(+) Site {@Site} ({@Load}) moved to outer route R{@OuterRoute}",
                            candidateId, candidate.Load, outerRoute.Id);

                        adjacency.TransferSite(candidate);
                        transferred = true;
                        numTransfers++;
                        break;
                    }
                    if (candidate != null)
                    {
                        var indices = adjacency.OuterRoute.GetExceededCapacityIndicesOnAddSite(candidate);
                        LimitingCapacitiesIndices.AddRange(indices);
                    }

                    Logger.Debug("Site {@Site} ({@Load}) kept in inner route R{@InnerRoute}",
                        candidateId, candidate?.Load, innerRoute.Id);
                }

                if (!transferred)
                {
                    Logger.Debug("No sites transferred.");
                    break;
                }
            }

            return numTransfers;
        }

        /// <summary>
        /// Processes an overcapacity outer route by moving sites into the inner route.
        /// </summary>
        /// <returns>The number of transferred sites.</returns>
        /// <param name="adjacency">Adjacency.</param>
        private int ProcessOverCapacityOuterRoute(RouteAdjacency adjacency)
        {
            var numTransfers = 0;
            var innerRoute = adjacency.InnerRoute;
            var outerRoute = adjacency.OuterRoute;

            while (outerRoute.IsOverCapacity && outerRoute.Sites.Count > 0)
            {
                Logger.Debug("Outer route R{@Route} is over capacity {@AvailableCapacity}",
                    outerRoute.Id, outerRoute.AvailableCapacity);

                //var transferCandidateIds = GetTransferCandidatesByAffinity(outerRoute, innerRoute);
                var transferCandidateIds = GetTransferCandidatesFromOuterRoute(adjacency);
                bool transferred = false;

                foreach (var candidateId in transferCandidateIds)
                {
                    var candidate = outerRoute.Sites.Find(s => s.Id == candidateId);
                    var willEmptyBalanceRoute = outerRoute.Sites.Count == 1 && IsBalanceEnabled;
                    if (candidate != null && !willEmptyBalanceRoute)
                    {
                        Logger.Debug("(-) Site {@Site} ({@Load}) moved to inner route R{@InnerRoute}",
                            candidateId, candidate.Load, innerRoute.Id);

                        adjacency.TransferSite(candidate);
                        transferred = true;
                        numTransfers++;
                        break;
                    }
                }

                if (!transferred)
                {
                    Logger.Debug("No sites transferred.");
                    break;
                }
            }

            return numTransfers;
        }

        private IEnumerable<string> GetTransferCandidatesFromOuterRoute(RouteAdjacency adjacency)
        {
            var unlockedSiteIds = adjacency.OuterRoute.UnlockedSites.Select(s => s.Id);
            return adjacency
                .OuterRouteSiteIdsSortedByDistance
                .Where(id => unlockedSiteIds.Contains(id))
                .Take(NClosestTransferCandidates)
                .ToList();
        }

        private IEnumerable<string> GetTransferCandidatesFromInnerRoute(RouteAdjacency adjacency)
        {
            var unlockedSiteIds = adjacency.InnerRoute.UnlockedSites.Select(s => s.Id);
            return adjacency
                .InnerRouteSiteIdsSortedByDistance
                .Where(id => unlockedSiteIds.Contains(id))
                .Take(NClosestTransferCandidates)
                .ToList();
        }

        private IEnumerable<string> GetTransferCandidatesByAffinity(Route sourceRoute, Route targetRoute)
        {
            var affinityScores = new Dictionary<string, double[]>();

            // Initialize
            sourceRoute.Sites.ForEach(s =>
            {
                affinityScores[s.Id] = new[] { 0.0, 0.0 };
            });

            // Affinity with self
            sourceRoute.Sites.ForEach(s1 =>
            {
                var score = 0.0;
                sourceRoute.Sites.ForEach(s2 =>
                {
                    var d = s1 == s2 ? double.PositiveInfinity :
                        MasterTransitMatrix.GetUndirectedDistanceBetween(s1.Id, s2.Id) * 1000.0;
                    score += Math.Exp(-d / 200.0);
                });
                affinityScores[s1.Id][0] = score;
            });

            // Affinity with adjacent route
            sourceRoute.Sites.ForEach(s1 =>
            {
                var score = 0.0;
                targetRoute.Sites.ForEach(s2 =>
                {
                    var d = MasterTransitMatrix.GetUndirectedDistanceBetween(s1.Id, s2.Id) * 1000.0;
                    score += Math.Exp(-d / 200.0);
                });
                affinityScores[s1.Id][1] = score;
            });

            var candidates = affinityScores
                .OrderByDescending(kvp => kvp.Value[1])
                .Take(NClosestTransferCandidates);
            return candidates
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// Returns the fraction (or percentage) of the route that would be loaded
        /// if the specified load could be balanced perfectly. 
        /// </summary>
        /// <param name="loadIndex"></param>
        /// <returns></returns>
        private double GetIdealBalanceLoadFraction(int loadIndex)
        {
            var totalCapacity = Routes
                .Where(r => r.Sites.Count > 0 && !r.IsOverCapacitySitesRoute)
                .Select(r => r.MaxCapacity[loadIndex])
                .Sum();
            var totalLoad = Routes
                .Where(r => r.Sites.Count > 0 && !r.IsOverCapacitySitesRoute)
                .Select(r => r.Load[loadIndex])
                .Sum();
            return totalLoad / totalCapacity;
        }

        private bool TryIncreaseTargetCapacityOfRoutes()
        {
            // Can increase capacity if any capacity is under the maximum
            var canIncrease = IsAnyTargetCapacityLessThanMaxCapacity();
            if (!canIncrease) return false;
            CurrentCapacityIncrement = ComputeNewCapacityIncrement(CurrentCapacityIncrement, BalanceParameterIndex ?? 0);
            Logger.Information($"Increase capacity {CurrentCapacityIncrement}");
            AdjustTargetCapacities(CurrentCapacityIncrement);
            return true;
        }

        /// <summary>
        /// Adjusts the route capacities.
        /// If the algorithm is packing (balancing not enabled), then the route capacities
        /// are set to the max capacity.
        /// If the algorithm is balancing (balancing enabled), then the route capacities
        /// are set to a fraction of the max capacity such that their sum is larger than or equal
        /// to the total load. For example, if there are 2 routes of max 50 cases each,
        /// and there are 60 cases to be routed, the routes will be set to a capacity of 30 cases each,
        /// so that they are balanced when full. This constraint can be relaxed by providing a
        /// capacityIncrementFraction > 0, to allow for variations that prevent the load from being
        /// perfectly balanced (e.g. 32 cases in one route and 28 in another).
        /// </summary>
        /// <param name="capacityIncrementFraction"></param>
        private void AdjustTargetCapacities(double capacityIncrementFraction = 0)
        {
            if (!IsBalanceEnabled)
            {
                Routes.ForEach(r => r.TargetCapacity = r.MaxCapacity.Select(c => c).ToList());
                return;
            }

            var totalCapacity = Routes
                .Where(r => r.Sites.Count > 0 && !r.IsOverCapacitySitesRoute)
                .Select(r => r.MaxCapacity)
                .SumElements();
            var totalLoad = Routes
                .Where(r => r.Sites.Count > 0 && !r.IsOverCapacitySitesRoute)
                .Select(r => r.Load)
                .SumElements();

            if (BalanceParameterIndex == null)
            {
                var loadPercentage = 0.0;
                var selectedIdx = 0;
                for (var i = 0; i < totalCapacity.Count; i++)
                {
                    if (totalCapacity[i] == 0) continue;
                    var p = totalLoad[i] / totalCapacity[i];
                    if (p > loadPercentage)
                    {
                        loadPercentage = p;
                        selectedIdx = i;
                    }
                }
                BalanceParameterIndex = selectedIdx;
            }

            var idx = BalanceParameterIndex ?? 0;
            if (capacityIncrementFraction < 1e-12)
            {
                var p = Math.Min(1, totalLoad[idx] / totalCapacity[idx]);
                Routes.ForEach(r =>
                {
                    r.TargetCapacity = r.MaxCapacity.Select(c => c).ToList();
                    r.TargetCapacity[idx] = p * r.MaxCapacity[idx];
                });
            }
            else
            {
                var i = 0;
                var ideal = GetIdealBalanceLoadFraction(idx);
                Routes
                    .Where(r => r.TargetCapacity[idx] / r.MaxCapacity[idx] < ideal * (1 + capacityIncrementFraction))
                    .OrderBy(r => r.TargetCapacity[idx] / r.MaxCapacity[idx])
                    .ToList().ForEach(r =>
                    {
                        var oldCapacity = r.TargetCapacity[idx];
                        var p = (1 + capacityIncrementFraction) * totalLoad[idx] / totalCapacity[idx];
                        var newCapacity1 = p * r.MaxCapacity[idx];
                        var newCapacity2 = r.TargetCapacity[idx] + DetermineMinimumCapacityIncrease(idx);
                        r.TargetCapacity = r.MaxCapacity.Select(c => c).ToList();
                        if (i <= Math.Ceiling(Routes.Count / 4.0))
                        {
                            r.TargetCapacity[idx] = Math.Min(r.MaxCapacity[idx], Math.Max(newCapacity1, newCapacity2));
                        }
                        else
                        {
                            r.TargetCapacity[idx] = oldCapacity;
                        }

                        i++;
                    });
            }

            LastBalanceParameterIndex = idx;
        }

        /// <summary>
        /// Determines the minimum capacity increase, based on the input loads
        /// </summary>
        /// <param name="parameterIndex"></param>
        /// <returns></returns>
        private double DetermineMinimumCapacityIncrease(int parameterIndex)
        {
            var distinctValues = Routes
                .SelectMany(r => r.Sites.Select(s => s.Load[parameterIndex]))
                .Distinct()
                .ToList();
            if (distinctValues.Count == 1) return distinctValues.First();
            return 0;
        }

        /// <summary>
        /// Loops through all routes and returns true if any target capacity is
        /// less than the max capacity.
        /// </summary>
        /// <returns></returns>
        private bool IsAnyTargetCapacityLessThanMaxCapacity()
        {
            foreach (var route in Routes)
            {
                for (var i = 0; i < route.NumCapacityConstraints; i++)
                {
                    if (route.TargetCapacity[i] < route.MaxCapacity[i])
                    {
                        return true;
                    }
                }
            };
            return false;
        }

        private bool IsLimitingCapacity(int balanceParameterIndex)
        {
            return LimitingCapacitiesIndices.Contains(balanceParameterIndex);
        }

        private double ComputeNewCapacityIncrement(double capacityIncrement, int balanceParameterIndex)
        {
            var idx = balanceParameterIndex;
            var ideal = GetIdealBalanceLoadFraction(idx);
            if (Routes.All(r => r.TargetCapacity[idx] / r.MaxCapacity[idx] >= ideal * (1 + capacityIncrement) - 1e-12))
            {
                capacityIncrement +=
                    Math.Max(Routes.Count, 1.0) / Routes.SelectMany(r => r.Sites).Count();
            }
            return capacityIncrement;
        }

        /// <summary>
        /// Spawns a new route from an existing one, by transferring a site that
        /// is near the boundary of the route, opposite to an adjacency of the route (if any). 
        /// </summary>
        /// <param name="route"></param>
        /// <returns></returns>
        private Route SplitRoute(Route route)
        {
            var siteToTransfer = EnableAxisSplit ?
                GetSplitCandidateOnMajorAxis(route) : GetSplitCandidate(route);

            if (siteToTransfer == null)
            {
                return null;
            }

            var newRoute = CreateRoute(siteToTransfer, route);
            var newAdjacency = new RouteAdjacency
            {
                InnerRoute = newRoute,
                OuterRoute = route,
            };
            newRoute.Adjacencies.Add(newAdjacency);
            Routes.Add(newRoute);
            RouteAdjacencies.Add(newAdjacency);
            newAdjacency.TransferSite(siteToTransfer);
            newRoute.UpdateCentroid();
            route.UpdateCentroid();
            newAdjacency.UpdateDistances();
            AdjustTargetCapacities();
            return newRoute;
        }

        private Site GetSplitCandidate(Route route)
        {
            var unlockedSiteIds = Routes.SelectMany(r => r.UnlockedSites).Select(s => s.Id).ToList();
            var farthestSiteIdByAdjacency = RouteAdjacencies
                .FirstOrDefault(adj => adj.InnerRoute == route)?
                .InnerRouteSiteIdsSortedByDistance
                .LastOrDefault(id => unlockedSiteIds.Contains(id));
            var farthestSiteIdByLatLng = route.UnlockedSites
                .OrderBy(s => s.Latitude * 1000 + s.Longitude)
                .LastOrDefault()
                ?.Id;
            var largeSites = route.GetLargeSites(0.8)
                .Where(s => !s.IsLockedToRoute)
                .ToList();
            Site siteToTransfer;
            if (largeSites.Count > 0)
            {
                siteToTransfer = largeSites.First();
            }
            else
            {
                var farthestSiteId = farthestSiteIdByAdjacency ?? farthestSiteIdByLatLng;
                if (farthestSiteId == null) return null;
                siteToTransfer = route.Sites.Find(s => s.Id == farthestSiteId);
            }
            return siteToTransfer;
        }

        private Site GetSplitCandidateOnMajorAxis(Route route)
        {
            var sites = route.UnlockedSites.ToList();
            if (sites.Count == 0) return null;

            var x = sites.Select(s => SiteXYs[s.Id].X).ToList();
            var y = sites.Select(s => SiteXYs[s.Id].Y).ToList();
            var w = sites.Select(s => 1.0).ToList();
            var moment = new ImageMoment(x, y);
            var angle = moment.GetAngle();
            var vy = Math.Tan(angle);
            var vx = 1;
            var avgX = moment.AvgX;
            var avgY = moment.AvgY;
            return sites.OrderByDescending(site =>
            {
                var dx = SiteXYs[site.Id].X - avgX;
                var dy = SiteXYs[site.Id].Y - avgY;
                return Math.Abs(dx * vx + dy * vy);
            }).FirstOrDefault();
        }

        private Route CreateRoute(Site site, Route route)
        {
            var availableRoutes = OriginalRoutes
                .Where(or => !Routes.Select(r => r.Id).Contains(or.Id))
                .ToList();
            var availableRoute = availableRoutes.Contains(site.OriginalRoute)
                ? site.OriginalRoute
                : availableRoutes.FirstOrDefault();
            var newRoute = new Route
            {
                Id = availableRoute?.Id ?? GetNewRouteId(),
                TruckTypeId = availableRoute?.TruckTypeId ?? route.TruckTypeId,
                Capacity = (availableRoute?.Capacity ?? route.Capacity).Select(val => val).ToList(),
                TargetCapacity = (availableRoute?.TargetCapacity ?? route.TargetCapacity).Select(val => val).ToList(),
                MaxCapacity = (availableRoute?.MaxCapacity ?? route.MaxCapacity).Select(val => val).ToList(),
                Sites = new List<Site>(),
                Adjacencies = new List<RouteAdjacency>(),
                RequiresDriveTimeUpdate = true
            };
            return newRoute;
        }

        public async Task AutoAdjustAdjacencies()
        {
            if (IsAutoAdjustAdjacenciesEnabled)
            {
                if (SkipNextAdjacencyAdjustment)
                {
                    AllowedSkipAdjacencyAdjustments--;
                    SkipNextAdjacencyAdjustment = false;
                }
                else
                {
                    AdjustAdjacencies();
                }
            }
        }

        /// <summary>
        /// Recomputes the adjacencies.
        /// </summary>
        /// <returns></returns>
        public void AdjustAdjacencies(bool includeSingleOverCapacity = false)
        {
            if (Base != null)
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();
                var distancesToBase = EstimateRouteDistancesToBase();
                distancesToBase.Add(0); // base to base
                Logger.Information("Retrieved hull distances to base in {time} ms", sw.ElapsedMilliseconds);
                // Note: we are intentionally providing the distance to the base in meters instead of 
                // kilometers, in order to force the shortest path tree to go through the route
                // that is closest to the base
                sw.Restart();
                if (!includeSingleOverCapacity)
                {
                    var workingRoutes = Routes.Where(r => !r.IsOverCapacitySitesRoute).ToList();
                    RouteAdjacencies =
                        SpiderLegBuilder.Build(workingRoutes, Base, distancesToBase, MasterTransitMatrix);
                }
                else
                {
                    RouteAdjacencies = SpiderLegBuilder.Build(Routes, Base, distancesToBase, MasterTransitMatrix);
                }
                Logger.Information("Built spider leg in {time} ms", sw.ElapsedMilliseconds);

                RouteAdjacencies.ForEach(a =>
                {
                    // a.UpdateDistances();
                });
            }
        }

        public void AssignToNearestRoute(List<Site> sites, List<Route> routes)
        {
            sites?.ForEach(s =>
            {
                var point = new Point { Latitude = s.Latitude, Longitude = s.Longitude };
                var closestRoute = routes.OrderBy(r => GeospatialFormula.Distance(point, r.Centroid)).First();
                closestRoute.AddSite(s);
            });
        }

        private void ClearLimitingCapacitiesIndices()
        {
            LimitingCapacitiesIndices = new List<int>();
        }

        public void AssignBestOriginalRoute()
        {
            var assignedRoutes = new List<Route>();
            var assignedOriginalRoutes = new List<Route>();
            var assignments = new List<KeyValuePair<Route, (Route, double)>>();

            // Compute assignments
            Routes.ToList().ForEach(route =>
            {
                OriginalRoutes.ForEach(originalRoute =>
                {
                    var score = route.Sites.Count(s => s.OriginalRoute == originalRoute);
                    assignments.Add(new KeyValuePair<Route, (Route, double)>(route, (originalRoute, score)));
                });
            });

            // Process assignments
            assignments.OrderByDescending(kvp => kvp.Value.Item2).ToList().ForEach(asg =>
            {
                var route = asg.Key;
                var originalRoute = asg.Value.Item1;
                var otherRoute = Routes.FirstOrDefault(r => r.Id == originalRoute.Id);
                var score = asg.Value.Item2;

                // Skip if already processed before
                if (assignedRoutes.Contains(route) || assignedOriginalRoutes.Contains(originalRoute)) return;

                // Try swap route identities
                var tempMaxCapacity = route.MaxCapacity;
                var tempCapacity = route.Capacity;
                route.MaxCapacity = originalRoute.MaxCapacity;
                route.Capacity = originalRoute.Capacity;
                var canExchange = !route.IsOverMaxCapacityExcludeIndex(ServiceTimeHParameterIndex);
                if (otherRoute != null)
                {
                    otherRoute.MaxCapacity = tempMaxCapacity;
                    otherRoute.Capacity = tempCapacity;
                    canExchange &= !otherRoute.IsOverMaxCapacityExcludeIndex(ServiceTimeHParameterIndex);
                }

                canExchange &= !route.HasLockedSites && (otherRoute == null || !otherRoute.HasLockedSites);

                if (canExchange)
                {
                    // Exchange identities
                    var tempId = route.Id;
                    var tempTruckTypeId = route.TruckTypeId;
                    route.Id = originalRoute.Id;
                    route.TruckTypeId = originalRoute.TruckTypeId;
                    if (otherRoute != null)
                    {
                        otherRoute.Id = tempId;
                        otherRoute.TruckTypeId = tempTruckTypeId;
                    }
                    // Record as assigned
                    assignedRoutes.Add(route);
                    assignedOriginalRoutes.Add(originalRoute);
                    Logger.Information("Relabel: {r} => {or} (score: {s})", tempId, route.Id, score);
                }
                else
                {
                    // Restore
                    if (otherRoute != null)
                    {
                        otherRoute.MaxCapacity = route.MaxCapacity;
                        otherRoute.Capacity = route.Capacity;
                    }
                    route.MaxCapacity = tempMaxCapacity;
                    route.Capacity = tempCapacity;
                }
            });

        }

        private void UpdateServiceTimeConstraints(List<Route> routes, bool filterRoutes = true, bool maxCapacityOnly = true)
        {
            if (MaxTotalTimeH == null || ServiceTimeHParameterIndex == null) return;

            var stopWatch = Stopwatch.StartNew();
            var idx = ServiceTimeHParameterIndex ?? 0;
            var routesToEvaluate = !filterRoutes
                ? routes
                : routes
                    .Where(r => r.Sites.Count > 0)
                    .Where(r => (r.Load[idx] / r.MaxCapacity[idx] > 0.8) || r.RequiresDriveTimeUpdate)
                    .ToList();
            ///The evaluator is no longer needed, since routesequencer is getting the actual times distances in every iteration.
            ///
            var routeIds = routesToEvaluate.Select(r => r.Id).ToArray();
            if (routeIds.Count() > 0)
            {
                Logger.Information($"Before updates Arr: {stopWatch.ElapsedMilliseconds} ms");
            updateSArrOrder();
                Logger.Information($"Before updates Srr: {stopWatch.ElapsedMilliseconds} ms");
                updateSTrucks(false);
                Logger.Information($"Before EValuate: {stopWatch.ElapsedMilliseconds} ms");
                updateRouteTimeAndDistance(routeIds, false);
                Logger.Information($"After EValuate: {stopWatch.ElapsedMilliseconds} ms");

            routes.ForEach(route =>
            {
                var newCapacity = Math.Min(
                    MaxServiceTimeH ?? 1e3,
                        Math.Max(MaxTotalTimeH[route.Id] - route.DriveTimeH - 0.1, 1)
                );
                route.MaxCapacity[idx] = newCapacity;
                if (!maxCapacityOnly)
                {
                    route.Capacity[idx] = newCapacity;
                }

                route.RequiresDriveTimeUpdate = false;
            });
            Logger.Information($"Evaluation time: {stopWatch.ElapsedMilliseconds} ms");
        }
        

        }
        
        /// <summary>
        /// Returns a distance (in meters) estimate from each route to the base
        /// </summary>
        /// <returns></returns>
        private List<double> EstimateRouteDistancesToBase()
        {
            var routeIdx = 0;
            var routeIndices = new List<int>();
            var allRouteReferencePoints = new List<Point>();
            var convexHullPoints = new List<List<Point>>();
            var basePoint = new Point() {Latitude = Base.Latitude, Longitude = Base.Longitude};
            Routes.ForEach(r =>
            {
                var points = r.Sites
                    .Select(s => new Point {Latitude = s.Latitude, Longitude = s.Longitude})
                    .ToList();
                var pointsToAdd = r.Sites.Count <= 2 ? points : ConvexHull.GetConvexHull(points);
                convexHullPoints.Add(pointsToAdd);
                pointsToAdd.ForEach(p =>
                {
                    routeIndices.Add(routeIdx);
                    allRouteReferencePoints.Add(p);
                });
                routeIdx++;
            });
            allRouteReferencePoints.Add(basePoint);

            //if (_countOsrmFailures > 5)
            //{
            return EstimateDirectRouteDistancesToBase(convexHullPoints, basePoint);
            //}
            
            //try
            //{
            //    var destinationsIdx = new List<int> {allRouteReferencePoints.Count - 1};
            //    var tm = await OsrmService.GetTable(allRouteReferencePoints, null, destinationsIdx);
            //    var distanceByRoute = Routes.Select(r => 1e9).ToList();
            //    var distances = new List<double>();
            //    for (var i = 0; i < tm.Distances.Count - 1; i++)
            //    {
            //        distances.Add(tm.Distances[i][0]);
            //        if (i + 1 == tm.Distances.Count - 1 || routeIndices[i] != routeIndices[i + 1])
            //        {
            //            distanceByRoute[routeIndices[i]] = distances.Average();
            //            distances = new List<double>();
            //        }
            //    }
            //    return distanceByRoute;
            //}
            //catch (Exception e)
            //{
            //    _countOsrmFailures++;
            //    return EstimateDirectRouteDistancesToBase(convexHullPoints, basePoint);
            //}

        }

        private List<double> EstimateDirectRouteDistancesToBase(List<List<Point>> convexHullPoints, Point basePoint)
        {
            var distances = new List<double>();
            convexHullPoints.ForEach(points =>
            {
                var distance = 0.0;
                points.ForEach(point =>
                {
                    distance += GeospatialFormula.Distance(point, basePoint);
                });
                distances.Add(distance / points.Count);
            });
            return distances;
        }

        public void PrintState()
        {
            Routes.ForEach(r =>
            {
                Logger.Information("R{routeId}\t{t} h\tSites {sites}\tCapacity {capacity}\tLoad {load}",
                    r.Id, r.DriveTimeH, r.Sites.Count.ToString("000"), r.Capacity, r.Load);
            });
            Logger.Information("Routes: {routes}\tSites {sites}", Routes.Count, Routes.Sum(r => r.Sites.Count));
            
            Logger.Information("Adjacencies");
            RouteAdjacencies.ForEach(a =>
            {
                Logger.Information("R{outer} -> R{inner}", a.OuterRoute.Id, a.InnerRoute.Id);
            });
        }

        /// <summary>
        /// Computes a string that represents the current routes configuration
        /// (the routes, the assignment of sites to routes, and the route capacities).
        /// This is useful for detecting when the algorithm is not able to converge because
        /// it is jumping between two different states or configurations.
        /// </summary>
        /// <returns></returns>
        public string GetIterationSignature()
        {
            var orderedRoutes = Routes
                .Where(r => r.Sites.Count > 0)
                .OrderBy(r => r.Sites.First().Id);
            var routeSignatures = orderedRoutes.Select(r =>
            {
                var orderedSites = r.Sites
                    .Select(s => s.Id)
                    .OrderBy(id => id);
                var sites = string.Join(",", orderedSites);
                var capacities = string.Join(",", r.TargetCapacity);
                return $"s:{sites};c:{capacities}";
            });
            return string.Join("|", routeSignatures);
        }

        /// <summary>
        /// Check if the current route configuration has already appeared in previous iterations,
        /// if it has, then check if it can skip the next adjacency adjustment and continue.
        /// If not possible, then stop the algorithm, to prevent it from jumping between two configurations
        /// </summary>
        public void PerformIterationSignatureCheck()
        {
            // Reset flag
            ShouldBreakIterationsDueToRepeatedSignatures = false;

            // Evaluate current signature
            var currentSignature = GetIterationSignature();
            var repeatedIteration = IterationSignatures.Contains(currentSignature);
            if (repeatedIteration)
            {
                if (AllowedSkipAdjacencyAdjustments > 0)
                {
                    Logger.Information("Repeating configuration. Skip next adjustment.");
                    SkipNextAdjacencyAdjustment = true;
                }
                else
                {
                    Logger.Information("Repeating configuration. Break.");
                    ShouldBreakIterationsDueToRepeatedSignatures = true;
                }
            }
            else
            {
                IterationSignatures.Add(currentSignature);
            }
        }

        private string GetNewRouteId()
        {
            AddedRouteIdx++;
            return "ADAPT-X" + AddedRouteIdx;
        }

        private void ComputeSitesXY(List<Site> sites)
        {
            if (SiteXYs?.Keys.Count > 0) return;

            SiteXYs = new Dictionary<string, (double X, double Y)>();

            // Coordinate systems
            var cs4326 = ProjNet.CoordinateSystems.GeographicCoordinateSystem.WGS84;
            var cs3857 = ProjNet.CoordinateSystems.ProjectedCoordinateSystem.WebMercator;

            // Transformation
            var transformationFactory = new CoordinateTransformationFactory();
            var transformation = transformationFactory.CreateFromCoordinateSystems(cs4326, cs3857);

            sites.ForEach(site =>
            {
                var from = new[] { site.Longitude, site.Latitude };
                var to = transformation.MathTransform.Transform(from);
                SiteXYs[site.Id] = (to[0], to[1]);
            });
        }

        public void updateSArrOrder()
        {
            this.Routes.ForEach(r => {
                r.arrSOrders = r.Sites.SelectMany(s => s.OrderList).ToArray();
                r.numSOrders = r.arrSOrders.Count();
                });
        }

        public void updateSTrucks(bool lastUpdate)
        {
            this.sHierarchicalRouteConstruction.UpdateScilogRoutes(lastUpdate);
        }

        public void updateRouteTimeAndDistance(string[] routeIds, bool allRoutes)
        {
            ScilogInterfaces.ScilogInterfaces.ITruck[] truckArray = (ScilogInterfaces.ScilogInterfaces.ITruck[])this.sHierarchicalRouteConstruction.GetAllTrucks();
            //if (allRoutes)
            //{
            //    truckArray = (ScilogInterfaces.ScilogInterfaces.ITruck[])this.sHierarchicalRouteConstruction.RunIntraRouteImprovementAllRoutes();               
            //}
            //else
            //{
            //    truckArray = (ScilogInterfaces.ScilogInterfaces.ITruck[])this.sHierarchicalRouteConstruction.RunRouteSequencerOnSelectedRoutes(routeIds);
            //}
            /// Update the time and distance for current A routes from the information acquired by the sequencer.
            this.Routes.ForEach(r => {
                for (int i = 1; i < truckArray.Count(); i++)
                {
                    var truck = truckArray[i];
                    if (truck is null)
                    {
                        continue;
                    }

                    if (truck.szTruckID == r.Id)
                    {
                        r.DriveTimeH = truck.dTotalHours - truck.dTotalSiteVisitTimeInHours - truck.dTotalUnloadTimeInHours;
                        //r.DriveTimeH = truck.dTotalDrivingHours;
                        r.DistanceKm = truck.dTotalMiles;
                        r.ServiceTimeH = truck.dTotalSiteVisitTimeInHours + truck.dTotalUnloadTimeInHours;
                        r.TotalTimeH = truck.dTotalHours;
                    }

                }
                
            });
            

        }
    }

    public static class Helpers
    {
        public static List<double> SumElements(this IEnumerable<List<double>> vectors)
        {
            var n = vectors.ElementAt(0)?.Count;
            var sum = new List<double>();
            for (var i = 0; i < n; i++)
            {
                sum.Add(vectors.Sum(v => v[i]));
            }

            return sum;
        }

    }

}