using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using System.Web;
//using System.Web.Http;
//using UrDeliveries.Identity;
using UrDeliveries.Models.SpiderLegModels;
//using UrDeliveries.Models.TransitMatrix;
//using UrDeliveries.Services;
//using UrDeliveries.Services.Routing;
//using UrDeliveries.Utilities.Geospatial;
using Route = UrDeliveries.Models.SpiderLegModels.Route;

namespace UrDeliveries.Models.SpiderLegModels
{
    //public class SpiderLegController : BaseApiController
    //{
    //    [WebApiAuthorize]
    //    [HttpPost, Route("api/spider-leg")]
    //    public async Task<IHttpActionResult> Solve(SpiderLegParams problemParams)
    //    {
    //        HttpContext.Current.Server.ScriptTimeout = 300;

    //        var routes = problemParams.Routes
    //            .Where(r => r.Sites.Any())
    //            .ToList();
    //        problemParams.RouteAdjacencies.ForEach(adj =>
    //        {
    //            adj.Sequence = problemParams.RouteAdjacencies.IndexOf(adj);
    //        });
    //        var routeIds = routes.Select(r => r.Id);
    //        var inputAdjacencies = problemParams.RouteAdjacencies
    //            .Where(adj => routeIds.Contains(adj.InnerRouteId) && routeIds.Contains(adj.OuterRouteId))
    //            .OrderBy(adj => adj.Sequence);

    //        var sites = problemParams.Routes
    //            .SelectMany(r => r.Sites)
    //            .ToList();
    //        var allSites = sites.Select(s => s).ToList();
    //        if (problemParams.Base != null)
    //        {
    //            allSites.Add(problemParams.Base);
    //        }

    //        var transitMatrix = BuildMasterTransitMatrix(allSites, problemParams.GreatCircleDistanceOnly);
    //        var adjacencies = inputAdjacencies.Select(adj => new RouteAdjacency
    //        {
    //            InnerRoute = routes.Find(r => r.Id == adj.InnerRouteId),
    //            OuterRoute = routes.Find(r => r.Id == adj.OuterRouteId),
    //            Sequence = adj.Sequence,
    //            MasterTransitMatrix = transitMatrix
    //        }).ToList();

    //        var osrmService = new RoutingServiceSetup().CreateOSRMServiceForCompany(GetCompanyId());

    //        var spiderLegProblem = new SpiderLegProblem
    //        {
    //            Routes = routes,
    //            Max
    //            H = problemParams.MaxTotalTimeH, 
    //            MaxServiceTimeH = problemParams.MaxServiceTimeH,
    //            ServiceTimeHParameterIndex = problemParams.ServiceTimeHParameterIndex,
    //            RouteAdjacencies = adjacencies,
    //            Base = problemParams.Base,
    //            MasterTransitMatrix = transitMatrix,
    //            BalanceParameterIndex = problemParams.BalanceParameterIndex,
    //            NClosestTransferCandidates = problemParams.NClosestTransferCandidates.GetValueOrDefault(10),
    //            IsAutoAdjustAdjacenciesEnabled = problemParams.AdjustAdjacencies,
    //            MaxIterations = problemParams.MaxIterations.GetValueOrDefault(250),
    //            OsrmService = osrmService
    //        };

    //        switch (problemParams.Mode)
    //        {
    //            case "pack-and-balance":
    //                spiderLegProblem.IsBalanceEnabled = false;
    //                await spiderLegProblem.Solve();
    //                await spiderLegProblem.ImproveAffinity(allowBreakingConstraints: true);
    //                spiderLegProblem.IsBalanceEnabled = !spiderLegProblem.ShouldSkipBalance();
    //                await spiderLegProblem.Solve();
    //                await spiderLegProblem.ImproveAffinity(
    //                    allowBreakingConstraints: false,
    //                    allowedImbalancePercentage: 15);
    //                if (problemParams.EnableCompact)
    //                {
    //                    await spiderLegProblem.Compact(allowedImbalancePercentage: 15, enableHullCompact: true);
    //                }
    //                break;
    //            case "balance":
    //                spiderLegProblem.IsBalanceEnabled = true;
    //                await spiderLegProblem.Solve();
    //                await spiderLegProblem.ImproveAffinity(allowBreakingConstraints: true);
    //                spiderLegProblem.IsBalanceEnabled = true;
    //                await spiderLegProblem.Solve();
    //                await spiderLegProblem.ImproveAffinity(
    //                    allowBreakingConstraints: false,
    //                    allowedImbalancePercentage: 15);
    //                if (problemParams.EnableCompact)
    //                {
    //                    await spiderLegProblem.Compact(allowedImbalancePercentage: 15, enableHullCompact: true);
    //                }
    //                break;
    //            case "pack":
    //                spiderLegProblem.IsBalanceEnabled = false;
    //                await spiderLegProblem.Solve();
    //                await spiderLegProblem.ImproveAffinity(allowBreakingConstraints: true);
    //                await spiderLegProblem.Solve();
    //                await spiderLegProblem.ImproveAffinity(allowBreakingConstraints: false);
    //                if (problemParams.EnableCompact)
    //                {
    //                    await spiderLegProblem.Compact(allowedImbalancePercentage: 15, enableHullCompact: true);
    //                }
    //                break;
    //        }

    //        spiderLegProblem.AssignBestOriginalRoute();

    //        if (problemParams.EnableEvaluation)
    //        {
    //            await RoutesEvaluator.Evaluate(osrmService, problemParams.Base, routes);
    //        }

    //        return Ok(new
    //        {
    //            Adjacencies = spiderLegProblem.RouteAdjacencies.Select(adj => new
    //            {
    //                InnerRouteId = adj.InnerRoute.Id,
    //                OuterRouteId = adj.OuterRoute.Id
    //            }),
    //            Routes = spiderLegProblem.Routes.Select(r => new
    //            {
    //                r.Id,
    //                Sites = r.Sites.Select(s => new
    //                {
    //                    s.Id
    //                }),
    //                r.DistanceKm,
    //                r.DriveTimeH,
    //                r.TruckTypeId,
    //                IsOverCapacity = r.IsOverMaxCapacity,
    //                r.Load,
    //                Capacity = r.MaxCapacity
    //            })
    //        });
    //    }

    //    
    //}

    //public class TM
    //{
    //    private TransitMatrix BuildMasterTransitMatrix(List<Site> sites, bool greatCircleDistanceOnly = false)
    //    {
    //        Dictionary<(string, string), Transit> baseTransitMatrix;

    //        if (greatCircleDistanceOnly)
    //        {
    //            // Initialize blank TM
    //            baseTransitMatrix = new Dictionary<(string, string), Transit>();
    //        }
    //        else
    //        {
    //            // Load TM from database
    //            var siteInternalIds = sites.Select(s => s.Id).ToList();
    //            baseTransitMatrix = TransitMatrixService
    //                .GetTransitMatrixFromDatabase(GetCompanyId().ToString(), siteInternalIds);
    //        }

    //        var transitMatrix = new TransitMatrix();
    //        sites.ForEach(a =>
    //        {
    //            sites.ForEach(b =>
    //            {
    //                var available = baseTransitMatrix.TryGetValue((a.Id, b.Id), out Transit transit);
    //                if (available && transit.TimeH != 0)
    //                {
    //                    // Use entry from base TM
    //                    transitMatrix.AddEntry(a.Id, b.Id, transit.DistanceKm ?? double.MaxValue);
    //                }
    //                else
    //                {
    //                    // Create a new entry using GC distance
    //                    var p1 = new Point { Latitude = a.Latitude, Longitude = a.Longitude };
    //                    var p2 = new Point { Latitude = b.Latitude, Longitude = b.Longitude };
    //                    var distance = GeospatialFormula.Distance(p1, p2) / 1000.0;
    //                    transitMatrix.AddEntry(a.Id, b.Id, distance);
    //                }
    //            });
    //        });
    //        return transitMatrix;
    //    }
    //}
    


    public class SpiderLegParams
    {
        public bool GreatCircleDistanceOnly { get; set; } = false;
        public string Mode { get; set; }
        public int? BalanceParameterIndex { get; set; } = null;
        public double? MaxTotalTimeH { get; set; }
        public double? MaxServiceTimeH { get; set; }
        public int? ServiceTimeHParameterIndex { get; set; } = null;
        public List<Route> Routes { get; set; }
        public List<RouteAdjacencyParam> RouteAdjacencies { get; set; }
        public Site Base { get; set; }
        public int? NClosestTransferCandidates { get; set; }
        public bool AdjustAdjacencies { get; set; }
        public int? MaxIterations { get; set; }
        public bool EnableCompact { get; set; }
        public bool? EnableHullCompact { get; set; }
        public bool DetectConnectedComponents { get; set; } = false;
        public bool EnableEvaluation { get; set; }
    }

    public class RouteAdjacencyParam
    {
        public string OuterRouteId { get; set; }
        public string InnerRouteId { get; set; }

        public int Sequence { get; set; }
    }
}